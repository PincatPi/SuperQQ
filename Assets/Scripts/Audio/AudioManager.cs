using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 音频管理器（单例，跨场景常驻）。
    /// 全项目唯一的音频播放入口：BGM、环境循环音、UI 音效、场景音效统一经本类静态 API 播放，
    /// 外部模块无需接触 AudioSource/AudioClip/AudioMixer 的任何细节。
    ///
    /// 生命周期：无需在场景手动挂载，首次调用任意 API（或场景加载完成后）自动创建常驻实例；
    /// 也可在场景中手动挂载并拖配 Catalog/Mixer 引用（手动实例优先于自动引导实例）。
    ///
    /// 资源配置约定：AudioCatalog 与 MainAudioMixer 未在 Inspector 指定时，
    /// 自动从 Resources/Audio/AudioCatalog、Resources/Audio/MainAudioMixer 加载。
    ///
    /// 使用方式：
    ///   AudioManager.PlaySfx(SfxId.UiClick);                    // UI/2D 音效
    ///   AudioManager.PlaySfxAt(SfxId.Pickup, transform.position); // 3D 定位音效
    ///   AudioManager.PlayMusic(SfxId.BgmLobby);                 // BGM 交叉切换
    ///   AudioManager.SetVolume(AudioBus.Music, 0.8f);           // 总线音量（持久化）
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("资源配置")]
        [Tooltip("音效目录（ScriptableObject）。留空时自动从 Resources/Audio/AudioCatalog 加载")]
        [SerializeField] private AudioCatalog _catalog;

        [Tooltip("主混音器（Master → Music / SFX / UI 四分组）。留空时自动从 Resources/Audio/MainAudioMixer 加载")]
        [SerializeField] private AudioMixer _mixer;

        [Header("对象池")]
        [Tooltip("SFX/UI 复音 AudioSource 池容量（同时发声上限，超出时抢占最早播放者）")]
        [SerializeField, Min(4)] private int _poolSize = 16;

        // ==================== 常量 ====================

        // Mixer 暴露参数名（须与 MainAudioMixer 资产中暴露的参数一致）
        private const string MasterVolParam = "MasterVol";
        private const string MusicVolParam = "MusicVol";
        private const string SfxVolParam = "SfxVol";
        private const string UiVolParam = "UiVol";

        // Resources 约定加载路径
        private const string CatalogResourcePath = "Audio/AudioCatalog";
        private const string MixerResourcePath = "Audio/MainAudioMixer";

        // PlayerPrefs 持久化键前缀
        private const string PrefKeyPrefix = "SuperQQ.Audio.Volume.";
        private const string PrefKeyMuted = "SuperQQ.Audio.Muted";

        // 线性音量 → 分贝映射的静音下限（低于该值视为静音，避免 Log10(0)）
        private const float SilenceThreshold = 0.0001f;
        private const float SilenceDb = -80f;

        // ==================== 运行时状态 ====================

        private SfxSourcePool _sfxPool;
        private LoopChannel _musicChannel;      // BGM 通道（Music 组）

        private AudioMixerGroup _musicGroup;
        private AudioMixerGroup _sfxGroup;
        private AudioMixerGroup _uiGroup;

        private readonly Dictionary<AudioBus, float> _volumes = new();   // 各总线线性音量 0~1
        private bool _muted;

        // ==================== 引导 ====================

        /// <summary>
        /// 场景加载完成后自动引导，无需手动挂载。
        /// 选择 AfterSceneLoad：场景中手动挂载并配置好引用的实例 Awake 先行执行，优先于本引导。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureExists();
        }

        /// <summary>
        /// 获取单例；场景中未挂载时自动创建一个常驻实例
        /// </summary>
        public static AudioManager EnsureExists()
        {
            if (Instance == null)
            {
                var obj = new GameObject("AudioManager");
                obj.AddComponent<AudioManager>();
            }
            return Instance;
        }

        private void Awake()
        {
            // 场景中只允许一个实例
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 跨场景保持音乐/环境音不中断

            ResolveAssets();
            ResolveMixerGroups();

            _sfxPool = new SfxSourcePool(transform, _poolSize, this, GetGroup);
            _musicChannel = new LoopChannel("MusicChannel", transform, _musicGroup, this);

            LoadVolumes();
        }

        /// <summary>未指定引用时按约定路径从 Resources 加载配置资产</summary>
        private void ResolveAssets()
        {
            if (_catalog == null)
            {
                _catalog = Resources.Load<AudioCatalog>(CatalogResourcePath);
            }
            if (_mixer == null)
            {
                _mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            }

#if UNITY_EDITOR
            if (_catalog == null)
            {
                Debug.LogWarning($"[AudioManager] AudioCatalog 未配置，且 Resources/{CatalogResourcePath} 不存在；音效播放将被跳过。");
            }
            if (_mixer == null)
            {
                Debug.LogWarning($"[AudioManager] AudioMixer 未配置，且 Resources/{MixerResourcePath} 不存在；总线音量控制不可用，音频直连 AudioListener。");
            }
#endif
        }

        /// <summary>从 Mixer 解析分组；Mixer 缺失时分组为 null（AudioSource 直连监听者，仍可发声）</summary>
        private void ResolveMixerGroups()
        {
            _musicGroup = FindGroup("Music");
            _sfxGroup = FindGroup("SFX");
            _uiGroup = FindGroup("UI");
        }

        private AudioMixerGroup FindGroup(string name)
        {
            if (_mixer == null)
            {
                return null;
            }
            AudioMixerGroup[] groups = _mixer.FindMatchingGroups(name);
            if (groups.Length == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[AudioManager] MainAudioMixer 中未找到分组 \"{name}\"，该总线音频将直连 AudioListener。");
#endif
                return null;
            }
            return groups[0];
        }

        /// <summary>总线 → Mixer 分组（供对象池逐次播放时解析）</summary>
        private AudioMixerGroup GetGroup(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return _musicGroup;
                case AudioBus.UI: return _uiGroup;
                case AudioBus.SFX:
                default: return _sfxGroup;
            }
        }

        // ==================== 对外 API：音效 ====================

        /// <summary>
        /// 播放 2D/UI 音效。
        /// 未注册或未配置 Clip 时 Editor 下警告、运行时静默跳过（调用方无需判空）。
        /// </summary>
        /// <param name="id">音效标识</param>
        /// <param name="volumeScale">调用方音量缩放 0~1（与条目音量区间相乘）</param>
        public static void PlaySfx(SfxId id, float volumeScale = 1f)
        {
            AudioManager m = EnsureExists();
            m.PlayInternal(id, volumeScale, null);
        }

        /// <summary>
        /// 在世界坐标播放 3D 定位音效（供场景道具、远端玩家事件表现使用）
        /// </summary>
        public static void PlaySfxAt(SfxId id, Vector3 worldPos)
        {
            AudioManager m = EnsureExists();
            m.PlayInternal(id, 1f, worldPos);
        }

        private void PlayInternal(SfxId id, float volumeScale, Vector3? worldPos)
        {
            if (!TryGetEntry(id, out SfxEntry entry))
            {
                return;
            }
            _sfxPool.Play(entry, volumeScale, worldPos);
        }

        // ==================== 对外 API：音乐与环境音 ====================

        /// <summary>
        /// 播放/切换 BGM（Music 通道，交叉淡入淡出；与当前相同的 Clip 不重启）
        /// </summary>
        /// <param name="id">音乐音效标识（Clip 在 AudioCatalog 中配置）</param>
        /// <param name="fadeTime">交叉淡化时长（秒）</param>
        public static void PlayMusic(SfxId id, float fadeTime = 1f)
        {
            AudioManager m = EnsureExists();
            if (!m.TryGetEntry(id, out SfxEntry entry))
            {
                return;
            }
            m._musicChannel.CrossFadeTo(entry.Clip, fadeTime, entry.Volume);
        }

        /// <summary>淡出停止 BGM</summary>
        public static void StopMusic(float fadeTime = 1f)
        {
            // 不为「停止」而创建实例
            if (Instance != null)
            {
                Instance._musicChannel.Stop(fadeTime);
            }
        }

        /// <summary>当前 BGM 的 Clip（无则为 null），供 UI 展示等查询</summary>
        public static AudioClip CurrentMusicClip => Instance != null ? Instance._musicChannel.CurrentClip : null;

        // ==================== 对外 API：音量与静音 ====================

        /// <summary>是否静音（作用于 Master 总线，各分组音量设置保留）</summary>
        public static bool IsMuted => Instance != null && Instance._muted;

        /// <summary>
        /// 设置总线音量（线性 0~1），写入 Mixer 暴露参数并经 PlayerPrefs 持久化
        /// </summary>
        public static void SetVolume(AudioBus bus, float linear01)
        {
            AudioManager m = EnsureExists();
            m.SetVolumeInternal(bus, linear01);
        }

        /// <summary>读取总线音量（线性 0~1，未设置过时为 1）</summary>
        public static float GetVolume(AudioBus bus)
        {
            if (Instance == null)
            {
                return PlayerPrefs.GetFloat(PrefKey(bus), 1f);
            }
            return Instance._volumes.TryGetValue(bus, out float v) ? v : 1f;
        }

        /// <summary>静音开关（不改动各总线已设音量，仅将 Master 输出置为静默），经 PlayerPrefs 持久化</summary>
        public static void SetMuted(bool muted)
        {
            AudioManager m = EnsureExists();
            m._muted = muted;
            PlayerPrefs.SetInt(PrefKeyMuted, muted ? 1 : 0);
            PlayerPrefs.Save();
            m.ApplyBusVolume(AudioBus.Master);
        }

        // ==================== 音量内部实现 ====================

        private void SetVolumeInternal(AudioBus bus, float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            _volumes[bus] = linear01;
            PlayerPrefs.SetFloat(PrefKey(bus), linear01);
            PlayerPrefs.Save();
            ApplyBusVolume(bus);
        }

        /// <summary>从 PlayerPrefs 恢复全部总线音量与静音状态（Awake 时调用）</summary>
        private void LoadVolumes()
        {
            _muted = PlayerPrefs.GetInt(PrefKeyMuted, 0) == 1;
            for (int i = 0; i <= (int)AudioBus.UI; i++)
            {
                var bus = (AudioBus)i;
                _volumes[bus] = PlayerPrefs.GetFloat(PrefKey(bus), 1f);
                ApplyBusVolume(bus);
            }
        }

        /// <summary>将某总线的有效音量写入 Mixer（静音时 Master 取 0）</summary>
        private void ApplyBusVolume(AudioBus bus)
        {
            if (_mixer == null)
            {
                return;
            }
            float linear = _volumes.TryGetValue(bus, out float v) ? v : 1f;
            if (bus == AudioBus.Master && _muted)
            {
                linear = 0f;
            }
            _mixer.SetFloat(ParamName(bus), LinearToDb(linear));
        }

        private static string ParamName(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return MusicVolParam;
                case AudioBus.SFX: return SfxVolParam;
                case AudioBus.UI: return UiVolParam;
                case AudioBus.Master:
                default: return MasterVolParam;
            }
        }

        private static string PrefKey(AudioBus bus) => PrefKeyPrefix + bus;

        /// <summary>线性音量 0~1 → 分贝（0 附近钳制为静音 -80dB）</summary>
        private static float LinearToDb(float linear)
        {
            return linear <= SilenceThreshold ? SilenceDb : Mathf.Log10(linear) * 20f;
        }

        // ==================== 内部：条目查询 ====================

        /// <summary>查询音效条目；Catalog 缺失/未注册/无 Clip 时 Editor 警告、运行时静默失败</summary>
        private bool TryGetEntry(SfxId id, out SfxEntry entry)
        {
            entry = null;
            if (id == SfxId.None)
            {
                return false;
            }
            if (_catalog == null)
            {
                WarnEditor($"[AudioManager] AudioCatalog 未配置，无法播放 {id}。");
                return false;
            }
            if (!_catalog.TryGet(id, out entry))
            {
                WarnEditor($"[AudioManager] 音效 {id} 未在 AudioCatalog 中注册，播放跳过。");
                return false;
            }
            if (!entry.HasValidClip)
            {
                WarnEditor($"[AudioManager] 音效 {id} 未配置任何 AudioClip，播放跳过。");
                return false;
            }
            return true;
        }

        private static void WarnEditor(string message)
        {
#if UNITY_EDITOR
            Debug.LogWarning(message);
#endif
        }
    }
}

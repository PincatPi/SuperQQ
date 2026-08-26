using System;
using UnityEngine;

namespace SuperQQ.Microphone
{
    /// <summary>
    /// 麦克风音量管理器（单例，跨场景常驻）
    /// 开启麦克风后实时检测玩家输入音量，对外提供分贝值与归一化音量
    ///
    /// 生命周期：进入游玩阶段（PlayingPhase）时开麦，退出游玩阶段时关麦（PlayingPhase 调用 StartMic/StopMic）
    ///
    /// 使用方式：
    ///   float db = MicVolumeManager.Instance.SplDecibels;  // 估算声压级分贝（dB SPL，正值，供 UI 展示）
    ///   float v  = MicVolumeManager.Instance.Volume;       // 归一化音量 0~1（推荐用于玩法判定）
    /// </summary>
    public class MicVolumeManager : MonoBehaviour
    {
        public static MicVolumeManager Instance { get; private set; }

        [Header("采样配置")]
        [SerializeField] private int _sampleRate = 44100;           // 采样率
        [SerializeField] private int _sampleWindow = 1024;          // 每帧分析的采样数
        [SerializeField] private float _updateInterval = 0.05f;     // 音量刷新间隔（秒）

        [Header("音量映射")]
        [SerializeField] private float _minDb = -60f;               // 归一化下界（低于此分贝视为 0）
        [SerializeField] private float _maxDb = -5f;                // 归一化上界（高于此分贝视为 1）
        [SerializeField, Range(0f, 1f)] private float _smoothing = 0.3f; // 平滑系数，越大越平滑

        [Header("声压级（SPL）展示")]
        [Tooltip("dBFS 转 dB SPL 的校准偏移：SPL = dBFS + 校准值。0 dBFS 对应的实际声压级因设备麦克风灵敏度/增益而异，" +
                 "经验值约 90~100（手机满量程约对应 94 dB SPL）。如需精确值，请用标准声级计在固定距离实测后填入")]
        [SerializeField] private float _splCalibration = 94f;       // dBFS → dB SPL 校准偏移

        [Header("设备轮询")]
        [SerializeField] private float _devicePollInterval = 1f;    // 麦克风设备/权限轮询间隔（秒）

        /// <summary>分贝量程下限（静音基准，-120dB）</summary>
        public const float MinDecibels = -120f;

        /// <summary>声压级展示满量程（100 dB SPL，展示范围 0~100）</summary>
        public const float MaxSplDecibels = 100f;

        /// <summary>当前分贝值（dBFS，范围约 -120 ~ 0，静音为 -120），仅供内部/调试使用</summary>
        public float Decibels { get; private set; } = MinDecibels;

        /// <summary>
        /// 估算声压级分贝（dB SPL，类似苹果手表展示的正值分贝，范围 0~100，如安静室内约 40，交谈约 60）。
        /// 由 dBFS 加校准偏移得出，静音为 0，超过 100 封顶。未经真机校准时为近似值，仅供 UI 展示，勿用于玩法判定
        /// </summary>
        public float SplDecibels => Mathf.Clamp(Decibels + _splCalibration, 0f, MaxSplDecibels);

        /// <summary>声压级分贝占展示满量程的比例 0~1（= SplDecibels / 100），供音量条填充使用</summary>
        public float NormalizedSplDecibels => SplDecibels / MaxSplDecibels;

        /// <summary>归一化音量 0~1（已平滑，推荐玩法逻辑使用）</summary>
        public float Volume { get; private set; }

        /// <summary>麦克风是否正在采集</summary>
        public bool IsRunning { get; private set; }

        /// <summary>当前使用的设备名（null 表示系统默认设备）</summary>
        public string DeviceName { get; private set; }

        /// <summary>当前采集使用的麦克风 AudioClip（未采集时为 null），供语音识别等模块共享读取，避免同一设备重复开麦</summary>
        public AudioClip MicClip => _clip;

        /// <summary>当前采集的采样率</summary>
        public int SampleRate => _sampleRate;

        /// <summary>音量更新事件，参数为归一化音量 0~1</summary>
        public event Action<float> OnVolumeUpdated;

        /// <summary>是否有可用麦克风设备</summary>
        public static bool HasDevice => UnityEngine.Microphone.devices.Length > 0;

        private AudioClip _clip;
        private float[] _samples;
        private float _timer;
        private Coroutine _retryCoroutine;
        private Coroutine _deviceWatchCoroutine;
        private string _requestedDevice;
        private bool _shouldBeRunning;   // 标记「当前应当处于采集状态」，供断连轮询判断是否自动恢复
        private int _lastMicPosition;    // 上一次轮询时的录音位置，用于检测录音流是否停走（设备断连特征）

        /// <summary>
        /// 获取单例；场景中未挂载时自动创建一个常驻实例
        /// </summary>
        public static MicVolumeManager EnsureExists()
        {
            if (Instance == null)
            {
                var obj = new GameObject("MicVolumeManager");
                obj.AddComponent<MicVolumeManager>();
            }
            return Instance;
        }

        /// <summary>
        /// 麦克风未成功启动时（如等待用户授权），每 0.5s 自动重试直到开麦成功
        /// </summary>
        private System.Collections.IEnumerator RetryStartMic()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            // 每轮重试前检查应有采集状态：挂起期间收到 StopMic 时不得再尝试开麦
            while (_shouldBeRunning && !IsRunning)
            {
                TryStartMic(_requestedDevice);
                if (!IsRunning)
                {
                    yield return wait;
                }
            }
            _retryCoroutine = null;
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
            DontDestroyOnLoad(gameObject); // 跨场景保持录音不中断

            // 防线：场景卸载时强制关麦，防止对局中途退出（如直接返回大厅）
            // 绕过 PlayingPhase.OnExit 时麦克风跨场景残留采集
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private void HandleSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (_shouldBeRunning)
            {
                StopMic();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                StopMic();
                Instance = null;
            }
        }

        private void OnApplicationPause(bool pause)
        {
            // 移动端切后台时系统会中断录音，回到前台后自动恢复
            if (!pause && _shouldBeRunning)
            {
                RestartMic();
            }
        }

        /// <summary>
        /// 开启麦克风采集（重复调用安全）。启动失败（如等待授权）会自动重试直到成功
        /// </summary>
        /// <param name="deviceName">设备名，null 使用系统默认设备</param>
        /// <returns>是否成功开启</returns>
        public bool StartMic(string deviceName = null)
        {
            _requestedDevice = deviceName;
            _shouldBeRunning = true;
            bool ok = TryStartMic(deviceName);

            // 失败时启动自动重试（如移动端首次需等待用户授权）
            if (!ok && _retryCoroutine == null)
            {
                _retryCoroutine = StartCoroutine(RetryStartMic());
            }

            // 启动设备轮询：采集期间设备断连/权限变化后自动恢复
            if (_deviceWatchCoroutine == null)
            {
                _deviceWatchCoroutine = StartCoroutine(DeviceWatchLoop());
            }
            return ok;
        }

        private bool TryStartMic(string deviceName)
        {
            if (IsRunning)
            {
                return true;
            }

            if (!HasDevice)
            {
                Debug.LogWarning("[MicVolumeManager] 未检测到麦克风设备");
                return false;
            }

            DeviceName = deviceName;
            // 循环缓冲 1 秒，足够做窗口采样
            _clip = UnityEngine.Microphone.Start(DeviceName, true, 1, _sampleRate);
            if (_clip == null)
            {
                Debug.LogWarning("[MicVolumeManager] 麦克风启动失败（可能被占用或无权限）");
                return false;
            }

            if (_samples == null || _samples.Length != _sampleWindow)
            {
                _samples = new float[_sampleWindow];
            }

            Decibels = MinDecibels;
            Volume = 0f;
            _timer = 0f;
            _lastMicPosition = -1;
            IsRunning = true;
            return true;
        }

        /// <summary>
        /// 关闭麦克风采集（默认一直开麦，一般无需调用）
        /// </summary>
        public void StopMic()
        {
            // 清除应有采集状态，设备轮询与切前台恢复随之失效（无论当前是否采集都必须执行）
            _shouldBeRunning = false;

            // 停止自动重试，避免关麦后被重新打开
            if (_retryCoroutine != null)
            {
                StopCoroutine(_retryCoroutine);
                _retryCoroutine = null;
            }

            // 直接停止设备轮询协程：协程可能正处于 yield 挂起中，
            // 仅靠 _shouldBeRunning 标志无法阻止它醒来后先执行一轮恢复逻辑再退出
            if (_deviceWatchCoroutine != null)
            {
                StopCoroutine(_deviceWatchCoroutine);
                _deviceWatchCoroutine = null;
            }

            // 即便 IsRunning 已为 false（如录音流被系统中断），只要还有残留录音会话也要确保结束
            if (IsRunning || _clip != null)
            {
                UnityEngine.Microphone.End(DeviceName);
            }
            _clip = null;
            IsRunning = false;
            Decibels = MinDecibels;
            Volume = 0f;
        }

        private void RestartMic()
        {
            UnityEngine.Microphone.End(DeviceName);

            if (!HasDevice)
            {
                // 设备已拔出：标记为未采集，交由设备轮询在设备恢复后重新开麦
                _clip = null;
                IsRunning = false;
                return;
            }

            _clip = UnityEngine.Microphone.Start(DeviceName, true, 1, _sampleRate);
            _lastMicPosition = -1;
            IsRunning = _clip != null;
        }

        /// <summary>
        /// 设备轮询：开麦期间以固定间隔检查权限、设备列表与录音流活性。
        /// 设备断连后（GetPosition 停走/设备消失）自动重开录音，设备重新接入或权限授予后自动恢复采集。
        /// StopMic 后协程自动退出，不会影响非游玩阶段。
        /// </summary>
        private System.Collections.IEnumerator DeviceWatchLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(0.2f, _devicePollInterval));
            while (_shouldBeRunning)
            {
                yield return wait;

                // 挂起期间可能收到 StopMic：醒来后先复核应有采集状态再执行任何恢复逻辑
                if (!_shouldBeRunning)
                {
                    break;
                }

                // 1) 权限被撤销或正在等待授权：触发重试链路（内部含权限请求）
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                {
                    if (_retryCoroutine == null)
                    {
                        _retryCoroutine = StartCoroutine(RetryStartMic());
                    }
                    continue;
                }

                // 2) 应有采集但实际未在采集（启动失败、设备拔出等）：尝试直接重开
                if (!IsRunning || _clip == null)
                {
                    TryStartMic(_requestedDevice);
                    continue;
                }

                // 3) 正在采集：通过录音位置是否推进判断录音流是否存活
                int pos = UnityEngine.Microphone.GetPosition(DeviceName);
                if (pos == 0 && _lastMicPosition == 0)
                {
                    // 位置连续停走，视为设备断连/流中断，重开录音（设备仍拔出时内部会标记未采集，下轮继续重试）
                    Debug.LogWarning("[MicVolumeManager] 检测到录音流中断，尝试重启麦克风");
                    RestartMic();
                }
                _lastMicPosition = pos;
            }
            _deviceWatchCoroutine = null;
        }

        private void Update()
        {
            if (!IsRunning || _clip == null)
            {
                return;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < _updateInterval)
            {
                return;
            }
            _timer = 0f;

            SampleVolume();
        }

        /// <summary>
        /// 从循环缓冲读取最新采样，计算 RMS 并换算分贝
        /// </summary>
        private void SampleVolume()
        {
            int pos = UnityEngine.Microphone.GetPosition(DeviceName);
            if (pos < _sampleWindow)
            {
                return; // 缓冲区刚启动，数据还不够一个窗口
            }

            _clip.GetData(_samples, pos - _sampleWindow);

            float sum = 0f;
            for (int i = 0; i < _sampleWindow; i++)
            {
                sum += _samples[i] * _samples[i];
            }
            float rms = Mathf.Sqrt(sum / _sampleWindow);

            float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-5f));
            db = Mathf.Max(db, MinDecibels);
            float target = Mathf.Clamp01(Mathf.InverseLerp(_minDb, _maxDb, db));

            // 指数平滑，避免数值剧烈跳动
            float lerpT = 1f - Mathf.Pow(_smoothing, _updateInterval / 0.05f);
            Volume = Mathf.Lerp(Volume, target, lerpT);
            Decibels = Mathf.Lerp(Decibels, db, lerpT);

            OnVolumeUpdated?.Invoke(Volume);
        }

        /// <summary>
        /// 音量是否超过指定阈值（归一化 0~1），方便玩法直接判定
        /// </summary>
        public bool IsAboveThreshold(float threshold)
        {
            return IsRunning && Volume >= threshold;
        }

    }
}

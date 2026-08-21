using System;
using UnityEngine;

namespace SuperQQ.Microphone
{
    /// <summary>
    /// 麦克风音量管理器（单例，跨场景常驻）
    /// 开启麦克风后实时检测玩家输入音量，对外提供分贝值与归一化音量
    ///
    /// 生命周期：进入房间时开麦（UIRoomController 调用 StartMic），退出房间时关麦（UIRoomController 调用 StopMic）
    ///
    /// 使用方式：
    ///   float db = MicVolumeManager.Instance.Decibels;  // 当前分贝（静音约 -90dB）
    ///   float v  = MicVolumeManager.Instance.Volume;    // 归一化音量 0~1（推荐用于玩法判定）
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

        /// <summary>当前分贝值（范围约 -90 ~ 0，静音为 -90）</summary>
        public float Decibels { get; private set; } = -90f;

        /// <summary>归一化音量 0~1（已平滑，推荐玩法逻辑使用）</summary>
        public float Volume { get; private set; }

        /// <summary>麦克风是否正在采集</summary>
        public bool IsRunning { get; private set; }

        /// <summary>当前使用的设备名（null 表示系统默认设备）</summary>
        public string DeviceName { get; private set; }

        /// <summary>音量更新事件，参数为归一化音量 0~1</summary>
        public event Action<float> OnVolumeUpdated;

        /// <summary>是否有可用麦克风设备</summary>
        public static bool HasDevice => UnityEngine.Microphone.devices.Length > 0;

        private AudioClip _clip;
        private float[] _samples;
        private float _timer;
        private Coroutine _retryCoroutine;
        private string _requestedDevice;

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
            while (!IsRunning)
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
            if (!pause && IsRunning)
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
            bool ok = TryStartMic(deviceName);

            // 失败时启动自动重试（如移动端首次需等待用户授权）
            if (!ok && _retryCoroutine == null)
            {
                _retryCoroutine = StartCoroutine(RetryStartMic());
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

            Decibels = -90f;
            Volume = 0f;
            _timer = 0f;
            IsRunning = true;
            return true;
        }

        /// <summary>
        /// 关闭麦克风采集（默认一直开麦，一般无需调用）
        /// </summary>
        public void StopMic()
        {
            // 停止自动重试，避免关麦后被重新打开
            if (_retryCoroutine != null)
            {
                StopCoroutine(_retryCoroutine);
                _retryCoroutine = null;
            }
            if (!IsRunning)
            {
                return;
            }

            UnityEngine.Microphone.End(DeviceName);
            _clip = null;
            IsRunning = false;
            Decibels = -90f;
            Volume = 0f;
        }

        private void RestartMic()
        {
            UnityEngine.Microphone.End(DeviceName);
            _clip = UnityEngine.Microphone.Start(DeviceName, true, 1, _sampleRate);
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
            db = Mathf.Max(db, -90f);
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

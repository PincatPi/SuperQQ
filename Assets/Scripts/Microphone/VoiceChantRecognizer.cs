using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SuperQQ.Microphone
{
    /// <summary>
    /// 语音吟唱识别器（单例）— 供玩法调用的语音识别门面
    /// 调用 StartChantCapture(duration) 后：采集指定时长（如 5s）的麦克风输入，
    /// 实时流式送腾讯云 ASR 识别为文本，识别结果以黑色大字体显示在屏幕中央（调试 HUD），
    /// 同时通过 OnChantRecognized 事件输出完整识别文本
    ///
    /// 麦克风复用策略：优先共享 MicVolumeManager 的采集流（44.1kHz，线性插值降采样到 16kHz），
    /// 其未开麦时才自行开启 16kHz 麦克风（同一设备不能重复开麦）
    ///
    /// 使用方式（玩法侧）：
    ///   VoiceChantRecognizer.EnsureExists().StartChantCapture(5f);
    ///   VoiceChantRecognizer.Instance.OnChantRecognized += text => ...;
    /// </summary>
    public class VoiceChantRecognizer : MonoBehaviour
    {
        public static VoiceChantRecognizer Instance { get; private set; }

        [Header("腾讯云 ASR 凭证（留空则从 StreamingAssets/asr_credentials.json 读取；切勿提交真实密钥到仓库）")]
        [SerializeField] private string _appId = "";
        [SerializeField] private string _secretId = "";
        [SerializeField] private string _secretKey = "";

        [Header("识别参数")]
        [Tooltip("引擎模型类型（16k_zh 中文通用 / 16k_game 游戏娱乐等）")]
        [SerializeField] private string _engineModelType = "16k_zh";

        [Tooltip("是否开启 VAD 静音检测（开启后自动按停顿切分语音段）")]
        [SerializeField] private bool _needVad = true;

        [Header("调试显示")]
        [Tooltip("是否在屏幕中央以黑色大字体显示识别状态与结果（OnGUI 调试 HUD）")]
        [SerializeField] private bool _showDebugHud = true;

        [SerializeField] private int _hudFontSize = 48;

        [Tooltip("识别结束后结果文本在屏幕上的驻留时长（秒），便于测试观察")]
        [Min(1f)]
        [SerializeField] private float _hudResultDuration = 10f;

        // ASR 要求的音频格式：16kHz 单声道 PCM16，每帧 200ms = 3200 采样 = 6400 字节
        private const int TARGET_SAMPLE_RATE = 16000;
        private const int FRAME_BYTES = 6400;

        /// <summary>一次吟唱的完整识别文本（会话结束时回调；无识别结果时为空字符串）</summary>
        public event Action<string> OnChantRecognized;

        /// <summary>是否正在采集识别</summary>
        public bool BIsCapturing { get; private set; }

        // ==================== 运行状态 ====================

        // ASR 传输层（同 GameObject 上的组件，Awake 时创建并接线）
        private TencentRealtimeASR _asr;

        // 音频源：共享 MicVolumeManager 或自持麦克风
        private AudioClip _clip;
        private string _deviceName;
        private int _sourceSampleRate;
        private bool _bOwnsMic;
        private int _lastMicPos;

        // 采集窗口
        private float _captureEndTime;
        private bool _bSessionActive;

        // 降采样器状态（源采样率 ≠ 16k 时的流式线性插值重采样）
        private readonly List<float> _pendingSamples = new List<float>(2048);
        private long _pendingStart;      // _pendingSamples[0] 对应的全局源采样序号
        private double _nextOutPosition; // 下一个输出采样对应的全局源采样位置
        private double _resampleRatio;   // 源/目标采样率比（如 44100/16000 ≈ 2.756）

        // PCM 字节累积（凑满一帧 6400 字节即推给传输层）
        private readonly List<byte> _pcmAccumulator = new List<byte>(FRAME_BYTES * 2);

        // 识别文本累积与 HUD 显示
        private string _sessionText = "";
        private string _partialText = "";
        private string _statusText = "空闲";

        // 结果文本 HUD 驻留截止时刻（Time.unscaledTime）
        private float _hudExpireTime;

        // ==================== 生命周期 ====================

        [Serializable]
        private class AsrCredentials
        {
            public string appId;
            public string secretId;
            public string secretKey;
        }

        /// <summary>
        /// Inspector 未配置凭证时，从 StreamingAssets/asr_credentials.json 读取（该文件已加入 .gitignore，不会提交到仓库）。
        /// 格式参考同目录 asr_credentials.example.json
        /// </summary>
        private void LoadCredentialsFromConfig()
        {
            if (!string.IsNullOrEmpty(_secretId) && !string.IsNullOrEmpty(_secretKey))
            {
                return;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "asr_credentials.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var credentials = JsonUtility.FromJson<AsrCredentials>(File.ReadAllText(path));
                if (credentials == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(_appId)) _appId = credentials.appId;
                if (string.IsNullOrEmpty(_secretId)) _secretId = credentials.secretId;
                if (string.IsNullOrEmpty(_secretKey)) _secretKey = credentials.secretKey;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoiceChantRecognizer] 读取 ASR 凭证配置失败: {e.Message}");
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LoadCredentialsFromConfig();

            _asr = GetComponent<TencentRealtimeASR>();
            if (_asr == null)
            {
                _asr = gameObject.AddComponent<TencentRealtimeASR>();
            }
            _asr.OnPartialResult += HandlePartialResult;
            _asr.OnFinalSegment += HandleFinalSegment;
            _asr.OnError += HandleError;
            _asr.OnStateChanged += HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                EndCapture();
                Instance = null;
            }
        }

        /// <summary>
        /// 获取单例；场景中未挂载时自动创建一个实例（凭证需在 Inspector 配置，自动创建的实例无凭证会报错提示）
        /// </summary>
        public static VoiceChantRecognizer EnsureExists()
        {
            if (Instance == null)
            {
                var obj = new GameObject(nameof(VoiceChantRecognizer));
                obj.AddComponent<VoiceChantRecognizer>();
            }
            return Instance;
        }

        // ==================== 对外接口 ====================

        /// <summary>
        /// 开启一次吟唱识别：采集 duration 秒的麦克风输入并识别为文本
        /// 采集进行中重复调用会被忽略；识别完成后通过 OnChantRecognized 输出完整文本
        /// </summary>
        /// <param name="duration">采集时长（秒）</param>
        /// <returns>是否成功开启（麦克风不可用或已在采集时返回 false）</returns>
        public bool StartChantCapture(float duration)
        {
            if (BIsCapturing || _bSessionActive)
            {
                Debug.Log("[VoiceChantRecognizer] 吟唱识别进行中，忽略重复开启。");
                return false;
            }

            if (!ResolveAudioSource())
            {
                return false;
            }

            // 从当前录音位置开始读，跳过开启前缓冲的旧音频
            _lastMicPos = UnityEngine.Microphone.GetPosition(_deviceName);
            _pendingSamples.Clear();
            _pendingStart = _lastMicPos >= 0 ? _lastMicPos : 0;
            _nextOutPosition = _pendingStart;
            _resampleRatio = (double)_sourceSampleRate / TARGET_SAMPLE_RATE;
            _pcmAccumulator.Clear();
            _sessionText = "";
            _partialText = "";
            _statusText = "连接识别服务中...";

            _captureEndTime = Time.unscaledTime + Mathf.Max(0.5f, duration);
            BIsCapturing = true;
            _bSessionActive = true;
            _asr.StartSession(_appId, _secretId, _secretKey, _engineModelType, _needVad);
            return true;
        }

        /// <summary>
        /// 提前结束当前吟唱识别（采集窗口到期会自动调用，一般无需手动调用）
        /// </summary>
        public void EndCapture()
        {
            if (!BIsCapturing && !_bSessionActive)
            {
                return;
            }

            BIsCapturing = false;

            if (_bOwnsMic)
            {
                UnityEngine.Microphone.End(_deviceName);
            }
            _bOwnsMic = false;
            _clip = null;

            _asr.EndSession();
            _statusText = "识别收尾中...";
        }

        // ==================== 采集与送帧 ====================

        private void Update()
        {
            if (!BIsCapturing)
            {
                return;
            }

            PumpMicrophoneAudio();

            if (Time.unscaledTime >= _captureEndTime)
            {
                EndCapture();
            }
        }

        /// <summary>
        /// 从麦克风循环缓冲读取新增采样，降采样为 16kHz PCM16 并按帧推给传输层
        /// 必须在主线程调用（Unity 麦克风 API 线程限制）
        /// </summary>
        private void PumpMicrophoneAudio()
        {
            if (_clip == null)
            {
                return;
            }

            int pos = UnityEngine.Microphone.GetPosition(_deviceName);
            if (pos < 0 || pos == _lastMicPos)
            {
                return;
            }

            int available = pos - _lastMicPos;
            if (available < 0)
            {
                available += _clip.samples; // 循环缓冲回绕
            }
            if (available <= 0)
            {
                return;
            }

            // 循环缓冲保护：读取滞后超过半个缓冲说明追不上，丢弃旧数据防读到覆写区
            // 注意：丢弃的只是输入内容，_pendingSamples 是连续采样流，无需调整全局序号
            // （移动 _pendingStart 会导致降采样索引错位越界）
            if (available > _clip.samples / 2)
            {
                int drop = available - _clip.samples / 2;
                _lastMicPos = (_lastMicPos + drop) % _clip.samples;
                available -= drop;
            }

            ReadSamples(available);
            _lastMicPos = (_lastMicPos + available) % _clip.samples;
        }

        /// <summary>
        /// 读取 available 个采样（处理循环缓冲回绕），送入降采样管线
        /// GetData 会填满传入的整个数组，故按精确长度分段读取（仅采集期间有少量小数组分配）
        /// </summary>
        private void ReadSamples(int available)
        {
            int firstPart = Mathf.Min(available, _clip.samples - _lastMicPos);
            int secondPart = available - firstPart;

            var first = new float[firstPart];
            _clip.GetData(first, _lastMicPos);
            FeedSamples(first, firstPart);

            if (secondPart > 0)
            {
                // 回绕：后半段从循环缓冲头部继续读
                var second = new float[secondPart];
                _clip.GetData(second, 0);
                FeedSamples(second, secondPart);
            }
        }

        /// <summary>
        /// 将源采样送入流式重采样器，输出 16kHz 采样并转 PCM16 累积成帧
        /// </summary>
        private void FeedSamples(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _pendingSamples.Add(samples[i]);
            }

            long end = _pendingStart + _pendingSamples.Count; // 最新采样的全局序号 +1

            // 线性插值：输出位置需要 floor(pos) 与 floor(pos)+1 两个源采样都在缓冲内
            while (_nextOutPosition + 1.0 < end)
            {
                int idx = (int)(Math.Floor(_nextOutPosition) - _pendingStart);
                double t = _nextOutPosition - Math.Floor(_nextOutPosition);
                float a = _pendingSamples[idx];
                float b = _pendingSamples[idx + 1];
                EmitPcmSample((float)(a + (b - a) * t));
                _nextOutPosition += _resampleRatio;
            }

            // 丢弃已消费的源采样前缀（保留插值所需的上一个采样）
            long keepFrom = (long)Math.Floor(_nextOutPosition) - 1;
            int dropCount = (int)(keepFrom - _pendingStart);
            if (dropCount > 0)
            {
                _pendingSamples.RemoveRange(0, dropCount);
                _pendingStart += dropCount;
            }
        }

        /// <summary>
        /// 输出一个 16kHz 采样：float 转 PCM16 累积，凑满 6400 字节即推给传输层
        /// </summary>
        private void EmitPcmSample(float sample)
        {
            short s = (short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), short.MinValue, short.MaxValue);
            _pcmAccumulator.Add((byte)(s & 0xFF));
            _pcmAccumulator.Add((byte)((s >> 8) & 0xFF));

            if (_pcmAccumulator.Count >= FRAME_BYTES)
            {
                byte[] frame = _pcmAccumulator.GetRange(0, FRAME_BYTES).ToArray();
                _pcmAccumulator.RemoveRange(0, FRAME_BYTES);
                _asr.SendAudioFrame(frame);
            }
        }

        // ==================== 音频源 ====================

        /// <summary>
        /// 解析音频源：优先共享 MicVolumeManager 的采集流（避免重复开麦），
        /// 其未开麦时自行开启 16kHz 循环录音
        /// </summary>
        private bool ResolveAudioSource()
        {
            MicVolumeManager volumeManager = MicVolumeManager.Instance;
            if (volumeManager != null && volumeManager.IsRunning && volumeManager.MicClip != null)
            {
                _clip = volumeManager.MicClip;
                _deviceName = volumeManager.DeviceName;
                _sourceSampleRate = volumeManager.SampleRate;
                _bOwnsMic = false;
                return true;
            }

            if (!MicVolumeManager.HasDevice)
            {
                Debug.LogWarning("[VoiceChantRecognizer] 未检测到麦克风设备，无法开启吟唱识别。");
                _statusText = "无麦克风设备";
                return false;
            }

            _deviceName = null; // 系统默认设备
            _sourceSampleRate = TARGET_SAMPLE_RATE;
            _clip = UnityEngine.Microphone.Start(_deviceName, true, 10, TARGET_SAMPLE_RATE);
            if (_clip == null)
            {
                Debug.LogWarning("[VoiceChantRecognizer] 麦克风启动失败（可能被占用或无权限）。");
                _statusText = "麦克风启动失败";
                return false;
            }

            _bOwnsMic = true;
            return true;
        }

        // ==================== 传输层事件 ====================

        private void HandlePartialResult(string text)
        {
            _partialText = text;
        }

        private void HandleFinalSegment(string text)
        {
            _partialText = "";
            if (!string.IsNullOrEmpty(text))
            {
                _sessionText += text;
            }
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[VoiceChantRecognizer] 识别服务错误: {error}");
            _statusText = $"识别错误: {error}";
        }

        private void HandleStateChanged(TencentRealtimeASR.ASRState state)
        {
            switch (state)
            {
                case TencentRealtimeASR.ASRState.Connecting:
                    _statusText = "连接识别服务中...";
                    break;
                case TencentRealtimeASR.ASRState.Recognizing:
                    _statusText = "识别中，请吟唱...";
                    break;
                case TencentRealtimeASR.ASRState.Stopping:
                    _statusText = "识别收尾中...";
                    break;
                case TencentRealtimeASR.ASRState.Idle:
                    // 会话完全结束：输出本次吟唱的完整识别文本，结果驻留一段时间便于测试观察
                    if (_bSessionActive)
                    {
                        _bSessionActive = false;
                        BIsCapturing = false;
                        _statusText = string.IsNullOrEmpty(_sessionText) ? "未识别到语音" : "识别完成";
                        _partialText = "";
                        _hudExpireTime = Time.unscaledTime + _hudResultDuration;
                        OnChantRecognized?.Invoke(_sessionText);
                    }
                    break;
            }
        }

        // ==================== 调试 HUD（黑色大字体） ====================

        private GUIStyle _hudStyle;

        private void OnGUI()
        {
            if (!_showDebugHud)
            {
                return;
            }

            // 识别会话期间全程显示；结束后结果按配置时长驻留（含"未识别到语音"的空结果）
            bool bHasContent = _bSessionActive || BIsCapturing || Time.unscaledTime < _hudExpireTime;
            if (!bHasContent)
            {
                return;
            }

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.black }
                };
            }
            _hudStyle.fontSize = _hudFontSize;

            float lineHeight = _hudFontSize * 1.4f;
            float y = Screen.height * 0.18f;
            DrawCenteredLine($"[语音吟唱] {_statusText}", ref y, lineHeight);

            if (!string.IsNullOrEmpty(_partialText))
            {
                DrawCenteredLine(_partialText, ref y, lineHeight);
            }

            if (!string.IsNullOrEmpty(_sessionText))
            {
                DrawCenteredLine(_sessionText, ref y, lineHeight);
            }
        }

        private void DrawCenteredLine(string text, ref float y, float lineHeight)
        {
            GUI.Label(new Rect(0f, y, Screen.width, lineHeight), text, _hudStyle);
            y += lineHeight;
        }
    }
}

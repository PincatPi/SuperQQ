using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SuperQQ.Microphone
{
    /// <summary>
    /// 腾讯云实时语音识别（WebSocket 版）传输层
    /// 只负责鉴权建连、PCM 音频帧发送与识别消息收转，不持有麦克风、不含任何玩法逻辑
    ///
    /// 调用时序：
    ///   StartSession() → 开始建连并进入识别
    ///   SendAudioFrame() → 持续送入 16kHz 单声道 PCM16 音频帧（任意来源）
    ///   EndSession() → 发送结束标记，等待服务器返回最终结果后自动关闭
    ///
    /// 识别结果通过事件回调（主线程）：
    ///   OnPartialResult — 识别过程中的中间结果
    ///   OnFinalSegment  — 一段语音的最终结果（VAD 切分后会有多段）
    ///   OnError / OnStateChanged — 错误与状态变化
    /// </summary>
    public class TencentRealtimeASR : MonoBehaviour
    {
        public enum ASRState { Idle, Connecting, Recognizing, Stopping }

        /// <summary>当前连接状态（主线程读取）</summary>
        public ASRState State { get; private set; } = ASRState.Idle;

        /// <summary>会话是否进行中（建连中或识别中）</summary>
        public bool IsSessionActive => State == ASRState.Connecting || State == ASRState.Recognizing;

        /// <summary>识别中间结果（可能反复变化）</summary>
        public event Action<string> OnPartialResult;

        /// <summary>一段语音的最终识别结果（一次会话可能有多段）</summary>
        public event Action<string> OnFinalSegment;

        /// <summary>发生错误</summary>
        public event Action<string> OnError;

        /// <summary>状态变化</summary>
        public event Action<ASRState> OnStateChanged;

        private const string WS_HOST = "asr.cloud.tencent.com";
        private const int SAMPLE_RATE = 16000;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        // 待发送的 PCM 音频帧队列（SendAudioFrame 主线程入队，发送协程消费）
        private readonly ConcurrentQueue<byte[]> _pendingFrames = new ConcurrentQueue<byte[]>();

        // 会话参数（StartSession 时传入）
        private string _appId;
        private string _secretId;
        private string _secretKey;
        private string _engineModelType;
        private bool _needVad;
        private string _voiceId;
        private bool _bEndSent;
        private bool _bHasSentAudio;

        // 后台线程到主线程的回调队列
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // ==================== 会话生命周期 ====================

        /// <summary>
        /// 开始一次识别会话：鉴权建连、启动发送/接收循环
        /// 凭证由调用方持有并传入（避免传输层序列化密钥）
        /// </summary>
        public void StartSession(string appId, string secretId, string secretKey, string engineModelType, bool needVad)
        {
            if (IsSessionActive)
            {
                Debug.LogWarning("[TencentASR] 会话进行中，忽略重复启动。");
                return;
            }

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretId) || string.IsNullOrEmpty(secretKey))
            {
                RaiseError("腾讯云凭证未配置（AppId / SecretId / SecretKey）。");
                return;
            }

            _appId = appId;
            _secretId = secretId;
            _secretKey = secretKey;
            _engineModelType = string.IsNullOrEmpty(engineModelType) ? "16k_zh" : engineModelType;
            _needVad = needVad;
            _voiceId = Guid.NewGuid().ToString("N");
            _bEndSent = false;
            _bHasSentAudio = false;
            while (_pendingFrames.TryDequeue(out _)) { }

            _ = ConnectAsync();
        }

        /// <summary>
        /// 送入一帧 16kHz 单声道 PCM16 音频数据（仅识别中生效）
        /// </summary>
        public void SendAudioFrame(byte[] frame)
        {
            if (State != ASRState.Recognizing || frame == null || frame.Length == 0)
            {
                return;
            }
            _pendingFrames.Enqueue(frame);
        }

        /// <summary>
        /// 结束会话：发送结束标记，等待服务器返回最终结果后自动关闭
        /// </summary>
        public void EndSession()
        {
            if (!IsSessionActive)
            {
                return;
            }
            SetState(ASRState.Stopping);
            _ = SendEndAsync();
        }

        private async Task ConnectAsync()
        {
            SetState(ASRState.Connecting);
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            try
            {
                string url = BuildSignedUrl();
                await _ws.ConnectAsync(new Uri(url), _cts.Token);
            }
            catch (Exception e)
            {
                RaiseError($"WebSocket 连接失败: {e.Message}");
                Cleanup();
                return;
            }

            SetState(ASRState.Recognizing);
            _ = ReceiveLoopAsync();
            _ = SendLoopAsync();
        }

        // ==================== 发送 ====================

        /// <summary>
        /// 发送循环：严格按 1:1 实时速率推流（多少毫秒的音频就花多少毫秒发送）
        /// 服务器限流：1 秒墙钟时间内最多发送 3 秒音频（错误码 4000），
        /// 因此即使队列出现积压（网络抖动等）也不突刺式补发，而是丢弃旧帧保持实时
        /// 发送"end"后仍继续推流直至收到最终结果，保证吟唱尾部音频完整送达
        /// </summary>
        private async Task SendLoopAsync()
        {
            // 积压容忍帧数：超过则丢弃最旧帧，避免补发超速
            const int MAX_BACKLOG_FRAMES = 2;

            DateTime nextSendTime = DateTime.UtcNow;
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    if (_pendingFrames.TryDequeue(out byte[] frame))
                    {
                        // 发送节流：未到该帧的发送时刻则等待
                        DateTime now = DateTime.UtcNow;
                        if (nextSendTime > now)
                        {
                            await Task.Delay(nextSendTime - now, _cts.Token);
                        }

                        await _ws.SendAsync(new ArraySegment<byte>(frame),
                            WebSocketMessageType.Binary, true, _cts.Token);
                        _bHasSentAudio = true;

                        // 帧时长 = 字节数 / 2(PCM16) / 16000 采样率
                        nextSendTime = nextSendTime.AddMilliseconds(frame.Length / 32.0);

                        // 卡顿后重置节流基准，丢弃积压旧帧，保持实时不追发
                        if (DateTime.UtcNow > nextSendTime)
                        {
                            nextSendTime = DateTime.UtcNow;
                        }
                        while (_pendingFrames.Count > MAX_BACKLOG_FRAMES
                            && _pendingFrames.TryDequeue(out _)) { }
                    }
                    else
                    {
                        await Task.Delay(10, _cts.Token);
                    }
                }
            }
            catch (Exception e)
            {
                if (!_cts.IsCancellationRequested)
                {
                    EnqueueMain(() => Debug.LogWarning($"[TencentASR] 发送循环异常: {e.Message}"));
                }
            }
        }

        /// <summary>
        /// 发送结束标记（end 消息）；发送后由发送循环继续推流剩余音频直至收到最终结果
        /// </summary>
        private async Task SendEndAsync()
        {
            try
            {
                if (_ws == null || _ws.State != WebSocketState.Open || _bEndSent)
                {
                    return;
                }

                // 从未发过音频数据时，直接结束会导致服务器报错，改为静默关闭
                if (!_bHasSentAudio)
                {
                    EnqueueMain(() => Debug.LogWarning("[TencentASR] 未采集到音频数据，跳过结束标记发送。"));
                    Cleanup();
                    return;
                }

                _bEndSent = true;
                string endMsg = "{\"type\":\"end\"}";
                await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(endMsg)),
                    WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception e)
            {
                EnqueueMain(() => Debug.LogWarning($"[TencentASR] 发送结束标记失败: {e.Message}"));
            }
        }

        // ==================== 接收 ====================

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Cleanup();
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    bool bFinal = HandleMessage(sb.ToString());
                    if (bFinal)
                    {
                        Cleanup();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (!_cts.IsCancellationRequested)
                {
                    RaiseError($"接收消息异常: {e.Message}");
                    Cleanup();
                }
            }
        }

        /// <summary>
        /// 解析识别消息，分发中间/最终结果事件
        /// </summary>
        /// <returns>是否为会话结束消息（final=1 或服务器报错）</returns>
        private bool HandleMessage(string json)
        {
            try
            {
                var response = JsonUtility.FromJson<ASRResponse>(json);

                if (response.code != 0)
                {
                    RaiseError($"服务器错误 {response.code}: {response.message} (voice_id: {response.voice_id})");
                    return true;
                }

                if (response.result != null && response.result.voice_text_str != null)
                {
                    // slice_type: 0=开始 1=识别中 2=一段话结束
                    if (response.result.slice_type == 2)
                    {
                        EnqueueMain(() => OnFinalSegment?.Invoke(response.result.voice_text_str));
                    }
                    else if (response.result.slice_type == 1)
                    {
                        EnqueueMain(() => OnPartialResult?.Invoke(response.result.voice_text_str));
                    }
                }

                return response.@final == 1;
            }
            catch (Exception e)
            {
                EnqueueMain(() => Debug.LogWarning($"[TencentASR] 解析消息失败: {e.Message}\n原始消息: {json}"));
                return false;
            }
        }

        // ==================== 签名（HMAC-SHA1 + Base64，实时语音 WebSocket 接口专用方案） ====================

        /// <summary>
        /// 构建带签名的 WebSocket URL
        /// 签名原文 = host/asr/v2/{appId}?{按参数名升序排列的查询串}，
        /// 用 SecretKey 做 HMAC-SHA1 后 Base64，再 URL 编码拼到 URL 末尾
        /// </summary>
        private string BuildSignedUrl()
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expired = timestamp + 86400;
            int nonce = UnityEngine.Random.Range(1, int.MaxValue);

            var parameters = new SortedDictionary<string, string>
            {
                { "engine_model_type", _engineModelType },
                { "expired", expired.ToString() },
                { "needvad", _needVad ? "1" : "0" },
                { "nonce", nonce.ToString() },
                { "secretid", _secretId },
                { "timestamp", timestamp.ToString() },
                { "voice_format", "1" },
                { "voice_id", _voiceId },
            };

            var sb = new StringBuilder();
            foreach (var kv in parameters)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('&');
            }
            string query = sb.ToString().TrimEnd('&');

            // 签名原文不含 wss:// 协议头
            string signText = $"{WS_HOST}/asr/v2/{_appId}?{query}";

            byte[] signatureBytes;
            using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_secretKey)))
            {
                signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signText));
            }
            string signature = Convert.ToBase64String(signatureBytes);

            return $"wss://{signText}&signature={Uri.EscapeDataString(signature)}";
        }

        // ==================== 状态与回调 ====================

        private void SetState(ASRState newState)
        {
            State = newState;
            EnqueueMain(() => OnStateChanged?.Invoke(newState));
        }

        private void RaiseError(string msg)
        {
            EnqueueMain(() => OnError?.Invoke(msg));
        }

        private void EnqueueMain(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        private void Update()
        {
            // 将后台线程的回调转移到主线程执行
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

        private void Cleanup()
        {
            _cts?.Cancel();
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open)
                {
                    _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                _ws.Dispose();
                _ws = null;
            }
            _cts?.Dispose();
            _cts = null;
            _bEndSent = false;
            _bHasSentAudio = false;
            while (_pendingFrames.TryDequeue(out _)) { }
            SetState(ASRState.Idle);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        // ==================== 消息数据结构 ====================

        [Serializable]
        private class ASRResponse
        {
            public int code;
            public string message;
            public string voice_id;
            public string message_id;
            public ASRResult result;
            public int @final;
        }

        [Serializable]
        private class ASRResult
        {
            public int slice_type;
            public int index;
            public long start_time;
            public long end_time;
            public string voice_text_str;
        }
    }
}

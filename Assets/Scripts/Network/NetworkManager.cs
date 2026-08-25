using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Minigame.Account.V1;
using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Match.V1;
using Minigame.Room.V1;
using UnityEngine;


namespace SuperQQ.Network
{
    /// <summary>
    /// 网络管理器（持久层单例，DontDestroyOnLoad）。
    /// 与后端网关通信：WebSocket 二进制帧 + ClientEnvelope/ServerEnvelope 封装。
    ///
    /// 使用方式：
    ///   NetworkManager.Instance.LocalPlayerId = "p1001";
    ///   NetworkManager.Instance.Connect("ws://127.0.0.1:8080/ws");
    ///   NetworkManager.Instance.Register<RoomSnapshot>(OnRoomSnapshot);
    ///   NetworkManager.Instance.Send(new HeartbeatRequest { ClientTimeMs = Now() });
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        #region 会话信息（登录/进房后由业务层写入，自动填充到各请求）

        /// <summary>本地玩家ID（登录成功后设置）</summary>
        public string LocalPlayerId = "";
        /// <summary>当前房间ID（进房后设置）</summary>
        public string RoomId = "";
        /// <summary>进房成功时的完整房间数据（含已在房间的玩家列表），跨场景传递给关卡使用</summary>
        public global::Minigame.Room.V1.Room JoinedRoom;
        /// <summary>登录令牌</summary>
        public string Token = "";
        /// <summary>网关会话ID（连接成功后由网关分配，若协议需要）</summary>
        public string SessionId = "";
        /// <summary>网关ID</summary>
        public string GatewayId = "";

        #endregion

        #region 消息路由表（类型 -> Envelope oneof 字段 的双向映射）

        // 发送侧：消息类型 -> 装入 ClientEnvelope 的方式
        private static readonly Dictionary<Type, Action<ClientEnvelope, IMessage>> sendRouters = new()
        {
            { typeof(HeartbeatRequest),         (e, m) => e.Heartbeat = (HeartbeatRequest)m },
            { typeof(LoginRequest),             (e, m) => e.Login = (LoginRequest)m },
            { typeof(LogoutRequest),            (e, m) => e.Logout = (LogoutRequest)m },
            { typeof(GetPlayerRequest),         (e, m) => e.GetPlayer = (GetPlayerRequest)m },
            { typeof(StartMatchRequest),        (e, m) => e.StartMatch = (StartMatchRequest)m },
            { typeof(CancelMatchRequest),       (e, m) => e.CancelMatch = (CancelMatchRequest)m },
            { typeof(GetTicketRequest),         (e, m) => e.GetTicket = (GetTicketRequest)m },
            { typeof(JoinRoomRequest),          (e, m) => e.JoinRoom = (JoinRoomRequest)m },
            { typeof(LeaveRoomRequest),         (e, m) => e.LeaveRoom = (LeaveRoomRequest)m },
            { typeof(CreateRoomRequest),        (e, m) => e.CreateRoom = (CreateRoomRequest)m },
            { typeof(SubmitPlayerInputRequest), (e, m) => e.SubmitPlayerInput = (SubmitPlayerInputRequest)m },
            { typeof(SyncPlayerStateRequest),   (e, m) => e.SyncPlayerState = (SyncPlayerStateRequest)m },
            { typeof(SetReadyRequest),          (e, m) => e.SetReady = (SetReadyRequest)m },
            { typeof(StartGameRequest),         (e, m) => e.StartGame = (StartGameRequest)m },
            { typeof(GetRoomRequest),           (e, m) => e.GetRoom = (GetRoomRequest)m },
            { typeof(ItemClaimIntent),          (e, m) => e.ItemClaimIntent = (ItemClaimIntent)m },
            { typeof(ItemClaimConfirm),         (e, m) => e.ItemClaimConfirm = (ItemClaimConfirm)m },
            { typeof(ItemPlaceState),           (e, m) => e.ItemPlaceState = (ItemPlaceState)m },
            { typeof(ItemPlaceConfirm),         (e, m) => e.ItemPlaceConfirm = (ItemPlaceConfirm)m },
            { typeof(PlayerOutReport),          (e, m) => e.PlayerOutReport = (PlayerOutReport)m },
            { typeof(RoundScoreReport),         (e, m) => e.RoundScoreReport = (RoundScoreReport)m },
            { typeof(PlayerEvent),              (e, m) => e.PlayerEvent = (PlayerEvent)m },
            { typeof(PickupClaim),              (e, m) => e.PickupClaim = (PickupClaim)m },
            { typeof(ItemStateEvent),           (e, m) => e.ItemStateEvent = (ItemStateEvent)m },
            { typeof(GmSetPhase),               (e, m) => e.GmSetPhase = (GmSetPhase)m },
            { typeof(TrapKillEvent),            (e, m) => e.TrapKillEvent = (TrapKillEvent)m },
            { typeof(ItemDemolishConfirm),      (e, m) => e.ItemDemolishConfirm = (ItemDemolishConfirm)m },
        };

        // 接收侧：消息类型 -> 从 ServerEnvelope 取出的方式
        private static readonly Dictionary<Type, Func<ServerEnvelope, IMessage>> recvRouters = new()
        {
            { typeof(HeartbeatResponse),         e => e.Heartbeat },
            { typeof(LoginResponse),             e => e.Login },
            { typeof(LogoutResponse),            e => e.Logout },
            { typeof(GetPlayerResponse),         e => e.GetPlayer },
            { typeof(StartMatchResponse),        e => e.StartMatch },
            { typeof(CancelMatchResponse),       e => e.CancelMatch },
            { typeof(GetTicketResponse),         e => e.GetTicket },
            { typeof(MatchAssignment),           e => e.MatchAssignment },
            { typeof(JoinRoomResponse),          e => e.JoinRoom },
            { typeof(LeaveRoomResponse),         e => e.LeaveRoom },
            { typeof(CreateRoomResponse),        e => e.CreateRoom },
            { typeof(RoomSnapshot),              e => e.RoomSnapshot },
            { typeof(global::Minigame.Room.V1.Settlement), e => e.Settlement },
            { typeof(SubmitPlayerInputResponse), e => e.SubmitPlayerInput },
            { typeof(SyncPlayerStateResponse),   e => e.SyncPlayerState },
            { typeof(SetReadyResponse),          e => e.SetReady },
            { typeof(StartGameResponse),         e => e.StartGame },
            { typeof(RoomUpdated),               e => e.RoomUpdated },
            { typeof(GetRoomResponse),           e => e.GetRoom },
            { typeof(ItemOfferList),             e => e.ItemOfferList },
            { typeof(ItemClaimIntentBroadcast),  e => e.ItemClaimIntentBroadcast },
            { typeof(ItemClaimResult),           e => e.ItemClaimResult },
            { typeof(GamePhaseSync),             e => e.GamePhaseSync },
            { typeof(ItemPlaceStateBroadcast),   e => e.ItemPlaceStateBroadcast },
            { typeof(ItemPlaceResult),           e => e.ItemPlaceResult },
            { typeof(PlayerOutBroadcast),        e => e.PlayerOutBroadcast },
            { typeof(PlayerEventBroadcast),      e => e.PlayerEventBroadcast },
            { typeof(PickupClaimBroadcast),      e => e.PickupClaimBroadcast },
            { typeof(ItemStateEventBroadcast),   e => e.ItemStateEventBroadcast },
            { typeof(TrapKillBroadcast),         e => e.TrapKillBroadcast },
            { typeof(ItemDemolishResult),        e => e.ItemDemolishResult },
            { typeof(ErrorResponse),             e => e.Error },
        };

        #endregion

        #region 消息分发（Register/Unregister，回调均在主线程执行）

        private readonly Dictionary<Type, Action<IMessage>> handlers = new();

        /// <summary>注册某类消息的处理器，重复注册会覆盖</summary>
        public void Register<T>(Action<T> handler) where T : IMessage
        {
            handlers[typeof(T)] = m => handler((T)m);
        }

        public void Unregister<T>() where T : IMessage => handlers.Remove(typeof(T));

        private void Dispatch(ServerEnvelope envelope)
        {
            // 自动捕获网关在回包 trace 中分配的会话信息（首包到达时生效）
            if (envelope.Trace != null)
            {
                if (string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(envelope.Trace.SessionId))
                    SessionId = envelope.Trace.SessionId;
                if (string.IsNullOrEmpty(GatewayId) && !string.IsNullOrEmpty(envelope.Trace.GatewayId))
                    GatewayId = envelope.Trace.GatewayId;
            }

            foreach (var pair in recvRouters)
            {
                IMessage msg = pair.Value(envelope);
                if (msg == null) continue;

                // 高频消息（房间快照/心跳回包/状态上报应答）降频打印，其余全量打印
                if (msg is RoomSnapshot or HeartbeatResponse or SyncPlayerStateResponse)
                {
                    _recvHighFreqCount++;
                    if (_recvHighFreqCount % 100 == 1)
                        Debug.Log($"[NetWork] 收到 {pair.Key.Name}（每100条打印一次）seq={envelope.Seq}");
                }
                else
                {
                    Debug.Log($"[NetWork] 收到 {pair.Key.Name} seq={envelope.Seq}\n{msg}");
                }

                // 未注册处理器属正常情况（如心跳回包无人订阅），静默忽略，不打警告
                if (handlers.TryGetValue(pair.Key, out var handler))
                {
                    try { handler(msg); }
                    catch (Exception e) { Debug.LogError($"[NetWork] 处理 {pair.Key.Name} 异常: {e}"); }
                }
                return;
            }
            Debug.LogWarning("[NetWork] 收到空的 ServerEnvelope");
        }

        #endregion

        #region 连接管理（单例 / WebSocket / 心跳）

        public static NetworkManager Instance { get; private set; }

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        /// <summary>连接状态变化事件（主线程触发），true=已连接，false=失败或断开</summary>
        public event Action<bool> OnConnectionChanged;

        [Header("服务器地址")]
        [SerializeField] private string serverUrl = "wss://12931fc.r29.cpolar.top/ws";

        [Header("心跳间隔（秒）")]
        [SerializeField] private float heartbeatInterval = 5f;

        [Header("断线重连")]
        [Tooltip("断线后自动重连的总时长上限（秒），与服务器房间保留期一致；仅对已登录玩家生效")]
        [SerializeField] private float reconnectMaxDuration = 120f;
        [Tooltip("重连尝试的最大间隔（秒），指数退避到此值封顶")]
        [SerializeField] private float reconnectMaxInterval = 15f;

        // ---- 断线重连状态 ----
        private string _lastUrl;                       // 最近一次 Connect 的地址
        private LoginRequest _lastLoginRequest;        // Send 拦截缓存的登录请求（重连后自动恢复会话）
        private bool _bIntentionalDisconnect;          // 主动断开（Disconnect/退出）不触发重连
        private bool _reconnecting;
        private int _reconnectAttempt;
        private float _nextReconnectAt;                // 下一次重连尝试的本地时刻
        private float _reconnectDeadline;              // 重连总时长截止时刻
        private float _reconnectStepDeadline;          // 等待登录/进房回包的超时时刻（0=未在等待）
        private bool _bReconnectHandlersRegistered;    // 重连流程是否占用了登录/进房回包 handler

        /// <summary>重连并恢复会话（重新登录+重新进房）成功时触发</summary>
        public event Action OnReconnected;
        /// <summary>重连彻底失败（超时/房间解散）时触发，参数为原因</summary>
        public event Action<string> OnReconnectFailed;
        /// <summary>是否处于断线重连流程中</summary>
        public bool BIsReconnecting => _reconnecting;

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private ulong sendSeq;
        private int _recvHighFreqCount;

        // 网络线程 -> 主线程：收到完整帧后入队（null 表示连接断开事件）
        private readonly ConcurrentQueue<ServerEnvelope> recvQueue = new();
        // 发送队列：任何线程只入队，主线程统一发送
        private readonly ConcurrentQueue<byte[]> sendQueue = new();

        // 连接结果内部事件（null=连接成功回调占位）
        private readonly ConcurrentQueue<bool> connEvents = new();

        private float heartbeatTimer;
        // 后台心跳线程：场景加载卡顿主线程时，Update 停转但心跳必须继续，
        // 否则服务器心跳超时断连。心跳直接用 socket.SendAsync 发送（不经主线程队列）。
        private Task _heartbeatTask;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>使用 Inspector 配置的服务器地址连接网关</summary>
        public void Connect() => Connect(serverUrl);

        /// <summary>连接网关（异步，结果通过 OnConnectionChanged 通知）</summary>
        public void Connect(string url)
        {
            if (IsConnected) return;
            CloseSocket();

            _lastUrl = url;
            _bIntentionalDisconnect = false;
            cts = new CancellationTokenSource();
            socket = new ClientWebSocket();
            // ngrok 免费版对非浏览器请求返回告警页导致握手失败，移动端必现；带此头跳过
            socket.Options.SetRequestHeader("ngrok-skip-browser-warning", "true");

            Debug.Log($"[NetWork] 正在连接服务器: {url}");

            Task.Run(async () =>
            {
                try
                {
                    await socket.ConnectAsync(new Uri(url), cts.Token);
                    Debug.Log("[NetWork] WebSocket 握手成功，连接已建立");
                    connEvents.Enqueue(true);
                    StartHeartbeatLoop();
                    await ReceiveLoop();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NetWork] 连接/接收异常: {e}");
                }
                connEvents.Enqueue(false);
            });
        }

        /// <summary>发送一条业务消息（自动装入 ClientEnvelope，线程安全）</summary>
        public void Send(IMessage payload)
        {
            if (payload == null) return;
            if (!sendRouters.TryGetValue(payload.GetType(), out var setField))
            {
                Debug.LogWarning($"[NetWork] 不支持发送的消息类型: {payload.GetType().Name}");
                return;
            }

            // 缓存登录请求：断线重连后用它自动恢复会话（覆盖两种登录入口，业务层无需关心）
            if (payload is LoginRequest loginReq)
            {
                _lastLoginRequest = loginReq.Clone();
            }

            var envelope = new ClientEnvelope { Seq = ++sendSeq, Trace = BuildTrace() };
            setField(envelope, payload);
            sendQueue.Enqueue(envelope.ToByteArray());

            // 高频消息（状态上报/心跳）降频打印，其余全量打印
            if (payload is SyncPlayerStateRequest or HeartbeatRequest)
            {
                if (sendSeq % 100 == 0)
                    Debug.Log($"[NetWork] 发送 {payload.GetType().Name}（每100条打印一次）seq={sendSeq}");
            }
            else
            {
                Debug.Log($"[NetWork] 发送 {payload.GetType().Name} seq={sendSeq}\n{payload}");
            }
        }

        /// <summary>主动断开连接（不触发自动重连）</summary>
        public void Disconnect()
        {
            _bIntentionalDisconnect = true;
            StopReconnect();
            CloseSocket();
        }

        /// <summary>关闭底层 socket（内部 teardown，不改动重连状态）</summary>
        private void CloseSocket()
        {
            try { cts?.Cancel(); } catch { }
            try
            {
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client quit", CancellationToken.None).Wait(500);
                }
            }
            catch { }
            try { socket?.Dispose(); } catch { }
            socket = null;
            cts = null;
            Debug.Log("[NetWork] 连接已断开");
        }

        /// <summary>主线程：连接事件 → 分发消息 → 冲刷发送队列 → 心跳</summary>
        private void Update()
        {
            while (connEvents.TryDequeue(out bool connected))
            {
                Debug.Log(connected
                    ? "[NetWork] 连接状态: 已连接"
                    : "[NetWork] 连接状态: 已断开/连接失败");
                if (connected)
                {
                    OnConnectionChanged(true);
                    HandleReconnectedSocket();
                }
                else
                {
                    OnConnectionChanged(false);
                    HandleUnexpectedDisconnect();
                }
            }

            TickReconnect();

            while (recvQueue.TryDequeue(out var envelope))
            {
                if (envelope != null) Dispatch(envelope);
            }

            FlushSendQueue();
            UpdateHeartbeat();
        }

        private void FlushSendQueue()
        {
            if (!IsConnected || sendQueue.IsEmpty) return;
            while (sendQueue.TryDequeue(out var packet))
            {
                _ = socket.SendAsync(new ArraySegment<byte>(packet),
                    WebSocketMessageType.Binary, true, cts.Token);
            }
        }

        /// <summary>
        /// 后台心跳循环（连接成功后启动）：独立于主线程 Update，
        /// 场景加载卡顿期间也能按时发送心跳，避免服务器心跳超时断连。
        /// 直接调用 socket.SendAsync（WebSocket 单帧写入是线程安全的）。
        /// </summary>
        private void StartHeartbeatLoop()
        {
            CancellationToken token = cts.Token;
            float intervalSec = heartbeatInterval;
            ClientWebSocket hbSocket = socket;

            _heartbeatTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested
                       && hbSocket != null
                       && hbSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(intervalSec), token);
                        if (token.IsCancellationRequested || hbSocket.State != WebSocketState.Open) break;

                        var envelope = new ClientEnvelope
                        {
                            Seq = 0,
                            Heartbeat = new HeartbeatRequest { ClientTimeMs = NowMs() }
                        };
                        byte[] packet = envelope.ToByteArray();
                        await hbSocket.SendAsync(new ArraySegment<byte>(packet),
                            WebSocketMessageType.Binary, true, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[NetWork] 后台心跳发送失败: {e.Message}");
                        break;
                    }
                }
            }, token);
        }

        // 主线程心跳已废弃（改由 StartHeartbeatLoop 后台线程发送，场景加载时不停发）。
        // 保留空实现以兼容 Update 中的调用点。
        private void UpdateHeartbeat()
        {
            if (!IsConnected) return;
            heartbeatTimer += Time.deltaTime;
            if (heartbeatTimer >= heartbeatInterval)
            {
                heartbeatTimer = 0f;
                // 心跳已由后台线程负责，此处不再发送
            }
        }

        /// <summary>接收循环：组帧（WebSocket 可能分片）后解析入队</summary>
        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[8192];
            var ms = new System.IO.MemoryStream();

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                ms.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                byte[] frame = ms.ToArray();
                ms.SetLength(0);

                try { recvQueue.Enqueue(ServerEnvelope.Parser.ParseFrom(frame)); }
                catch (Exception e) { Debug.LogWarning($"[NetWork] 解析帧失败: {e.Message}"); }
            }
        }

        // ==================== 断线重连 ====================

        /// <summary>
        /// 连接意外断开（含重连尝试失败）后的处理：仅对已登录玩家启动/延续自动重连。
        /// 每次失败按指数退避安排下一次尝试（1s/2s/4s…封顶 reconnectMaxInterval），
        /// 总时长超过 reconnectMaxDuration（与服务器房间保留期一致）则判失败。
        /// </summary>
        private void HandleUnexpectedDisconnect()
        {
            if (_bIntentionalDisconnect
                || string.IsNullOrEmpty(LocalPlayerId)
                || _lastLoginRequest == null
                || string.IsNullOrEmpty(_lastUrl))
            {
                return;
            }

            if (!_reconnecting)
            {
                _reconnecting = true;
                _reconnectAttempt = 0;
                _reconnectDeadline = Time.realtimeSinceStartup + reconnectMaxDuration;
                Debug.Log($"[NetWork] 检测到断线，开始自动重连（上限 {reconnectMaxDuration:F0}s）...");
            }

            float delay = Mathf.Min(Mathf.Pow(2f, _reconnectAttempt), reconnectMaxInterval);
            _reconnectAttempt++;
            _nextReconnectAt = Time.realtimeSinceStartup + delay;
            _reconnectStepDeadline = 0f;
            Debug.Log($"[NetWork] 第 {_reconnectAttempt} 次重连将在 {delay:F0}s 后发起");
        }

        /// <summary>重连状态机推进：到点发起 Connect；等待会话回包超时则断开重试</summary>
        private void TickReconnect()
        {
            if (!_reconnecting)
            {
                return;
            }

            if (Time.realtimeSinceStartup > _reconnectDeadline)
            {
                FailReconnect("超过最大重连时长");
                return;
            }

            // 已连上但在等登录/进房回包：超时则断开，由 false 事件安排下一次尝试
            if (IsConnected)
            {
                if (_reconnectStepDeadline > 0f && Time.realtimeSinceStartup > _reconnectStepDeadline)
                {
                    Debug.LogWarning("[NetWork] 重连后恢复会话超时，断开重试");
                    _reconnectStepDeadline = 0f;
                    CloseSocket();
                }
                return;
            }

            if (Time.realtimeSinceStartup >= _nextReconnectAt)
            {
                _nextReconnectAt = float.MaxValue; // 防同帧/失败事件前重复触发
                Debug.Log($"[NetWork] 发起第 {_reconnectAttempt} 次重连: {_lastUrl}");
                Connect(_lastUrl);
            }
        }

        /// <summary>重连中的 socket 握手成功：重新登录恢复会话；不在重连流程则什么都不做</summary>
        private void HandleReconnectedSocket()
        {
            if (!_reconnecting)
            {
                return;
            }

            // 房间为空说明断线时不在对局中（如大厅）：socket 恢复即可，交给场景内控制器继续
            if (string.IsNullOrEmpty(RoomId))
            {
                FinishReconnect();
                return;
            }

            // 重新登录 → 重新进房。此流程只在房间/对局场景发生（Home 场景的
            // LoginController 不存在于此），临时占用两个回包 handler 不会覆盖他人。
            Register<LoginResponse>(HandleReconnectLogin);
            Register<JoinRoomResponse>(HandleReconnectJoin);
            _bReconnectHandlersRegistered = true;
            _reconnectStepDeadline = Time.realtimeSinceStartup + 8f;
            Debug.Log("[NetWork] 重连握手成功，正在恢复登录会话...");
            Send(_lastLoginRequest);
        }

        private void HandleReconnectLogin(LoginResponse resp)
        {
            if (!_reconnecting)
            {
                return;
            }
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok || resp.Player == null)
            {
                Debug.LogWarning($"[NetWork] 重连登录失败: {resp.Status?.Message}，断开重试");
                CloseSocket(); // false 事件驱动下一次尝试
                return;
            }

            Token = resp.Token;
            LocalPlayerId = resp.Player.PlayerId;

            _reconnectStepDeadline = Time.realtimeSinceStartup + 8f;
            Debug.Log($"[NetWork] 重连登录成功，重新进房: room={RoomId}");
            Send(new JoinRoomRequest
            {
                RoomId = RoomId,
                PlayerId = LocalPlayerId,
                GatewayId = GatewayId,
                SessionId = SessionId
            });
        }

        private void HandleReconnectJoin(JoinRoomResponse resp)
        {
            if (!_reconnecting)
            {
                return;
            }
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                FailReconnect($"房间已不存在或无法加入（{resp.Status?.Message}）");
                return;
            }

            JoinedRoom = resp.Room;
            FinishReconnect();
        }

        private void FinishReconnect()
        {
            StopReconnect();
            Debug.Log("[NetWork] 断线重连完成，会话已恢复（后续快照/阶段消息自动同步状态）");
            OnReconnected?.Invoke();
        }

        private void FailReconnect(string reason)
        {
            StopReconnect();
            Debug.LogWarning($"[NetWork] 断线重连失败: {reason}");
            OnReconnectFailed?.Invoke(reason);
        }

        private void StopReconnect()
        {
            if (!_reconnecting)
            {
                return;
            }
            _reconnecting = false;
            _reconnectStepDeadline = 0f;
            if (_bReconnectHandlersRegistered)
            {
                _bReconnectHandlersRegistered = false;
                Unregister<LoginResponse>();
                Unregister<JoinRoomResponse>();
            }
        }

        /// <summary>构建追踪上下文，随每条消息自动携带</summary>
        private TraceContext BuildTrace()
        {
            return new TraceContext
            {
                SessionId = SessionId,
                PlayerId = LocalPlayerId,
                GatewayId = GatewayId,
                RequestAtMs = NowMs()
            };
        }

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ---- 服务器时间估算（倒计时对时锚点）----
        // 收到带 server_time_ms 的消息时记录"服务器时刻 + 本地接收时刻"，
        // 之后估算服务器当前时间 = 记录的服务器时刻 + 本地经过的时间。
        // 误差约为单向网络延迟（几十 ms），两端一致。
        private static long _serverTimeAnchor;
        private static float _serverTimeAnchorLocalTime;
        private static bool _serverTimeSynced;

        /// <summary>记录一次服务器时间锚点（收到含 server_time_ms 的消息时调用）</summary>
        public static void SyncServerTime(long serverTimeMs)
        {
            if (serverTimeMs <= 0) return;
            _serverTimeAnchor = serverTimeMs;
            _serverTimeAnchorLocalTime = UnityEngine.Time.realtimeSinceStartup;
            _serverTimeSynced = true;
        }

        /// <summary>估算当前服务器时间（毫秒）；未同步过时回退为本地 UTC 时间</summary>
        public static long EstimatedServerNowMs()
        {
            if (!_serverTimeSynced) return NowMs();
            return _serverTimeAnchor + (long)((UnityEngine.Time.realtimeSinceStartup - _serverTimeAnchorLocalTime) * 1000f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Disconnect();
        }

        private void OnApplicationQuit() => Disconnect();

        #endregion
    }
}

using Minigame.Room.V1;
using SuperQQ.GameFlow;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 联机流程门控：联机模式下阻止 GamePhaseManager 自动启动游戏流程，
    /// 等服务器推送 ItemOfferList（选择阶段道具列表）时才启动流程，
    /// 实现"选择阶段由服务器触发"。
    ///
    /// 工作方式：
    ///   场景加载后自动创建（AfterSceneLoad，早于各 Start）；
    ///   离线（未连接/未进房）时自动销毁，不影响单机流程。
    ///   首轮 ItemOfferList 到达 → 缓存并 StartGameFlow；
    ///   后续轮次的消息由 PropSelectionDirector 自己注册处理（注册会覆盖本门控）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetGameFlowGate : MonoBehaviour
    {
        private static ItemOfferList _pendingOffers;

        /// <summary>取出并清空缓存的首轮道具列表（由 PropSelectionDirector 在进入阶段时消费）</summary>
        public static ItemOfferList ConsumePendingOffers()
        {
            ItemOfferList offers = _pendingOffers;
            _pendingOffers = null;
            return offers;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindFirstObjectByType<NetGameFlowGate>() != null) return;
            if (NetworkManager.Instance == null) return;

            var go = new GameObject(nameof(NetGameFlowGate));
            go.AddComponent<NetGameFlowGate>();
        }

        private bool _initialized;

        private void Awake()
        {
            // 跨场景存活：注册一次永久有效，避免场景切换导致注册丢失/时序竞争。
            // 离线时保留物体但不做任何注册（Update 中检测到进房后再初始化）。
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryInit();
            }
            // SuppressLocalTransitions 必须在 GamePhaseManager 就绪后设置（它在 Level1 场景，
            // 可能晚于本 Gate 初始化）。消息注册后持续重试，直到设置成功，否则本地条件会切阶段。
            else if (!_flowSuppressed)
            {
                TrySuppressFlow();
            }
        }

        private bool _flowSuppressed;

        private void TryInit()
        {
            NetworkManager net = NetworkManager.Instance;

            // 未连接/未进房：等待（联机模式在进房后才接管）
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId))
            {
                return;
            }

            // 关键：消息注册不依赖 GamePhaseManager 就绪——发牌/阶段消息可能在 Level1
            // 加载（GamePhaseManager 创建）之前到达，必须先注册才能缓存。
            _initialized = true;
            net.Register<ItemOfferList>(OnServerOffers);
            net.Register<GamePhaseSync>(OnGamePhaseSync);
            net.Register<PlayerOutBroadcast>(OnPlayerOut);
            net.Register<global::Minigame.Room.V1.Settlement>(OnSettlement);
            Debug.Log("[NetWork] 联机模式：游戏流程与阶段切换由服务器驱动（ItemOfferList / GamePhaseSync）");

            TrySuppressFlow();
        }

        /// <summary>GamePhaseManager 就绪后设置本地转移屏蔽（持续重试直到成功）</summary>
        private void TrySuppressFlow()
        {
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == null)
            {
                return;
            }

            _flowSuppressed = true;
            flow.SetStartFlowOnStart(false);
            flow.SuppressLocalTransitions = true;
            Debug.Log("[NetWork] 已屏蔽本地阶段切换（阶段切换由服务器 GamePhaseSync 统一驱动）");
        }

        /// <summary>服务器出局裁决：记录名次并广播日志（结算展示数据以服务器为准）</summary>
        private void OnPlayerOut(PlayerOutBroadcast msg)
        {
            if (msg.OutType == PlayerOutType.Finished)
            {
                Debug.Log($"[NetWork] 玩家 {msg.PlayerId} 通关，名次: 第{msg.FinishRank}名");
            }
            else
            {
                Debug.Log($"[NetWork] 玩家 {msg.PlayerId} 出局（死亡）");
            }
        }

        /// <summary>服务器结算结果：权威排名/胜负（本地展示数据与服务器核对用）</summary>
        private void OnSettlement(global::Minigame.Room.V1.Settlement settlement)
        {
            Debug.Log($"[NetWork] 收到服务器结算: 胜者={settlement.WinnerPlayerId} 玩家数={settlement.Results.Count}");
            foreach (SettlementPlayerResult r in settlement.Results)
            {
                Debug.Log($"[NetWork]   第{r.Rank}名 {r.PlayerId} mmrΔ={r.MmrDelta} coinΔ={r.CoinDelta}");
            }
        }

        /// <summary>
        /// 进入单轮结算阶段前：本地先结算本轮得分（联机模式下 PlayingPhase 的本地转移被屏蔽，
        /// SettleCurrentRound 不会自动触发），随后把本轮得分上报服务器汇总排名。
        /// </summary>
        private void SettleRoundLocallyAndReport(int round)
        {
            NetworkManager net = NetworkManager.Instance;
            SuperQQ.Score.PlayerScoreManager scoreManager = SuperQQ.Score.PlayerScoreManager.Instance;
            if (net == null || scoreManager == null) return;

            scoreManager.SettleCurrentRound();

            // 取本地玩家本轮得分上报（计分记录以玩家名为键）
            string localName = ResolveLocalPlayerName();
            if (string.IsNullOrEmpty(localName)) return;

            int roundScore = 0;
            SuperQQ.Score.RoundScoreData data = scoreManager.GetPlayerRoundScore(localName, scoreManager.CurrentRoundIndex);
            if (data != null)
            {
                roundScore = data.RoundTotal;
            }

            net.Send(new RoundScoreReport
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                Round = round,
                Score = roundScore
            });
            Debug.Log($"[NetWork] 已上报本轮得分: round={round} score={roundScore}");
        }

        /// <summary>
        /// 对局开始时把房间内其他玩家注册档案并生成化身。
        /// 大厅流程（Hall→Room→Level1）下 NetDebugBootstrap 不参与，远程玩家档案需在此补注册。
        /// 数据源优先取最新房间快照（含后加入的玩家），JoinedRoom 兜底。
        /// </summary>
        public static void EnsureRemotePlayersReady()
        {
            NetworkManager net = NetworkManager.Instance;
            SuperQQ.Player.PlayerSessionManager session = SuperQQ.Player.PlayerSessionManager.Instance;
            if (net == null || session == null)
            {
                return;
            }

            // 选择阶段可能早于 LocalPlayerNetSetup.Update 启动；生成图标前必须先写入本地 playerId，
            // 否则 PlayerController.IdentityKey 会从场景名后变成服务器 ID，被补生成逻辑误判为新玩家。
            LocalPlayerNetSetup.TrySetupLocalPlayer();

            // 数据源：最新快照优先（含 RoomUpdated 后加入的玩家），JoinedRoom 兜底
            Minigame.Room.V1.RoomSnapshot snapshot = null;
            RoomSnapshotReceiver receiver = UnityEngine.Object.FindFirstObjectByType<RoomSnapshotReceiver>();
            if (receiver != null) snapshot = receiver.LatestSnapshot;

            // 选定玩家列表（快照优先），颜色/座位统一按"列表下标（=进房顺序）"计算，
            // 两端必然一致；color_index 后端若已正确分配则与下标等价，此处不依赖其正确性。
            System.Collections.Generic.IList<Minigame.Room.V1.RoomPlayerState> players = null;
            if (snapshot != null && snapshot.Players.Count > 0)
            {
                players = snapshot.Players;
            }
            else if (net.JoinedRoom != null && net.JoinedRoom.Players.Count > 0)
            {
                players = net.JoinedRoom.Players;
            }

            if (players == null)
            {
                return;
            }

            int registered = 0;
            for (int i = 0; i < players.Count; i++)
            {
                Minigame.Room.V1.RoomPlayerState p = players[i];
                registered += TryRegisterRemote(net, session, p.Player?.PlayerId, p.Player?.Nickname, i);
            }

            // 本地玩家也按同一下标规则着色（覆盖场景 prefab 的默认色），保证两端颜色统一
            ApplyLocalPlayerColor(net, players);

            if (registered > 0)
            {
                SuperQQ.Player.LevelPlayerRegistry.Instance?.SpawnMissingPlayerAvatars();
                Debug.Log($"[NetWork] 对局开始：注册 {registered} 名远程玩家并生成化身");
            }
        }

        /// <summary>按房间列表下标给本地玩家着色（两端对同一 playerId 算出相同颜色）</summary>
        private static void ApplyLocalPlayerColor(NetworkManager net,
            System.Collections.Generic.IList<Minigame.Room.V1.RoomPlayerState> players)
        {
            SuperQQ.Player.LevelPlayerRegistry registry = SuperQQ.Player.LevelPlayerRegistry.Instance;
            if (registry == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Player?.PlayerId != net.LocalPlayerId) continue;

                System.Collections.Generic.IReadOnlyList<SuperQQ.Player.PlayerController> all = registry.Players;
                for (int j = 0; j < all.Count; j++)
                {
                    if (all[j] != null && all[j].BIsLocal)
                    {
                        SuperQQ.Player.PlayerProfile profile = all[j].BuildProfile();
                        profile.PlayerColor = PlayerColorPalette.Get(i);
                        all[j].ApplyProfile(profile);
                        Debug.Log($"[NetWork] 本地玩家着色: 下标={i} 颜色={PlayerColorPalette.Get(i)}");
                    }
                }
                return;
            }
        }

        private static int TryRegisterRemote(NetworkManager net, SuperQQ.Player.PlayerSessionManager session,
            string playerId, string nickname, int colorIndex)
        {
            if (string.IsNullOrEmpty(playerId) || playerId == net.LocalPlayerId) return 0;
            if (session.HasPlayerByIdentity(playerId)) return 0;

            session.RegisterProfile(new SuperQQ.Player.PlayerProfile
            {
                PlayerId = playerId,
                IsLocal = false,
                PlayerName = string.IsNullOrEmpty(nickname) ? $"Remote_{playerId}" : nickname,
                PlayerColor = PlayerColorPalette.Get(colorIndex)
            });
            return 1;
        }

        private static string ResolveLocalPlayerName()
        {
            SuperQQ.Player.LevelPlayerRegistry registry = SuperQQ.Player.LevelPlayerRegistry.Instance;
            if (registry == null) return null;

            System.Collections.Generic.IReadOnlyList<SuperQQ.Player.PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal)
                {
                    return players[i].PlayerName;
                }
            }
            return null;
        }

        /// <summary>服务器下发的阶段切换指令：按阶段类型进入对应阶段</summary>
        /// <summary>当前阶段的结束时刻（服务器毫秒）；0 表示无倒计时。供各阶段倒计时 UI 读取</summary>
        public static long CurrentPhaseEndTimeMs { get; private set; }

        private void OnGamePhaseSync(GamePhaseSync sync)
        {
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == null) return;

            // 服务器时间对时 + 记录本阶段结束时刻（倒计时锚点，两端一致）
            NetworkManager.SyncServerTime(sync.ServerTimeMs);
            CurrentPhaseEndTimeMs = sync.PhaseEndTimeMs;

            // 选择阶段外由门控负责缓存发牌消息（选择阶段内 Director 会覆盖注册并消费缓存），
            // 防止 ItemOfferList 早于 GamePhaseSync 处理完成而丢失
            NetworkManager.Instance?.Register<ItemOfferList>(OnServerOffers);

            // 流程尚未启动时（首条阶段消息可能早于 ItemOfferList 的处理），先启动流程
            if (!flow.BFlowStarted)
            {
                Debug.Log($"[NetWork] 服务器触发游戏流程: phase={sync.Phase} round={sync.Round}");
                EnsureRemotePlayersReady();
                flow.StartGameFlow();
            }

            string reason = $"服务器阶段切换 round={sync.Round}";
            switch (sync.Phase)
            {
                case GamePhaseKind.PropSelection:
                    // 选择阶段：道具列表由 ItemOfferList 下发，此处只负责切阶段
                    flow.EnterPhaseByType<PropSelectionPhase>(reason);
                    break;
                case GamePhaseKind.PropPlacement:
                    flow.EnterPhaseByType<PropPlacementPhase>(reason);
                    break;
                case GamePhaseKind.Playing:
                    flow.EnterPhaseByType<PlayingPhase>(reason);
                    break;
                case GamePhaseKind.RoundSettlement:
                    SettleRoundLocallyAndReport(sync.Round);
                    flow.EnterPhaseByType<RoundSettlementPhase>(reason);
                    break;
                case GamePhaseKind.FinalSettlement:
                    flow.EnterPhaseByType<FinalSettlementPhase>(reason);
                    break;
                default:
                    Debug.LogWarning($"[NetWork] 未知的阶段切换: {sync.Phase}");
                    break;
            }
        }

        private void OnServerOffers(ItemOfferList list)
        {
            // 始终缓存最新发牌，供 PropSelectionDirector 进入阶段时消费
            _pendingOffers = list;
            Debug.Log($"[NetWork] Gate 缓存发牌: round={list.Round} 道具数={list.Offers.Count} flowStarted={GamePhaseManager.Instance?.BFlowStarted}");

            // 发牌到达即对局开始：确保远程玩家已注册（图标/化身依赖档案）
            EnsureRemotePlayersReady();

            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow != null && !flow.BFlowStarted)
            {
                Debug.Log($"[NetWork] 服务器触发游戏流程: round={list.Round} 道具数={list.Offers.Count}");
                flow.StartGameFlow();
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Unregister<ItemOfferList>();
                NetworkManager.Instance.Unregister<GamePhaseSync>();
                NetworkManager.Instance.Unregister<PlayerOutBroadcast>();
                NetworkManager.Instance.Unregister<global::Minigame.Room.V1.Settlement>();
            }
        }
    }
}

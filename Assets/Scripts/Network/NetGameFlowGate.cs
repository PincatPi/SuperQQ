using System.Collections.Generic;
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
            var gate = go.AddComponent<NetGameFlowGate>();

            // AfterSceneLoad 早于场景中各 Start：立即注册消息并屏蔽本地转移，
            // 否则关卡场景的 GamePhaseManager.Start 会先自动启动本地流程
            // （联机下出现"本地流程抢跑：0 候选进选择阶段并秒切"的时序竞争）
            gate.TryInit();
            gate.TrySuppressLocalTransitions();
        }

        private bool _initialized;

        private void Awake()
        {
            // 跨场景存活：注册一次永久有效，避免场景切换导致注册丢失/时序竞争。
            // 离线时保留物体但不做任何注册（Update 中检测到进房后再初始化）。
            DontDestroyOnLoad(gameObject);
            // 本对象跨场景存活：场景加载完成（早于场景中各 Start）立即屏蔽本地转移，
            // 防止关卡场景的 GamePhaseManager.Start 自动抢跑本地流程（联机时序竞争）。
            // 注意不能只在 AutoSpawn 里做：门控在房间场景已创建，进入关卡时 AutoSpawn 会提前返回。
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TryInit();
            TrySuppressLocalTransitions();
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryInit();
            }
            // 屏蔽本地阶段转移与消息注册解耦，持续重试直到 GamePhaseManager 就绪。
            // 否则在 Room 场景初始化时 Level1 未加载、GamePhaseManager 为 null，
            // 屏蔽被跳过一次后再不重试，整个对局本地倒计时/条件转移照常触发（阶段被"跳过"）。
            TrySuppressLocalTransitions();
            TryDeliverPendingOffers();
        }

        /// <summary>
        /// 缓存发牌的补投递：ItemOfferList/GamePhaseSync 可能先于关卡场景加载完成到达
        /// （此时 GamePhaseManager 不存在，消息被缓存），新 Manager 就绪后必须主动补启动流程，
        /// 否则联机下流程永远不启动（无选择阶段、无玩家化身）
        /// </summary>
        private void TryDeliverPendingOffers()
        {
            if (_pendingOffers == null)
            {
                return;
            }
            GamePhaseManager flow = GamePhaseManager.Instance;
            // 仅在联机屏蔽态下补投（单机模式的本地启动不该被抢）；管理器未就绪则下帧重试
            if (flow == null || flow.BFlowStarted || !ReferenceEquals(flow, _suppressedFlow))
            {
                return;
            }

            ItemOfferList list = _pendingOffers; // 不清空缓存：EnterPhase 后由 Director 消费（见 OnServerOffers 注释）
            Debug.Log($"[NetWork] 关卡场景就绪，补投缓存发牌: round={list.Round} 道具数={list.Offers.Count}");
            EnsureRemotePlayersReady();
            flow.StartGameFlow();
            flow.EnterPhaseByType<PropSelectionPhase>("补投缓存发牌");

            SuperQQ.Selection.Runtime.PropSelectionDirector director =
                UnityEngine.Object.FindFirstObjectByType<SuperQQ.Selection.Runtime.PropSelectionDirector>();
            if (director != null && director.BIsActive)
            {
                director.ReceiveOffers(list);
            }
        }

        // 记录已屏蔽的 Manager 实例而非单次标记：跨关卡/跨局时新场景的管理器会替换旧实例
        // （GamePhaseManager.Awake 新实例优先），屏蔽必须对新实例重新施加
        private GamePhaseManager _suppressedFlow;

        private void TrySuppressLocalTransitions()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId))
            {
                return; // 离线/未进房不接管
            }

            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == null)
            {
                return; // 关卡场景未加载，下一帧重试
            }
            if (ReferenceEquals(flow, _suppressedFlow))
            {
                return; // 当前实例已屏蔽过
            }

            flow.SetStartFlowOnStart(false);
            flow.SuppressLocalTransitions = true;
            _suppressedFlow = flow;
            Debug.Log("[NetWork] 已屏蔽本地阶段切换：阶段只响应服务器 GamePhaseSync");
        }

        private void TryInit()
        {
            NetworkManager net = NetworkManager.Instance;

            // 未连接/未进房：等待（联机模式在进房后才接管）
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId))
            {
                return;
            }

            // 关键：消息注册不依赖 GamePhaseManager 就绪——发牌/阶段消息可能在 Level1
            // 加载（GamePhaseManager 创建）之前到达，必须先注册才能缓存。阶段切换 handler
            // 内部自行判空 GamePhaseManager，未就绪时消息已缓存、后续补消费。
            _initialized = true;
            net.Register<ItemOfferList>(OnServerOffers);
            net.Register<GamePhaseSync>(OnGamePhaseSync);
            net.Register<PlayerOutBroadcast>(OnPlayerOut);
            net.Register<global::Minigame.Room.V1.Settlement>(OnSettlement);

            Debug.Log("[NetWork] 联机模式：游戏流程与阶段切换由服务器驱动（ItemOfferList / GamePhaseSync）");
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

        // ==================== 服务器权威分数 ====================

        /// <summary>服务器下发的玩家分数（本轮/累计 + 六个明细子类型）</summary>
        public struct ServerPlayerScore
        {
            public int RoundScore;
            public int TotalScore;
            public int FinishScore;       // 过关 +20
            public int FirstFinishScore;  // 第一个过关 +10
            public int SoloFinishScore;   // 唯一过关 +15
            public int CoinScore;         // 金币
            public int TrapKillScore;     // 道具杀人
            public int OvertakeScore;     // 总积分反超（翻盘）

            /// <summary>六个明细是否有效（服务器新版本下发；全 0 视为旧版本未实现，回退本地明细）</summary>
            public bool BHasBreakdown =>
                FinishScore != 0 || FirstFinishScore != 0 || SoloFinishScore != 0 ||
                CoinScore != 0 || TrapKillScore != 0 || OvertakeScore != 0;
        }

        // 服务器结算下发的分数表（键为 playerId），结算面板优先于本地算分使用
        private static readonly Dictionary<string, ServerPlayerScore> _serverScores = new();

        /// <summary>是否存在服务器下发的分数（联机收到 Settlement 后为 true）</summary>
        public static bool BHasServerScores => _serverScores.Count > 0;

        /// <summary>按 playerId 取服务器分数；无记录返回 false</summary>
        public static bool TryGetServerScore(string playerId, out ServerPlayerScore score)
        {
            if (playerId != null)
            {
                return _serverScores.TryGetValue(playerId, out score);
            }
            score = default;
            return false;
        }

        /// <summary>服务器结算结果：权威排名/胜负/分数（本地展示数据以服务器为准）</summary>
        private void OnSettlement(global::Minigame.Room.V1.Settlement settlement)
        {
            _serverScores.Clear();
            foreach (SettlementPlayerResult r in settlement.Results)
            {
                // 服务器未实现算分时字段整体缺省（proto3 缺省=0），不能用 0 覆盖本地分——
                // 只在接受非零分时纳入服务器权威，缺省则回退本地算分（规则一致，真实 0 分时两者相等）
                if (r.RoundScore != 0 || r.TotalScore != 0)
                {
                    _serverScores[r.PlayerId] = new ServerPlayerScore
                    {
                        RoundScore = r.RoundScore,
                        TotalScore = r.TotalScore,
                        FinishScore = r.FinishScore,
                        FirstFinishScore = r.FirstFinishScore,
                        SoloFinishScore = r.SoloFinishScore,
                        CoinScore = r.CoinScore,
                        TrapKillScore = r.TrapKillScore,
                        OvertakeScore = r.OvertakeScore
                    };
                }
            }

            bool bFinal = !string.IsNullOrEmpty(settlement.WinnerPlayerId);
            Debug.Log($"[NetWork] 收到服务器结算: round={settlement.Round} {(bFinal ? $"最终 胜者={settlement.WinnerPlayerId}" : "单轮")} 玩家数={settlement.Results.Count} 有效分数={_serverScores.Count}");
            foreach (SettlementPlayerResult r in settlement.Results)
            {
                Debug.Log($"[NetWork]   第{r.Rank}名 {r.PlayerId} 本轮={r.RoundScore} 累计={r.TotalScore} mmrΔ={r.MmrDelta} coinΔ={r.CoinDelta}");
            }

            // 服务器权威分数写回本地记分簿：结算柱动画/最终结算/面板本地回退都读本地簿，
            // 本地算分在联机下不可靠（状态口径与服务器不同步），统一以服务器为准
            ApplyServerScoresToLocalBook(settlement);

            // Settlement 通常晚于结算面板弹出到达（面板由 GamePhaseSync{ROUND_SETTLEMENT} 触发），
            // 若面板正开着则用最新分数重建一次，否则本次面板永远看不到服务器分数
            SuperQQ.Settlement.Runtime.RoundResultsDirector.Instance?.RefreshIfOpen();

            // 记分柱动画同理：阶段进入时按本地分数建的柱，服务器分数到达后按权威分数重建
            SuperQQ.Settlement.SettlementController.Instance?.RefreshSettlementIfShowing();
        }

        /// <summary>
        /// 把 Settlement 中各玩家的服务器权威分数写入本地记分簿（playerId → 玩家名映射后落账）。
        /// 仅处理服务器实际给了分数的玩家（与 _serverScores 的纳入口径一致）。
        /// </summary>
        private void ApplyServerScoresToLocalBook(global::Minigame.Room.V1.Settlement settlement)
        {
            SuperQQ.Score.PlayerScoreManager scoreManager = SuperQQ.Score.PlayerScoreManager.Instance;
            SuperQQ.Player.PlayerSessionManager session = SuperQQ.Player.PlayerSessionManager.Instance;
            if (scoreManager == null || session == null)
            {
                return;
            }

            foreach (SettlementPlayerResult r in settlement.Results)
            {
                if (r.RoundScore == 0 && r.TotalScore == 0)
                {
                    continue; // 与 _serverScores 纳入口径一致：全 0 视为服务器未实现算分，保留本地数据
                }

                SuperQQ.Player.PlayerProfile profile = session.GetProfileByIdentity(r.PlayerId);
                if (profile == null)
                {
                    Debug.LogWarning($"[NetWork] 服务器结算玩家 {r.PlayerId} 无本地档案，分数未写回记分簿");
                    continue;
                }

                Dictionary<SuperQQ.Score.ScoreType, int> breakdown = new()
                {
                    { SuperQQ.Score.ScoreType.Completion, r.FinishScore },
                    { SuperQQ.Score.ScoreType.FirstPlace, r.FirstFinishScore },
                    { SuperQQ.Score.ScoreType.SoloClear, r.SoloFinishScore },
                    { SuperQQ.Score.ScoreType.TrapKill, r.TrapKillScore },
                    { SuperQQ.Score.ScoreType.SpecialEffect, r.OvertakeScore },
                    { SuperQQ.Score.ScoreType.ScoreItem, r.CoinScore }
                };
                scoreManager.ApplyServerRoundScore(
                    profile.PlayerName, settlement.Round, r.RoundScore, r.TotalScore, breakdown);
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
            LocalPlayerNetSetup.EnsureLocalIdentityNow();

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
            bool localRegistered = false;
            for (int i = 0; i < players.Count; i++)
            {
                Minigame.Room.V1.RoomPlayerState p = players[i];
                string playerId = p.Player?.PlayerId;
                if (playerId == net.LocalPlayerId)
                {
                    // 本地玩家档案补注册：未在场景预置本地玩家的关卡（Level2 等）档案缺失，
                    // SpawnMissingPlayerAvatars 无档可生——整局没有一个角色。
                    // 注意去重：角色选择/场景预置等流程可能已按昵称注册过本地档案（无 PlayerId），
                    // 再按 PlayerId 注册会产生两份本地档案——选择阶段出现两个本地图标
                    string nickname = string.IsNullOrEmpty(p.Player?.Nickname) ? "P1" : p.Player.Nickname;
                    bool exists = session.HasPlayerByIdentity(playerId);
                    if (!exists)
                    {
                        foreach (SuperQQ.Player.PlayerProfile prof in session.Profiles)
                        {
                            // 匹配本地档案：昵称相同，或 PlayerId 为空的本地档案（局内名与服务器
                            // 昵称不一致时按名匹配会漏，PlayerId 为空的本地档案就是待合并对象）
                            if (prof.IsLocal && (prof.PlayerName == nickname || string.IsNullOrEmpty(prof.PlayerId)))
                            {
                                exists = true;
                                // 合并：老档案补齐 PlayerId（联机身份上报/匹配需要）
                                if (string.IsNullOrEmpty(prof.PlayerId))
                                {
                                    prof.PlayerId = playerId;
                                }
                                break;
                            }
                        }
                    }
                    // 场景预置了本地玩家对象（Level1 的 LocalPlayer）：对象已占坑，
                    // 身份由 LocalPlayerNetSetup 写入，不能再注册档案——否则会多生成一个克隆体
                    // （双本地玩家、缩放不一致、选择阶段双图标等一串问题）
                    if (!exists && SuperQQ.Player.LevelPlayerRegistry.Instance != null)
                    {
                        foreach (SuperQQ.Player.PlayerController pc in SuperQQ.Player.LevelPlayerRegistry.Instance.Players)
                        {
                            if (pc != null && pc.BIsLocal)
                            {
                                exists = true;
                                break;
                            }
                        }
                    }
                    if (!exists)
                    {
                        session.RegisterProfile(new SuperQQ.Player.PlayerProfile
                        {
                            PlayerId = playerId,
                            IsLocal = true,
                            PlayerName = nickname,
                            PlayerColor = PlayerColorPalette.Get(i),
                            CharacterIndex = i
                        });
                        localRegistered = true;
                        Debug.Log($"[NetWork] 本地玩家档案补注册: {playerId}（场景未预置本地玩家）");
                    }
                    continue;
                }
                registered += TryRegisterRemote(net, session, playerId, p.Player?.Nickname, i);
            }

            // 本地玩家也按同一下标规则着色/定角色（覆盖场景 prefab 的默认配置），保证两端一致
            ApplyLocalPlayerAppearance(net, players);

            if (registered > 0 || localRegistered)
            {
                SuperQQ.Player.LevelPlayerRegistry.Instance?.SpawnMissingPlayerAvatars();
                Debug.Log($"[NetWork] 对局开始：注册 {registered} 名远程玩家{(localRegistered ? " + 本地玩家" : "")}并生成化身");
            }
        }

        /// <summary>
        /// 按房间列表下标给本地玩家着色并选定角色（两端对同一 playerId 算出相同结果）。
        /// 场景预置的本地玩家无法在生成时选角色预制体，这里整体替换为角色预制体实例；
        /// 未配置角色预制体列表时退化为仅着色（旧行为）
        /// </summary>
        private static void ApplyLocalPlayerAppearance(NetworkManager net,
            System.Collections.Generic.IList<Minigame.Room.V1.RoomPlayerState> players)
        {
            SuperQQ.Player.LevelPlayerRegistry registry = SuperQQ.Player.LevelPlayerRegistry.Instance;
            if (registry == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Player?.PlayerId != net.LocalPlayerId) continue;

                // 先定位再操作：替换会向注册表增删玩家，不能在遍历注册表列表的过程中进行
                SuperQQ.Player.PlayerController local = null;
                System.Collections.Generic.IReadOnlyList<SuperQQ.Player.PlayerController> all = registry.Players;
                for (int j = 0; j < all.Count; j++)
                {
                    if (all[j] != null && all[j].BIsLocal)
                    {
                        local = all[j];
                        break;
                    }
                }
                if (local == null) return;

                SuperQQ.Player.PlayerProfile profile = local.BuildProfile();
                profile.PlayerColor = PlayerColorPalette.Get(i);
                profile.CharacterIndex = i;

                SuperQQ.Player.PlayerController replaced = registry.ReplacePlayerAvatar(local, profile);
                if (replaced != null)
                {
                    Debug.Log($"[NetWork] 本地玩家外观: 下标={i} 颜色={PlayerColorPalette.Get(i)} 已替换为角色{i}预制体");
                    // 新化身缺少联机上报组件（InputReporter/PlayerOutReporter 原挂在旧化身上），立即补挂
                    LocalPlayerNetSetup.EnsureLocalIdentityNow();
                }
                else
                {
                    // 无需替换（已是目标角色或未配置角色预制体）：仅应用颜色/角色索引
                    local.ApplyProfile(profile);
                    Debug.Log($"[NetWork] 本地玩家着色: 下标={i} 颜色={PlayerColorPalette.Get(i)}");
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
                PlayerColor = PlayerColorPalette.Get(colorIndex),
                CharacterIndex = colorIndex
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

        /// <summary>
        /// 服务器下发的当前轮次（随 GamePhaseSync / ItemOfferList 更新）；0 表示联机对局未开始。
        /// 供昼夜切换等纯表现组件读取——联机下本地 PlayerScoreManager.CurrentRoundIndex 不会被推进。
        /// </summary>
        public static int CurrentServerRound { get; private set; }

        /// <summary>
        /// 本轮随机种子（仅 PROP_SELECTION 的 GamePhaseSync 携带，见 proto 注释；其余阶段为 0）。
        /// 吐司尺寸/事件推演等跨阶段用途必须读缓存值，不能读后续阶段消息里的 RandomSeed（恒 0）
        /// </summary>
        public static int CurrentRoundSeed { get; private set; } = -1;

        private void OnGamePhaseSync(GamePhaseSync sync)
        {
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == null) return;

            // 服务器时间对时 + 记录本阶段结束时刻（倒计时锚点，两端一致）
            NetworkManager.SyncServerTime(sync.ServerTimeMs);
            CurrentPhaseEndTimeMs = sync.PhaseEndTimeMs;
            CurrentServerRound = sync.Round;

            // 本轮随机事件：应用服务器下发的 event_id/random_seed（Announcer 内部按轮幂等，重复下发为空操作）
            SuperQQ.Event.LevelEventAnnouncer.Instance?.ApplyServerEvent(sync.EventId, sync.RandomSeed, sync.Round);

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
                    // 本地记分簿跟随服务器轮次翻页（联机本地转移被屏蔽，
                    // AdvanceToNextRound 不会触发，不推进则永远读第 1 轮旧数据）
                    SuperQQ.Score.PlayerScoreManager.Instance?.SyncToServerRound(sync.Round);
                    // 新一轮开始即复活本地玩家并回出生点（而非等到 PLAYING）：
                    // 上一轮死亡的玩家在选择/摆放阶段（约 70s）一直是幽灵，会持续上报
                    // player_state=1（幽灵），若服务器参考该字段判定出局会误判秒切结算。
                    // 首轮玩家本就存活，Revive 为空操作，可安全调用。
                    SuperQQ.Player.LevelPlayerRegistry.Instance?.ReviveLocalPlayersForNewRound();
                    // 新一轮开始：清空快照道具恢复记录（RoomSnapshotReceiver 跨场景存活，
                    // 不清空会让本轮同 itemId 同锚点的道具被误判"已恢复"而永远补不出来）
                    UnityEngine.Object.FindFirstObjectByType<RoomSnapshotReceiver>()?.ClearRestoredItems();
                    // 旋转吐司尺寸：用服务器轮次种子确定性决定（各端结果天然一致）。
                    // 旧方案"放置者本地随机+广播"存在竞态：多名玩家同时持吐司时各端各自随机，
                    // 再互相应用对方广播，最终尺寸取决于收包顺序，两端可能不一致
                    CurrentRoundSeed = sync.RandomSeed;   // 缓存本轮种子：仅本阶段消息携带，后续阶段恒 0
                    SuperQQ.Item.RotatingToastSizeSync.DecideSizeBySeed(sync.RandomSeed);
                    // 选择阶段：道具列表由 ItemOfferList 下发，此处只负责切阶段
                    flow.EnterPhaseByType<PropSelectionPhase>(reason);
                    break;
                case GamePhaseKind.PropPlacement:
                    // 吐司尺寸兜底：选择阶段未处理到（迟到/断线重连直接进入摆放）时按种子补决定。
                    // 必须用缓存的选择阶段种子：本阶段的 GamePhaseSync.RandomSeed 恒为 0，
                    // 直接用会得到与其他端不同的尺寸——同锚点不同 footprint，两端吐司格子位置不一致
                    if (SuperQQ.Item.RotatingToastSizeSync.CurrentSize == 0)
                    {
                        int fallbackSeed = CurrentRoundSeed >= 0 ? CurrentRoundSeed : sync.RandomSeed;
                        Debug.LogWarning($"[NetWork] 选择阶段种子未生效，摆放阶段兜底决定吐司尺寸: seed={fallbackSeed}");
                        SuperQQ.Item.RotatingToastSizeSync.DecideSizeBySeed(fallbackSeed);
                    }
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
            // 选择阶段已激活时，Director 的注册可能被本 Gate 重新注册覆盖（本地流程先于服务器
            // 消息启动的竞争场景：BeginPhase 注册 → OnGamePhaseSync 重新注册 → 发牌到达）。
            // 此时缓存将无人消费，直接把发牌转发给激活中的 Director 并清缓存。
            SuperQQ.Selection.Runtime.PropSelectionDirector director =
                UnityEngine.Object.FindFirstObjectByType<SuperQQ.Selection.Runtime.PropSelectionDirector>();
            if (director != null && director.BIsActive)
            {
                _pendingOffers = null;
                director.ReceiveOffers(list);
                Debug.Log($"[NetWork] Gate 转发发牌给激活中的选择阶段: round={list.Round} 道具数={list.Offers.Count}");
                return;
            }

            // 始终缓存最新发牌，供 PropSelectionDirector 进入阶段时消费
            _pendingOffers = list;
            CurrentServerRound = list.Round;
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
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Unregister<ItemOfferList>();
                NetworkManager.Instance.Unregister<GamePhaseSync>();
                NetworkManager.Instance.Unregister<PlayerOutBroadcast>();
                NetworkManager.Instance.Unregister<global::Minigame.Room.V1.Settlement>();
            }
            CurrentPhaseEndTimeMs = 0;
            CurrentServerRound = 0;
        }
    }
}

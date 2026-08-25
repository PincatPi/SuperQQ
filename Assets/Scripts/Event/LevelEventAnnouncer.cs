using System;
using System.Collections;
using System.Collections.Generic;
using SuperQQ.GameFlow;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件播报器 — 场景级单例
    /// 进入关卡时经 LevelEventSelector 选定本关事件（固定事件全部执行，非固定事件按权重抽取一个）
    /// 事件选定后：
    ///   - 待进入 Playing 游玩阶段时，通过 PopupManager 依次播放每个事件的说明弹窗（3秒自动销毁），
    ///     并调用每个事件对应 LevelEventModifier 的 Activate 方法启动事件逻辑
    ///     （事件计时与弹窗播报统一从游玩阶段起算，道具选择/放置阶段不推进计时、不出现弹窗）
    /// 游玩阶段结束时停止弹窗播放并调用所有 Modifier 的 Deactivate 进行清理（事件随阶段结束而结束）；
    /// 场景销毁时同样调用 Deactivate 兜底
    /// 选取决策已抽离到 LevelEventSelector（纯 C#，可单元测试），本类只负责调度与播报
    /// </summary>
    public class LevelEventAnnouncer : MonoBehaviour
    {
        // 场景级单例实例
        private static LevelEventAnnouncer _instance;

        // 事件配置表引用，在 Inspector 中指定
        [Header("事件配置")]
        [SerializeField] private LevelEventConfig _eventConfig;

        [Header("临时测试开关（恢复正式逻辑时取消勾选）")]
        [Tooltip("【临时】客户端本地触发：忽略服务器事件下发，每轮游玩阶段开始时由客户端本地按权重选取并触发事件，便于特殊事件测试。取消勾选即恢复服务器触发与分发的正式逻辑")]
        [SerializeField] private bool _bTempClientLocalTrigger = false;

        // 弹窗自动关闭时长（秒），对应策划文档：3秒后自动销毁
        private const float POPUP_AUTO_CLOSE_DURATION = 3f;

        // 多个弹窗之间的播放间隔（秒），前一个弹窗关闭后等待此时长再播下一个
        // 避免多个弹窗同时弹出造成视觉叠加
        private const float POPUP_INTERVAL = 0.2f;

        // 本关选中的所有事件条目（固定事件 + 随机事件）
        private readonly List<LevelEventEntry> _selectedEntries = new();

        // 是否已完成事件选取（播报协程可能仍在进行中）
        private bool _bHasAnnounced;

        // 是否已激活事件 Modifier（游玩阶段闸门保证只激活一次）
        private bool _bModifiersActivated;

        // 弹窗依次播放的协程引用
        private Coroutine _popupPlaybackCoroutine;

        // 运行时上下文，在事件激活时创建，传递给各 LevelEventModifier
        private LevelEventContext _eventContext;

        // ==================== 联机事件同步状态 ====================

        // 服务器已下发事件的轮次（按轮幂等，同一轮重复 ApplyServerEvent 为空操作）
        private int _serverEventRound;

        // 本轮服务器事件是否已触发（快照 event_triggered 翻牌去重）
        private bool _bServerEventTriggered;

        // 服务器触发时刻的定时引爆协程
        private Coroutine _serverTriggerCoroutine;

        // ==================== 临时本地触发状态（测试用，恢复时随开关一并移除） ====================

        // 临时模式下首轮游玩阶段是否尚未激活（首轮沿用 Start 的选取，之后每轮重新掷签）
        private bool _bTempAwaitingFirstActivation;

        // ==================== 公开事件 ====================

        /// <summary>
        /// 事件选中事件：本关事件选取完成后触发
        /// 参数为本关所有选中的事件条目列表（固定事件 + 随机事件）
        /// 外部系统可订阅此事件做额外处理（如联机同步、UI 展示）
        /// </summary>
        public event Action<IReadOnlyList<LevelEventEntry>> OnEventsSelected;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 当前场景中的全局唯一实例
        /// </summary>
        public static LevelEventAnnouncer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<LevelEventAnnouncer>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 本关选中的所有事件条目（只读视图，按固定事件在前、随机事件在后的顺序排列）
        /// </summary>
        public IReadOnlyList<LevelEventEntry> SelectedEvents => _selectedEntries;

        /// <summary>
        /// 本关选中事件的数量
        /// </summary>
        public int SelectedEventCount => _selectedEntries.Count;

        /// <summary>
        /// 是否已完成事件选取
        /// </summary>
        public bool BHasAnnounced => _bHasAnnounced;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 场景级单例：不 DontDestroyOnLoad，场景卸载时本对象随之销毁
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            // 场景销毁时退订阶段切换（若事件尚未等到游玩阶段激活）
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChangedForActivation;
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePlayingPhaseEnded;
                // 【临时】测试开关的每轮掷签回调一并退订
                GamePhaseManager.Instance.OnPhaseChanged -= HandleTempPhaseChanged;
            }

            // 场景销毁时停止弹窗播放协程
            StopPopupPlayback();

            // 场景销毁时停用所有事件 Modifier，进行清理
            DeactivateAllModifiers();
        }

        private void Start()
        {
            // 订阅阶段切换：游玩阶段结束时统一结束本轮事件（弹窗播报 + Modifier 清理）
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged += HandlePlayingPhaseEnded;
            }

            // 【临时】测试开关开启时：联机/单机统一走客户端本地权重选取，
            // 并订阅阶段切换实现"每轮游玩阶段开始重新掷签"；服务器下发路径由守卫跳过
            if (_bTempClientLocalTrigger)
            {
                _bTempAwaitingFirstActivation = true;
                SelectAndAnnounceEvents();
                if (GamePhaseManager.Instance != null)
                {
                    GamePhaseManager.Instance.OnPhaseChanged += HandleTempPhaseChanged;
                }
                return;
            }

            // 联机模式：本轮事件由服务器 GamePhaseSync(event_id/random_seed) 决定，
            // 触发时机由 RoomSnapshot.event_triggered 翻牌驱动，本地不做随机选取
            if (IsNetMode())
            {
                Debug.Log("[LevelEventAnnouncer] 联机模式：等待服务器下发本轮事件。");
                return;
            }

            SelectAndAnnounceEvents();
        }

        /// <summary>
        /// 阶段切换回调：游玩阶段结束时结束本轮事件——
        /// 停止事件说明弹窗播放、停用所有已激活的 Modifier（陨石停止落石、液氮强制解冻等），
        /// 并重置激活状态，使下一轮游玩阶段开始时事件重新激活（联机新一轮由 ApplyServerEvent 驱动）
        /// </summary>
        private void HandlePlayingPhaseEnded(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            if (!(previousPhase is PlayingPhase) || nextPhase is PlayingPhase)
            {
                return;
            }

            StopPopupPlayback();
            DeactivateAllModifiers();
            _bModifiersActivated = false;
        }

        /// <summary>
        /// 【临时】阶段切换回调（测试开关开启时生效）：
        /// 离开游玩阶段时清理本轮事件；进入游玩阶段时重新按权重掷签（首轮沿用 Start 的选取）
        /// </summary>
        private void HandleTempPhaseChanged(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            if (previousPhase is PlayingPhase && !(nextPhase is PlayingPhase))
            {
                // 本轮游玩阶段结束的清理已由 HandlePlayingPhaseEnded 统一处理，此处不再重复
                return;
            }

            if (!(nextPhase is PlayingPhase) || previousPhase is PlayingPhase)
            {
                return;
            }

            if (_bTempAwaitingFirstActivation)
            {
                // 首轮游玩阶段：使用 Start 已完成的选取（由游玩阶段闸门激活）
                _bTempAwaitingFirstActivation = false;
                return;
            }

            // 新一轮：重置一次性状态并重新掷签；当前已在游玩阶段，选取后立即激活
            _bHasAnnounced = false;
            _bModifiersActivated = false;
            SelectAndAnnounceEvents();
        }

        /// <summary>联机模式判定：已连接且在房间内</summary>
        private static bool IsNetMode()
        {
            Network.NetworkManager net = Network.NetworkManager.Instance;
            return net != null && net.IsConnected && !string.IsNullOrEmpty(net.RoomId);
        }

        // ==================== 联机事件同步 ====================

        /// <summary>
        /// 应用服务器下发的本轮事件（联机模式，由 NetGameFlowGate 在收到 GamePhaseSync 时调用）。
        /// 以 event_id 从配置表取对应条目（固定事件照常执行），以 random_seed 作为事件随机源种子。
        /// 按轮幂等：同一轮重复调用为空操作；新一轮调用会重置触发状态。
        /// </summary>
        /// <param name="eventId">服务器下发的事件 ID（对应 LevelEventType 枚举值）</param>
        /// <param name="randomSeed">本轮随机种子，事件内部随机过程（如陨石落点序列）各端一致</param>
        /// <param name="round">当前轮次（从 1 开始）</param>
        public void ApplyServerEvent(int eventId, int randomSeed, int round)
        {
            // 【临时】测试开关开启时忽略服务器事件下发（恢复正式逻辑时取消勾选）
            if (_bTempClientLocalTrigger)
            {
                return;
            }

            if (!IsNetMode())
            {
                return;
            }

            if (_serverEventRound == round && _bHasAnnounced)
            {
                return; // 同一轮重复下发，幂等
            }

            // 新一轮：重置触发状态，停掉上一轮的引爆协程
            _serverEventRound = round;
            _bServerEventTriggered = false;
            _bWarnedNoServerDrivenModifier = false;
            _bWarnedNoServerDrivenModifier2 = false;
            if (_serverTriggerCoroutine != null)
            {
                StopCoroutine(_serverTriggerCoroutine);
                _serverTriggerCoroutine = null;
            }

            _selectedEntries.Clear();
            if (_eventConfig != null)
            {
                IReadOnlyList<LevelEventEntry> pool = _eventConfig.Events;
                for (int i = 0; i < pool.Count; i++)
                {
                    LevelEventEntry entry = pool[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    // 固定事件照常执行；随机事件由服务器 event_id 指定
                    if (entry.BIsFixed || (int)entry.EventType == eventId)
                    {
                        _selectedEntries.Add(entry);
                    }
                }
            }

            _bHasAnnounced = true;

            if (_selectedEntries.Count == 0)
            {
                Debug.LogWarning($"[LevelEventAnnouncer] 服务器事件 ID={eventId} 在配置表中无对应条目。");
                return;
            }

            // 上下文注入服务器种子 + 等待触发标记：Modifier 只做准备，落石等服务器翻牌
            _eventContext = new LevelEventContext
            {
                CoroutineRunner = this,
                SceneRoot = transform,
                RandomSeed = randomSeed,
                WaitForTrigger = true
            };

            OnEventsSelected?.Invoke(_selectedEntries);
            ActivateAndAnnounceWhenPlaying();

            Debug.Log($"[LevelEventAnnouncer] 应用服务器事件: id={eventId} seed={randomSeed} round={round} 事件数={_selectedEntries.Count}");
        }

        /// <summary>
        /// 服务器事件触发翻牌（由 RoomSnapshotReceiver 在 RoomSnapshot.event_triggered 变 true 时调用）。
        /// 以服务器触发时刻为锚点定时引爆：两端对齐到同一服务器时刻，而非各自收到包的时刻；
        /// 时刻已过（延迟大/断线重连）立即补爆。按轮去重，快照重复下发不会重复触发。
        /// </summary>
        /// <param name="triggeredAtMs">服务器事件触发时刻（毫秒时间戳）</param>
        public void OnServerEventTriggered(long triggeredAtMs)
        {
            // 【临时】测试开关开启时忽略服务器触发翻牌（恢复正式逻辑时取消勾选）
            if (_bTempClientLocalTrigger)
            {
                return;
            }

            if (!IsNetMode() || _bServerEventTriggered || !_bHasAnnounced)
            {
                return;
            }
            _bServerEventTriggered = true;

            Network.NetworkManager net = Network.NetworkManager.Instance;
            long delayMs = triggeredAtMs > 0 ? triggeredAtMs - Network.NetworkManager.EstimatedServerNowMs() : 0;
            float delaySeconds = Mathf.Max(0f, delayMs / 1000f);
            _serverTriggerCoroutine = StartCoroutine(ServerTriggerAfterDelay(delaySeconds));
            Debug.Log($"[LevelEventAnnouncer] 服务器事件已掷签: 触发时刻={triggeredAtMs} 预计 {delaySeconds:F2}s 后引爆");
        }

        /// <summary>
        /// 服务端随机事件参数下发（由 RoomSnapshotReceiver 在 RoomSnapshot.event_params1 到达时调用）。
        /// 路由给本轮选中事件中实现 IServerDrivenRandomEvent 的 Modifier（如小蛋糕陨石）：
        /// 首包驱动生成，后续包做位置校验。快照全量重复下发，Modifier 内部自行幂等。
        /// </summary>
        public void OnServerEventParams(Minigame.Room.V1.RandomEventParams eventParams)
        {
            // 【临时】测试开关开启时忽略服务器参数下发（恢复正式逻辑时取消勾选）
            if (_bTempClientLocalTrigger || eventParams == null)
            {
                return;
            }
            if (!IsNetMode())
            {
                return;
            }

            bool bRouted = false;
            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier is IServerDrivenRandomEvent serverDriven)
                {
                    serverDriven.ApplyServerEventParams(eventParams);
                    bRouted = true;
                }
            }

            // 联调诊断：参数包到了但本轮选中事件中没有服务端驱动实现（一次性告警，随新轮重置）
            if (!bRouted && !_bWarnedNoServerDrivenModifier)
            {
                _bWarnedNoServerDrivenModifier = true;
                Debug.LogWarning("[LevelEventAnnouncer] 收到事件参数包，但本轮选中事件中没有实现 IServerDrivenRandomEvent 的 Modifier（检查事件配置表/服务器 event_id 是否指向小蛋糕陨石）");
            }
        }

        /// <summary>
        /// 服务端随机事件2参数下发（由 RoomSnapshotReceiver 在 RoomSnapshot.event_params2 到达时调用）。
        /// 路由给本轮选中事件中实现 IServerDrivenRandomEvent2 的 Modifier（如液氮泄露冰冻事件）。
        /// </summary>
        public void OnServerEventParams2(Minigame.Room.V1.RandomEventParams2 eventParams)
        {
            // 【临时】测试开关开启时忽略服务器参数下发（恢复正式逻辑时取消勾选）
            if (_bTempClientLocalTrigger || eventParams == null)
            {
                return;
            }
            if (!IsNetMode())
            {
                return;
            }

            bool bRouted = false;
            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier is IServerDrivenRandomEvent2 serverDriven)
                {
                    serverDriven.ApplyServerEventParams(eventParams);
                    bRouted = true;
                }
            }

            if (!bRouted && !_bWarnedNoServerDrivenModifier2)
            {
                _bWarnedNoServerDrivenModifier2 = true;
                Debug.LogWarning("[LevelEventAnnouncer] 收到事件2参数包，但本轮选中事件中没有实现 IServerDrivenRandomEvent2 的 Modifier（检查服务器 event_id 是否指向液氮泄露冰冻事件）");
            }
        }

        // 联调诊断用：无服务端驱动 Modifier 告警去重（ApplyServerEvent 新一轮时重置）
        private bool _bWarnedNoServerDrivenModifier;
        private bool _bWarnedNoServerDrivenModifier2;

        /// <summary>按服务器时钟锚点延时后，触发本轮事件的 Modifier 逻辑</summary>
        private IEnumerator ServerTriggerAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
            _serverTriggerCoroutine = null;

            if (_eventContext == null)
            {
                yield break;
            }

            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier != null)
                {
                    _selectedEntries[i].Modifier.OnServerTrigger(_eventContext);
                }
            }
        }

        // ==================== 核心流程 ====================

        /// <summary>
        /// 选定本关事件并依次播报弹窗、激活事件逻辑
        /// 选取决策委托给 LevelEventSelector：固定事件全部执行，非固定事件按权重抽取一个
        /// 在 Start 中自动调用，确保每次进入关卡时都执行
        /// </summary>
        /// <param name="random">
        /// 随机源，用于非固定事件的权重抽取；为 null 时使用时间种子
        /// 联机模式下应由主机传入固定种子的实例，保证各端选取结果一致
        /// </param>
        public void SelectAndAnnounceEvents(System.Random random = null)
        {
            if (_bHasAnnounced)
            {
                return;
            }

            _selectedEntries.Clear();

            // 选取决策委托给纯 C# 选取器（本类不感知选取规则细节）
            if (_eventConfig != null)
            {
                _selectedEntries.AddRange(LevelEventSelector.SelectEvents(_eventConfig.Events, random));
            }

            _bHasAnnounced = true;

            if (_selectedEntries.Count == 0)
            {
                Debug.LogWarning("[LevelEventAnnouncer] 未选中任何事件，请检查配置表。");
                return;
            }

            // 创建运行时上下文，供 Modifier 启动协程和访问场景
            _eventContext = new LevelEventContext
            {
                CoroutineRunner = this,
                SceneRoot = transform
            };

            // 通知外部：本关事件已选定
            OnEventsSelected?.Invoke(_selectedEntries);

            // Modifier 激活与事件弹窗播报统一挂在游玩阶段闸门上：
            // 进入 Playing 阶段时才执行（当前已在游玩阶段则立即执行）
            ActivateAndAnnounceWhenPlaying();
        }

        // ==================== 内部方法：Modifier 激活/停用 ====================

        /// <summary>
        /// 游玩阶段闸门：事件 Modifier 的计时（首次落石延迟、随机触发时机等）从
        /// Playing 游玩阶段开始才起算，事件说明弹窗也在进入游玩阶段后才播放，
        /// 道具选择/放置等其它阶段不推进事件计时、不出现事件弹窗
        /// 当前已在游玩阶段或场景中无 GamePhaseManager（纯测试场景）时立即执行；
        /// 否则订阅阶段切换事件，待进入游玩阶段时执行
        /// </summary>
        private void ActivateAndAnnounceWhenPlaying()
        {
            GamePhaseManager phaseManager = GamePhaseManager.Instance;
            if (phaseManager == null || phaseManager.CurrentPhaseAsset is PlayingPhase)
            {
                ActivateSelectedModifiers();
                StartPopupPlayback();
                return;
            }

            phaseManager.OnPhaseChanged += HandlePhaseChangedForActivation;
        }

        /// <summary>
        /// 阶段切换回调：进入游玩阶段时激活事件 Modifier 并播放事件说明弹窗，随后退订（只执行一次）
        /// 单机走本地条件转移、联机走服务器 GamePhaseSync，二者均经 EnterPhase 触发本事件
        /// </summary>
        private void HandlePhaseChangedForActivation(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            if (!(nextPhase is PlayingPhase))
            {
                return;
            }

            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChangedForActivation;
            }

            ActivateSelectedModifiers();
            StartPopupPlayback();
        }

        /// <summary>
        /// 激活本关所有选中事件对应的 Modifier
        /// 直接遍历条目引用调用 Modifier.Activate，无需按枚举回查配置表
        /// 幂等：游玩阶段闸门保证只激活一次，重复调用为空操作
        /// </summary>
        private void ActivateSelectedModifiers()
        {
            if (_bModifiersActivated || _eventContext == null)
            {
                return;
            }
            _bModifiersActivated = true;

            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier != null)
                {
                    _selectedEntries[i].Modifier.Activate(_eventContext);
                }
            }
        }

        /// <summary>
        /// 停用本关所有已激活的 Modifier
        /// 在场景销毁或强制中断时调用，确保各事件逻辑正确清理协程和资源
        /// </summary>
        private void DeactivateAllModifiers()
        {
            if (_eventContext == null)
            {
                return;
            }

            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier != null)
                {
                    _selectedEntries[i].Modifier.Deactivate(_eventContext);
                }
            }
        }

        // ==================== 内部方法：弹窗播放 ====================

        /// <summary>
        /// 开始依次播放事件说明弹窗（进入游玩阶段时由阶段闸门调用；重启前先停掉上一轮播放）
        /// </summary>
        private void StartPopupPlayback()
        {
            StopPopupPlayback();

            if (_selectedEntries.Count == 0)
            {
                return;
            }
            _popupPlaybackCoroutine = StartCoroutine(ShowEventPopupsSequentially());
        }

        /// <summary>
        /// 停止弹窗播放协程（离开游玩阶段、重新播报或场景销毁时调用）
        /// </summary>
        private void StopPopupPlayback()
        {
            if (_popupPlaybackCoroutine != null)
            {
                StopCoroutine(_popupPlaybackCoroutine);
                _popupPlaybackCoroutine = null;
            }
        }

        /// <summary>
        /// 依次播放本关所有选中事件的说明弹窗
        /// 每个弹窗持续 POPUP_AUTO_CLOSE_DURATION 秒后自动关闭，
        /// 再等待 POPUP_INTERVAL 秒后播放下一个，避免视觉叠加
        /// </summary>
        private IEnumerator ShowEventPopupsSequentially()
        {
            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                ShowEventPopup(_selectedEntries[i]);

                // 最后一个事件无需等待
                if (i < _selectedEntries.Count - 1)
                {
                    yield return new WaitForSeconds(POPUP_AUTO_CLOSE_DURATION + POPUP_INTERVAL);
                }
            }

            _popupPlaybackCoroutine = null;
        }

        /// <summary>
        /// 通过 PopupManager 播放单个事件说明弹窗
        /// 弹窗持续3秒后自动关闭
        /// </summary>
        /// <param name="entry">要播报的事件条目</param>
        private void ShowEventPopup(LevelEventEntry entry)
        {
            if (entry.IntroPopup == PopupType.None)
            {
                Debug.LogWarning($"[LevelEventAnnouncer] 事件 {entry.EventType} 的说明弹窗未配置，跳过播放。");
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LevelEventAnnouncer] PopupManager 不存在，无法播放事件弹窗。");
                return;
            }

            PopupManager.Instance.ShowPopup(entry.IntroPopup, PopupArgs.WithDuration(POPUP_AUTO_CLOSE_DURATION));
            Debug.Log($"[LevelEventAnnouncer] 本关事件：{entry.DisplayName}（{entry.EventType}），弹窗已播放。");
        }
    }
}

using System;
using System.Collections.Generic;
using Cinemachine;
using Minigame.Room.V1;
using SuperQQ.GameFlow;
using SuperQQ.Item;
using SuperQQ.Network;
using SuperQQ.Placement.Runtime;
using SuperQQ.Player;
using SuperQQ.Selection.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 道具选择阶段场景门面（场景级单例）。
    /// 对 GameFlow 只暴露 <see cref="BeginPhase"/> / <see cref="EndPhase"/> 与只读状态查询，
    /// 内部负责：本轮候选道具的随机抽取、选择面板搭建、点击选中与选中状态锁定。
    ///
    /// 流程：进入阶段时从候选池随机抽取若干不重复道具展示在选择面板上，
    ///       场景角色被屏蔽（隐藏且停止响应输入），每名玩家以 Sprite 图标出现在面板固定位
    ///       （按注册顺序使用前 N 个位置）→
    ///       本地玩家点击道具图标后，其玩家图标平滑飞向对应槽位，到达后才正式认领
    ///       （每名玩家每轮限选一件，认领即确认、不可更改）→
    ///       已被选中的道具显示选中者颜色标记，其他玩家不可再选 →
    ///       阶段退出时由 PropSelectionPhase 读取 <see cref="LocalSelectedItem"/> 推入放置阶段。
    ///
    /// 联机预留：选择结果通过 <see cref="OnLocalSelectionConfirmed"/> 对外发布；
    /// 接入网络时新增订阅者上报本地选择，并把远端玩家的选择经 <see cref="ApplyRemoteSelection"/>
    /// 喂入即可（核心会话已保证多玩家互斥认领），本类无需改动。
    ///
    /// Editor 搭建步骤：
    ///   1. 关卡场景新建空物体挂载本组件，配置道具候选池（拖入挂有 ItemBase 的道具 prefab）与候选数量；
    ///   2. 选择面板可二选一：
    ///      a. 正式 UI：拖入面板根物体、槽位容器（RectTransform）与槽位 prefab（挂 PropSelectionSlotView，
    ///         视图按 ItemIcon / ItemNameText / ClaimMarker 子物体命名约定自动识别引用，无需手动拖拽）；
    ///      b. 全部留空：运行时自动搭建简易面板（黑底 + 网格槽位），供逻辑联调使用；
    ///   3. 倒计时文本、阶段摄像机均为可选，留空自动降级（不显示/不切换）；
    ///   4. 玩家出现位（Player Spot Anchors）拖入面板中 4 个固定位置的 RectTransform；
    ///      留空时先在面板下按名字查找 PlayerSpot 前缀的子物体，仍无则自动在面板四角生成；
    ///   5. 勾选 Debug Simulate Other Players 可让场景中其它非本地玩家在随机时刻自动认领，
    ///      用于本地验证「已被选中的道具不可再选」的互斥表现与图标飞行动画。
    /// </summary>
    public class PropSelectionDirector : MonoBehaviour
    {
        private const string LOG_TAG = "[PropSelection]";

        private static PropSelectionDirector _instance;

        /// <summary>场景内的选择阶段门面实例</summary>
        public static PropSelectionDirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PropSelectionDirector>();
                }
                return _instance;
            }
        }

        [Header("道具候选池")]
        [Tooltip("本轮随机抽取候选的道具（挂有 ItemBase 的 prefab）")]
        [SerializeField] private List<ItemBase> itemPool = new List<ItemBase>();
        [Tooltip("本轮展示的候选道具数量；候选池有效道具不足时按实际数量展示")]
        [SerializeField] private int offerCount = 6;

        [Header("选择面板")]
        [Tooltip("选择阶段显示的面板根物体（SetActive 切换）；留空则运行时自动搭建简易面板")]
        [SerializeField] private GameObject selectionPanel;
        [Tooltip("槽位生成容器；面板留空时无需配置")]
        [SerializeField] private RectTransform slotsContainer;
        [Tooltip("槽位视图 prefab（挂有 PropSelectionSlotView）；留空时代码生成简易槽位")]
        [SerializeField] private PropSelectionSlotView slotViewPrefab;
        [Tooltip("认领确认打勾按钮 prefab（需挂 Button）；留空则运行时搭建默认样式")]
        [SerializeField] private Button confirmCheckButtonPrefab;

        [Header("倒计时显示")]
        [Tooltip("显示阶段剩余秒数的文本；留空则不显示倒计时")]
        [SerializeField] private TMP_Text countdownText;

        [Header("阶段摄像机")]
        [Tooltip("选择阶段使用的固定视角 Virtual Camera；留空则不切换镜头")]
        [SerializeField] private CinemachineVirtualCamera selectionCamera;
        [Tooltip("选择阶段生效时 selectionCamera 的优先级，需高于游玩镜头（默认 10）")]
        [SerializeField] private int selectionCameraPriority = 20;

        [Header("玩家图标")]
        [Tooltip("面板中玩家图标的固定出现位置（一般 4 个）；留空时按 PlayerSpot 前缀名字查找，仍无则自动在面板四角生成")]
        [SerializeField] private List<RectTransform> playerSpotAnchors = new List<RectTransform>();
        [Tooltip("玩家图标尺寸（像素）")]
        [SerializeField] private float playerIconSize = 96f;
        [Tooltip("玩家图标飞向槽位的最大速度（像素/秒）")]
        [SerializeField] private float iconMaxSpeed = 1600f;
        [Tooltip("玩家图标加/减速度（像素/秒²），越大加减速越急促")]
        [SerializeField] private float iconAcceleration = 6000f;

        [Header("角色屏蔽")]
        [Tooltip("选择阶段屏蔽场景内角色（隐藏并停止响应输入与物理），退出阶段时还原")]
        [SerializeField] private bool suppressAvatars = true;

        [Header("调试")]
        [Tooltip("勾选后，场景中其它非本地玩家会在随机时刻自动认领道具（模拟联机互斥效果）")]
        [SerializeField] private bool debugSimulateOtherPlayers = false;

        /// <summary>等待自动认领的模拟玩家（仅调试）</summary>
        private struct FakePicker
        {
            public string PlayerKey;
            public float PickAt;    // 阶段内经过秒数；-1 表示已执行
        }

        private SelectionSession session;
        private readonly List<PropSelectionSlotView> slotViews = new List<PropSelectionSlotView>();
        private readonly List<FakePicker> fakePickers = new List<FakePicker>();
        private readonly Dictionary<string, PropSelectionPlayerIcon> playerIcons = new Dictionary<string, PropSelectionPlayerIcon>();
        private readonly Dictionary<PlayerController, PropSelectionPlayerIcon> playerIconsByController = new Dictionary<PlayerController, PropSelectionPlayerIcon>(); // 按化身实例去重，防止身份主键变更导致重复生成
        private readonly Dictionary<string, int> pendingClaims = new Dictionary<string, int>();   // 飞行中的待生效认领：playerKey -> 槽位下标
        private readonly PlayerAvatarGate avatarGate = new PlayerAvatarGate();
        private RectTransform iconLayer;                    // 玩家图标层（BeginPhase 创建，EndPhase 销毁；自动生成的出现位也挂在其下）
        private GameObject runtimeBuiltPanel;               // 自动搭建的面板（EndPhase 时销毁）
        private RectTransform runtimeSlotsContainer;        // 自动搭建面板内的槽位容器
        private PropSelectionPhase activePhase;             // 驱动本阶段的阶段资产（倒计时数据源）
        private ItemBase localSelectedItem;                 // 本地玩家选中道具的缓存（EndPhase 前由阶段资产读取）
        private string localPlayerKey = string.Empty;
        private int selectionCameraOriginalPriority;
        private float phaseElapsed;

        // 联机打勾确认按钮（运行时搭建，跟随待确认槽位）
        private RectTransform confirmCheckButton;       // 打勾按钮（EndPhase 销毁）
        private int confirmCheckSlot = -1;              // 当前待确认的槽位；-1 表示无

        /// <summary>本地玩家确认一次选择时触发（未来网络同步订阅点）</summary>
        public event Action<SelectionResult> OnLocalSelectionConfirmed;

        /// <summary>当前是否处于选择阶段</summary>
        public bool BIsActive { get; private set; }

        /// <summary>
        /// 本地玩家是否已完成选择。
        /// TODO: 联机接入后升级为「全员选择完毕」判定，届时需汇总各玩家的选择状态。
        /// </summary>
        public bool BIsLocalSelectionDone =>
            BIsActive && session != null && !string.IsNullOrEmpty(localPlayerKey) && session.BHasSelection(localPlayerKey);

        /// <summary>本地玩家选中的道具；未选中或阶段未开启时为 null</summary>
        public ItemBase LocalSelectedItem => localSelectedItem;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"{LOG_TAG} 场景中存在多个 PropSelectionDirector，已销毁重复实例。", this);
                Destroy(this);
                return;
            }
            _instance = this;

            if (selectionPanel != null)
            {
                selectionPanel.SetActive(false);
            }
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (!BIsActive || session == null)
            {
                return;
            }

            // 幂等屏蔽：覆盖阶段进行中才被生成/注册的角色（每帧仅按玩家数做几次字典查询）
            if (suppressAvatars)
            {
                avatarGate.Suppress();
            }

            // 联机：选择阶段进行中，化身晚于 BeginPhase 生成的玩家（快照兜底生成）补生成图标
            if (BNetMode)
            {
                iconSpawnCheckTimer += Time.deltaTime;
                if (iconSpawnCheckTimer >= 0.2f)
                {
                    iconSpawnCheckTimer = 0f;
                    SpawnMissingPlayerIcons();
                }
            }

            phaseElapsed += Time.deltaTime;
            UpdateCountdownText();
            TickFakePickers();
        }

        private float iconSpawnCheckTimer;

        /// <summary>为 registry 中已存在但还没有图标的玩家补生成图标（联机晚到化身）</summary>
        private void SpawnMissingPlayerIcons()
        {
            if (playerSpotAnchors == null || playerSpotAnchors.Count == 0) return;
            if (LevelPlayerRegistry.Instance == null) return;

            IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                // 同一化身只允许一个图标：已生成过但身份主键已变更（如联机 playerId 晚写入）时，
                // 迁移字典键而不是再生成一个，避免出现脱离管理的重复图标
                if (playerIconsByController.TryGetValue(player, out PropSelectionPlayerIcon existing) && existing != null)
                {
                    if (!playerIcons.TryGetValue(player.IdentityKey, out PropSelectionPlayerIcon current) || current != existing)
                    {
                        string staleKey = FindPlayerKeyByIcon(existing);
                        if (!string.IsNullOrEmpty(staleKey) && staleKey != player.IdentityKey)
                        {
                            playerIcons.Remove(staleKey);
                        }
                        playerIcons[player.IdentityKey] = existing;
                    }
                    continue;
                }

                int seat = GetRoomSeatIndex(player.IdentityKey, players.Count, i);
                PropSelectionPlayerIcon icon = CreatePlayerIcon(player, playerSpotAnchors[seat % playerSpotAnchors.Count]);
                if (icon != null)
                {
                    playerIcons[player.IdentityKey] = icon;
                    playerIconsByController[player] = icon;
                    Debug.Log($"{LOG_TAG} 补生成玩家图标: {player.IdentityKey} 座位={seat}");
                }
            }
        }

        // ==================== 阶段接口（供 GameFlow 调用） ====================

        /// <summary>
        /// 开启选择阶段（幂等）：抽取本轮候选、搭建选择面板、生成玩家图标、屏蔽角色、切换阶段镜头。
        /// </summary>
        public void BeginPhase()
        {
            if (BIsActive)
            {
                return;
            }

            session = new SelectionSession();
            session.OnOfferClaimed += HandleOfferClaimed;

            bool netMode = BNetMode;
            int rolled = 0;
            if (netMode)
            {
                // 联机模式：候选由服务器 ItemOfferList 下发，收到前只搭面板等发牌
                RegisterNetHandlers();
            }

            else
            {
                rolled = session.RollOffers(itemPool, offerCount);
                if (rolled == 0)
                {
                    Debug.LogWarning($"{LOG_TAG} 道具候选池为空，本阶段将无事可做并由倒计时兜底结束。", this);
                }
            }

            // 首轮发牌可能先于本阶段注册到达（由 NetGameFlowGate 缓存），此处补消费
            ItemOfferList pendingOffers = NetGameFlowGate.ConsumePendingOffers();
            Debug.Log($"{LOG_TAG} 消费缓存发牌: {(pendingOffers != null ? $"round={pendingOffers.Round} 道具数={pendingOffers.Offers.Count}" : "null")}");

            // 对局开始（大厅流程）：确保远程玩家档案已注册并生成化身，再生成图标
            NetGameFlowGate.EnsureRemotePlayersReady();

            // 联机：服务器阶段消息可能先于 LocalPlayerNetSetup 的 Update 到达，
            // 本地玩家 playerId 未写入时 IdentityKey 会回退为玩家名，
            // 导致图标按错误主键生成、身份写入后又被补生成一个（出现位置 index0 的重复图标）。
            // 进入阶段前立即写入本地网络身份，保证主键稳定。
            if (netMode)
            {
                LocalPlayerNetSetup.EnsureLocalIdentityNow();
            }

            localPlayerKey = ResolveLocalPlayerKey();
            localSelectedItem = null;
            phaseElapsed = 0f;

            // 旋转吐司尺寸须在选择阶段即确定（槽位角标要展示 1x1/2x2/3x3）：
            // 联机已由 NetGameFlowGate 按服务器轮次种子决定（先于本阶段进入）；
            // 单机无种子来源，此处本地随机（OnUploadSize 未挂钩时仅本地生效）
            if (!netMode && SuperQQ.Item.RotatingToastSizeSync.CurrentSize == 0)
            {
                SuperQQ.Item.RotatingToastSizeSync.DecideSizeLocally();
            }

            BuildPanel();
            if (!netMode)
            {
                BuildSlotViews();
            }
            SpawnPlayerIcons();
            SetupFakePickers();

            if (suppressAvatars)
            {
                avatarGate.Suppress();
            }

            activePhase = GamePhaseManager.Instance != null
                ? GamePhaseManager.Instance.CurrentPhaseAsset as PropSelectionPhase
                : null;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
            }
            SetSelectionCameraActive(true);

            BIsActive = true;
            Debug.Log($"{LOG_TAG} 进入选择阶段，候选道具 {rolled} 件");

            if (pendingOffers != null)
            {
                HandleServerOffers(pendingOffers);
            }
        }

        /// <summary>
        /// 结束选择阶段（幂等）：销毁槽位视图与玩家图标、隐藏面板、还原角色与镜头。
        /// </summary>
        public void EndPhase()
        {
            if (!BIsActive)
            {
                return;
            }

            if (session != null)
            {
                session.OnOfferClaimed -= HandleOfferClaimed;
                session = null;
            }
            UnregisterNetHandlers();
            SlotIntroVideoPlayer.Hide(); // 阶段退出：关闭介绍视频气泡

            ClearSlotViews();
            ClearPlayerIcons();
            HideConfirmCheck();
            HidePanel();
            fakePickers.Clear();
            avatarGate.Restore();

            activePhase = null;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
            }
            SetSelectionCameraActive(false);

            BIsActive = false;
            Debug.Log($"{LOG_TAG} 退出选择阶段");
        }

        // ==================== 选中入口 ====================

        /// <summary>
        /// 本地玩家点击槽位时发起选中（由 PropSelectionSlotView 回传）。
        /// 玩家图标先飞向槽位，到达后才正式认领生效。
        /// </summary>
        public void TrySelectLocal(int slotIndex)
        {
            if (!BIsActive || session == null || string.IsNullOrEmpty(localPlayerKey))
            {
                return;
            }

            if (BNetMode)
            {
                // 已确认认领（含服务器分配）后禁止再点任何道具；
                // 待确认期间（图标已飞到、未点打勾）允许改选：图标飞向新槽位，✔按钮跟随移动
                if (session.BHasSelection(localPlayerKey))
                {
                    return;
                }
                // 改选新槽位前，清掉旧的待确认状态
                if (confirmCheckSlot >= 0)
                {
                    HideConfirmCheck();
                }

                // 先上报认领意图（服务器透传广播，其他端让远端图标/化身也飞过去）
                NetworkManager net = NetworkManager.Instance;
                Debug.Log($"{LOG_TAG} 发送认领意图: slot={slotIndex} localKey={localPlayerKey} netPlayerId={net.LocalPlayerId}");
                net.Send(new ItemClaimIntent
                {
                    RoomId = net.RoomId,
                    PlayerId = net.LocalPlayerId,
                    SlotIndex = slotIndex
                });
            }

            TryInitiateClaim(localPlayerKey, slotIndex);
        }

        /// <summary>
        /// 应用远端玩家的选择（联机接入时由网络层调用；本地逻辑不使用）。
        /// 与本地选中走同一会话入口，互斥规则一致。
        /// </summary>
        public void ApplyRemoteSelection(string playerKey, int slotIndex)
        {
            if (!BIsActive || session == null)
            {
                return;
            }
            session.TrySelect(playerKey, slotIndex);
        }

        // ==================== 认领结果处理 ====================

        private void HandleOfferClaimed(SelectionResult result)
        {
            PropSelectionSlotView view = FindSlotView(result.SlotIndex);
            if (view != null)
            {
                view.SetClaimed(ResolvePlayerColor(result.PlayerKey), ResolvePlayerIcon(result.PlayerKey));
            }

            // 认领生效：隐藏该玩家飞行的 SelectionIconSprite 图标，
            // 此后该玩家的选择表现由槽位上的认领者头像（ClaimerIcon）与选中图标（SelectedIcon）接管
            if (playerIcons.TryGetValue(result.PlayerKey, out PropSelectionPlayerIcon claimedIcon)
                && claimedIcon != null)
            {
                claimedIcon.Stop();
                claimedIcon.gameObject.SetActive(false);
            }

            // 判断是否本地玩家：联机模式下 result.PlayerKey 是服务器 playerId，
            // 与 localPlayerKey（角色名）不同源，需用 NetworkManager.LocalPlayerId 对比
            bool isLocal = BNetMode
                ? (NetworkManager.Instance != null && result.PlayerKey == NetworkManager.Instance.LocalPlayerId)
                : (result.PlayerKey == localPlayerKey);

            if (isLocal)
            {
                // 联机认领的 key 是服务器 playerId，取值要用同一个 key
                string selfKey = BNetMode ? result.PlayerKey : localPlayerKey;
                localSelectedItem = session != null ? session.GetSelectedItem(selfKey) : null;
                string mappedId = (ItemCatalog.Instance != null && localSelectedItem != null)
                    ? ItemCatalog.Instance.GetItemId(localSelectedItem) ?? "(未在目录)"
                    : "(无目录)";
                Debug.Log($"{LOG_TAG} 本地选中的道具: prefab={(localSelectedItem != null ? localSelectedItem.name : "null")} 反查数字ID={mappedId} 槽位={result.SlotIndex}");

                // 本地玩家已完成认领：锁定全部槽位的本地点击（其他玩家的认领仍正常刷新表现）
                for (int i = 0; i < slotViews.Count; i++)
                {
                    if (slotViews[i] != null)
                    {
                        slotViews[i].SetLocalInputLocked(true);
                    }
                }

                OnLocalSelectionConfirmed?.Invoke(result);
            }

            Debug.Log($"{LOG_TAG} {result}");
        }

        /// <summary>槽位序号 → ItemCatalog 数字代号（无目录时回退 prefab 名；越界/空槽返回 null）</summary>
        private string ResolveItemIdAtSlot(int slotIndex)
        {
            if (session == null || slotIndex < 0 || slotIndex >= session.OfferItems.Count)
            {
                return null;
            }
            ItemBase item = session.OfferItems[slotIndex];
            if (item == null)
            {
                return null;
            }
            return ItemCatalog.Instance != null ? ItemCatalog.Instance.GetItemId(item) ?? item.name : item.name;
        }

        private PropSelectionSlotView FindSlotView(int slotIndex)
        {
            for (int i = 0; i < slotViews.Count; i++)
            {
                if (slotViews[i] != null && slotViews[i].SlotIndex == slotIndex)
                {
                    return slotViews[i];
                }
            }
            return null;
        }

        // ==================== 玩家图标 ====================

        /// <summary>
        /// 为关卡内每名玩家生成面板图标：Sprite 取自玩家配置的标识图、不染座位色（保留角色原始配色），
        /// 按注册顺序依次使用前 N 个出现位（3 名玩家固定用前 3 个位置；玩家数多于出现位时循环复用）。
        /// 图标层与自动生成的出现位整体挂在面板根下，EndPhase 时一并销毁。
        /// </summary>
        private void SpawnPlayerIcons()
        {
            playerIcons.Clear();
            playerIconsByController.Clear();

            Transform panelRoot = ResolvePanelRoot();
            if (panelRoot == null)
            {
                return;
            }

            var layerGo = new GameObject("PlayerIconLayer", typeof(RectTransform));
            iconLayer = (RectTransform)layerGo.transform;
            iconLayer.SetParent(panelRoot, false);
            StretchFull(iconLayer);
            iconLayer.SetAsLastSibling();   // 图标渲染在槽位之上

            List<RectTransform> anchors = ResolvePlayerAnchors();
            if (anchors.Count == 0)
            {
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            // 联机模式：座位由服务器进房顺序决定（房间玩家列表顺序即 color_index/seat 序号），
            // 两端分配规则一致，玩家图标位置必然对齐；单机模式沿用注册表顺序。
            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                int seat = BNetMode
                    ? GetRoomSeatIndex(player.IdentityKey, players.Count, i)
                    : i;

                PropSelectionPlayerIcon icon = CreatePlayerIcon(player, anchors[seat % anchors.Count]);
                if (icon != null)
                {
                    playerIcons[player.IdentityKey] = icon;
                    playerIconsByController[player] = icon;
                }
            }
        }

        /// <summary>
        /// 取玩家在房间中的座位序号（0~3）：
        /// ① 房间数据 RoomPlayerState.color_index（服务器按进房顺序分配，即座位号）；
        /// ② 房间玩家列表下标（进房顺序）；③ 玩家ID字典序兜底（两端规则一致即可）。
        /// </summary>
        private static int GetRoomSeatIndex(string identityKey, int playerCount, int fallback)
        {
            // 座位数据源优先级：最新房间快照 > JoinedRoom。
            // 判定规则：若列表中"存在 colorIndex 互不相同且非全 0"的有效分配，用 colorIndex；
            // 否则（后端未分配，全为 0）用玩家在列表中的下标（进房顺序，两端一致）。
            Network.NetworkManager net = Network.NetworkManager.Instance;
            Network.RoomSnapshotReceiver snapshotReceiver = UnityEngine.Object.FindFirstObjectByType<Network.RoomSnapshotReceiver>();
            Minigame.Room.V1.RoomSnapshot snapshot = snapshotReceiver != null ? snapshotReceiver.LatestSnapshot : null;

            if (snapshot != null && snapshot.Players.Count > 0)
            {
                int seat = FindSeatInList(snapshot.Players, identityKey);
                if (seat >= 0) return seat;
            }

            Minigame.Room.V1.Room room = net != null ? net.JoinedRoom : null;
            if (room != null && room.Players.Count > 0)
            {
                int seat = FindSeatInList(room.Players, identityKey);
                if (seat >= 0) return seat;
            }

            // 兜底：按玩家ID字典序排序（两端排序结果一致，保证位置对齐）
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                var ids = new List<string>();
                IReadOnlyList<PlayerController> all = registry.Players;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null) ids.Add(all[i].IdentityKey);
                }
                ids.Sort(System.StringComparer.Ordinal);
                int idx = ids.IndexOf(identityKey);
                if (idx >= 0) return idx;
            }

            return fallback % Mathf.Max(1, playerCount);
        }

        /// <summary>
        /// 在玩家列表中定位座位：若列表存在有效的 colorIndex 分配（非全 0 且互不重复）则用之，
        /// 否则用列表下标（进房顺序）。找不到该玩家返回 -1。
        /// </summary>
        private static int FindSeatInList(IReadOnlyList<Minigame.Room.V1.RoomPlayerState> players, string identityKey)
        {
            // 先判断 colorIndex 分配是否有效（全 0 = 后端未分配）
            bool hasValidColorIndex = false;
            var seen = new HashSet<int>();
            for (int i = 0; i < players.Count; i++)
            {
                int ci = players[i].ColorIndex;
                if (ci > 0 && seen.Add(ci))
                {
                    hasValidColorIndex = true;
                }
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Player?.PlayerId == identityKey)
                {
                    return hasValidColorIndex ? players[i].ColorIndex : i;
                }
            }
            return -1;
        }

        private void ClearPlayerIcons()
        {
            playerIcons.Clear();
            playerIconsByController.Clear();
            pendingClaims.Clear();
            if (iconLayer != null)
            {
                Destroy(iconLayer.gameObject);
                iconLayer = null;
            }
        }

        /// <summary>认领者的玩家图标飞向被认领槽位的中心；图标或槽位缺失时静默跳过</summary>
        private void MovePlayerIconToSlot(string playerKey, int slotIndex)
        {
            if (string.IsNullOrEmpty(playerKey)
                || !playerIcons.TryGetValue(playerKey, out PropSelectionPlayerIcon icon)
                || icon == null)
            {
                return;
            }

            PropSelectionSlotView view = FindSlotView(slotIndex);
            if (view == null)
            {
                return;
            }

            icon.MoveTo(GetRectWorldCenter((RectTransform)view.transform));
        }

        /// <summary>
        /// 发起一次认领：玩家图标先飞向目标槽位，到达后才执行 session 认领（选定逻辑以到达为准）。
        /// 该玩家已有生效认领或正在飞行认领中、目标槽位已被认领时忽略；
        /// 图标或槽位视图缺失时降级为直接认领。
        /// </summary>
        private void TryInitiateClaim(string playerKey, int slotIndex)
        {
            if (session == null || string.IsNullOrEmpty(playerKey))
            {
                return;
            }
            if (session.BHasSelection(playerKey) || pendingClaims.ContainsKey(playerKey) || session.BIsClaimed(slotIndex))
            {
                return;
            }

            PropSelectionSlotView view = FindSlotView(slotIndex);
            if (view != null
                && playerIcons.TryGetValue(playerKey, out PropSelectionPlayerIcon icon)
                && icon != null)
            {
                // 本地玩家图标起飞：先藏介绍气泡，到达新槽位后再播放
                if (playerKey == localPlayerKey)
                {
                    SlotIntroVideoPlayer.Hide();
                }
                pendingClaims[playerKey] = slotIndex;
                icon.MoveTo(GetRectWorldCenter((RectTransform)view.transform));
                return;
            }

            session.TrySelect(playerKey, slotIndex);
        }

        /// <summary>
        /// 玩家图标到达目标位置时结算飞行中的认领：
        /// 认领成功则选定生效；槽位已被他人抢先认领时，本地玩家飞回出现位可重新点击，模拟玩家改飞其它未认领槽位。
        /// </summary>
        private void HandleIconArrived(PropSelectionPlayerIcon icon)
        {
            string playerKey = FindPlayerKeyByIcon(icon);
            if (string.IsNullOrEmpty(playerKey) || !pendingClaims.TryGetValue(playerKey, out int slotIndex))
            {
                return;
            }
            pendingClaims.Remove(playerKey);

            // 本地玩家到达槽位：聊天气泡贴槽位右上角，循环播放该道具的介绍视频（纯本地表现）
            if (playerKey == localPlayerKey)
            {
                PropSelectionSlotView slotView = FindSlotView(slotIndex);
                SlotIntroVideoPlayer.Show(ResolveItemIdAtSlot(slotIndex),
                    slotView != null ? (RectTransform)slotView.transform : null);
            }

            if (session == null)
            {
                return;
            }

            // 联机模式：认领结果以服务器仲裁为准。到达后在道具上方显示打勾按钮，
            // 点击才向服务器发认领请求；期间仍可改点其他道具（打勾按钮跟随移动）。
            if (BNetMode)
            {
                if (playerKey == localPlayerKey)
                {
                    ShowConfirmCheck(slotIndex);
                }
                return;
            }

            if (session.TrySelect(playerKey, slotIndex))
            {
                return;
            }

            if (playerKey == localPlayerKey)
            {
                icon.MoveTo(icon.HomePos);
                return;
            }

            int fallbackSlot = PickRandomUnclaimedSlot();
            if (fallbackSlot >= 0)
            {
                TryInitiateClaim(playerKey, fallbackSlot);
            }
        }

        private string FindPlayerKeyByIcon(PropSelectionPlayerIcon icon)
        {
            foreach (KeyValuePair<string, PropSelectionPlayerIcon> pair in playerIcons)
            {
                if (pair.Value == icon)
                {
                    return pair.Key;
                }
            }
            return null;
        }

        private PropSelectionPlayerIcon CreatePlayerIcon(PlayerController player, RectTransform anchor)
        {
            var go = new GameObject($"PlayerIcon_{player.IdentityKey}", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(iconLayer, false);
            rect.sizeDelta = new Vector2(playerIconSize, playerIconSize);

            Image image = go.GetComponent<Image>();
            Sprite sprite = player.SelectionIconSprite;
            if (sprite == null)
            {
                Debug.LogWarning($"{LOG_TAG} 玩家 {player.PlayerName} 未配置选择阶段图标（Selection Icon Sprite），图标将显示为纯色块。", player);
            }
            image.sprite = sprite;
            image.color = Color.white;  // 不染座位色：保留角色图标原始配色（玩家身份由角色形象区分）
            image.preserveAspect = true;
            image.raycastTarget = false;    // 图标仅作表现，不拦截槽位点击

            rect.position = GetRectWorldCenter(anchor);

            var icon = go.AddComponent<PropSelectionPlayerIcon>();
            icon.Init(iconMaxSpeed, iconAcceleration);
            icon.MarkHome();
            icon.OnArrived += HandleIconArrived;
            return icon;
        }

        /// <summary>
        /// 解析玩家出现位：优先显式配置的列表；其次在面板下按 PlayerSpot 前缀名字查找；
        /// 仍无时自动在面板四角生成（挂在图标层下，随阶段结束销毁）。
        /// </summary>
        private List<RectTransform> ResolvePlayerAnchors()
        {
            if (playerSpotAnchors != null)
            {
                List<RectTransform> configured = new List<RectTransform>(playerSpotAnchors.Count);
                for (int i = 0; i < playerSpotAnchors.Count; i++)
                {
                    if (playerSpotAnchors[i] != null)
                    {
                        configured.Add(playerSpotAnchors[i]);
                    }
                }
                if (configured.Count > 0)
                {
                    return configured;
                }
            }

            if (selectionPanel != null)
            {
                List<RectTransform> found = new List<RectTransform>();
                RectTransform[] children = selectionPanel.GetComponentsInChildren<RectTransform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i] != selectionPanel.transform && children[i].name.StartsWith("PlayerSpot", StringComparison.Ordinal))
                    {
                        found.Add(children[i]);
                    }
                }
                if (found.Count > 0)
                {
                    // 按名字排序保证分配顺序稳定（PlayerSpot0/1/2/3...）
                    found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    return found;
                }
            }

            return CreateCornerAnchors();
        }

        /// <summary>在图标层四角自动生成 4 个出现位（靠近面板四角，对齐参考布局）</summary>
        private List<RectTransform> CreateCornerAnchors()
        {
            Vector2[] corners =
            {
                new Vector2(0.08f, 0.85f), new Vector2(0.92f, 0.85f),
                new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.15f),
            };

            List<RectTransform> anchors = new List<RectTransform>(corners.Length);
            for (int i = 0; i < corners.Length; i++)
            {
                var go = new GameObject($"PlayerSpot{i}", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(iconLayer, false);
                rect.anchorMin = corners[i];
                rect.anchorMax = corners[i];
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(playerIconSize, playerIconSize);
                anchors.Add(rect);
            }
            return anchors;
        }

        /// <summary>RectTransform 在屏幕/世界坐标下的几何中心（与 pivot 无关）</summary>
        private static Vector3 GetRectWorldCenter(RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        private Transform ResolvePanelRoot()
        {
            if (selectionPanel != null)
            {
                return selectionPanel.transform;
            }
            return runtimeBuiltPanel != null ? runtimeBuiltPanel.transform : null;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ==================== 面板与槽位搭建 ====================

        private void BuildPanel()
        {
            // 兜底接管：场景中可能摆着未接线的美术面板 prefab 实例（如 Level2 的 PropSelectionPanelNew，
            // 未激活时 GameObject.Find 找不到）——按名接管，避免退化成运行时简易面板
            if (selectionPanel == null)
            {
                TryAdoptArtPanel("PropSelectionPanelNew");
            }
            if (selectionPanel == null)
            {
                TryAdoptArtPanel("PropSelectionPanel");
            }

            if (selectionPanel != null)
            {
                selectionPanel.SetActive(true);
                if (slotsContainer == null || slotViewPrefab == null)
                {
                    Debug.LogWarning($"{LOG_TAG} 已配置选择面板但槽位容器或槽位 prefab 未配置，候选将无法展示。", this);
                }
                EnsureEventSystem();
                return;
            }

            BuildRuntimePanel();
        }

        /// <summary>层级深度查找子物体（含未激活）</summary>
        private static Transform FindDeepChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                Transform found = FindDeepChildByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>按名查找本场景节点（含未激活）</summary>
        private Transform FindSceneNodeByName(string name)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.name == gameObject.scene.name && go.name == name)
                {
                    return go.transform;
                }
            }
            return null;
        }

        /// <summary>按名接管场景中的美术面板（含未激活节点），并自动补齐槽位容器引用</summary>
        private void TryAdoptArtPanel(string panelName)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.name != gameObject.scene.name || go.name != panelName)
                {
                    continue;
                }
                selectionPanel = go;
                if (slotsContainer == null)
                {
                    // 槽位容器两种命名都兼容：SlotsContainer / PropSelectionContiner（prefab 内的拼写）
                    Transform slots = FindDeepChildByName(go.transform, "SlotsContainer")
                        ?? FindDeepChildByName(go.transform, "PropSelectionContiner");
                    if (slots == null)
                    {
                        // 面板内没有：容器可能是场景中的独立节点，全场景找
                        slots = FindSceneNodeByName("SlotsContainer")
                            ?? FindSceneNodeByName("PropSelectionContiner");
                    }
                    if (slots != null)
                    {
                        slotsContainer = slots as RectTransform;
                    }
                }
                Debug.Log($"{LOG_TAG} 接管场景美术面板: {panelName}（槽位容器={(slotsContainer != null ? slotsContainer.name : "未找到")}）", this);
                return;
            }
        }

        private void HidePanel()
        {
            if (runtimeBuiltPanel != null)
            {
                Destroy(runtimeBuiltPanel);
                runtimeBuiltPanel = null;
            }
            if (selectionPanel != null)
            {
                selectionPanel.SetActive(false);
            }
        }

        private void BuildSlotViews()
        {
            ClearSlotViews();

            RectTransform container = slotsContainer != null ? slotsContainer : runtimeSlotsContainer;
            if (container == null || session == null)
            {
                return;
            }

            IReadOnlyList<ItemBase> offers = session.OfferItems;
            for (int i = 0; i < offers.Count; i++)
            {
                ItemBase item = offers[i];
                PropSelectionSlotView view = CreateSlotView(container);
                if (view == null)
                {
                    continue;
                }

                view.Bind(this, i, item);
                slotViews.Add(view);
            }
        }

        private PropSelectionSlotView CreateSlotView(RectTransform container)
        {
            if (slotViewPrefab != null)
            {
                return Instantiate(slotViewPrefab, container);
            }
            return CreateRuntimeSlot(container);
        }

        private void ClearSlotViews()
        {
            for (int i = 0; i < slotViews.Count; i++)
            {
                if (slotViews[i] != null)
                {
                    Destroy(slotViews[i].gameObject);
                }
            }
            slotViews.Clear();
            runtimeSlotsContainer = null;
        }

        // ==================== 运行时简易面板（未配置正式 UI 时的降级方案） ====================

        private void BuildRuntimePanel()
        {
            EnsureEventSystem();

            // 画布
            var canvasGo = new GameObject("PropSelectionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            runtimeBuiltPanel = canvasGo;

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f; // 横屏统一匹配高度，与场景 Canvas 策略一致

            // 半透明背景
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            StretchFull((RectTransform)bgGo.transform);
            bgGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(bgGo.transform, false);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -60f);
            titleRect.sizeDelta = new Vector2(800f, 60f);
            var title = titleGo.GetComponent<TextMeshProUGUI>();
            title.text = "选择一件道具";
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 40f;
            title.color = Color.white;
            title.raycastTarget = false;

            // 槽位容器（网格布局，行列数按候选数量自适应）
            var gridGo = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(bgGo.transform, false);
            runtimeSlotsContainer = (RectTransform)gridGo.transform;
            runtimeSlotsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            runtimeSlotsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            runtimeSlotsContainer.pivot = new Vector2(0.5f, 0.5f);
            runtimeSlotsContainer.anchoredPosition = Vector2.zero;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(240f, 280f);
            grid.spacing = new Vector2(28f, 28f);
            grid.childAlignment = TextAnchor.MiddleCenter;

            int count = session != null ? session.OfferCount : 0;
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))));
            int rows = Mathf.CeilToInt(count / (float)columns);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            runtimeSlotsContainer.sizeDelta = new Vector2(
                columns * grid.cellSize.x + (columns - 1) * grid.spacing.x,
                rows * grid.cellSize.y + (rows - 1) * grid.spacing.y);
        }

        private PropSelectionSlotView CreateRuntimeSlot(Transform parent)
        {
            // 槽位根：Button + 背景
            var slotGo = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(Button));
            slotGo.transform.SetParent(parent, false);
            slotGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);

            // 道具图标（命名遵循 PropSelectionSlotView 的自动识别约定）
            var iconGo = new GameObject("ItemIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(slotGo.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0.1f, 0.28f);
            iconRect.anchorMax = new Vector2(0.9f, 0.97f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            Image icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;

            // 道具名称
            var nameGo = new GameObject("ItemNameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(slotGo.transform, false);
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0.26f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            var label = nameGo.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 24f;
            label.color = Color.black;
            label.raycastTarget = false;

            // 认领者颜色标记（右上角圆点，未认领时隐藏）
            var markerGo = new GameObject("ClaimMarker", typeof(RectTransform), typeof(Image));
            markerGo.transform.SetParent(slotGo.transform, false);
            var markerRect = (RectTransform)markerGo.transform;
            markerRect.anchorMin = new Vector2(1f, 1f);
            markerRect.anchorMax = new Vector2(1f, 1f);
            markerRect.pivot = new Vector2(1f, 1f);
            markerRect.anchoredPosition = new Vector2(-6f, -6f);
            markerRect.sizeDelta = new Vector2(32f, 32f);
            Image marker = markerGo.GetComponent<Image>();
            marker.raycastTarget = false;

            // 旋转吐司尺寸角标（图标右下角，非吐司道具时由视图自动隐藏）
            var badgeGo = new GameObject("SizeBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
            badgeGo.transform.SetParent(slotGo.transform, false);
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.anchorMin = new Vector2(0.5f, 0.24f);
            badgeRect.anchorMax = new Vector2(0.96f, 0.44f);
            badgeRect.offsetMin = Vector2.zero;
            badgeRect.offsetMax = Vector2.zero;
            var badge = badgeGo.GetComponent<TextMeshProUGUI>();
            badge.alignment = TextAlignmentOptions.Right;
            badge.fontSize = 18f;
            badge.fontStyle = FontStyles.Bold;
            badge.color = new Color(0.15f, 0.15f, 0.15f, 0.65f); // 半透明，弱化对图标的遮挡
            badge.outlineWidth = 0.12f;
            badge.outlineColor = new Color32(255, 255, 255, 115);
            badge.raycastTarget = false;

            // AddComponent 触发 Awake，此时子物体已齐备，视图按命名约定自动识别引用
            return slotGo.AddComponent<PropSelectionSlotView>();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            // 不 DontDestroyOnLoad：仅服务当前关卡场景的选择面板
            var _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ==================== 模拟玩家认领（仅调试） ====================

        private void SetupFakePickers()
        {
            fakePickers.Clear();
            // 联机模式下禁用：远端玩家的行为由网络驱动（意图/仲裁广播），
            // 本地模拟会把真实远端玩家当假玩家，导致其图标无操作自动飞向槽位
            if (!debugSimulateOtherPlayers || BNetMode)
            {
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null || player.BIsLocal)
                {
                    continue;
                }

                fakePickers.Add(new FakePicker
                {
                    PlayerKey = player.IdentityKey,
                    PickAt = UnityEngine.Random.Range(0.5f, 2.5f) + fakePickers.Count * 0.6f,
                });
            }
        }

        private void TickFakePickers()
        {
            for (int i = 0; i < fakePickers.Count; i++)
            {
                FakePicker picker = fakePickers[i];
                if (picker.PickAt < 0f || phaseElapsed < picker.PickAt)
                {
                    continue;
                }

                picker.PickAt = -1f;    // 标记已执行
                fakePickers[i] = picker;

                // 与本地玩家同规则：图标先飞向槽位，到达后才认领
                int slotIndex = PickRandomUnclaimedSlot();
                if (slotIndex >= 0)
                {
                    TryInitiateClaim(picker.PlayerKey, slotIndex);
                }
            }
        }

        private int PickRandomUnclaimedSlot()
        {
            int unclaimedCount = 0;
            int offerTotal = session.OfferCount;
            for (int i = 0; i < offerTotal; i++)
            {
                if (!session.BIsClaimed(i))
                {
                    unclaimedCount++;
                }
            }
            if (unclaimedCount == 0)
            {
                return -1;
            }

            int picked = UnityEngine.Random.Range(0, unclaimedCount);
            for (int i = 0; i < offerTotal; i++)
            {
                if (!session.BIsClaimed(i) && picked-- == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        // ==================== 联机打勾确认按钮（到达后二次确认，非模态） ====================

        /// <summary>
        /// 图标到达槽位后在该槽位上方显示打勾按钮：点击才向服务器发送认领请求。
        /// 非模态：期间仍可改点其他未认领槽位，图标飞过去、打勾按钮跟随移动。
        /// </summary>
        private void ShowConfirmCheck(int slotIndex)
        {
            PropSelectionSlotView view = FindSlotView(slotIndex);
            if (view == null)
            {
                // 槽位缺失时降级为直接确认，避免流程卡死
                SendClaimConfirm(slotIndex);
                return;
            }

            if (confirmCheckButton == null)
            {
                BuildConfirmCheckButton();
            }
            if (confirmCheckButton == null)
            {
                SendClaimConfirm(slotIndex);
                return;
            }

            confirmCheckSlot = slotIndex;

            // 定位到槽位正上方
            RectTransform slotRect = (RectTransform)view.transform;
            confirmCheckButton.SetParent(slotRect, false);
            confirmCheckButton.anchorMin = new Vector2(0.5f, 1f);
            confirmCheckButton.anchorMax = new Vector2(0.5f, 1f);
            confirmCheckButton.pivot = new Vector2(0.5f, 0f);
            confirmCheckButton.anchoredPosition = new Vector2(0f, 8f);
            confirmCheckButton.SetAsLastSibling();

            confirmCheckButton.gameObject.SetActive(true);
        }

        private void HideConfirmCheck()
        {
            confirmCheckSlot = -1;
            if (confirmCheckButton != null)
            {
                confirmCheckButton.gameObject.SetActive(false);
            }
        }

        /// <summary>打勾按钮回调：向服务器发送认领请求，等待仲裁结果（按钮保留显示直到结果返回）</summary>
        private void OnConfirmCheckClicked()
        {
            int slotIndex = confirmCheckSlot;
            if (slotIndex >= 0)
            {
                SendClaimConfirm(slotIndex);
            }
        }

        private void SendClaimConfirm(int slotIndex)
        {
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ItemClaimConfirm
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                SlotIndex = slotIndex,
                ClientTimeMs = NetworkManager.NowMs()
            });
        }

        /// <summary>搭建打勾按钮：优先实例化 Inspector 指定的 prefab，未指定时退回运行时搭建的默认样式（绿色圆角方块+白色对勾，挂到槽位上方）</summary>
        private void BuildConfirmCheckButton()
        {
            Transform panelRoot = ResolvePanelRoot();
            if (panelRoot == null)
            {
                return;
            }

            if (confirmCheckButtonPrefab != null)
            {
                Button instance = Instantiate(confirmCheckButtonPrefab, panelRoot, false);
                instance.name = confirmCheckButtonPrefab.name;
                instance.onClick.AddListener(OnConfirmCheckClicked);
                confirmCheckButton = (RectTransform)instance.transform;
                confirmCheckButton.gameObject.SetActive(false);
                return;
            }

            var btnGo = new GameObject("ClaimConfirmCheck", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panelRoot, false);
            confirmCheckButton = (RectTransform)btnGo.transform;
            confirmCheckButton.sizeDelta = new Vector2(72f, 72f);
            btnGo.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f, 0.95f);
            btnGo.GetComponent<Button>().onClick.AddListener(OnConfirmCheckClicked);

            var labelGo = new GameObject("Check", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "✔";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 44f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            confirmCheckButton.gameObject.SetActive(false);
        }

        // ==================== 联机同步（服务器仲裁模式） ====================

        /// <summary>是否处于联机模式：已连接且已进房时，候选发牌与认领仲裁均以服务器为准</summary>
        private static bool BNetMode =>
            NetworkManager.Instance != null
            && NetworkManager.Instance.IsConnected
            && !string.IsNullOrEmpty(NetworkManager.Instance.RoomId);

        private void RegisterNetHandlers()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null) return;
            net.Register<ItemOfferList>(HandleServerOffers);
            net.Register<ItemClaimIntentBroadcast>(HandleRemoteClaimIntent);
            net.Register<ItemClaimResult>(HandleClaimResult);
        }

        private void UnregisterNetHandlers()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null) return;
            net.Unregister<ItemOfferList>();
            net.Unregister<ItemClaimIntentBroadcast>();
            net.Unregister<ItemClaimResult>();
        }

        /// <summary>供 NetGameFlowGate 在其注册覆盖本 Director 注册的竞争场景下转发发牌（与直接收包等价）</summary>
        public void ReceiveOffers(ItemOfferList list)
        {
            HandleServerOffers(list);
        }

        /// <summary>收到服务器下发的道具列表：按 itemId 映射本地 prefab 并发牌建槽位</summary>
        // 本轮发牌的 itemId→prefab 解析结果（选择阶段解析成功后登记）：
        // 摆放阶段远端回放/虚影按数字 itemId 解析 prefab 时的兜底映射
        // （传送门等未登记 ItemCatalog 的道具，靠此表在远端也能实例化）
        private static readonly Dictionary<string, ItemBase> resolvedOfferPrefabs = new Dictionary<string, ItemBase>();

        /// <summary>摆放阶段兜底：从本轮发牌解析结果按 itemId 取 prefab（无则 null）</summary>
        public static ItemBase ResolveOfferPrefab(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && resolvedOfferPrefabs.TryGetValue(itemId, out ItemBase prefab)
                ? prefab : null;
        }

        private void HandleServerOffers(ItemOfferList list)
        {
            if (!BIsActive || session == null || session.OfferCount > 0)
            {
                return;
            }

            // 全量打印后端下发内容，便于核对座位/道具字段
            Debug.Log($"{LOG_TAG} ItemOfferList 原始内容: round={list.Round} offers={list.Offers.Count}\n{list}");

            // 新一轮发牌：重建解析映射（itemId 与 prefab 的对应以服务器每轮下发为准）
            resolvedOfferPrefabs.Clear();

            List<ItemBase> items = new List<ItemBase>(list.Offers.Count);
            foreach (ItemOffer offer in list.Offers)
            {
                ItemBase prefab = FindPoolItem(offer.ItemId);
                if (prefab == null)
                {
                    Debug.LogWarning($"{LOG_TAG} 服务器下发的道具 \"{offer.ItemId}\" 无法映射：目录与候选池中均不存在。"
                        + $"可用目录: {(ItemCatalog.Instance != null ? ItemCatalog.Instance.DumpIds() : "(未配置)")}；"
                        + $"候选池: [{string.Join(", ", itemPool.ConvertAll(p => p != null ? p.name : "null"))}]");
                    continue;
                }
                items.Add(prefab);
                resolvedOfferPrefabs[offer.ItemId] = prefab;
            }

            session.SetOffers(items);
            BuildSlotViews();
            Debug.Log($"{LOG_TAG} 收到服务器道具列表: round={list.Round} 共 {items.Count} 件");
        }

        /// <summary>
        /// 按网络 itemId 查找道具：先查 ItemCatalog 目录（服务器数字/字符串 ID 映射），
        /// 再按 prefab 名称匹配（兼容旧约定）。
        /// </summary>
        private ItemBase FindPoolItem(string itemId)
        {
            ItemBase fromCatalog = ItemCatalog.Instance != null ? ItemCatalog.Instance.Find(itemId) : null;
            if (fromCatalog != null)
            {
                return fromCatalog;
            }

            for (int i = 0; i < itemPool.Count; i++)
            {
                if (itemPool[i] != null && itemPool[i].name == itemId)
                {
                    return itemPool[i];
                }
            }

            // 兜底：从目录所有 prefab 里按 prefab 名匹配（服务器发名字而非数字ID时兜底）
            if (ItemCatalog.Instance != null)
            {
                ItemBase byName = ItemCatalog.Instance.FindByPrefabName(itemId);
                if (byName != null)
                {
                    return byName;
                }
            }
            return null;
        }

        /// <summary>远端玩家的认领意图：只驱动其图标飞向槽位做表现，不产生任何认领效果</summary>
        private void HandleRemoteClaimIntent(ItemClaimIntentBroadcast msg)
        {
            Debug.Log($"{LOG_TAG} 收到远端认领意图: playerId={msg.PlayerId} slot={msg.SlotIndex} localKey={localPlayerKey} 是自己={msg.PlayerId == localPlayerKey}");
            if (!BIsActive || msg.PlayerId == localPlayerKey)
            {
                return;
            }
            MovePlayerIconToSlot(msg.PlayerId, msg.SlotIndex);
        }

        /// <summary>服务器认领仲裁结果：成功方应用认领；本地失败则图标飞回出现位可重选</summary>
        private void HandleClaimResult(ItemClaimResult result)
        {
            Debug.Log($"{LOG_TAG} 收到认领结果: playerId={result.PlayerId} slot={result.SlotIndex} item={result.ItemId} success={result.Success} localKey={localPlayerKey}");
            if (!BIsActive || session == null)
            {
                return;
            }

            if (result.PlayerId == localPlayerKey)
            {
                HideConfirmCheck();
            }

            if (!result.Success)
            {
                if (result.PlayerId == localPlayerKey
                    && playerIcons.TryGetValue(localPlayerKey, out PropSelectionPlayerIcon icon)
                    && icon != null)
                {
                    icon.MoveTo(icon.HomePos);
                    Debug.Log($"{LOG_TAG} 认领槽位 {result.SlotIndex} 被抢，可重新选择");
                }
                return;
            }

            // 服务器已仲裁归属，本地会话应用结果（触发 HandleOfferClaimed 刷新表现）。
            // 注：服务器对未选择玩家的随机分配也走本条消息（success=true），
            // localSelectedItem 在 HandleOfferClaimed 中缓存，阶段退出时正常推入放置阶段。
            bool applied = session.TrySelect(result.PlayerId, result.SlotIndex);
            if (applied && result.PlayerId == localPlayerKey)
            {
                SlotIntroVideoPlayer.Hide(); // 本地认领生效：选择已定，关闭介绍气泡
                Debug.Log($"{LOG_TAG} 已认领槽位 {result.SlotIndex}（含服务器分配）");
            }
        }

        // ==================== 阶段摄像机 ====================

        /// <summary>
        /// 切换阶段镜头：通过优先级接管/归还 Cinemachine Brain 的主镜头。
        /// </summary>
        private void SetSelectionCameraActive(bool bActive)
        {
            if (selectionCamera == null)
            {
                return;
            }

            if (bActive)
            {
                selectionCameraOriginalPriority = selectionCamera.Priority;
                selectionCamera.Priority = selectionCameraPriority;
            }
            else
            {
                selectionCamera.Priority = selectionCameraOriginalPriority;
            }
        }

        // ==================== 倒计时显示 ====================

        /// <summary>
        /// 刷新倒计时文本：联机模式按服务器阶段结束时刻计算（两端锚点一致）；
        /// 单机模式取当前阶段条件的本地剩余时间。
        /// </summary>
        private void UpdateCountdownText()
        {
            if (countdownText == null)
            {
                return;
            }

            // 联机：以服务器 phase_end_time_ms 为锚点，与另一端显示严格一致
            if (BNetMode && NetGameFlowGate.CurrentPhaseEndTimeMs > 0)
            {
                long remainMs = NetGameFlowGate.CurrentPhaseEndTimeMs - NetworkManager.EstimatedServerNowMs();
                countdownText.text = Mathf.Max(0, Mathf.CeilToInt(remainMs / 1000f)).ToString();
                return;
            }

            if (activePhase == null)
            {
                // 未经 GameFlow 驱动（如调试时直接调用 BeginPhase）时没有倒计时来源
                countdownText.text = string.Empty;
                return;
            }

            countdownText.text = Mathf.CeilToInt(activePhase.RemainingTime).ToString();
        }

        // ==================== 玩家解析 ====================

        /// <summary>按认领者标识解析玩家颜色；未找到时返回白色</summary>
        private static Color ResolvePlayerColor(string playerKey)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].IdentityKey == playerKey)
                    {
                        return players[i].PlayerColor;
                    }
                }
            }
            return Color.white;
        }

        /// <summary>按认领者标识解析玩家 PlayerIcon（选择阶段标识图）；未找到时返回 null</summary>
        private static Sprite ResolvePlayerIcon(string playerKey)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].IdentityKey == playerKey)
                    {
                        return players[i].SelectionIconSprite;
                    }
                }
            }
            return null;
        }

        /// <summary>解析本地玩家角色；未找到时返回 null，不阻断选择流程</summary>
        private static PlayerController ResolveLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].BIsLocal)
                    {
                        return players[i];
                    }
                }
            }

            return null;
        }

        /// <summary>解析本地玩家标识；未找到本地角色时回退为固定标识，不阻断选择流程</summary>
        private static string ResolveLocalPlayerKey()
        {
            PlayerController localPlayer = ResolveLocalPlayer();
            if (localPlayer != null)
            {
                return localPlayer.IdentityKey;
            }

            Debug.LogWarning($"{LOG_TAG} 未找到本地玩家，选择结果将使用占位标识 Local。");
            return "Local";
        }
    }
}

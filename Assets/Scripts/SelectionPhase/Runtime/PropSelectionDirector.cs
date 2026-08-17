using System;
using System.Collections.Generic;
using Cinemachine;
using SuperQQ.GameFlow;
using SuperQQ.Item;
using SuperQQ.Placement.Runtime;
using SuperQQ.Player;
using SuperQQ.Selection.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

            phaseElapsed += Time.deltaTime;
            UpdateCountdownText();
            TickFakePickers();
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
            int rolled = session.RollOffers(itemPool, offerCount);
            if (rolled == 0)
            {
                Debug.LogWarning($"{LOG_TAG} 道具候选池为空，本阶段将无事可做并由倒计时兜底结束。", this);
            }

            localPlayerKey = ResolveLocalPlayerKey();
            localSelectedItem = null;
            phaseElapsed = 0f;

            BuildPanel();
            BuildSlotViews();
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

            ClearSlotViews();
            ClearPlayerIcons();
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
                view.SetClaimed(ResolvePlayerColor(result.PlayerKey));
            }

            // 图标归位：本地点击路径下图标已飞抵槽位（认领以到达为准），此处为空操作；
            // 远端/直接认领路径（ApplyRemoteSelection 等）由认领事件驱动图标飞过去
            MovePlayerIconToSlot(result.PlayerKey, result.SlotIndex);

            if (result.PlayerKey == localPlayerKey)
            {
                localSelectedItem = session != null ? session.GetSelectedItem(localPlayerKey) : null;

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
        /// 为关卡内每名玩家生成面板图标：Sprite 取自玩家配置的标识图、染玩家颜色，
        /// 按注册顺序依次使用前 N 个出现位（3 名玩家固定用前 3 个位置；玩家数多于出现位时循环复用）。
        /// 图标层与自动生成的出现位整体挂在面板根下，EndPhase 时一并销毁。
        /// </summary>
        private void SpawnPlayerIcons()
        {
            playerIcons.Clear();

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

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                PropSelectionPlayerIcon icon = CreatePlayerIcon(player, anchors[i % anchors.Count]);
                if (icon != null)
                {
                    playerIcons[player.IdentityKey] = icon;
                }
            }
        }

        private void ClearPlayerIcons()
        {
            playerIcons.Clear();
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

            if (session == null || session.TrySelect(playerKey, slotIndex))
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
            image.color = player.PlayerColor;
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
            if (!debugSimulateOtherPlayers)
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

        /// <summary>刷新倒计时文本：取当前阶段条件的剩余时间，向上取整显示秒数</summary>
        private void UpdateCountdownText()
        {
            if (countdownText == null)
            {
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

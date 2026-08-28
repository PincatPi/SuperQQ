using System;
using System.Collections.Generic;
using Cinemachine;
using Minigame.Room.V1;
using SuperQQ.GameFlow;
using SuperQQ.Grid;
using SuperQQ.Item;
using SuperQQ.Network;
using SuperQQ.Placement.Core;
using SuperQQ.Player;
using SuperQQ.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Placement.Runtime
{
    /// <summary>
    /// 道具放置阶段场景门面（场景级单例）。
    /// 对 GameFlow 只暴露 <see cref="BeginPhase"/> / <see cref="EndPhase"/> 与只读状态查询，
    /// 内部负责：本地放置会话的生命周期、鼠标输入采集、网格显隐、角色屏蔽与阶段镜头切换。
    ///
    /// 流程：进入阶段时给本地玩家发放一件道具（本期从候选池随机，后续由道具选择阶段推入）→
    ///       道具立即跟随鼠标并吸附网格（绿/红虚线框提示落点）→
    ///       左键确认 / R 旋转 / Esc 取消（取消后道具重新跟随鼠标）；
    ///       成对道具（传送门）确认后自动衔接摆放。
    ///
    /// 联机预留：放置结果与本地光标位置通过 <see cref="OnLocalPlacementConfirmed"/> /
    /// <see cref="OnLocalPointerMoved"/> 对外发布，接入网络时新增订阅者即可，本类无需改动。
    /// </summary>
    public class PropPlacementDirector : MonoBehaviour
    {
        private const string LOG_TAG = "[PropPlacement]";

        private static PropPlacementDirector _instance;

        /// <summary>场景内的放置阶段门面实例</summary>
        public static PropPlacementDirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PropPlacementDirector>();
                }
                return _instance;
            }
        }

        [Header("道具候选池")]
        [Tooltip("本轮随机发牌的候选道具（挂有 ItemBase 的 prefab）；道具选择阶段实现后由 SetPendingItem 推入，不再随机")]
        [SerializeField] private List<ItemBase> itemPool = new List<ItemBase>();

        [Header("落点提示颜色")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.3f, 0.9f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        [Header("操作按键")]
        [SerializeField] private KeyCode rotateKey = KeyCode.R;
        [SerializeField] private KeyCode cancelKey = KeyCode.Escape;

        [Header("倒计时显示")]
        [Tooltip("显示阶段剩余秒数的文本；留空则不显示倒计时")]
        [SerializeField] private TMP_Text countdownText;

        [Header("阶段摄像机")]
        [Tooltip("放置阶段使用的固定视角 Virtual Camera；留空则不切换镜头")]
        [SerializeField] private CinemachineVirtualCamera placementCamera;
        [Tooltip("放置阶段生效时 placementCamera 的优先级，需高于游玩镜头（默认 10）")]
        [SerializeField] private int placementCameraPriority = 20;

        [Header("光标玩家标记")]
        [Tooltip("未摆放道具时标记相对光标的世界坐标偏移")]
        [SerializeField] private Vector2 cursorMarkerOffset = new Vector2(0.8f, 0.8f);
        [Tooltip("摆放中标记贴道具包围盒左边中点时的外扩距离（世界单位，x 向左、y 向上微调）")]
        [SerializeField] private Vector2 markerCornerInset = new Vector2(0.15f, 0f);
        [Tooltip("标记的 Sorting Order，需高于网格与虚线框（默认为 10）")]
        [SerializeField] private int cursorMarkerSortingOrder = 100;
        [Tooltip("光标标记在世界空间中的显示高度（世界单位）；按此值自动缩放 Sprite，避免 PPU 不同导致过大")]
        [SerializeField] private float cursorMarkerWorldSize = 1f;
        [Tooltip("标记的 Sorting Layer（地图地形在 Map 层，标记必须在 Item 层才不会被地形遮挡）")]
        [SerializeField] private string cursorMarkerSortingLayer = "Item";

        private SpriteRenderer cursorMarker;

        private readonly PlayerAvatarGate avatarGate = new PlayerAvatarGate();
        private PlacementSession localSession;
        private ItemBase pendingItem;           // 由 SetPendingItem 预置的道具（未指定时从候选池随机）
        private PropPlacementPhase activePhase; // 驱动本阶段的阶段资产（倒计时数据源）
        private Camera inputCamera;
        private int placementCameraOriginalPriority;
        private int selectFrame = -1;           // 取出道具发生的帧号（避免同帧点击直接误确认）
        private Vector2 lastPointerWorld;

        [Header("联机同步")]
        [Tooltip("拖拽状态上报频率（次/秒）")]
        [SerializeField] private float placeStateReportRate = 10f;

        [Header("操作按钮 UI")]
        [Tooltip("打勾确认按钮 prefab（需挂 Button）；留空则运行时搭建默认样式")]
        [SerializeField] private Button confirmPlaceButtonPrefab;
        [Tooltip("旋转按钮 prefab（需挂 Button）；留空则运行时搭建默认样式")]
        [SerializeField] private Button rotatePlaceButtonPrefab;
        [Tooltip("确认按钮相对道具包围盒顶部中点的偏移（像素）")]
        [SerializeField] private Vector2 confirmButtonOffset = new Vector2(70f, 24f);
        [Tooltip("旋转按钮相对道具包围盒顶部中点的偏移（像素）")]
        [SerializeField] private Vector2 rotateButtonOffset = new Vector2(-70f, 24f);

        // ---- 联机状态 ----
        private float placeStateTimer;                                  // 拖拽上报节流
        private bool awaitingPlaceResult;                               // 已发确认、等待服务器仲裁
        private Button confirmPlaceButton;                              // 打勾确认按钮（prefab 实例或运行时搭建，跟随摆放中道具）
        private Button rotateButton;                                    // 旋转按钮（prefab 实例或运行时搭建，跟随摆放中道具）
        private Canvas actionButtonCanvas;                              // 操作按钮所在 Canvas（世界坐标→UI 坐标换算用）
        private readonly Dictionary<string, GameObject> remoteGhosts = new(); // playerId -> 远端玩家摆放中的虚化道具
        private readonly Dictionary<string, string> remoteGhostItemIds = new();
        private readonly Dictionary<string, SpriteRenderer> remoteCursors = new(); // playerId -> 远端玩家的光标标记

        /// <summary>本地玩家确认一次放置时触发（未来网络同步订阅点）</summary>
        public event Action<PlacementResult> OnLocalPlacementConfirmed;

        /// <summary>本地光标世界坐标变化时触发（未来他人光标同步订阅点）</summary>
        public event Action<Vector2> OnLocalPointerMoved;

        /// <summary>当前是否处于放置阶段</summary>
        public bool BIsActive { get; private set; }

        /// <summary>本地玩家的放置会话（未处于放置阶段时为 null）</summary>
        public PlacementSession LocalSession => localSession;

        /// <summary>
        /// 本地玩家是否已放置完毕（道具已确认且无待确认摆放）。
        /// TODO: 联机接入后升级为「全员放置完毕」判定，届时需汇总各玩家的放置状态。
        /// </summary>
        public bool BIsLocalHandExhausted => BIsActive && localSession != null && localSession.BIsFinished;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"{LOG_TAG} 场景中存在多个 PropPlacementDirector，已销毁重复实例。", this);
                Destroy(this);
                return;
            }
            _instance = this;

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
            if (!BIsActive || localSession == null)
            {
                return;
            }

            // 幂等屏蔽：覆盖阶段进行中才被生成/注册的角色（每帧仅按玩家数做几次字典查询）
            avatarGate.Suppress();

            UpdateCountdownText();
            TryBeginPendingItem();
            PollDrag();
            PollHotkeys();
            TickPlaceStateReport();
        }

        // ==================== 阶段接口（供 GameFlow 调用） ====================

        /// <summary>
        /// 开启放置阶段（幂等）：显示网格、发放本轮道具并立即跟随鼠标、屏蔽角色、切换阶段镜头。
        /// </summary>
        public void BeginPhase()
        {
            if (BIsActive)
            {
                return;
            }

            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                Debug.LogError($"{LOG_TAG} 场景中缺少 GridManager，放置阶段不会开启（阶段倒计时仍照常推进）。", this);
                return;
            }

            ItemBase item = pendingItem != null ? pendingItem : PickRandomItem();
            pendingItem = null;
            if (item == null)
            {
                Debug.LogWarning($"{LOG_TAG} 道具候选池为空，本阶段将无事可做并由提前完成条件立即结束。", this);
            }

            localSession = new PlacementSession(ResolveLocalPlayerKey(), validColor, invalidColor);
            localSession.OnPlacementConfirmed += HandlePlacementConfirmed;
            localSession.OnPlacementRejected += HandlePlacementRejected;
            localSession.Deal(item);

            grid.ShowGrid();
            // 摆放阶段开启禁区标红：起点/终点区域（SpawnGoal 区，不可布置道具）
            grid.SetOccupiedOverlay(true);
            avatarGate.Suppress();
            SetPlacementCameraActive(true);
            ShowCursorMarker();

            activePhase = GamePhaseManager.Instance != null
                ? GamePhaseManager.Instance.CurrentPhaseAsset as PropPlacementPhase
                : null;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
            }

            selectFrame = -1;
            BIsActive = true;

            if (BNetMode)
            {
                RegisterNetHandlers();
                ShowConfirmPlaceButton();
            }

            // 旋转吐司：摆放阶段开始即决定本轮尺寸（在道具实例生成/拖拽占格判定之前）。
            // 联机尺寸由服务器轮次种子决定（NetGameFlowGate 进选择/摆放阶段时），各端一致；
            // 联机下绝不允许本地随机兜底——各端各随机必不一致，同锚点不同 footprint
            // 表现为两端吐司格子位置不同。单机/测试场景才本地随机。
            if (item is SuperQQ.Item.RotatingToast && SuperQQ.Item.RotatingToastSizeSync.CurrentSize == 0)
            {
                if (!BNetMode)
                {
                    int size = SuperQQ.Item.RotatingToastSizeSync.DecideSizeLocally();
                    Debug.Log($"{LOG_TAG} 摆放阶段开始，本地随机吐司尺寸: {size}");
                }
                else
                {
                    Debug.LogError($"{LOG_TAG} 联机摆放阶段吐司尺寸未决定（选择阶段种子未生效），本端吐司将用 prefab 默认尺寸，可能与远端不一致！");
                }
            }

            // 发牌后立即开始跟随鼠标（等下一帧 Update 会晚一拍）
            TryBeginPendingItem();
            string dealId = (ItemCatalog.Instance != null && item != null)
                ? ItemCatalog.Instance.GetItemId(item) ?? "(未在目录)"
                : "(无目录)";
            Debug.Log($"{LOG_TAG} 进入放置阶段，发放道具：{(item != null ? item.name : "无")} 数字ID={dealId}");
        }

        /// <summary>
        /// 结束放置阶段（幂等）：丢弃未确认的摆放与未取出的道具、隐藏网格、还原角色与镜头。
        /// </summary>
        public void EndPhase()
        {
            if (!BIsActive)
            {
                return;
            }

            if (localSession != null)
            {
                localSession.DiscardUnconfirmed();
                localSession.OnPlacementConfirmed -= HandlePlacementConfirmed;
                localSession.OnPlacementRejected -= HandlePlacementRejected;
                localSession = null;
            }

            UnregisterNetHandlers();
            HideConfirmPlaceButton();
            ClearRemoteGhosts();
            awaitingPlaceResult = false;

            GridManager.Instance?.HideGrid();
            avatarGate.Restore();
            SetPlacementCameraActive(false);
            HideCursorMarker();

            activePhase = null;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
            }

            BIsActive = false;
            Debug.Log($"{LOG_TAG} 退出放置阶段");
        }

        /// <summary>
        /// 指定下一次放置使用的道具。
        /// 道具选择阶段实现后由其在阶段切换前推入；未调用时从候选池随机发放。
        /// TODO: 联机接入后道具应由服务端统一下发，保证各客户端发牌结果一致。
        /// </summary>
        public void SetPendingItem(ItemBase item)
        {
            pendingItem = item;

            // 阶段已激活时直接补发给当前会话
            if (BIsActive && localSession != null && !localSession.BHasPendingItem && !localSession.BIsPlacing)
            {
                localSession.Deal(item);
            }
        }

        // ==================== 输入采集 ====================

        [Header("拖拽手感（移动端防手指遮挡）")]
        [Tooltip("拖拽时道具相对手指的悬浮高度（屏幕像素）；横屏手机建议 160~220，0=关闭悬浮")]
        [SerializeField] private float pointerLiftPixels = 200f;
        [Tooltip("按下后移动超过该距离（像素）才开始悬浮抬起，避免按下瞬间道具跳起")]
        [SerializeField] private float dragThresholdPixels = 12f;
        [Tooltip("悬浮偏移渐入时间（秒）：拖动开始后偏移平滑生效，而非瞬移")]
        [SerializeField] private float liftRampTime = 0.12f;

        // 拖拽状态：道具仅在按住拖拽时跟随指针，松开后停留在原地（适配触屏与鼠标）
        private bool isDragging;
        private Vector2 dragPressScreenPos; // 按下瞬间的屏幕坐标（拖拽阈值判定用）
        private float liftFactor;           // 悬浮偏移渐入系数（0=未抬起 1=完全抬起）
        private bool liftEngaged;           // 移动距离已越过阈值，悬浮偏移生效中

        /// <summary>
        /// 有待放置道具时取出开始摆放。触屏/拖拽模式（联机）下不自动取出，
        /// 由 PollDrag 的按下手势触发取出，避免道具无故跟随手指。
        /// </summary>
        private void TryBeginPendingItem()
        {
            if (localSession.BIsPlacing || !localSession.BHasPendingItem)
            {
                return;
            }
            if (BNetMode)
            {
                return; // 联机（触屏/拖拽）模式：等待按下手势再取出
            }

            if (localSession.BeginPlace(PointerWorldPos()))
            {
                // 防止取出的同帧点击（含触发阶段切换的那次点击）直接确认
                selectFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// 拖拽驱动摆放：按下开始拖拽并取出道具，拖动期间道具跟随指针，
        /// 松开后道具停留在原地。确认统一由打勾按钮（联机）或左键（单机）发起。
        /// 同时更新光标标记位置与光标位置变化事件。
        /// </summary>
        private void PollDrag()
        {
            Vector2 pointerWorld = PointerWorldPos();

            // 按下：开始拖拽；若还有待放置道具则取出
            if (Input.GetMouseButtonDown(0))
            {
                // 【临时诊断】点击未生效排查：记录每次按下的拦截原因，定位后删除
                if (IsPointerOverUI())
                {
                    Debug.Log($"{LOG_TAG} 按下被 UI 拦截: {DescribeBlockingUI()}");
                }
                else
                {
                    Debug.Log($"{LOG_TAG} 按下生效: BIsPlacing={localSession.BIsPlacing} BHasPendingItem={localSession.BHasPendingItem}");
                }
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                isDragging = true;
                dragPressScreenPos = Input.mousePosition;
                liftFactor = 0f;
                liftEngaged = false;
                HideConfirmPlaceButton(); // 开始拖动：隐藏确认/旋转按钮
                if (!localSession.BIsPlacing && localSession.BHasPendingItem)
                {
                    if (localSession.BeginPlace(pointerWorld))
                    {
                        selectFrame = Time.frameCount;
                        Debug.Log($"{LOG_TAG} 取出道具开始拖拽: {localSession.CurrentItemId}");
                    }
                    else
                    {
                        Debug.LogWarning($"{LOG_TAG} BeginPlace 失败（GridManager 缺失或状态异常）");
                    }
                }
            }

            // 松开：结束拖拽，道具停留在当前位置
            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
                liftFactor = 0f;
                liftEngaged = false;
                // 停止拖动：确认/旋转按钮出现在道具上方（仅联机模式搭建了按钮时生效）
                ShowActionButtonsAboveItem();
            }

            // 拖拽中：道具跟随指针（叠加悬浮偏移，移动端不被手指遮挡）
            if (isDragging && localSession.BIsPlacing)
            {
                localSession.UpdatePointer(ApplyPointerLift(pointerWorld));
            }

            // 光标标记与事件（摆放中贴道具左下角，未摆放时跟随指针）
            if (cursorMarker != null && cursorMarker.enabled)
            {
                cursorMarker.transform.position = ResolveLocalMarkerPos(pointerWorld);
            }
            if (pointerWorld != lastPointerWorld)
            {
                lastPointerWorld = pointerWorld;
                OnLocalPointerMoved?.Invoke(pointerWorld);
            }
        }

        /// <summary>【临时诊断】返回当前拦截指针射线的第一个 UI 对象路径，定位后删除</summary>
        private static string DescribeBlockingUI()
        {
            if (EventSystem.current == null)
            {
                return "(无 EventSystem)";
            }

            var eventData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            if (results.Count == 0)
            {
                return "(射线无命中)";
            }

            Transform t = results[0].gameObject.transform;
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        /// <summary>指针是否悬停在 UI 上（触屏与鼠标统一判断）</summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;
            if (Input.touchCount > 0)
            {
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }
            return EventSystem.current.IsPointerOverGameObject();
        }

        private void PollHotkeys()
        {
            if (!localSession.BIsPlacing)
            {
                return;
            }

            if (Input.GetKeyDown(cancelKey))
            {
                localSession.Cancel();
                HideConfirmPlaceButton();
                return;
            }
            if (Input.GetKeyDown(rotateKey))
            {
                localSession.Rotate();
                ShowActionButtonsAboveItem(); // 旋转后包围盒尺寸变化，按钮位置跟随刷新
            }

            // 左键确认：取出道具的同帧、以及指针悬停在 UI 上时不触发。
            // 联机模式（含 PC 调试）只允许打勾按钮确认——屏幕点击统一作为拖拽开始手势，
            // 避免"点屏幕想拖拽却直接确认"的手势冲突；单机模式保留左键确认。
            bool allowClickConfirm = !BNetMode;
            if (allowClickConfirm
                && Input.GetMouseButtonDown(0)
                && Time.frameCount != selectFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                if (BNetMode)
                {
                    OnConfirmPlaceClicked(); // 联机：发 ItemPlaceConfirm 等服务器仲裁
                }
                else
                {
                    localSession.Confirm();
                }
            }
        }

        /// <summary>
        /// 悬浮抬起（移动端防手指遮挡）：按下后先跟随手指（liftFactor=0，点选不跳起），
        /// 移动越过阈值后偏移渐入，道具悬在手指上方；道具的视觉位置即逻辑落点（玩家用道具本体瞄准），
        /// 确认/取消/联机上报均以抬起后的位置为准，无跳变
        /// </summary>
        private Vector2 ApplyPointerLift(Vector2 pointerWorld)
        {
            if (pointerLiftPixels <= 0f || inputCamera == null)
            {
                return pointerWorld;
            }

            if (!liftEngaged
                && Vector2.Distance(dragPressScreenPos, Input.mousePosition) >= dragThresholdPixels)
            {
                liftEngaged = true;
            }
            if (liftEngaged)
            {
                liftFactor = Mathf.MoveTowards(liftFactor, 1f,
                    liftRampTime > 0f ? Time.deltaTime / liftRampTime : float.MaxValue);
            }
            if (liftFactor <= 0f)
            {
                return pointerWorld;
            }

            // 屏幕像素 → 世界高度（双点采样，与相机正交尺寸/分辨率无关）
            float depth = -inputCamera.transform.position.z;
            Vector3 a = inputCamera.ScreenToWorldPoint(new Vector3(0f, 0f, depth));
            Vector3 b = inputCamera.ScreenToWorldPoint(new Vector3(0f, pointerLiftPixels, depth));
            return pointerWorld + Vector2.up * (Mathf.Abs(b.y - a.y) * liftFactor);
        }

        /// <summary>
        /// 本地玩家标记位置：摆放中贴道具包围盒左边中点（道具悬浮抬起后与手指有固定距离，
        /// 继续跟随手指会和道具拉开）；未摆放时跟随光标
        /// </summary>
        private Vector2 ResolveLocalMarkerPos(Vector2 pointerWorld)
        {
            PlacementController pc = localSession != null && localSession.BIsPlacing
                ? localSession.CurrentPlacementController : null;
            if (pc != null && GridManager.Instance != null)
            {
                FootprintBoxView box = pc.GetComponent<FootprintBoxView>();
                return ItemBoxLeftMiddle(pc.transform.position, box, pc.RotationSteps)
                    + new Vector2(-markerCornerInset.x, markerCornerInset.y);
            }
            return pointerWorld + cursorMarkerOffset;
        }

        /// <summary>道具包围盒（footprint 矩形）左边中点的世界坐标；rootPos 为根节点（框中心）</summary>
        private static Vector2 ItemBoxLeftMiddle(Vector2 rootPos, FootprintBoxView box, int rotationSteps)
        {
            if (box == null || GridManager.Instance == null)
            {
                return rootPos;
            }
            Vector2Int size = GridManager.GetRotatedSize(box.Footprint, rotationSteps);
            float half = GridManager.Instance.PublicCellSize * 0.5f;
            return rootPos - new Vector2(size.x * half, 0f);
        }

        /// <summary>由 transform 的 Z 轴欧拉角反推旋转档（0=0° 1=顺时针90° 2=180° 3=270°）</summary>
        private static int RotationStepsFromTransform(Transform t)
        {
            int steps = Mathf.RoundToInt(-t.eulerAngles.z / 90f);
            return ((steps % 4) + 4) % 4;
        }

        /// <summary>鼠标当前的世界坐标（2D 平面）</summary>
        private Vector2 PointerWorldPos()
        {
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
            if (inputCamera == null)
            {
                Debug.LogWarning($"{LOG_TAG} 场景中缺少主摄像机，无法换算鼠标世界坐标。", this);
                return lastPointerWorld;
            }

            Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x, Input.mousePosition.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        // ==================== 联机同步（拖拽广播 + 服务器占用仲裁） ====================

        /// <summary>是否处于联机模式：已连接且已进房时，拖拽同步与摆放确认以服务器为准</summary>
        private static bool BNetMode =>
            NetworkManager.Instance != null
            && NetworkManager.Instance.IsConnected
            && !string.IsNullOrEmpty(NetworkManager.Instance.RoomId);

        private void RegisterNetHandlers()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null) return;
            net.Register<ItemPlaceStateBroadcast>(HandleRemotePlaceState);
            net.Register<ItemPlaceResult>(HandlePlaceResult);
            net.Register<ItemDemolishResult>(HandleDemolishResult);
        }

        private void UnregisterNetHandlers()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null) return;
            net.Unregister<ItemPlaceStateBroadcast>();
            net.Unregister<ItemPlaceResult>();
            net.Unregister<ItemDemolishResult>();
        }

        /// <summary>节流上报本地摆放中道具的位置/朝向（远端据此显示虚化道具跟随）</summary>
        private void TickPlaceStateReport()
        {
            if (!BNetMode || localSession == null || !localSession.BIsPlacing || awaitingPlaceResult)
            {
                return;
            }

            placeStateTimer += Time.deltaTime;
            if (placeStateTimer < 1f / placeStateReportRate)
            {
                return;
            }
            placeStateTimer = 0f;

            Vector2? pos = localSession.CurrentPosition;
            if (pos == null)
            {
                return;
            }

            NetworkManager net = NetworkManager.Instance;
            net.Send(new ItemPlaceState
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                ItemId = localSession.CurrentItemId,
                Position = new Minigame.Room.V1.Vector2 { X = pos.Value.x, Y = pos.Value.y },
                Rotation = localSession.CurrentRotation,
                Mirrored = localSession.CurrentMirrored
            });
        }

        // ---------- 远端玩家拖拽表现（虚化道具） ----------

        private void HandleRemotePlaceState(ItemPlaceStateBroadcast msg)
        {
            if (!BIsActive || msg.PlayerId == NetworkManager.Instance.LocalPlayerId)
            {
                return;
            }

            GameObject ghost = GetOrCreateRemoteGhost(msg.PlayerId, msg.ItemId);
            if (ghost == null)
            {
                return;
            }

            ghost.transform.position = new Vector3(msg.Position.X, msg.Position.Y, 0f);
            ghost.transform.rotation = GridManager.GetRotationQuaternion(msg.Rotation);
            // 镜像朝向同步（樱桃发射器/流星锤等）
            ItemBase ghostItem = ghost.GetComponent<ItemBase>();
            if (ghostItem != null)
            {
                ghostItem.SetMirrored(msg.Mirrored);
            }

            // 远端玩家的光标标记：跟随其道具位置（与本地 Cursor 一致的偏移与配色）
            ShowRemoteCursor(msg.PlayerId, new Vector2(msg.Position.X, msg.Position.Y));
        }

        /// <summary>显示/更新远端玩家的光标标记（Sprite 取自该玩家化身的 CursorMarkerSprite，颜色用其玩家色）</summary>
        private void ShowRemoteCursor(string playerId, Vector2 itemPos)
        {
            PlayerController remote = FindPlayerByIdentity(playerId);
            Sprite sprite = remote != null ? remote.CursorMarkerSprite : null;
            if (sprite == null)
            {
                return; // 化身未生成或未配置标识图时静默跳过
            }

            if (!remoteCursors.TryGetValue(playerId, out SpriteRenderer cursor) || cursor == null)
            {
                var go = new GameObject($"RemoteCursor_{playerId}");
                go.transform.SetParent(transform, false);
                cursor = go.AddComponent<SpriteRenderer>();
                cursor.sortingOrder = cursorMarkerSortingOrder;
                cursor.sortingLayerName = cursorMarkerSortingLayer;
                remoteCursors[playerId] = cursor;
            }

            cursor.sprite = sprite;
            cursor.color = remote.PlayerColor;
            ApplyCursorMarkerScale(cursor);
            // 贴远端虚影道具的包围盒左边中点（与本地标记行为一致）；虚影缺失时退回根位置偏移
            Vector2 markerPos = itemPos + cursorMarkerOffset;
            if (remoteGhosts.TryGetValue(playerId, out GameObject ghost) && ghost != null)
            {
                FootprintBoxView ghostBox = ghost.GetComponent<FootprintBoxView>();
                markerPos = ItemBoxLeftMiddle(ghost.transform.position, ghostBox,
                    RotationStepsFromTransform(ghost.transform))
                    + new Vector2(-markerCornerInset.x, markerCornerInset.y);
            }
            cursor.transform.position = markerPos;
            cursor.enabled = true;
        }

        /// <summary>按 playerId 在注册表中找玩家化身</summary>
        private static PlayerController FindPlayerByIdentity(string playerId)
        {
            if (LevelPlayerRegistry.Instance == null) return null;
            System.Collections.Generic.IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].IdentityKey == playerId)
                {
                    return players[i];
                }
            }
            return null;
        }

        /// <summary>按玩家获取/创建其摆放中的虚化道具（itemId 变化时重建）</summary>
        private GameObject GetOrCreateRemoteGhost(string playerId, string itemId)
        {
            if (remoteGhosts.TryGetValue(playerId, out GameObject ghost)
                && ghost != null
                && remoteGhostItemIds.TryGetValue(playerId, out string oldId)
                && oldId == itemId)
            {
                return ghost;
            }

            DestroyRemoteGhost(playerId);

            ItemBase prefab = FindPoolItem(itemId);
            if (prefab == null)
            {
                Debug.LogWarning($"{LOG_TAG} 远端道具 {itemId} 不在本地候选池中，跳过虚化显示。");
                return null;
            }

            ghost = Instantiate(prefab.gameObject);
            ghost.name = $"RemoteGhost_{playerId}_{itemId}";

            // 虚化显示 + 关闭碰撞与摆放组件，纯表现不干扰本地判定
            PlacementController pc = ghost.GetComponent<PlacementController>();
            if (pc != null)
            {
                pc.DebugHotkeys = false;
                pc.GhostOn();
                pc.enabled = false;
            }
            foreach (Collider2D col in ghost.GetComponentsInChildren<Collider2D>(true))
            {
                col.enabled = false;
            }

            remoteGhosts[playerId] = ghost;
            remoteGhostItemIds[playerId] = itemId;
            return ghost;
        }

        private void DestroyRemoteGhost(string playerId)
        {
            if (remoteGhosts.TryGetValue(playerId, out GameObject ghost) && ghost != null)
            {
                Destroy(ghost);
            }
            remoteGhosts.Remove(playerId);
            remoteGhostItemIds.Remove(playerId);

            // 同步清理其光标标记（确认摆放后道具转实体，光标应消失）
            if (remoteCursors.TryGetValue(playerId, out SpriteRenderer cursor) && cursor != null)
            {
                Destroy(cursor.gameObject);
            }
            remoteCursors.Remove(playerId);
        }

        private void ClearRemoteGhosts()
        {
            foreach (KeyValuePair<string, GameObject> pair in remoteGhosts)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }
            remoteGhosts.Clear();
            remoteGhostItemIds.Clear();

            foreach (KeyValuePair<string, SpriteRenderer> pair in remoteCursors)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }
            remoteCursors.Clear();
        }

        // ---------- 打勾确认按钮 ----------

        /// <summary>在屏幕上方搭建打勾确认按钮与旋转按钮（联机模式；确认点击后向服务器请求占用仲裁）</summary>
        private void ShowConfirmPlaceButton()
        {
            if (confirmPlaceButton != null)
            {
                return; // 已搭建：显隐由拖拽手势控制（松手显示 / 拖动隐藏）
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("PlacementCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 90;
                CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f; // 横屏统一匹配高度，与场景 Canvas 策略一致
            }
            EnsureEventSystem();
            actionButtonCanvas = canvas;

            confirmPlaceButton = SpawnActionButton(canvas.transform, confirmPlaceButtonPrefab,
                "ConfirmPlaceButton", "✔", new Color(0.2f, 0.7f, 0.3f, 0.95f));
            confirmPlaceButton.onClick.AddListener(OnConfirmPlaceClicked);

            rotateButton = SpawnActionButton(canvas.transform, rotatePlaceButtonPrefab,
                "RotatePlaceButton", "⟳", new Color(0.25f, 0.45f, 0.85f, 0.95f));
            rotateButton.onClick.AddListener(OnRotateClicked);

            // 初始隐藏：拖拽松手时才出现在道具上方
            HideConfirmPlaceButton();
        }

        /// <summary>
        /// 把确认/旋转按钮移动到摆放中道具包围盒顶部中点上方并显示；
        /// 无摆放实例、未搭建按钮（单机）或等待仲裁时保持隐藏
        /// </summary>
        private void ShowActionButtonsAboveItem()
        {
            if (confirmPlaceButton == null || rotateButton == null)
            {
                return;
            }
            PlacementController pc = localSession != null && localSession.BIsPlacing
                ? localSession.CurrentPlacementController : null;
            if (pc == null || actionButtonCanvas == null || awaitingPlaceResult)
            {
                HideConfirmPlaceButton();
                return;
            }

            // 道具包围盒顶部中点的世界坐标（rootPos 为框中心，上边缘 = 框高一半）
            Vector2 worldTop = pc.transform.position;
            FootprintBoxView box = pc.GetComponent<FootprintBoxView>();
            if (box != null && GridManager.Instance != null)
            {
                Vector2Int size = GridManager.GetRotatedSize(box.Footprint, pc.RotationSteps);
                worldTop += new Vector2(0f, size.y * GridManager.Instance.PublicCellSize * 0.5f);
            }

            // 世界坐标 → 屏幕像素 → 按钮 Canvas 局部坐标
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
            Vector3 screen = inputCamera.WorldToScreenPoint(worldTop);
            Camera uiCam = actionButtonCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? actionButtonCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)actionButtonCanvas.transform, screen, uiCam, out Vector2 local);

            ((RectTransform)confirmPlaceButton.transform).anchoredPosition = local + confirmButtonOffset;
            ((RectTransform)rotateButton.transform).anchoredPosition = local + rotateButtonOffset;
            confirmPlaceButton.gameObject.SetActive(true);
            rotateButton.gameObject.SetActive(true);
        }

        /// <summary>生成操作按钮：优先实例化 Inspector 指定的 prefab，未指定时退回运行时搭建的默认样式</summary>
        private static Button SpawnActionButton(Transform parent, Button prefab,
            string fallbackName, string fallbackLabel, Color fallbackColor)
        {
            if (prefab != null)
            {
                Button instance = Instantiate(prefab, parent, false);
                instance.name = prefab.name;
                // 统一锚定 Canvas 中心、pivot 底部居中：定位时 anchoredPosition = 道具顶部局部坐标 + Inspector 偏移
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                return instance;
            }
            return CreateActionButton(parent, fallbackName, fallbackLabel, fallbackColor);
        }

        /// <summary>创建一个操作按钮（锚定 Canvas 中心、pivot 底部居中，位置由 ShowActionButtonsAboveItem 驱动）</summary>
        private static Button CreateActionButton(Transform parent, string name, string label, Color color)
        {
            var btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            var rect = (RectTransform)btnGo.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(120f, 80f);
            btnGo.GetComponent<Image>().color = color;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 48f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return btnGo.GetComponent<Button>();
        }

        private void HideConfirmPlaceButton()
        {
            if (confirmPlaceButton != null)
            {
                confirmPlaceButton.gameObject.SetActive(false);
            }
            if (rotateButton != null)
            {
                rotateButton.gameObject.SetActive(false);
            }
        }

        /// <summary>旋转按钮回调：旋转本地摆放中的道具；朝向随下一次 ItemPlaceState 广播同步到远端，
        /// 此处把节流计时器拉满让下一帧立即上报，远端虚化道具即时跟随</summary>
        private void OnRotateClicked()
        {
            if (awaitingPlaceResult || localSession == null || !localSession.BIsPlacing)
            {
                return;
            }

            localSession.Rotate();
            placeStateTimer = 1f / placeStateReportRate;
            ShowActionButtonsAboveItem(); // 旋转后包围盒尺寸变化，按钮位置跟随刷新
        }

        /// <summary>打勾按钮回调：落点本地合法才发确认请求，占据格子列表交服务器仲裁</summary>
        private void OnConfirmPlaceClicked()
        {
            if (awaitingPlaceResult || localSession == null || !localSession.BIsPlacing)
            {
                return;
            }

            Vector2? pos = localSession.CurrentPosition;
            Vector2Int? anchor = localSession.CurrentAnchorCell;
            List<Vector2Int> cells = localSession.CurrentOccupiedCells();
            if (pos == null || anchor == null || cells == null || cells.Count == 0)
            {
                return;
            }

            // 本地预检：明显不合法（出界/与已知占用冲突）时不发请求，省一次往返
            PlacementController pc = localSession.CurrentPlacementController;
            if (pc == null || !pc.CanPlaceAt(pos.Value))
            {
                Debug.LogWarning($"{LOG_TAG} 当前落点不合法，请移动到合法位置后再确认。");
                ShowInvalidPlacementHint();
                return;
            }

            HideConfirmPlaceButton(); // 确认请求发出：隐藏按钮，等待服务器仲裁
            NetworkManager net = NetworkManager.Instance;

            // 拆除类道具（爆破范围允许覆盖已占格子）走专用拆除仲裁通道，不走格子冲突仲裁
            bool bDemolition = false;
            ItemBase prefab = FindPoolItem(localSession.CurrentItemId);
            if (prefab != null)
            {
                bDemolition = prefab is SuperQQ.Item.DemolitionItemBase;
            }

            if (bDemolition)
            {
                var demolishConfirm = new ItemDemolishConfirm
                {
                    RoomId = net.RoomId,
                    PlayerId = net.LocalPlayerId,
                    ItemId = localSession.CurrentItemId,
                    AnchorCell = new GridCell { X = anchor.Value.x, Y = anchor.Value.y },
                    Rotation = localSession.CurrentRotation,
                    ClientTimeMs = NetworkManager.NowMs()
                };
                foreach (Vector2Int cell in cells)
                {
                    demolishConfirm.Cells.Add(new GridCell { X = cell.x, Y = cell.y });
                }

                awaitingPlaceResult = true;
                net.Send(demolishConfirm);

                // 本地同步完成放置：DemolitionItemBase.OnPlaced 联机分支把炸弹按锚点挂起，
                // 等待 ItemDemolishResult 到达后统一引爆；同时结束摆放会话（停止拖拽广播）。
                // 此前漏掉这一步会导致会话一直卡在摆放中（ItemPlaceState 持续广播、确认可重复点击）。
                if (!localSession.Confirm())
                {
                    awaitingPlaceResult = false; // 落点非法未 finalize，允许重新确认
                }
                return;
            }

            var confirm = new ItemPlaceConfirm
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                ItemId = localSession.CurrentItemId,
                AnchorCell = new GridCell { X = anchor.Value.x, Y = anchor.Value.y },
                Rotation = localSession.CurrentRotation,
                ClientTimeMs = NetworkManager.NowMs(),
                // 附着类道具（黄油块等，声明 AllowsOccupiedOverlap 但非拆除类）：
                // 通知服务器跳过冲突检查且不记录占据（客户端权威，与本地不登记占据的口径一致）
                AllowOverlap = prefab != null && prefab.AllowsOccupiedOverlap,
                Mirrored = localSession.CurrentMirrored,
                // 链式道具（传送门第一段）：通知服务器本玩家后续还有确认，
                // 勿在本次确认后计入"全员确认完毕"（否则第一段摆完阶段就提前推进）
                ExpectMore = pc != null
                    && pc.GetComponent<SuperQQ.Item.Portal>() is { } portal && portal.HasChainedItem
            };
            foreach (Vector2Int cell in cells)
            {
                confirm.Cells.Add(new GridCell { X = cell.x, Y = cell.y });
            }

            awaitingPlaceResult = true;
            net.Send(confirm);
        }

        // ---------- 服务器仲裁结果 ----------

        private void HandlePlaceResult(ItemPlaceResult result)
        {
            NetworkManager net = NetworkManager.Instance;
            bool isMine = result.PlayerId == net.LocalPlayerId;
            Debug.Log($"{LOG_TAG} 收到摆放结果: playerId={result.PlayerId} itemId={result.ItemId} success={result.Success} isMine={isMine} BIsActive={BIsActive} anchor=({result.AnchorCell.X},{result.AnchorCell.Y})");

            if (!result.Success)
            {
                if (isMine)
                {
                    awaitingPlaceResult = false;
                    if (BIsActive)
                    {
                        Debug.LogWarning($"{LOG_TAG} 摆放失败：落点格子已被他人占用，请调整位置。");
                    }
                }
                return;
            }

            if (isMine)
            {
                awaitingPlaceResult = false;
                if (!BIsActive || localSession == null)
                {
                    // 迟到的仲裁（阶段边界确认）：本地会话已结束、道具被 EndPhase 丢弃，
                    // 由 RoomSnapshotReceiver 按快照 placed_items 补回实体，不再整条丢弃
                    Debug.LogWarning($"{LOG_TAG} 阶段已结束才收到本地摆放结果: {result.ItemId} @ ({result.AnchorCell.X},{result.AnchorCell.Y})，等待快照补放");
                    return;
                }
                // 服务器已批准：本地执行确认（登记占据、锁定道具；传送门等衔接摆放由会话内部处理）
                localSession.Confirm();
                Debug.Log($"{LOG_TAG} 摆放已确认: {result.ItemId} @ ({result.AnchorCell.X},{result.AnchorCell.Y})");

                // 旋转吐司尺寸诊断：确认时打印尺寸与锚点（联机排查两端位置不一致用）。
                // 尺寸决策统一在选择/摆放阶段切换时按服务器种子完成，此处不再本地随机
                ItemBase confirmedPrefab = FindPoolItem(result.ItemId);
                if (confirmedPrefab is SuperQQ.Item.RotatingToast)
                {
                    Debug.Log($"{LOG_TAG} 吐司确认: size={SuperQQ.Item.RotatingToastSizeSync.CurrentSize} anchor=({result.AnchorCell.X},{result.AnchorCell.Y}) round={SuperQQ.Network.NetGameFlowGate.CurrentServerRound} seed={SuperQQ.Network.NetGameFlowGate.CurrentRoundSeed}");
                }
            }
            else
            {
                // 远端玩家摆放成功：生成实体道具并登记占用，其他玩家无法再摆到这些格子
                PlaceRemoteItem(result);
            }
        }

        /// <summary>
        /// 服务器拆除仲裁结果：各端统一执行爆破——
        /// 投放者端取出挂起的本地炸弹引爆；远端生成炸弹实体后引爆；
        /// 移除集合以服务器 removed_items 为准，各端占据表保持一致
        /// </summary>
        private void HandleDemolishResult(ItemDemolishResult result)
        {
            NetworkManager net = NetworkManager.Instance;
            bool isMine = net != null && result.PlayerId == net.LocalPlayerId;
            Debug.Log($"{LOG_TAG} 收到拆除结果: playerId={result.PlayerId} itemId={result.ItemId} removed={result.RemovedItems.Count} isMine={isMine} BIsActive={BIsActive}");

            // 阶段边界迟到的拆除结果也要执行：移除集合以服务器裁定为准，
            // 丢弃会导致各端场上道具集合不一致（本地分支不依赖会话，可安全执行）
            Vector2Int anchor = new Vector2Int(result.AnchorCell.X, result.AnchorCell.Y);

            // 汇总被拆道具锚点（去重）。removed_items 非空时严格按服务器裁定（各端一致）；
            // 为空时本地按炸弹爆破范围与占据表交集计算兜底——服务器 placedItems 未维护
            // /跨轮清空的场景下仲裁结果恒空，会导致炸弹炸了个寂寞
            HashSet<Vector2Int> removedAnchors;
            if (result.RemovedItems.Count > 0)
            {
                removedAnchors = new HashSet<Vector2Int>();
                foreach (PlacedItemState removed in result.RemovedItems)
                {
                    removedAnchors.Add(new Vector2Int(removed.AnchorCell.X, removed.AnchorCell.Y));
                }
            }
            else
            {
                removedAnchors = CollectDemolishTargetsLocally(result);
                if (removedAnchors.Count > 0)
                {
                    Debug.LogWarning($"{LOG_TAG} 服务器 removed_items 为空，本地按爆破范围兜底拆除 {removedAnchors.Count} 个道具（各端计算口径一致时结果相同）");
                }
            }

            if (isMine)
            {
                awaitingPlaceResult = false;
                // 本地炸弹在确认摆放时已生成并挂起（DemolitionItemBase.OnPlaced 联机分支）
                if (SuperQQ.Item.DemolitionItemBase.TryTakePending(anchor, out SuperQQ.Item.DemolitionItemBase localBomb))
                {
                    localBomb.DetonateSynced(removedAnchors);
                }
                else
                {
                    // 挂起实例缺失（异常时序）也要保证移除执行到位
                    ExecuteRemoteDemolishRemoval(removedAnchors);
                }
                return;
            }

            // 远端投放：生成炸弹实体（表现用），随即按服务器裁定引爆
            DestroyRemoteGhost(result.PlayerId);
            SpawnRemoteBomb(result, removedAnchors);
        }

        /// <summary>生成远端炸弹实体并立即引爆（移除集合以服务器裁定为准）</summary>
        private void SpawnRemoteBomb(ItemDemolishResult result, HashSet<Vector2Int> removedAnchors)
        {
            ItemBase prefab = FindPoolItem(result.ItemId);
            GridManager grid = GridManager.Instance;
            if (prefab == null || grid == null)
            {
                // 炸弹本体只是表现，缺失时也要保证移除执行到位
                ExecuteRemoteDemolishRemoval(removedAnchors);
                return;
            }

            Vector2Int anchor = new Vector2Int(result.AnchorCell.X, result.AnchorCell.Y);
            FootprintBoxView prefabBox = prefab.GetComponent<FootprintBoxView>();
            Vector2Int footprint = prefabBox != null ? prefabBox.Footprint : Vector2Int.one;
            Vector2 worldPos = grid.GetPlacementWorldPos(anchor, footprint, result.Rotation);
            GameObject item = Instantiate(prefab.gameObject, worldPos,
                GridManager.GetRotationQuaternion(result.Rotation));
            item.name = $"RemoteDemolish_{result.PlayerId}_{result.ItemId}";

            var placed = item.AddComponent<PlacedItem>();
            placed.Init(null, anchor, result.Rotation, -1);
            placed.SetOwnerKey(result.PlayerId);

            // 不登记占据、不触发 OnPlaced（避免远端炸弹自行挂起等待），直接同步引爆
            SuperQQ.Item.DemolitionItemBase bomb = item.GetComponent<SuperQQ.Item.DemolitionItemBase>();
            if (bomb != null)
            {
                bomb.NetItemId = result.ItemId; // ItemLifecycleSync 实例键与所有者端一致
                bomb.InitPlaced(placed, result.Rotation);
                bomb.DetonateSynced(removedAnchors);
            }
            else
            {
                ExecuteRemoteDemolishRemoval(removedAnchors);
                Destroy(item);
            }
        }

        /// <summary>
        /// 本地计算拆除目标：按炸弹锚点+footprint 得出爆破范围格子，
        /// 取与占据表的交集（排除炸弹自身锚点），返回目标道具锚点集合。
        /// 与 DemolitionItemBase.CollectTargetsInArea 口径一致。
        /// </summary>
        private static HashSet<Vector2Int> CollectDemolishTargetsLocally(ItemDemolishResult result)
        {
            var anchors = new HashSet<Vector2Int>();
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return anchors;
            }

            Vector2Int anchor = new Vector2Int(result.AnchorCell.X, result.AnchorCell.Y);
            ItemBase prefab = FindPoolItemStatic(result.ItemId);
            FootprintBoxView prefabBox = prefab != null ? prefab.GetComponent<FootprintBoxView>() : null;
            Vector2Int size = prefabBox != null ? prefabBox.Footprint : Vector2Int.one;

            for (int dx = 0; dx < size.x; dx++)
            {
                for (int dy = 0; dy < size.y; dy++)
                {
                    Vector2Int cell = new Vector2Int(anchor.x + dx, anchor.y + dy);
                    if (cell == anchor)
                    {
                        continue; // 排除炸弹自身
                    }
                    PlacedItem target = grid.GetItemAt(cell);
                    // 只消除道具（有 ItemBase 的占据物）：Map 下的关卡物体（船/平台等）
                    // 也登记在占据表中但没有 ItemBase，一律不可被消除
                    if (target != null && target.GetComponent<ItemBase>() != null)
                    {
                        anchors.Add(target.AnchorCell);
                    }
                }
            }
            return anchors;
        }

        /// <summary>FindPoolItem 的静态版本（供静态兜底计算使用）</summary>
        private static ItemBase FindPoolItemStatic(string itemId)
        {
            return Instance != null ? Instance.FindPoolItem(itemId) : null;
        }

        /// <summary>兜底：炸弹 prefab 缺失或组件缺失时，仅按服务器裁定执行移除</summary>
        private void ExecuteRemoteDemolishRemoval(HashSet<Vector2Int> removedAnchors)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || removedAnchors == null)
            {
                return;
            }
            foreach (Vector2Int anchor in removedAnchors)
            {
                grid.RemoveAt(anchor);
            }
        }

        /// <summary>远端玩家确认摆放后，在本地生成实体道具并登记网格占用</summary>
        private void PlaceRemoteItem(ItemPlaceResult result)
        {
            DestroyRemoteGhost(result.PlayerId);

            ItemBase prefab = FindPoolItem(result.ItemId);
            GridManager grid = GridManager.Instance;
            if (prefab == null)
            {
                Debug.LogWarning($"{LOG_TAG} 远端道具 \"{result.ItemId}\" 无法实例化：目录与摆放池中均不存在。"
                    + $"可用目录: {(ItemCatalog.Instance != null ? ItemCatalog.Instance.DumpIds() : "(未配置)")}；"
                    + $"摆放池: [{string.Join(", ", itemPool.ConvertAll(p => p != null ? p.name : "null"))}]");
                return;
            }
            if (grid == null)
            {
                Debug.LogWarning($"{LOG_TAG} 远端道具 \"{result.ItemId}\" 生成失败：GridManager.Instance 为 null");
                return;
            }

            Debug.Log($"{LOG_TAG} 开始生成远端道具: itemId={result.ItemId} prefab={prefab.name} anchor=({result.AnchorCell.X},{result.AnchorCell.Y})");

            var anchor = new Vector2Int(result.AnchorCell.X, result.AnchorCell.Y);
            FootprintBoxView prefabBox = prefab.GetComponent<FootprintBoxView>();
            Vector2Int footprint = prefabBox != null ? prefabBox.Footprint : Vector2Int.one;

            Vector2 worldPos = grid.GetPlacementWorldPos(anchor, footprint, result.Rotation);
            GameObject item = Instantiate(prefab.gameObject, worldPos,
                GridManager.GetRotationQuaternion(result.Rotation));
            item.name = $"RemotePlaced_{result.PlayerId}_{result.ItemId}";

            // 吐司诊断：远端生成时打印尺寸/footprint/锚点（两端对日志即可定位是哪一侧不一致）
            if (prefab is SuperQQ.Item.RotatingToast)
            {
                Debug.Log($"{LOG_TAG} 远端吐司生成: 本端size={SuperQQ.Item.RotatingToastSizeSync.CurrentSize} footprint={footprint} anchor=({anchor.x},{anchor.y}) world=({worldPos.x:F2},{worldPos.y:F2}) round={SuperQQ.Network.NetGameFlowGate.CurrentServerRound} seed={SuperQQ.Network.NetGameFlowGate.CurrentRoundSeed}");
            }

            // 登记占用：占据格子对所有人生效，本地落点合法性检查自动包含这些格子。
            // 附着类道具（RegistersOccupancy=false，如黄油块）不登记占据——与本地端口径一致
            var placed = item.AddComponent<PlacedItem>();
            placed.Init(null, anchor, result.Rotation, -1);
            placed.SetOwnerKey(result.PlayerId); // 陷阱击杀计分归属
            if (prefab.RegistersOccupancy)
            {
                grid.Occupy(anchor, footprint, placed, result.Rotation);
            }

            // 锁定：摆放组件与碰撞体不再需要参与交互
            PlacementController pc = item.GetComponent<PlacementController>();
            if (pc != null)
            {
                pc.DebugHotkeys = false;
                pc.enabled = false;
            }
            ItemBase itemBase = item.GetComponent<ItemBase>();
            if (itemBase != null)
            {
                itemBase.NetItemId = result.ItemId; // ItemLifecycleSync 实例键与所有者端一致
                itemBase.InitPlaced(placed, result.Rotation);
                itemBase.SetMirrored(result.Mirrored); // 镜像朝向同步（樱桃发射器/流星锤等）
                itemBase.OnPlaced();
            }
            Debug.Log($"{LOG_TAG} 远端道具生成完成: {item.name} 位置=({worldPos.x},{worldPos.y})");

            // 传送门：远端玩家的首段（入口）摆好后，其出口由该玩家自己衔接摆放，
            // 第二条 ItemPlaceResult 到达时会各自 OnPlaced 自动配对。等一帧确认没配上
            // （出口结果被时序/阶段边界丢弃、或只恢复出首段），再原地补建配对端，
            // 否则对方的传送门在本端永远是落单状态：自己用不了、道具也过不去
            if (itemBase is SuperQQ.Item.Portal remotePortal && !remotePortal.IsLinked)
            {
                StartCoroutine(EnsureRemotePortalLinkedNextFrame(remotePortal));
            }
        }

        /// <summary>远端传送门补配对：等一帧让同批到达的出口结果先完成自动配对，仍未配对才补建</summary>
        private System.Collections.IEnumerator EnsureRemotePortalLinkedNextFrame(SuperQQ.Item.Portal portal)
        {
            yield return null;
            if (portal != null && !portal.IsLinked)
            {
                portal.LinkWithRemoteCounterpart();
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>按道具ID（= prefab 名称）在本地候选池中查找</summary>
        private ItemBase FindPoolItem(string itemId)
        {
            // 1. ItemCatalog 目录按 itemId 精确匹配
            ItemBase fromCatalog = ItemCatalog.Instance != null ? ItemCatalog.Instance.Find(itemId) : null;
            if (fromCatalog != null)
            {
                return fromCatalog;
            }

            // 2. 摆放池按 prefab 名匹配
            for (int i = 0; i < itemPool.Count; i++)
            {
                if (itemPool[i] != null && itemPool[i].name == itemId)
                {
                    return itemPool[i];
                }
            }

            // 3. 兜底：从目录所有 prefab 里按 prefab 名匹配（服务器发名字而非数字ID时兜底）
            if (ItemCatalog.Instance != null)
            {
                ItemBase byName = ItemCatalog.Instance.FindByPrefabName(itemId);
                if (byName != null)
                {
                    return byName;
                }
            }

            // 4. 本轮发牌解析映射兜底（传送门等未登记 ItemCatalog 的道具：
            // 选择阶段已按 offer.ItemId 解析出 prefab，远端回放/虚影直接复用该结果）
            ItemBase fromOfferMap = SuperQQ.Selection.Runtime.PropSelectionDirector.ResolveOfferPrefab(itemId);
            if (fromOfferMap != null)
            {
                return fromOfferMap;
            }
            return null;
        }

        // ==================== 阶段摄像机 ====================

        /// <summary>
        /// 切换阶段镜头：通过优先级接管/归还 Cinemachine Brain 的主镜头。
        /// 放置阶段把固定镜头优先级抬到游玩镜头之上，退出时还原为其原始优先级
        /// </summary>
        private void SetPlacementCameraActive(bool bActive)
        {
            if (placementCamera == null)
            {
                return;
            }

            if (bActive)
            {
                placementCameraOriginalPriority = placementCamera.Priority;
                placementCamera.Priority = placementCameraPriority;
            }
            else
            {
                placementCamera.Priority = placementCameraOriginalPriority;
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

        // ==================== 内部实现 ====================

        /// <summary>从候选池随机抽取一件道具；池为空或全为 null 时返回 null</summary>
        private ItemBase PickRandomItem()
        {
            int validCount = 0;
            for (int i = 0; i < itemPool.Count; i++)
            {
                if (itemPool[i] != null)
                {
                    validCount++;
                }
            }
            if (validCount == 0)
            {
                return null;
            }

            int picked = UnityEngine.Random.Range(0, validCount);
            for (int i = 0; i < itemPool.Count; i++)
            {
                if (itemPool[i] == null)
                {
                    continue;
                }
                if (picked-- == 0)
                {
                    return itemPool[i];
                }
            }

            return null;
        }

        // ==================== 光标玩家标记 ====================

        /// <summary>
        /// 显示跟随光标的玩家标记：Sprite 取自本地玩家自身配置的标识图
        /// （PlayerController.CursorMarkerSprite，未配置时回退角色本体 Sprite；角色隐藏不影响读取）
        /// </summary>
        private void ShowCursorMarker()
        {
            PlayerController localPlayer = ResolveLocalPlayer();
            Sprite sprite = localPlayer != null ? localPlayer.CursorMarkerSprite : null;

            if (sprite == null)
            {
                // 无可展示的形象时静默跳过，不阻断放置流程
                Debug.LogWarning($"{LOG_TAG} 本地玩家未配置光标标识图，跳过显示（请在玩家 prefab 的 Cursor Marker Sprite 上配置）。");
                return;
            }

            if (cursorMarker == null)
            {
                var go = new GameObject("CursorPlayerMarker");
                go.transform.SetParent(transform, false);
                cursorMarker = go.AddComponent<SpriteRenderer>();
                cursorMarker.sortingOrder = cursorMarkerSortingOrder;
                cursorMarker.sortingLayerName = cursorMarkerSortingLayer;
            }

            cursorMarker.sprite = sprite;
            cursorMarker.color = localPlayer != null ? localPlayer.PlayerColor : Color.white;
            ApplyCursorMarkerScale(cursorMarker);
            cursorMarker.enabled = true;
        }

        /// <summary>
        /// 按 cursorMarkerWorldSize 缩放标记，使 Sprite 显示高度恒定为配置的世界尺寸，
        /// 与 Sprite 自身的 Pixels Per Unit 设置无关（避免标识图在世界里显得过大/过小）
        /// </summary>
        private void ApplyCursorMarkerScale(SpriteRenderer marker)
        {
            if (marker == null || marker.sprite == null)
            {
                return;
            }

            float spriteHeight = marker.sprite.bounds.size.y;
            if (spriteHeight <= 0f)
            {
                return;
            }

            float parentScale = marker.transform.parent != null ? marker.transform.parent.lossyScale.y : 1f;
            if (parentScale <= 0f)
            {
                parentScale = 1f;
            }
            float uniformScale = cursorMarkerWorldSize / (spriteHeight * parentScale);
            marker.transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
        }

        /// <summary>隐藏光标玩家标记（阶段结束时调用）</summary>
        private void HideCursorMarker()
        {
            if (cursorMarker != null)
            {
                cursorMarker.enabled = false;
            }
        }

        private void HandlePlacementConfirmed(PlacementResult result)
        {
            Debug.Log($"{LOG_TAG} {result}");
            OnLocalPlacementConfirmed?.Invoke(result);
        }

        /// <summary>确认放置被会话拒绝（单机左键确认路径）：在道具上方弹出提示</summary>
        private void HandlePlacementRejected()
        {
            ShowInvalidPlacementHint();
        }

        /// <summary>
        /// 在当前摆放中道具上方弹出「不可放置」浮动文本提示
        /// （联机打勾按钮本地预检失败、单机左键确认失败两条路径共用）
        /// 文本内容、位置偏移与时长统一由 PopupManager 浮动文本注册表（FloatingTextType.InvalidPlacement）配置
        /// </summary>
        private void ShowInvalidPlacementHint()
        {
            if (PopupManager.Instance == null)
            {
                return;
            }
            PopupManager.Instance.ShowFloatingText(
                FloatingTextType.InvalidPlacement, ResolvePlacingItemTopWorldPos());
        }

        /// <summary>
        /// 摆放中道具包围盒顶部中点的世界坐标（与 ShowActionButtonsAboveItem 同一取点口径）；未摆放时回退指针位置
        /// 注意返回类型必须是 Vector3：ShowFloatingText 的 Vector2 重载按容器局部坐标处理，
        /// 返回 Vector2 会被重载决议绑定到错误接口，导致文本固定在界面中央
        /// </summary>
        private Vector3 ResolvePlacingItemTopWorldPos()
        {
            PlacementController pc = localSession != null && localSession.BIsPlacing
                ? localSession.CurrentPlacementController : null;
            if (pc == null)
            {
                return lastPointerWorld;
            }

            Vector2 worldTop = pc.transform.position;
            FootprintBoxView box = pc.GetComponent<FootprintBoxView>();
            if (box != null && GridManager.Instance != null)
            {
                Vector2Int size = GridManager.GetRotatedSize(box.Footprint, pc.RotationSteps);
                worldTop += new Vector2(0f, size.y * GridManager.Instance.PublicCellSize * 0.5f);
            }
            // 位置偏移由 PopupManager 浮动文本注册表统一配置，此处仅提供世界锚点
            return worldTop;
        }

        /// <summary>解析本地玩家角色；未找到时返回 null，不阻断放置流程</summary>
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

        /// <summary>解析本地玩家标识；未找到本地角色时回退为固定标识，不阻断放置流程</summary>
        private static string ResolveLocalPlayerKey()
        {
            PlayerController localPlayer = ResolveLocalPlayer();
            if (localPlayer != null)
            {
                return localPlayer.IdentityKey;
            }

            Debug.LogWarning($"{LOG_TAG} 未找到本地玩家，放置结果将使用占位标识 Local。");
            return "Local";
        }
    }
}

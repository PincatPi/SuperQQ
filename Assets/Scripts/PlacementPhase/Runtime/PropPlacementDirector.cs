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
        [Tooltip("标记相对光标的世界坐标偏移，避免被道具本体与光标遮挡")]
        [SerializeField] private Vector2 cursorMarkerOffset = new Vector2(0.8f, 0.8f);
        [Tooltip("标记的 Sorting Order，需高于网格与虚线框（默认为 10）")]
        [SerializeField] private int cursorMarkerSortingOrder = 100;

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

        // ---- 联机状态 ----
        private float placeStateTimer;                                  // 拖拽上报节流
        private bool awaitingPlaceResult;                               // 已发确认、等待服务器仲裁
        private Button confirmPlaceButton;                              // 屏幕上方打勾确认按钮（运行时搭建）
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
            localSession.Deal(item);

            grid.ShowGrid();
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

        // 拖拽状态：道具仅在按住拖拽时跟随指针，松开后停留在原地（适配触屏与鼠标）
        private bool isDragging;

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
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                isDragging = true;
                if (!localSession.BIsPlacing && localSession.BHasPendingItem)
                {
                    if (localSession.BeginPlace(pointerWorld))
                    {
                        selectFrame = Time.frameCount;
                    }
                }
            }

            // 松开：结束拖拽，道具停留在当前位置
            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
            }

            // 拖拽中：道具跟随指针
            if (isDragging && localSession.BIsPlacing)
            {
                localSession.UpdatePointer(pointerWorld);
            }

            // 光标标记与事件（始终跟随指针，便于观察）
            if (cursorMarker != null && cursorMarker.enabled)
            {
                cursorMarker.transform.position = pointerWorld + cursorMarkerOffset;
            }
            if (pointerWorld != lastPointerWorld)
            {
                lastPointerWorld = pointerWorld;
                OnLocalPointerMoved?.Invoke(pointerWorld);
            }
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
                return;
            }
            if (Input.GetKeyDown(rotateKey))
            {
                localSession.Rotate();
            }

            // 左键确认：取出道具的同帧、以及指针悬停在 UI 上时不触发。
            // PC 调试（编辑器/Standalone）允许左键确认；手机触屏只允许打勾按钮（避免与拖拽冲突）。
            // 联机模式左键确认走服务器仲裁，单机走本地直接确认。
            bool allowClickConfirm = !BNetMode || BIsPCDebug;
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

        /// <summary>是否为 PC 调试环境（编辑器或桌面平台）：此类平台允许鼠标左键确认摆放</summary>
        private static bool BIsPCDebug =>
            Application.isEditor
            || Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.OSXPlayer
            || Application.platform == RuntimePlatform.LinuxPlayer;

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
        }

        private void UnregisterNetHandlers()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null) return;
            net.Unregister<ItemPlaceStateBroadcast>();
            net.Unregister<ItemPlaceResult>();
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
                Rotated = localSession.CurrentRotated
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
            ghost.transform.rotation = msg.Rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;

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
                remoteCursors[playerId] = cursor;
            }

            cursor.sprite = sprite;
            cursor.color = remote.PlayerColor;
            cursor.transform.position = itemPos + cursorMarkerOffset;
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

        /// <summary>在屏幕上方搭建打勾确认按钮（联机模式；点击后向服务器请求占用仲裁）</summary>
        private void ShowConfirmPlaceButton()
        {
            if (confirmPlaceButton != null)
            {
                confirmPlaceButton.gameObject.SetActive(true);
                return;
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
            }
            EnsureEventSystem();

            var btnGo = new GameObject("ConfirmPlaceButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)btnGo.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -30f);
            rect.sizeDelta = new Vector2(120f, 80f);
            btnGo.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f, 0.95f);

            confirmPlaceButton = btnGo.GetComponent<Button>();
            confirmPlaceButton.onClick.AddListener(OnConfirmPlaceClicked);

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
            tmp.fontSize = 48f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        private void HideConfirmPlaceButton()
        {
            if (confirmPlaceButton != null)
            {
                confirmPlaceButton.gameObject.SetActive(false);
            }
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
                return;
            }

            NetworkManager net = NetworkManager.Instance;
            var confirm = new ItemPlaceConfirm
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                ItemId = localSession.CurrentItemId,
                AnchorCell = new GridCell { X = anchor.Value.x, Y = anchor.Value.y },
                Rotated = localSession.CurrentRotated,
                ClientTimeMs = NetworkManager.NowMs()
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

            if (!BIsActive)
            {
                return;
            }

            if (!result.Success)
            {
                if (isMine)
                {
                    awaitingPlaceResult = false;
                    Debug.LogWarning($"{LOG_TAG} 摆放失败：落点格子已被他人占用，请调整位置。");
                }
                return;
            }

            if (isMine)
            {
                // 服务器已批准：本地执行确认（登记占据、锁定道具；传送门等衔接摆放由会话内部处理）
                awaitingPlaceResult = false;
                localSession.Confirm();
                Debug.Log($"{LOG_TAG} 摆放已确认: {result.ItemId} @ ({result.AnchorCell.X},{result.AnchorCell.Y})");
            }
            else
            {
                // 远端玩家摆放成功：生成实体道具并登记占用，其他玩家无法再摆到这些格子
                PlaceRemoteItem(result);
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

            Vector2 worldPos = grid.GetPlacementWorldPos(anchor, footprint, result.Rotated);
            GameObject item = Instantiate(prefab.gameObject, worldPos,
                result.Rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity);
            item.name = $"RemotePlaced_{result.PlayerId}_{result.ItemId}";

            // 登记占用：占据格子对所有人生效，本地落点合法性检查自动包含这些格子
            var placed = item.AddComponent<PlacedItem>();
            placed.Init(null, anchor, result.Rotated, -1);
            grid.Occupy(anchor, footprint, placed, result.Rotated);

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
                itemBase.InitPlaced(placed, result.Rotated ? 1 : 0);
                itemBase.OnPlaced();
            }
            Debug.Log($"{LOG_TAG} 远端道具生成完成: {item.name} 位置=({worldPos.x},{worldPos.y})");
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
            }

            cursorMarker.sprite = sprite;
            cursorMarker.color = localPlayer != null ? localPlayer.PlayerColor : Color.white;
            cursorMarker.enabled = true;
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

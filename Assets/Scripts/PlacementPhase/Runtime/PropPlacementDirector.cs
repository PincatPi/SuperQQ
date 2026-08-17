using System;
using System.Collections.Generic;
using Cinemachine;
using SuperQQ.GameFlow;
using SuperQQ.Grid;
using SuperQQ.Item;
using SuperQQ.Placement.Core;
using SuperQQ.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

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
            PollPointer();
            PollHotkeys();
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

            // 发牌后立即开始跟随鼠标（等下一帧 Update 会晚一拍）
            TryBeginPendingItem();
            Debug.Log($"{LOG_TAG} 进入放置阶段，发放道具：{(item != null ? item.name : "无")}");
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

        /// <summary>有待放置道具且当前未在摆放时自动取出跟随鼠标（发牌后、Esc 取消后都经由此处）</summary>
        private void TryBeginPendingItem()
        {
            if (localSession.BIsPlacing || !localSession.BHasPendingItem)
            {
                return;
            }

            if (localSession.BeginPlace(PointerWorldPos()))
            {
                // 防止取出的同帧点击（含触发阶段切换的那次点击）直接确认
                selectFrame = Time.frameCount;
            }
        }

        /// <summary>把鼠标世界坐标喂给放置会话，同步光标玩家标记位置，并对外发布光标位置变化</summary>
        private void PollPointer()
        {
            Vector2 pointerWorld = PointerWorldPos();
            localSession.UpdatePointer(pointerWorld);

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

            // 左键确认：取出道具的同帧、以及指针悬停在 UI 上时不触发
            if (Input.GetMouseButtonDown(0)
                && Time.frameCount != selectFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                localSession.Confirm();
            }
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

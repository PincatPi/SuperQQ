using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 房间等待界面视图（UI/Room 场景）— 纯展示层。
    /// 只负责把数据渲染到场景 UI，不含任何网络/业务逻辑；
    /// 由控制器（UIRoomController）单向驱动，按钮点击通过 ReadyClicked 事件上抛。
    ///
    /// Editor 接线：
    ///   roomCodeText     ← Canvas/TopArea/TopRight/RoomCodeText
    ///   progressText     ← Canvas/BottomArea/ProgressPanel/ProgressText
    ///   barFill          ← ProgressPanel 下的 BarFill（Image Type 需设为 Filled / Horizontal）
    ///   readyButton      ← Canvas/BottomArea/BtnReady
    ///   readyButtonLabel ← BtnReady 下的 Label（TMP）
    /// </summary>
    public class RoomView : MonoBehaviour
    {
        [Header("玩家槽位（按顺序拖入 Slot_1~4，运行时按房间人数显隐）")]
        [SerializeField] private GameObject[] slots;

        [Header("房间码")]
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private string roomCodePrefix = "房间码 ";

        [Header("准备进度")]
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image barFill;
        [SerializeField] private string progressFormat = "{0} / {1} 已准备";

        [Header("返回按钮（退出房间）")]
        [SerializeField] private Button backButton;

        [Header("准备/开始按钮（同一按钮，按身份切换模式）")]
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private string notReadyText = "点击准备";
        [SerializeField] private string readyText = "取消准备";
        [SerializeField] private string startGameText = "开始游戏";

        [Header("选关投票（场景美术节点：BtnVote 按钮 + MapVoteSection/Content 得票文本；留空自动按名查找，再兜底运行时自建）")]
        [SerializeField] private Button levelButton;
        [SerializeField] private TMP_Text levelButtonLabel;
        [SerializeField] private TMP_Text voteSummaryText;
        [Tooltip("中文字体（运行时自建的按钮/弹窗文本用；TMP 默认字体无中文会乱码。拖 GUIPackCartoon 的 AaYuanWeiTuSi-2 SDF）")]
        [SerializeField] private TMP_FontAsset chineseFont;

        /// <summary>准备模式下按钮被点击（由控制器订阅，处理网络逻辑）</summary>
        public event Action ReadyClicked;
        /// <summary>开始游戏模式下按钮被点击（由控制器订阅，处理网络逻辑）</summary>
        public event Action StartClicked;
        /// <summary>返回按钮被点击（由控制器订阅，处理退出房间逻辑）</summary>
        public event Action BackClicked;
        /// <summary>选关投票按钮被点击（由控制器订阅，打开投票弹窗）</summary>
        public event Action VoteOpenClicked;
        /// <summary>投票弹窗中选中某关（由控制器订阅，发投票请求；参数为 levelId）</summary>
        public event Action<int> VoteSubmitted;

        // 当前按钮模式：false=准备切换（普通玩家），true=开始游戏（房主）
        private bool _isStartMode;

        // 槽位子视图缓存（运行时从 slots 上获取/自动挂载 RoomSlotView）
        private RoomSlotView[] _slotViews;

        private void Awake()
        {
            if (readyButton != null)
            {
                readyButton.onClick.AddListener(OnReadyButtonClicked);
            }
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackButtonClicked);
            }

            // 运行时兜底：BarFill 必须是 Filled/Horizontal，fillAmount 才生效。
            // 场景里若被配成 Simple（或被编辑器保存覆盖），这里强制纠正。
            if (barFill != null && barFill.type != Image.Type.Filled)
            {
                barFill.type = Image.Type.Filled;
                barFill.fillMethod = Image.FillMethod.Horizontal;
                barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            // 槽位默认全部隐藏，等待房间数据驱动
            SetPlayerCount(0);

            // 选关投票：优先接场景美术节点（RoomView 挂在管理器物体上、不在 Canvas 下，必须全场景查找）
            if (levelButton == null)
            {
                GameObject btnGo = GameObject.Find("BtnVote");
                if (btnGo != null)
                {
                    levelButton = btnGo.GetComponent<Button>();
                }
            }
            if (voteSummaryText == null)
            {
                GameObject section = GameObject.Find("MapVoteSection");
                Transform content = section != null ? FindDeepChild(section.transform, "Content") : null;
                if (content != null)
                {
                    voteSummaryText = content.GetComponent<TMP_Text>();
                }
            }
            // 美术按钮自身可能没有可接收射线的主体图形（图形在子物体上且 raycastTarget 未开）：
            // 补一张全透明 Image 作为点击受体，保证按钮可点
            if (levelButton != null && !HasRaycastTarget(levelButton.transform))
            {
                var catcher = levelButton.gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
            // 兜底：场景里没有投票按钮时运行时自动创建（顶部中央）
            if (levelButton == null)
            {
                CreateLevelButtonRuntime();
            }
            if (levelButton != null)
            {
                levelButton.onClick.AddListener(OnLevelButtonClicked);
            }
        }

        /// <summary>层级内是否存在可接收射线的图形</summary>
        private static bool HasRaycastTarget(Transform root)
        {
            foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
            {
                if (g.raycastTarget)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>层级深度查找子物体（含非激活）</summary>
        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                Transform found = FindDeepChild(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // 运行时解析/自建的 UI Canvas（RoomView 挂在管理器物体上、不在 Canvas 下，不能依赖 GetComponentInParent）
        private Canvas uiCanvas;

        /// <summary>解析可用 Canvas：父级 → 场景主 Canvas → 自建置顶 Canvas（含 Raycaster）</summary>
        private Canvas ResolveUiCanvas()
        {
            if (uiCanvas != null)
            {
                return uiCanvas;
            }
            uiCanvas = GetComponentInParent<Canvas>();
            if (uiCanvas == null)
            {
                foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        uiCanvas = c;
                        break;
                    }
                }
            }
            if (uiCanvas == null)
            {
                var canvasGo = new GameObject("RoomVoteCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                uiCanvas = canvasGo.GetComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCanvas.sortingOrder = 90;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
            }
            return uiCanvas;
        }

        /// <summary>运行时创建选关按钮（顶部中央的简易按钮；无父 Canvas 时自建一个）</summary>
        private void CreateLevelButtonRuntime()
        {
            Canvas canvas = ResolveUiCanvas();
            if (canvas == null)
            {
                return;
            }
            var go = new GameObject("LevelCycleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta = new Vector2(320f, 56f);
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            levelButton = go.GetComponent<Button>();

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(rect, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            levelButtonLabel = labelGo.GetComponent<TextMeshProUGUI>();
            levelButtonLabel.alignment = TextAlignmentOptions.Center;
            levelButtonLabel.fontSize = 28f;
            levelButtonLabel.color = Color.white;
            levelButtonLabel.raycastTarget = false;
            ApplyChineseFont(levelButtonLabel);
        }

        /// <summary>运行时创建的文本统一套用中文字体（默认 LiberationSans 无中文字形会乱码）</summary>
        private void ApplyChineseFont(TMP_Text text)
        {
            if (text != null && chineseFont != null)
            {
                text.font = chineseFont;
            }
        }

        private void OnLevelButtonClicked()
        {
            VoteOpenClicked?.Invoke();
        }

        /// <summary>刷新投票显示：写入美术面板的得票文本（"欢乐写字楼 · 2 票"式），自建按钮时同步按钮文本</summary>
        public void SetVoteSummary(string summary)
        {
            if (voteSummaryText != null)
            {
                voteSummaryText.text = summary;
            }
            if (levelButtonLabel != null)
            {
                levelButtonLabel.text = summary;
            }
            // 投票全员可点（含房主），房主特权只是"开始游戏"按钮
            if (levelButton != null)
            {
                levelButton.interactable = true;
            }
        }

        // ==================== 投票弹窗 ====================

        private GameObject votePopup;
        private int[] voteRowIds;
        private TMP_Text[] voteRowLabels;

        /// <summary>投票弹窗是否打开</summary>
        public bool BVotePopupOpen => votePopup != null && votePopup.activeSelf;

        /// <summary>
        /// 打开/刷新投票弹窗：每关一行（名称 + 当前得票），点击行即投票。
        /// 数据由控制器从房间状态计票后传入；弹窗已打开时重复调用只刷新数据
        /// </summary>
        /// <param name="leadingId">当前计票领先的关卡 ID（高亮显示）</param>
        public void OpenVotePopup(int[] levelIds, string[] labels, int[] votes, int leadingId)
        {
            if (votePopup == null)
            {
                BuildVotePopup(levelIds);
            }
            RefreshVotePopup(labels, votes, leadingId);
            votePopup.SetActive(true);
        }

        /// <summary>关闭投票弹窗</summary>
        public void CloseVotePopup()
        {
            if (votePopup != null)
            {
                votePopup.SetActive(false);
            }
        }

        private void BuildVotePopup(int[] levelIds)
        {
            Canvas canvas = ResolveUiCanvas();
            if (canvas == null)
            {
                return;
            }
            voteRowIds = levelIds;
            voteRowLabels = new TMP_Text[levelIds.Length];

            // 弹窗根（半透明底，挡点击）
            votePopup = new GameObject("VotePopup", typeof(RectTransform), typeof(Image), typeof(Button));
            votePopup.transform.SetParent(canvas.transform, false);
            var rootRect = (RectTransform)votePopup.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            votePopup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            // 点空白处关闭
            votePopup.GetComponent<Button>().onClick.AddListener(CloseVotePopup);

            // 面板
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(rootRect, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 120f + levelIds.Length * 76f);
            panelGo.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.16f, 0.97f);

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(panelRect, false);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -12f);
            titleRect.sizeDelta = new Vector2(0f, 48f);
            var title = titleGo.GetComponent<TextMeshProUGUI>();
            title.text = "投票选择关卡";
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 34f;
            title.color = Color.white;
            title.raycastTarget = false;
            ApplyChineseFont(title);

            // 关卡行
            for (int i = 0; i < levelIds.Length; i++)
            {
                int levelId = levelIds[i]; // 闭包捕获
                var rowGo = new GameObject($"Row_{levelId}", typeof(RectTransform), typeof(Image), typeof(Button));
                rowGo.transform.SetParent(panelRect, false);
                var rowRect = (RectTransform)rowGo.transform;
                rowRect.anchorMin = new Vector2(0.5f, 1f);
                rowRect.anchorMax = new Vector2(0.5f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                rowRect.anchoredPosition = new Vector2(0f, -70f - i * 76f);
                rowRect.sizeDelta = new Vector2(440f, 64f);
                rowGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
                rowGo.GetComponent<Button>().onClick.AddListener(() =>
                {
                    VoteSubmitted?.Invoke(levelId);
                    CloseVotePopup();
                });

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(rowRect, false);
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                voteRowLabels[i] = labelGo.GetComponent<TextMeshProUGUI>();
                voteRowLabels[i].alignment = TextAlignmentOptions.Center;
                voteRowLabels[i].fontSize = 30f;
                voteRowLabels[i].raycastTarget = false;
                ApplyChineseFont(voteRowLabels[i]);
            }
            votePopup.SetActive(false);
        }

        private void RefreshVotePopup(string[] labels, int[] votes, int leadingId)
        {
            if (voteRowLabels == null)
            {
                return;
            }
            for (int i = 0; i < voteRowLabels.Length; i++)
            {
                bool leading = voteRowIds[i] == leadingId;
                voteRowLabels[i].text = $"{labels[i]}　{votes[i]} 票" + (leading ? "　★" : "");
                voteRowLabels[i].color = leading ? new Color(1f, 0.85f, 0.3f) : Color.white;
            }
        }

        private void OnDestroy()
        {
            if (readyButton != null)
            {
                readyButton.onClick.RemoveListener(OnReadyButtonClicked);
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackButtonClicked);
            }
            if (levelButton != null)
            {
                levelButton.onClick.RemoveListener(OnLevelButtonClicked);
            }
        }

        /// <summary>显示房间码</summary>
        public void SetRoomCode(string roomCode)
        {
            if (roomCodeText != null) roomCodeText.text = roomCodePrefix + roomCode;
        }

        /// <summary>按玩家数量显隐槽位：前 count 个槽位显示（P1、P2…依入座顺序），其余隐藏</summary>
        public void SetPlayerCount(int count)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].SetActive(i < count);
            }
        }

        /// <summary>填充指定槽位的玩家信息（昵称 + 准备状态文本），并告知是否为本地玩家（驱动 PlayerSlot 高亮）</summary>
        public void SetSlotPlayer(int index, string playerName, bool isReady, bool isLocal = false)
        {
            RoomSlotView slot = GetSlotView(index);
            if (slot != null) slot.SetPlayer(playerName, isReady);

            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null) return;
            PlayerSlot playerSlot = slots[index].GetComponent<PlayerSlot>();
            if (playerSlot != null) playerSlot.SetLocalHighlight(isLocal);
        }

        /// <summary>获取槽位子视图：未挂 RoomSlotView 时自动挂载（其 Awake 会按名字自动绑定子物体）</summary>
        private RoomSlotView GetSlotView(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null) return null;
            if (_slotViews == null || _slotViews.Length != slots.Length)
            {
                _slotViews = new RoomSlotView[slots.Length];
            }
            if (_slotViews[index] == null)
            {
                _slotViews[index] = slots[index].GetComponent<RoomSlotView>();
                if (_slotViews[index] == null)
                {
                    _slotViews[index] = slots[index].AddComponent<RoomSlotView>();
                }
            }
            return _slotViews[index];
        }

        /// <summary>刷新准备进度：文本 "n / m 已准备" + 进度条填充比例</summary>
        public void SetReadyProgress(int readyCount, int totalCount)
        {
            if (progressText != null)
            {
                progressText.text = string.Format(progressFormat, readyCount, totalCount);
            }
            if (barFill != null)
            {
                barFill.fillAmount = totalCount > 0 ? Mathf.Clamp01((float)readyCount / totalCount) : 0f;
            }
        }

        /// <summary>切到准备模式（普通玩家）：未准备显示"点击准备"，已准备显示"取消准备"</summary>
        public void SetReadyMode(bool isReady)
        {
            _isStartMode = false;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = isReady ? readyText : notReadyText;
            }
            if (readyButton != null) readyButton.interactable = true;
        }

        /// <summary>切到开始游戏模式（房主）：canStart=false 时按钮置灰不可点</summary>
        public void SetStartMode(bool canStart)
        {
            _isStartMode = true;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = startGameText;
            }
            if (readyButton != null) readyButton.interactable = canStart;
        }

        /// <summary>设置按钮是否可交互（未进房时禁用）</summary>
        public void SetReadyInteractable(bool interactable)
        {
            if (readyButton != null) readyButton.interactable = interactable;
        }

        private void OnReadyButtonClicked()
        {
            if (_isStartMode)
            {
                StartClicked?.Invoke();
            }
            else
            {
                ReadyClicked?.Invoke();
            }
        }

        private void OnBackButtonClicked()
        {
            BackClicked?.Invoke();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 槽位介绍视频面板 — 玩家图标到达道具槽位时弹出，循环播放该道具的介绍 mp4。
    /// 默认锚定【屏幕右上角】；当目标槽位（含其上方打勾确认按钮预留区）与面板重叠时，
    /// 按 右上→左上→右下→左下 顺序自动换到第一个不遮挡的角落。
    /// 纯本地表现，不联网。无气泡装饰：视频直接铺满面板（按视频宽高比居中等比适配）。
    ///
    /// 视频文件约定：Assets/Resources/Videos/Items/{itemId}.mp4（itemId 为 ItemCatalog 数字代号，如 11.mp4）；
    /// 无对应视频时面板自动隐藏（静默降级）。
    /// 也可在 Inspector 的 fallbackClips 表中直接拖 VideoClip 指定（itemId -> clip），优先级高于约定路径。
    /// </summary>
    public class SlotIntroVideoPlayer : MonoBehaviour
    {
        [Header("面板外观")]
        [Tooltip("视频面板尺寸（像素），16:9 以内等比适配")]
        [SerializeField] private Vector2 panelSize = new Vector2(480f, 320f);
        [Tooltip("面板相对屏幕右上角的边距（像素）")]
        [SerializeField] private Vector2 screenMargin = new Vector2(24f, 24f);
        [Tooltip("关闭按钮尺寸（像素）")]
        [SerializeField] private float closeButtonSize = 32f;

        [Header("位置自适应")]
        [Tooltip("避让区域外扩边距（像素，按槽位所在 Canvas 缩放）")]
        [SerializeField] private float avoidPadding = 16f;
        [Tooltip("槽位上方为打勾确认按钮预留的高度（像素，按钮本体 72 + 间距 8，留余量）")]
        [SerializeField] private float confirmReserveHeight = 96f;

        [Header("视频")]
        [Tooltip("视频分辨率（RenderTexture 尺寸）；越大越清晰越耗")]
        [SerializeField] private int renderTextureSize = 512;
        [Tooltip("指定 itemId -> VideoClip 的映射（优先于 Resources/Videos/Items 约定路径）")]
        [SerializeField] private ItemClipEntry[] fallbackClips;

        [System.Serializable]
        public class ItemClipEntry
        {
            public string itemId;
            public VideoClip clip;
        }

        private static SlotIntroVideoPlayer _instance;

        private Canvas canvas;
        private RectTransform bubbleRoot;
        private RawImage videoImage;
        private AspectRatioFitter aspectFitter;
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private string currentItemId;

        /// <summary>当前气泡是否可见</summary>
        public bool BVisible { get; private set; }

        /// <summary>获取或惰性创建单例</summary>
        public static SlotIntroVideoPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[SlotIntroVideoPlayer]");
                    _instance = go.AddComponent<SlotIntroVideoPlayer>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUi();
        }

        // ==================== 对外接口 ====================

        /// <summary>
        /// 显示面板并循环播放指定道具的介绍视频（固定锚定在屏幕右上角）。
        /// 无视频文件时静默隐藏（返回 false）。
        /// </summary>
        /// <param name="itemId">ItemCatalog 数字代号（如 "11"）</param>
        public static bool Show(string itemId)
        {
            return Instance.ShowInternal(itemId, null);
        }

        /// <summary>
        /// 显示面板并循环播放指定道具的介绍视频（默认屏幕右上角；
        /// slotAnchor 为目标槽位矩形，用于位置自适应——与槽位及其上方确认按钮
        /// 重叠时自动换角避让，可为 null 表示不做避让）。
        /// 无视频文件时静默隐藏（返回 false）。
        /// </summary>
        public static bool Show(string itemId, RectTransform slotAnchor)
        {
            return Instance.ShowInternal(itemId, slotAnchor);
        }

        /// <summary>隐藏气泡并停止播放（含关闭按钮点击）</summary>
        public static void Hide()
        {
            if (_instance != null)
            {
                _instance.HideInternal();
            }
        }

        // ==================== 内部实现 ====================

        private bool ShowInternal(string itemId, RectTransform slotAnchor)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                HideInternal();
                return false;
            }
            VideoClip clip = ResolveClip(itemId);
            if (clip == null)
            {
                HideInternal();
                return false;
            }

            currentItemId = itemId;
            videoPlayer.Stop();
            videoPlayer.clip = clip;
            videoPlayer.isLooping = true;
            bubbleRoot.gameObject.SetActive(true);
            UpdateAvoidance(slotAnchor);
            BVisible = true;
            videoPlayer.Play();
            return true;
        }

        private void HideInternal()
        {
            currentItemId = null;
            BVisible = false;
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                videoPlayer.Stop();
            }
            if (bubbleRoot != null)
            {
                bubbleRoot.gameObject.SetActive(false);
            }
        }

        // ==================== 位置自适应（避让槽位/确认按钮） ====================

        // 角落候选顺序：右上（默认）→ 左上 → 右下 → 左下；（x: 1=右 0=左, y: 1=上 0=下）
        private static readonly int[,] cornerPreference = { { 1, 1 }, { 0, 1 }, { 1, 0 }, { 0, 0 } };

        private readonly Vector3[] cornerBuffer = new Vector3[4];

        /// <summary>
        /// 位置自适应：面板默认锚定右上角；当目标槽位（含其上方打勾确认按钮的预留区）
        /// 与面板屏幕矩形相交时，按候选顺序换到第一个不遮挡的角落。
        /// 打勾确认按钮挂在槽位正上方（见 PropSelectionDirector.ShowConfirmCheck），
        /// 且比本方法晚一拍显示，故避让矩形需向上预留其高度。
        /// </summary>
        private void UpdateAvoidance(RectTransform slotAnchor)
        {
            if (slotAnchor == null)
            {
                AnchorToCorner(1, 1);
                return;
            }

            Rect avoidRect = GetSlotAvoidScreenRect(slotAnchor);
            for (int i = 0; i < cornerPreference.GetLength(0); i++)
            {
                AnchorToCorner(cornerPreference[i, 0], cornerPreference[i, 1]);
                if (!GetPanelScreenRect().Overlaps(avoidRect))
                {
                    return;
                }
            }
        }

        /// <summary>把面板锚定到屏幕某个角落（right: 1=右 0=左；top: 1=上 0=下）</summary>
        private void AnchorToCorner(int right, int top)
        {
            bubbleRoot.anchorMin = new Vector2(right, top);
            bubbleRoot.anchorMax = new Vector2(right, top);
            bubbleRoot.pivot = new Vector2(right, top);
            bubbleRoot.anchoredPosition = new Vector2(
                right == 1 ? -screenMargin.x : screenMargin.x,
                top == 1 ? -screenMargin.y : screenMargin.y);
        }

        /// <summary>面板在屏幕像素空间的矩形（本组件 Canvas 为 ScreenSpaceOverlay，世界坐标即屏幕像素）</summary>
        private Rect GetPanelScreenRect()
        {
            bubbleRoot.GetWorldCorners(cornerBuffer); // 顺序：左下、左上、右上、右下
            return Rect.MinMaxRect(cornerBuffer[0].x, cornerBuffer[0].y, cornerBuffer[2].x, cornerBuffer[2].y);
        }

        /// <summary>
        /// 槽位在屏幕像素空间的避让矩形：槽位矩形外扩边距，并向上延伸确认按钮预留高度
        /// </summary>
        private Rect GetSlotAvoidScreenRect(RectTransform slotAnchor)
        {
            Canvas slotCanvas = slotAnchor.GetComponentInParent<Canvas>();
            Camera cam = slotCanvas != null && slotCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? slotCanvas.worldCamera : null;
            float scale = slotCanvas != null ? slotCanvas.scaleFactor : 1f;

            slotAnchor.GetWorldCorners(cornerBuffer);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, cornerBuffer[0]);
            Vector2 max = min;
            for (int i = 1; i < cornerBuffer.Length; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, cornerBuffer[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            float pad = avoidPadding * scale;
            return Rect.MinMaxRect(min.x - pad, min.y - pad,
                max.x + pad, max.y + pad + confirmReserveHeight * scale);
        }

        /// <summary>
        /// 视频准备完成：按视频真实分辨率重建 RenderTexture（RenderTexture 渲染模式本身不保比例，
        /// 方形 RT 会把 16:9 视频压扁）并同步 AspectRatioFitter，保证显示不变形、不超出内圈
        /// </summary>
        private void OnVideoPrepared(VideoPlayer vp)
        {
            int w = vp.texture != null ? vp.texture.width : (vp.clip != null ? (int)vp.clip.width : 0);
            int h = vp.texture != null ? vp.texture.height : (vp.clip != null ? (int)vp.clip.height : 0);
            if (w <= 0 || h <= 0)
            {
                return;
            }
            float aspect = (float)w / h;
            if (aspectFitter != null)
            {
                aspectFitter.aspectRatio = aspect;
            }

            int rtH = renderTextureSize;
            int rtW = Mathf.Clamp(Mathf.RoundToInt(rtH * aspect), 16, 4096);
            if (renderTexture.width != rtW || renderTexture.height != rtH)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = new RenderTexture(rtW, rtH, 0);
                renderTexture.Create();
                vp.targetTexture = renderTexture;
                videoImage.texture = renderTexture;
            }
        }

        /// <summary>解析 itemId 的视频：优先 Inspector 映射表，其次 Resources/Videos/Items/{itemId}.mp4 约定路径</summary>
        private VideoClip ResolveClip(string itemId)
        {
            if (fallbackClips != null)
            {
                foreach (ItemClipEntry entry in fallbackClips)
                {
                    if (entry != null && entry.itemId == itemId && entry.clip != null)
                    {
                        return entry.clip;
                    }
                }
            }
            return Resources.Load<VideoClip>($"Videos/Items/{itemId}");
        }

        // ==================== UI 搭建 ====================

        private void BuildUi()
        {
            // 独立 Canvas（置顶）
            var canvasGo = new GameObject("SlotIntroVideoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f; // 横屏统一匹配高度，与场景 Canvas 策略一致

            // 面板根：固定锚定屏幕右上角（无气泡装饰，视频直接铺满）
            var panelGo = new GameObject("VideoPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            bubbleRoot = (RectTransform)panelGo.transform;
            bubbleRoot.sizeDelta = panelSize;
            AnchorToCorner(1, 1); // 默认右上角（无避让目标时的落点）

            // 关闭按钮（气泡右上角外侧）
            var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(bubbleRoot, false);
            var closeRect = (RectTransform)closeGo.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.sizeDelta = new Vector2(closeButtonSize, closeButtonSize);
            closeRect.anchoredPosition = new Vector2(closeButtonSize * 0.3f, closeButtonSize * 0.3f);
            Image closeBg = closeGo.GetComponent<Image>();
            closeBg.color = new Color(0.85f, 0.2f, 0.2f, 0.95f);
            closeGo.GetComponent<Button>().onClick.AddListener(HideInternal);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(closeRect, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text label = labelGo.GetComponent<Text>();
            label.text = "X";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null)
            {
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            label.fontSize = Mathf.RoundToInt(closeButtonSize * 0.55f);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;

            // 视频显示区：直接铺满面板，AspectRatioFitter 按视频宽高比居中等比适配（不超边、不留白边）
            var imageGo = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            imageGo.transform.SetParent(bubbleRoot, false);
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            videoImage = imageGo.GetComponent<RawImage>();
            videoImage.raycastTarget = false;
            aspectFitter = imageGo.GetComponent<AspectRatioFitter>();
            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspectFitter.aspectRatio = 16f / 9f; // 视频 prepare 后按真实分辨率更新

            // VideoPlayer：输出到 RenderTexture → RawImage
            renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 0);
            renderTexture.Create();
            videoImage.texture = renderTexture;

            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None; // 介绍视频静音播放
            videoPlayer.prepareCompleted += OnVideoPrepared;

            bubbleRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }
    }
}

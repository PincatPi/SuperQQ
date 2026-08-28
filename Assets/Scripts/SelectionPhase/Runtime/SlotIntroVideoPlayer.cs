using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 槽位介绍视频面板 — 玩家图标到达道具槽位时在【屏幕右上角】弹出，循环播放该道具的介绍 mp4。
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
            return Instance.ShowInternal(itemId);
        }

        /// <summary>
        /// 显示面板并循环播放指定道具的介绍视频（位置固定屏幕右上角，
        /// slotAnchor 参数仅为兼容旧调用保留，不再跟随槽位）。
        /// 无视频文件时静默隐藏（返回 false）。
        /// </summary>
        public static bool Show(string itemId, RectTransform slotAnchor)
        {
            return Instance.ShowInternal(itemId);
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

        private bool ShowInternal(string itemId)
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
            bubbleRoot.anchorMin = new Vector2(1f, 1f);
            bubbleRoot.anchorMax = new Vector2(1f, 1f);
            bubbleRoot.pivot = new Vector2(1f, 1f);
            bubbleRoot.sizeDelta = panelSize;
            bubbleRoot.anchoredPosition = new Vector2(-screenMargin.x, -screenMargin.y);

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

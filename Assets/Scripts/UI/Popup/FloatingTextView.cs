using TMPro;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 浮动文本视图 — 挂在浮动文本 Prefab 的根节点上
    /// 仅承载一段临时文本：由 PopupManager.ShowFloatingText 实例化播放，
    /// 固定时长后自动关闭销毁（不支持手动关闭，无按钮等额外绑定）
    /// 播放期间由本类自驱动渐显/渐隐动画（CanvasGroup 透明度），并在渐显/渐隐阶段向上缓慢漂移，
    /// 完全不透明期间保持静止；渐隐在展示时长结束前完成，销毁时机仍由 PopupManager 统一管理
    /// 位置语义：局部坐标直接写入 anchoredPosition；世界坐标版本自动转换为父容器局部坐标
    /// 本类不含任何游戏业务逻辑，也不感知 PopupManager 的具体实现
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class FloatingTextView : MonoBehaviour
    {
        [Tooltip("文本组件；未配置时回退查找子级 TMP_Text")]
        [SerializeField] private TMP_Text _label;

        [Header("渐显渐隐")]
        [Tooltip("渐显时长（秒），0 表示立即完全显示")]
        [Min(0f)]
        [SerializeField] private float _fadeInDuration = 0.25f;

        [Tooltip("渐隐时长（秒），在展示时长结束前完成，0 表示不渐隐")]
        [Min(0f)]
        [SerializeField] private float _fadeOutDuration = 0.5f;

        [Header("位移")]
        [Tooltip("向上漂移速度（父容器局部单位/秒）：仅在渐显与渐隐阶段生效，文本完全不透明时静止")]
        [SerializeField] private float _driftSpeed = 20f;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;

        // 播放状态：仅当 PopupManager 调用 Play 后才开始推进
        private float _totalDuration;
        private float _elapsed;
        private bool _bIsPlaying;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            if (_label == null)
            {
                _label = GetComponentInChildren<TMP_Text>();
            }
            if (_label == null)
            {
                Debug.LogWarning("[FloatingTextView] 未找到 TMP 文本组件，SetContent 将不生效。", this);
            }

            // CanvasGroup 用于整体透明度控制；Prefab 未配置时自动补挂，保证渐隐渐显始终可用
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void Update()
        {
            if (!_bIsPlaying)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            _canvasGroup.alpha = EvaluateAlpha(_elapsed);

            // 仅渐显/渐隐阶段向上漂移，完全不透明期间保持静止
            if (_driftSpeed != 0f && IsInFadePhase(_elapsed))
            {
                Vector3 position = _rectTransform.localPosition;
                position.y += _driftSpeed * Time.deltaTime;
                _rectTransform.localPosition = position;
            }

            if (_elapsed >= _totalDuration)
            {
                // 展示时长已到：停止动画，实例销毁由 PopupManager 统一执行
                _bIsPlaying = false;
            }
        }

        /// <summary>
        /// 开始播放：记录展示总时长并从头开始渐显（由 PopupManager 在设置内容与位置后调用）
        /// </summary>
        /// <param name="duration">展示总时长（秒），须与 PopupManager 的自动销毁时长一致</param>
        public void Play(float duration)
        {
            _totalDuration = Mathf.Max(0f, duration);
            _elapsed = 0f;
            _bIsPlaying = true;
            _canvasGroup.alpha = EvaluateAlpha(0f);
        }

        /// <summary>
        /// 设置文本内容（由 PopupManager.ShowFloatingText 调用）
        /// </summary>
        /// <param name="content">文本内容；为 null 时显示为空</param>
        public void SetContent(string content)
        {
            if (_label == null)
            {
                return;
            }
            _label.text = content ?? string.Empty;
        }

        /// <summary>
        /// 设置展示位置（父容器局部坐标，等同 RectTransform.anchoredPosition）
        /// </summary>
        public void SetPosition(Vector2 anchoredPosition)
        {
            _rectTransform.anchoredPosition = anchoredPosition;
        }

        /// <summary>
        /// 按世界坐标设置展示位置：世界坐标 → 屏幕坐标 → 父容器局部坐标
        /// 需在实例化到容器下之后调用（PopupManager 内部已保证）
        /// </summary>
        /// <param name="camera">观察世界的相机（通常为场景主相机）；为 null 时回退 Camera.main</param>
        public void SetWorldPosition(Vector3 worldPosition, Camera camera = null)
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null)
            {
                Debug.LogWarning("[FloatingTextView] 父节点不是 RectTransform，世界坐标转换失败。", this);
                return;
            }

            // 世界→屏幕投影必须使用观察世界的相机；传 null 时 WorldToScreenPoint 不做投影，
            // 会把世界坐标原样当作屏幕像素，导致文本落在屏幕左下角
            Camera worldCamera = camera != null ? camera : Camera.main;
            if (worldCamera == null)
            {
                Debug.LogWarning("[FloatingTextView] 未找到世界相机（Camera.main），世界坐标转换失败。", this);
                return;
            }

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition);
            Camera uiCamera = ResolveUICamera();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, uiCamera, out Vector2 localPoint))
            {
                // 写入 localPosition 而非 anchoredPosition：转换结果是父容器局部坐标（原点在父 pivot），
                // 与自身锚点配置无关，避免 Prefab 锚点不在中心时产生偏移
                _rectTransform.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            }
        }

        /// <summary>
        /// 判断当前是否处于渐显或渐隐阶段（与 EvaluateAlpha 的阶段划分保持一致，位移只在这两阶段生效）
        /// </summary>
        private bool IsInFadePhase(float elapsed)
        {
            if (_fadeInDuration > 0f && elapsed < _fadeInDuration)
            {
                return true;
            }
            return _fadeOutDuration > 0f && elapsed >= _totalDuration - _fadeOutDuration;
        }

        /// <summary>
        /// 按播放进度求值当前透明度：0 ~ 渐显时长线性升至 1，展示末期线性降至 0，其余时间为 1
        /// 渐显与渐隐时段重叠（总时长过短）时优先渐显，渐隐从当前值继续，避免跳变
        /// </summary>
        private float EvaluateAlpha(float elapsed)
        {
            if (_fadeInDuration > 0f && elapsed < _fadeInDuration)
            {
                return Mathf.Clamp01(elapsed / _fadeInDuration);
            }

            float fadeOutStart = _totalDuration - _fadeOutDuration;
            if (_fadeOutDuration > 0f && elapsed >= fadeOutStart)
            {
                return Mathf.Clamp01((_totalDuration - elapsed) / _fadeOutDuration);
            }

            return 1f;
        }

        /// <summary>
        /// 解析渲染 UI 的相机：仅用于屏幕→局部坐标转换；Overlay 画布返回 null（RectTransformUtility 约定）
        /// </summary>
        private Camera ResolveUICamera()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                return canvas.worldCamera;
            }
            return null;
        }
    }
}

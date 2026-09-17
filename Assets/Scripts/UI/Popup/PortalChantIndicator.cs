using SuperQQ.Player;
using TMPro;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 传送门吟唱倒计时指示器 — 参考 FloatingTextView 显示逻辑，挂载在吟唱提示 Prefab 根节点
    /// （基于 FloatingText Prefab 制作：根节点 RectTransform + CanvasGroup + TMP 文本）
    /// 由 Portal 在玩家进入触发区开始停留计时时实例化到浮动文本容器下，
    /// 玩家头顶跟随显示"吟唱中...剩余X.Xs"（0.1s 精度）；
    /// 传送完成 / 中途离开触发区时由 Portal 销毁（不走 PopupManager 自动销毁，无渐隐漂移）
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PortalChantIndicator : MonoBehaviour
    {
        [Tooltip("文本组件；未配置时回退查找子级 TMP_Text")]
        [SerializeField] private TMP_Text _label;

        [Header("跟随")]
        [Tooltip("文本锚点相对玩家世界坐标的偏移（世界单位，如 1.2 = 头顶上方 1.2 格）")]
        [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 1.2f, 0f);

        [Tooltip("文本格式（{0} 为剩余秒数，保留 1 位小数）")]
        [SerializeField] private string _format = "吟唱中...剩余{0:F1}s";

        [Header("渐显")]
        [Tooltip("渐显时长（秒），0 表示立即完全显示")]
        [Min(0f)]
        [SerializeField] private float _fadeInDuration = 0.2f;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;

        /// <summary>跟随的玩家（null 时停止跟随）</summary>
        private PlayerController _target;
        private float _elapsed;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            if (_label == null)
            {
                _label = GetComponentInChildren<TMP_Text>();
            }
            if (_label == null)
            {
                Debug.LogWarning("[PortalChantIndicator] 未找到 TMP 文本组件，倒计时文本将不生效。", this);
            }

            // CanvasGroup 用于整体透明度控制；Prefab 未配置时自动补挂（与 FloatingTextView 同口径）
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            _canvasGroup.alpha = _fadeInDuration > 0f ? 0f : 1f;
        }

        /// <summary>
        /// 绑定要跟随的玩家并立即定位到其头顶（由 Portal 在实例化后调用）
        /// </summary>
        public void Bind(PlayerController player)
        {
            _target = player;
            _elapsed = 0f;
            SyncPosition();
        }

        /// <summary>
        /// 刷新剩余时间显示（remaining 秒，精确到 0.1s，由 Portal 每物理帧调用）
        /// </summary>
        public void UpdateRemaining(float remaining)
        {
            if (_label != null)
            {
                _label.text = string.Format(_format, Mathf.Max(0f, remaining));
            }
        }

        private void Update()
        {
            // 渐显推进（无渐隐：销毁时机由 Portal 决定，倒计时文本通常瞬时消失）
            _elapsed += Time.deltaTime;
            if (_fadeInDuration > 0f)
            {
                _canvasGroup.alpha = Mathf.Clamp01(_elapsed / _fadeInDuration);
            }

            if (_target != null)
            {
                SyncPosition();
            }
        }

        /// <summary>
        /// 世界坐标 → 屏幕坐标 → 父容器局部坐标（与 FloatingTextView.SetWorldPosition 同口径）
        /// </summary>
        private void SyncPosition()
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null)
            {
                Debug.LogWarning("[PortalChantIndicator] 父节点不是 RectTransform，位置跟随失败。", this);
                return;
            }

            Camera worldCamera = Camera.main;
            if (worldCamera == null)
            {
                return;
            }

            Vector3 worldPos = _target.transform.position + _worldOffset;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPos);
            Camera uiCamera = ResolveUICamera();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, uiCamera, out Vector2 localPoint))
            {
                _rectTransform.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            }
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

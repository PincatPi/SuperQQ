using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 触屏输入按钮：挂在 uGUI Image/Button 上，
    /// 通过 EventSystem 的 Pointer 事件维护按压状态（IsPressed）。
    /// EventSystem 按 pointerId 区分每个触点，天然支持多点触控。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class TouchInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>按钮当前是否被按住（只读）</summary>
        public bool IsPressed { get; private set; }

        [Header("按压视觉反馈")]
        [SerializeField, Tooltip("按压时的缩放比例")]
        private float pressedScale = 0.92f;

        [SerializeField, Tooltip("按压时叠加的颜色（与 Image 颜色相乘）")]
        private Color pressedTint = new Color(0.7f, 0.7f, 0.7f, 1f);

        private Image image;
        private Color normalColor;
        private Vector3 normalScale;

        private void Awake()
        {
            image = GetComponent<Image>();
            normalColor = image.color;
            normalScale = transform.localScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsPressed = true;
            ApplyPressedVisual(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
            ApplyPressedVisual(false);
        }

        /// <summary>手指滑出按钮区域时释放，防止卡键</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!IsPressed) return;
            IsPressed = false;
            ApplyPressedVisual(false);
        }

        private void OnDisable()
        {
            // 面板被隐藏/禁用时强制释放，避免状态残留
            ForceRelease();
        }

        /// <summary>强制释放按压状态并复位视觉（面板经 CanvasGroup 隐藏时由外部调用）</summary>
        public void ForceRelease()
        {
            IsPressed = false;
            ApplyPressedVisual(false);
        }

        private void ApplyPressedVisual(bool pressed)
        {
            if (image != null)
                image.color = pressed ? normalColor * pressedTint : normalColor;
            transform.localScale = pressed ? normalScale * pressedScale : normalScale;
        }
    }
}

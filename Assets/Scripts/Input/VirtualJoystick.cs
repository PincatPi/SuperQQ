using UnityEngine;
using UnityEngine.EventSystems;

namespace SuperQQ.UI
{
    /// <summary>
    /// 虚拟摇杆（类王者荣耀移动轮盘）：挂在 background（底盘）上，
    /// handle（center，中心把手）可被手指拖动偏移，松手自动回中。
    /// 通过 EventSystem 的 Pointer/Drag 事件驱动，按下后即使手指滑出底盘仍持续跟随。
    /// 输出归一化方向 Direction（各轴 -1~1，模长 0~1），供输入层消费。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("摇杆引用")]
        [SerializeField, Tooltip("摇杆底盘 RectTransform；留空则使用自身（本组件应挂在底盘上）")]
        private RectTransform background;

        [SerializeField, Tooltip("摇杆中心把手（center），被拖动偏移的子物体")]
        private RectTransform handle;

        [Header("摇杆参数")]
        [SerializeField, Tooltip("把手可偏移的最大半径（像素）；0 = 自动取底盘宽度的一半")]
        private float handleRange = 0f;

        /// <summary>当前摇杆方向（归一化，各轴 -1~1，模长 0~1；松手/死区内为 zero）</summary>
        public Vector2 Direction { get; private set; }

        /// <summary>当前是否有手指按住摇杆</summary>
        public bool IsHeld { get; private set; }

        // 把手初始 anchoredPosition（回中目标）
        private Vector2 _handleHomePos;

        private void Awake()
        {
            if (background == null)
            {
                background = transform as RectTransform;
            }
            if (handle != null)
            {
                _handleHomePos = handle.anchoredPosition;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            UpdateHandle(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateHandle(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetHandle();
        }

        private void OnDisable()
        {
            // 面板被隐藏/禁用时强制回中，避免输入状态残留
            ForceRelease();
        }

        /// <summary>强制松手回中并清空方向输出（面板经 CanvasGroup 隐藏时由外部调用）</summary>
        public void ForceRelease()
        {
            ResetHandle();
        }

        /// <summary>根据触点位置更新把手偏移与方向输出</summary>
        private void UpdateHandle(PointerEventData eventData)
        {
            if (background == null)
            {
                return;
            }

            // 触点转换为底盘局部坐标（Overlay 画布 pressEventCamera 为 null，API 已兼容）
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                return;
            }

            // 局部坐标原点为 pivot，统一换算为相对底盘中心的偏移，兼容任意 pivot 配置
            Rect rect = background.rect;
            Vector2 rectCenter = new Vector2(
                (0.5f - background.pivot.x) * rect.width,
                (0.5f - background.pivot.y) * rect.height);
            Vector2 offset = localPoint - rectCenter;

            // 夹紧在最大半径内
            float range = handleRange > 0f ? handleRange : rect.width * 0.5f;
            if (range <= 0f)
            {
                return;
            }
            offset = Vector2.ClampMagnitude(offset, range);

            if (handle != null)
            {
                handle.anchoredPosition = _handleHomePos + offset;
            }
            Direction = offset / range;
        }

        /// <summary>把手回中并清空方向输出</summary>
        private void ResetHandle()
        {
            IsHeld = false;
            Direction = Vector2.zero;
            if (handle != null)
            {
                handle.anchoredPosition = _handleHomePos;
            }
        }
    }
}

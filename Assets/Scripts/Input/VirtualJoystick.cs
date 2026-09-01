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

        [SerializeField, Range(0f, 0.9f), Tooltip("中心死区（归一化半径比例，0~0.9）：偏移比例小于该值时 Direction 输出为 zero，把手仍跟随手指")]
        private float deadZone = 0f;

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

            Vector2 normalized = offset / range;
            float magnitude = normalized.magnitude;
            if (magnitude < deadZone)
            {
                // 死区内：把手跟随但不输出方向，不触发玩家移动
                Direction = Vector2.zero;
            }
            else
            {
                // 越过死区后重映射为 0~1，保证输出从 0 平滑起步而非从 deadZone 突变
                Direction = normalized * ((magnitude - deadZone) / ((1f - deadZone) * magnitude));
            }
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

#if UNITY_EDITOR
        // Gizmos 圆环分段数
        private const int GizmoSegments = 48;

        /// <summary>Scene 视图可视化：黄色 = 最大偏移半径，红色 = 中心死区</summary>
        private void OnDrawGizmos()
        {
            RectTransform bg = background != null ? background : transform as RectTransform;
            if (bg == null)
            {
                return;
            }

            Rect rect = bg.rect;
            float range = handleRange > 0f ? handleRange : rect.width * 0.5f;
            if (range <= 0f)
            {
                return;
            }

            // 底盘中心与局部 X/Y 单位向量换算到世界空间，兼容 Canvas 缩放与旋转
            Vector3 center = bg.TransformPoint(new Vector3(
                (0.5f - bg.pivot.x) * rect.width,
                (0.5f - bg.pivot.y) * rect.height, 0f));
            Vector3 right = bg.TransformVector(Vector3.right);
            Vector3 up = bg.TransformVector(Vector3.up);

            DrawGizmoCircle(center, right, up, range, Color.yellow);
            if (deadZone > 0f)
            {
                DrawGizmoCircle(center, right, up, range * deadZone, Color.red);
            }
        }

        private static void DrawGizmoCircle(Vector3 center, Vector3 right, Vector3 up, float radius, Color color)
        {
            Gizmos.color = color;
            Vector3 prev = center + right * radius;
            for (int i = 1; i <= GizmoSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f) / GizmoSegments;
                Vector3 next = center + right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}

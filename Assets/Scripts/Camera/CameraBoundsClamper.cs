using Cinemachine;
using UnityEngine;

namespace SuperQQ.CameraControl
{
    /// <summary>
    /// 相机边界钳制扩展 — Cinemachine 管线扩展，直接挂载在 VirtualCamera 上使用
    /// （选中 VirtualCamera → Extensions 下拉 → Camera Bounds Clamper）
    ///
    /// 在 Cinemachine 取景管线内对相机位置做边界钳制，替代 CinemachineConfiner2D：
    /// 当最大视野尺寸与边界尺寸相等（镜头最远恰好铺满整张地图）时，
    /// Confiner2D 预计算的"相机中心可活动区域"会退化为空集，导致钳制失效、相机卡死在越界位置
    /// 本扩展对该情形做了显式处理：视野大于等于边界时在该轴上居中
    ///
    /// 因为工作在 Cinemachine 管线内部，不受 MonoBehaviour 执行顺序影响，
    /// 且钳制结果参与阻尼计算，移动平滑无跳变
    /// </summary>
    [AddComponentMenu("Cinemachine/Extensions/Camera Bounds Clamper")]
    [SaveDuringPlay]
    public class CameraBoundsClamper : CinemachineExtension
    {
        [Header("边界来源（二选一）")]
        [SerializeField] private Collider2D _boundsCollider;            // 边界碰撞体（Box/Polygon/Composite 均可，取其整体包围盒）
        [SerializeField] private bool _useManualRect;                   // 勾选后使用手动填写的矩形边界
        [SerializeField] private Rect _manualBounds = new(-10f, -5f, 20f, 10f); // 手动边界（x, y, 宽, 高）

        /// <summary>
        /// Cinemachine 管线回调：在 Body 阶段（位置计算完成后）钳制相机位置
        /// </summary>
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage,
            ref CameraState state,
            float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body)
            {
                return;
            }

            if (!state.Lens.Orthographic)
            {
                return;
            }

            if (!TryGetBounds(out Bounds bounds))
            {
                return;
            }

            float halfHeight = state.Lens.OrthographicSize;
            float halfWidth = halfHeight * state.Lens.Aspect;

            Vector3 pos = state.RawPosition;
            pos.x = ClampAxis(pos.x, bounds.center.x, bounds.extents.x, halfWidth);
            pos.y = ClampAxis(pos.y, bounds.center.y, bounds.extents.y, halfHeight);
            state.RawPosition = pos;
        }

        /// <summary>
        /// 单轴钳制：视野半宽/高小于边界半径时正常夹取；视野大于等于边界时居中
        /// 居中策略保证"镜头铺满整张地图"时永远对准地图中心，不会偏移漏出界外
        /// </summary>
        private static float ClampAxis(float value, float center, float extent, float halfView)
        {
            if (halfView >= extent)
            {
                return center;
            }
            return Mathf.Clamp(value, center - extent + halfView, center + extent - halfView);
        }

        private bool TryGetBounds(out Bounds bounds)
        {
            if (_useManualRect)
            {
                bounds = new Bounds(_manualBounds.center, _manualBounds.size);
                return true;
            }

            if (_boundsCollider != null)
            {
                bounds = _boundsCollider.bounds;
                return true;
            }

            bounds = default;
            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!TryGetBounds(out Bounds bounds))
            {
                return;
            }
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif
    }
}

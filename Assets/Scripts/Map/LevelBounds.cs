using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// 关卡边界 — 场景级单例，对外提供地图边界的包围盒数据与位置钳制辅助
    /// 挂载到场景中已有的边界物体上（与 BoxCollider2D 同物体，即 CameraBoundsClamper 引用的那个）
    /// 相机（CameraBoundsClamper）与玩家（PlayerController 状态机）共用同一份边界数据，
    /// 调整边界只需改这一个物体上的 BoxCollider2D
    /// 注意：本组件只读碰撞体的 bounds（轴对齐包围盒），不参与任何物理碰撞
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LevelBounds : MonoBehaviour
    {
        [Header("边界偏移")]
        [Tooltip("四条边相对于 BoxCollider2D 边缘的偏移量（x=左, y=右, z=下, w=上），正值向外扩张、负值向内收缩")]
        [SerializeField] private Vector4 _edgeOffset = Vector4.zero;

        // 单例实例（场景级，不 DontDestroyOnLoad）
        private static LevelBounds _instance;

        // 边界碰撞体（RequireComponent 保证存在，Awake 中缓存）
        private Collider2D _boundsCollider;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 当前场景中的全局唯一实例
        /// </summary>
        public static LevelBounds Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<LevelBounds>();
                }
                return _instance;
            }
        }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 场景级单例：重复挂载时销毁多余实例
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;

            _boundsCollider = GetComponent<Collider2D>();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ==================== 边界数据 ====================

        /// <summary>
        /// 应用四边偏移后的边界包围盒（实时读取引擎维护的碰撞体 bounds，边界物体移动时自动同步）
        /// </summary>
        public Bounds Bounds
        {
            get
            {
                Bounds bounds = _boundsCollider.bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                min.x -= _edgeOffset.x;  // 左边界：正值向外（-x 方向）扩张
                max.x += _edgeOffset.y;  // 右边界：正值向外（+x 方向）扩张
                min.y -= _edgeOffset.z;  // 下边界：正值向外（-y 方向）扩张
                max.y += _edgeOffset.w;  // 上边界：正值向外（+y 方向）扩张
                bounds.SetMinMax(min, max);
                return bounds;
            }
        }

        // ==================== 钳制辅助 ====================

        /// <summary>
        /// 边界盒中心（越界死亡后幽灵重生的目标位置）
        /// </summary>
        public Vector2 Center => Bounds.center;

        /// <summary>
        /// 四边夹紧：x/y 均夹紧到包围盒内
        /// 用于幽灵状态：上下左右均不允许越界
        /// </summary>
        public Vector2 ClampAll(Vector2 pos)
        {
            Bounds bounds = Bounds;
            pos.x = Mathf.Clamp(pos.x, bounds.min.x, bounds.max.x);
            pos.y = Mathf.Clamp(pos.y, bounds.min.y, bounds.max.y);
            return pos;
        }

        /// <summary>
        /// 指定高度是否低于下边界（掉落兜底判定）
        /// </summary>
        public bool IsBelow(float y)
        {
            return y < Bounds.min.y;
        }

        /// <summary>
        /// 指定位置是否越过任意一条边界（上/下/左/右）
        /// 用于存活/冻结状态的越界死亡判定：四边均不夹紧，越界即死
        /// </summary>
        public bool IsOutOfBounds(Vector2 pos)
        {
            Bounds bounds = Bounds;
            return pos.x < bounds.min.x || pos.x > bounds.max.x
                || pos.y < bounds.min.y || pos.y > bounds.max.y;
        }

        // ==================== 调试可视化 ====================

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_boundsCollider == null)
            {
                _boundsCollider = GetComponent<Collider2D>();
            }
            if (_boundsCollider == null)
            {
                return;
            }
            Bounds bounds = Bounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif
    }
}

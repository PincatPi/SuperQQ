using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 冷气飞行体 — 挂在冷气 Prefab 上
    /// 沿本地坐标系固定方向匀速直线运动，运动参数由 AirConditioner 初始化时指定
    /// 实例化时继承空调的旋转，因此本地方向即空调本地坐标系下的方向
    /// 到达自动销毁延迟后自行销毁，避免资源泄漏
    /// 命中检测和玩家冻结逻辑为次要部分，预留 OnTriggerEnter2D 扩展点
    /// </summary>
    public class ColdAirProjectile : MonoBehaviour
    {
        [Header("自动销毁")]
        // 自动销毁延迟（秒），超时后冷气自行销毁，避免资源泄漏
        [SerializeField] private float _autoDestroyDelay = 5f;

        // 本地坐标系下的运动方向（归一化后的向量）
        private Vector2 _direction;

        // 飞行速度（px/s）
        private float _speed;

        // 是否已完成初始化
        private bool _bIsInitialized;

        /// <summary>
        /// 初始化冷气运动参数并启动自动销毁计时
        /// </summary>
        /// <param name="direction">本地坐标系下的飞行方向</param>
        /// <param name="speed">飞行速度（px/s）</param>
        public void Initialize(Vector2 direction, float speed)
        {
            _direction = direction.normalized;
            _speed = speed;
            _bIsInitialized = true;

            // 延迟自动销毁，避免冷气飞出屏幕后持续占用资源
            Destroy(gameObject, _autoDestroyDelay);
        }

        private void Update()
        {
            if (!_bIsInitialized)
            {
                return;
            }

            // 沿本地坐标系固定方向匀速移动
            // 使用 Space.Self 使方向相对于冷气自身的旋转（与空调旋转一致）
            transform.Translate(_direction * _speed * Time.deltaTime, Space.Self);
        }

        // ==================== 次要部分预留扩展点 ====================

        // private void OnTriggerEnter2D(Collider2D other)
        // {
        //     // 命中玩家时触发冻结逻辑
        //     // 通过 IFreezable 接口调用玩家冻结
        //     // var freezable = other.GetComponent<IFreezable>();
        //     // if (freezable != null) { freezable.Freeze(); }
        // }
    }
}

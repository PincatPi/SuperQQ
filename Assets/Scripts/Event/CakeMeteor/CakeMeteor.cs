using SuperQQ.Map;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 小蛋糕陨石 — 陨石个体行为
    /// 由 CakeMeteorModifier 实例化后调用 Launch 注入飞行参数
    /// 匀速直线飞行，穿过地面与已放置道具（不与其交互），仅对玩家生效：
    /// 命中玩家即死并沿运动方向击飞（死亡表现），随后自身销毁
    /// 消亡保障：落出 LevelBounds 下边界销毁 + 生命周期超时兜底销毁，防止物体累积
    /// </summary>
    [RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
    public class CakeMeteor : MonoBehaviour
    {
        // 飞行速度（世界方向），由 Launch 注入
        private Vector2 _velocity;

        // 命中玩家时的击飞速度，由 Launch 注入
        private float _knockbackSpeed;

        // 最大存活时间（秒），超时强制销毁，由 Launch 注入
        private float _maxLifetime;

        // 已存活时间
        private float _elapsed;

        // 是否已注入飞行参数（未 Launch 前静止不动）
        private bool _bLaunched;

        // 运动学刚体（RequireComponent 保证存在，Awake 中缓存）
        private Rigidbody2D _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();

            // 运动完全确定（匀速直线），不受物理引擎扰动，利于未来按种子确定性复现
            _rigidbody.bodyType = RigidbodyType2D.Kinematic;
            _rigidbody.gravityScale = 0f;
        }

        /// <summary>
        /// 注入飞行参数并启动移动
        /// 由 CakeMeteorModifier 在实例化后立即调用
        /// </summary>
        /// <param name="velocity">飞行速度（世界方向）</param>
        /// <param name="knockbackSpeed">命中玩家时的击飞速度（死亡表现）</param>
        /// <param name="maxLifetime">最大存活时间（秒），超时强制销毁</param>
        public void Launch(Vector2 velocity, float knockbackSpeed, float maxLifetime)
        {
            _velocity = velocity;
            _knockbackSpeed = knockbackSpeed;
            _maxLifetime = maxLifetime;
            _elapsed = 0f;
            _bLaunched = true;
        }

        private void FixedUpdate()
        {
            if (!_bLaunched)
            {
                return;
            }

            _rigidbody.MovePosition(_rigidbody.position + _velocity * Time.fixedDeltaTime);

            // 生命周期兜底销毁
            _elapsed += Time.fixedDeltaTime;
            if (_maxLifetime > 0f && _elapsed >= _maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            // 落出关卡下边界销毁
            if (LevelBounds.Instance != null && LevelBounds.Instance.IsBelow(_rigidbody.position.y))
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 只影响玩家：地面、道具等其他碰撞体直接穿过，不作响应
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.BIsDead || player.BIsGhost)
            {
                return;
            }

            // 弹飞死亡：沿陨石运动方向击飞（死亡表现），短暂延迟后进入幽灵状态
            Vector2 dir = _velocity.sqrMagnitude > 0f ? _velocity.normalized : Vector2.down;
            Vector2 knockback = dir * _knockbackSpeed + Vector2.up * (_knockbackSpeed * 0.5f);
            player.PlayerKnockbackDie(knockback);

            Destroy(gameObject);
        }
    }
}

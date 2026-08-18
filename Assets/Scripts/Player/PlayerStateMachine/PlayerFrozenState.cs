using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 冻结状态：玩家被冰封，无法操作，刚体完全冻结（位置/旋转均锁定）
    /// 纯逻辑状态，不持有任何表现层资产：冰块等视觉由冻结来源（事件/道具）自行挂接
    /// 冻结期间仍可被击杀：PlayerDie/PlayerKnockbackDie 可正常切入死亡状态，
    /// Exit 会恢复冻结前的刚体约束，保证击飞等死亡表现物理正常
    /// </summary>
    public class PlayerFrozenState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // 进入冻结前的刚体约束，Exit 时原样恢复
        private RigidbodyConstraints2D _previousConstraints;

        public PlayerFrozenState(PlayerController ctx)
        {
            _ctx = ctx;
        }

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 冻结视为在地面（保持站立姿态，供动画层播放待机动画）
        /// </summary>
        public bool BIsGrounded => true;

        /// <summary>
        /// 冻结状态不可跳跃
        /// </summary>
        public bool BIsJumping => false;

        /// <summary>
        /// 冻结状态无跳跃滞空期
        /// </summary>
        public bool BIsJumpAirborne => false;

        /// <summary>
        /// 冻结状态水平速度恒为 0（刚体已全约束）
        /// </summary>
        public float HorizontalVelocity => 0f;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入冻结状态：清零速度并锁定刚体全部自由度
        /// </summary>
        public void Enter()
        {
            if (_ctx.Rb == null)
            {
                return;
            }

            _previousConstraints = _ctx.Rb.constraints;
            _ctx.Rb.velocity = Vector2.zero;
            _ctx.Rb.angularVelocity = 0f;
            _ctx.Rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        /// <summary>
        /// 退出冻结状态：恢复冻结前的刚体约束
        /// 无论切换到存活（解冻）还是死亡（冻结中被击杀），均保证物理表现正常
        /// </summary>
        public void Exit()
        {
            if (_ctx.Rb != null)
            {
                _ctx.Rb.constraints = _previousConstraints;
            }
        }

        /// <summary>
        /// 每帧更新：不消费任何输入，冻结期间无法操作
        /// </summary>
        public void Update() { }

        /// <summary>
        /// 物理帧更新：刚体已全约束，无需任何主动控制
        /// </summary>
        public void FixedUpdate() { }
    }
}

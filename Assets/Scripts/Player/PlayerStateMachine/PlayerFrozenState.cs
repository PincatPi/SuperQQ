using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 冻结状态：玩家被冰封，无法进行移动/跳跃输入，但物理模拟保持正常
    /// （重力、道具击飞等外力照常作用——冻结时被击飞会照常飞出去，空中冻结会照常下坠）
    /// 实现方式：刚体仅锁定旋转，冻结期间每物理帧仅将"非击飞导致的"水平速度归零
    /// （本帧被击飞施加的速度保留自然衰减，不会被立刻抹除）
    /// 纯逻辑状态，不持有任何表现层资产：冰块等视觉由冻结来源（事件/道具）自行挂接
    /// 冻结期间仍可被击杀：PlayerDie/PlayerKnockbackDie 可正常切入死亡状态，
    /// Exit 会恢复冻结前的刚体约束，保证死亡表现物理正常
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
        /// 冻结状态水平速度恒为 0（输入驱动已禁用，供动画层判断）
        /// </summary>
        public float HorizontalVelocity => 0f;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入冻结状态：清零水平速度、仅锁定旋转（保留重力与外力模拟）
        /// 竖直速度不清零：空中被冻结时保持当前上升/下落趋势，由重力自然接管
        /// </summary>
        public void Enter()
        {
            if (_ctx.Rb == null)
            {
                return;
            }

            _previousConstraints = _ctx.Rb.constraints;
            _ctx.Rb.velocity = new Vector2(0f, _ctx.Rb.velocity.y);
            _ctx.Rb.angularVelocity = 0f;
            _ctx.Rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        /// <summary>
        /// 退出冻结状态：恢复冻结前的刚体约束
        /// 无论切换到存活（解冻）还是死亡（冻结中被击杀），均保证物理表现正常；
        /// 解冻瞬间刚体速度保持冻结前的物理状态（如正在下落则继续自然下落）
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
        /// 物理帧更新：仅将"非击飞导致的"水平速度归零（贴边挤压、传送带等持续外力），
        /// 本帧被击飞（击退压制窗口内）时保留水平速度自然衰减；
        /// 竖直方向完全交给物理引擎（重力/击飞均不受影响）
        /// </summary>
        public void FixedUpdate()
        {
            if (_ctx.Rb == null || _ctx.BIsKnockbackStunned)
            {
                return;
            }

            Vector2 velocity = _ctx.Rb.velocity;
            if (velocity.x != 0f)
            {
                velocity.x = 0f;
                _ctx.Rb.velocity = velocity;
            }
        }
    }
}

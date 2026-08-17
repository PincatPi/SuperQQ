using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 死亡状态：存活 → 幽灵之间的短暂过渡
    /// 期间不消费任何输入（无法操作），物理照常模拟以保留击飞/坠落动量
    /// 倒计时结束后自动切换到幽灵状态
    /// </summary>
    public class PlayerDyingState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // 过渡时长（秒）：正值覆盖 PlayerController.DeathDuration，负值使用默认配置
        private readonly float _durationOverride;

        // 剩余过渡时间（秒）
        private float _timer;

        public PlayerDyingState(PlayerController ctx, float durationOverride = -1f)
        {
            _ctx = ctx;
            _durationOverride = durationOverride;
        }

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 死亡状态视为不在地面
        /// </summary>
        public bool BIsGrounded => false;

        /// <summary>
        /// 死亡状态不可跳跃
        /// </summary>
        public bool BIsJumping => false;

        /// <summary>
        /// 死亡状态无跳跃滞空期
        /// </summary>
        public bool BIsJumpAirborne => false;

        /// <summary>
        /// 当前水平速度（保留刚体实际速度，供动画层使用）
        /// </summary>
        public float HorizontalVelocity => _ctx.Rb != null ? _ctx.Rb.velocity.x : 0f;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入死亡状态：启动过渡倒计时
        /// 不修改刚体速度与重力，保留死亡瞬间的运动状态（击飞/坠落）
        /// </summary>
        public void Enter()
        {
            _timer = _durationOverride >= 0f ? _durationOverride : _ctx.DeathDuration;
        }

        /// <summary>
        /// 退出死亡状态
        /// </summary>
        public void Exit() { }

        /// <summary>
        /// 每帧更新：倒计时结束后由本状态自主切换到幽灵状态
        /// 不读取任何输入，死亡期间无法操作
        /// </summary>
        public void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _ctx.TransitionTo(new PlayerGhostState(_ctx));
            }
        }

        /// <summary>
        /// 物理帧无主动控制，由物理引擎自然模拟（重力、动量保留）
        /// </summary>
        public void FixedUpdate() { }
    }
}

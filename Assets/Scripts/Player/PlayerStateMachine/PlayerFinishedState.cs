using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 通关状态：角色到达终点后进入此状态
    /// 角色原地消失、禁用碰撞和物理、停止所有输入响应
    /// </summary>
    public class PlayerFinishedState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // Enter 时保存，Exit 时恢复
        private float _savedGravityScale;
        private Color _savedColor;

        public PlayerFinishedState(PlayerController ctx) => _ctx = ctx;

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 通关状态不在地面
        /// </summary>
        public bool BIsGrounded => false;

        /// <summary>
        /// 通关状态不跳跃
        /// </summary>
        public bool BIsJumping => false;

        /// <summary>
        /// 通关状态无水平速度
        /// </summary>
        public float HorizontalVelocity => 0f;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入通关状态：禁用碰撞体、取消重力、角色消失、停止速度
        /// </summary>
        public void Enter()
        {
            // 禁用碰撞体
            // 注意：Collider 可能为 null，必须做 null 检查，否则后续操作不会执行
            if (_ctx.Collider != null)
            {
                _ctx.Collider.enabled = false;
            }

            // 保存并取消重力
            _savedGravityScale = _ctx.Rb.gravityScale;
            _ctx.Rb.gravityScale = 0f;

            // 停止所有速度
            _ctx.Rb.velocity = Vector2.zero;
            _ctx.Rb.isKinematic = true;

            // 隐藏角色
            if (_ctx.Renderer != null)
            {
                _savedColor = _ctx.Renderer.color;
                _ctx.Renderer.enabled = false;
            }
        }

        /// <summary>
        /// 退出通关状态：恢复碰撞体、重力、角色可见、物理状态
        /// </summary>
        public void Exit()
        {
            // 恢复碰撞体
            if (_ctx.Collider != null)
            {
                _ctx.Collider.enabled = true;
            }

            // 恢复重力
            _ctx.Rb.gravityScale = _savedGravityScale;

            // 恢复物理
            _ctx.Rb.isKinematic = false;

            // 恢复角色可见
            if (_ctx.Renderer != null)
            {
                _ctx.Renderer.enabled = true;
                _ctx.Renderer.color = _savedColor;
            }
        }

        /// <summary>
        /// 通关状态无 Update 逻辑
        /// </summary>
        public void Update() { }

        /// <summary>
        /// 通关状态无 FixedUpdate 逻辑
        /// </summary>
        public void FixedUpdate() { }
    }
}

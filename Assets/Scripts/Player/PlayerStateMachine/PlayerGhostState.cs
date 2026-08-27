using UnityEngine;
using SuperQQ.Map;

namespace SuperQQ.Player
{
    /// <summary>
    /// 幽灵状态：四向平移、无重力、半透明、无碰撞
    /// 不与 Player/Ground 碰撞
    /// 边界行为：四边夹紧，上下左右均不允许越过地图边界
    /// </summary>
    public class PlayerGhostState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // 运行时数据
        private float _currentHorizontalVelocity;
        private float _currentVerticalVelocity;

        // Enter 时保存，Exit 时恢复
        private float _savedGravityScale;
        private Color _savedColor;

        public PlayerGhostState(PlayerController ctx) => _ctx = ctx;

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 幽灵始终不在地面
        /// </summary>
        public bool BIsGrounded => false;

        /// <summary>
        /// 幽灵不跳跃
        /// </summary>
        public bool BIsJumping => false;

        /// <summary>
        /// 幽灵无跳跃滞空期
        /// </summary>
        public bool BIsJumpAirborne => false;

        /// <summary>
        /// 当前水平速度
        /// </summary>
        public float HorizontalVelocity => _currentHorizontalVelocity;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入幽灵状态：禁用碰撞体、取消重力、半透明、重置速度、确定出生位置
        /// 出生位置：仅跌落下边界死亡传送至固定初始位置，其余死亡保持死亡位置
        /// </summary>
        public void Enter()
        {
            // 禁用碰撞体（不再与 Player/Ground 碰撞）
            // 注意：Collider 可能为 null（未配置预制体时），必须做 null 检查
            // 否则后续的取消重力、传送位置、设置半透明都不会执行
            if (_ctx.Collider != null)
            {
                _ctx.Collider.enabled = false;
            }

            // 保存并取消重力
            _savedGravityScale = _ctx.Rb.gravityScale;
            _ctx.Rb.gravityScale = 0f;
            _ctx.Rb.velocity = Vector2.zero;

            // 出生位置：仅跌落下边界死亡传送至固定初始位置，其余死亡保持死亡位置
            _ctx.Rb.position = _ctx.GhostSpawnAtFixedPosition
                ? (Vector2)_ctx.GhostSpawnPosition
                : _ctx.DeathPosition;

            // 保存并设置半透明
            if (_ctx.Renderer != null)
            {
                _savedColor = _ctx.Renderer.color;
                Color c = _savedColor;
                c.a = _ctx.GhostAlpha;
                _ctx.Renderer.color = c;
            }

            // 重置速度
            _currentHorizontalVelocity = 0f;
            _currentVerticalVelocity = 0f;
        }

        /// <summary>
        /// 退出幽灵状态：恢复碰撞体、重力、透明度
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

            // 恢复透明度
            if (_ctx.Renderer != null)
            {
                _ctx.Renderer.color = _savedColor;
            }
        }

        /// <summary>
        /// 幽灵状态无 Update 逻辑
        /// </summary>
        public void Update() { }

        /// <summary>
        /// 物理帧更新：四向平移、地图边界四边夹紧
        /// </summary>
        public void FixedUpdate()
        {
            ApplyGhostMovement();
            ClampToLevelBounds();
        }

        // ==================== 地图边界 ====================

        /// <summary>
        /// 边界约束：四边夹紧（上下左右均不允许越界）
        /// 未配置 LevelBounds 时静默跳过
        /// </summary>
        private void ClampToLevelBounds()
        {
            LevelBounds bounds = _ctx.LevelBounds;
            if (bounds == null)
            {
                return;
            }

            // 仅在产生修正时写回刚体位置
            Vector2 pos = _ctx.Rb.position;
            Vector2 clamped = bounds.ClampAll(pos);
            if (clamped != pos)
            {
                _ctx.Rb.position = clamped;
            }
        }

        // ==================== 四向平移 ====================

        /// <summary>
        /// 幽灵四向平移：水平竖直使用相同速度/加速度
        /// </summary>
        private void ApplyGhostMovement()
        {
            // 水平
            float targetH = _ctx.HorizontalInput * _ctx.GhostMoveSpeed;
            float rateH = Mathf.Abs(_ctx.HorizontalInput) > 0.01f
                ? _ctx.GhostAcceleration
                : _ctx.GhostDeceleration;
            _currentHorizontalVelocity = Mathf.MoveTowards(
                _currentHorizontalVelocity, targetH, rateH * Time.fixedDeltaTime);

            // 竖直
            float targetV = _ctx.VerticalInput * _ctx.GhostMoveSpeed;
            float rateV = Mathf.Abs(_ctx.VerticalInput) > 0.01f
                ? _ctx.GhostAcceleration
                : _ctx.GhostDeceleration;
            _currentVerticalVelocity = Mathf.MoveTowards(
                _currentVerticalVelocity, targetV, rateV * Time.fixedDeltaTime);

            _ctx.Rb.velocity = new Vector2(_currentHorizontalVelocity, _currentVerticalVelocity);
        }
    }
}

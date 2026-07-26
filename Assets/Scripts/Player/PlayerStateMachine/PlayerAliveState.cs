using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 存活状态：左右移动、可变高度跳跃、下落手感优化
    /// 所有运行时数据（土狼计时、跳跃保持计时等）归本状态私有
    /// 边界约束：左右限制、掉落死亡
    /// 提前放弃：当场上只剩一名存活玩家时，长按 Down Key 1.6 秒可提前结束关卡
    /// </summary>
    public class PlayerAliveState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // 运行时数据
        private bool _bIsGrounded;
        private bool _bIsJumping;
        private float _jumpHoldTimer;
        private float _coyoteTimer;
        private float _currentHorizontalVelocity;

        // 提前放弃长按计时器（秒）
        private float _earlyQuitHoldTimer;

        // 是否正在长按放弃中（用于进度显示）
        private bool _bIsEarlyQuitHolding;

        public PlayerAliveState(PlayerController ctx) => _ctx = ctx;

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 是否在地面上
        /// </summary>
        public bool BIsGrounded => _bIsGrounded;

        /// <summary>
        /// 是否正在跳跃
        /// </summary>
        public bool BIsJumping => _bIsJumping;

        /// <summary>
        /// 当前水平速度
        /// </summary>
        public float HorizontalVelocity => _currentHorizontalVelocity;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入存活状态，重置运行时数据
        /// </summary>
        public void Enter()
        {
            _currentHorizontalVelocity = _ctx.Rb.velocity.x;
            _bIsJumping = false;
            _jumpHoldTimer = 0f;
            _coyoteTimer = 0f;
            _earlyQuitHoldTimer = 0f;
            _bIsEarlyQuitHolding = false;
        }

        /// <summary>
        /// 退出存活状态
        /// </summary>
        public void Exit() { }

        /// <summary>
        /// 每帧更新：地面检测、跳跃起跳、跳跃截断、提前放弃长按检测
        /// </summary>
        public void Update()
        {
            CheckGround();
            HandleJumpStart();
            HandleJumpCut();
            HandleEarlyQuit();
        }

        /// <summary>
        /// 物理帧更新：水平移动、可变跳跃高度、下落手感、边界约束
        /// </summary>
        public void FixedUpdate()
        {
            ApplyHorizontalMovement();
            ApplyVariableJumpHeight();
            ApplyBetterFallGravity();
            ClampToMapBoundary();
        }

        // ==================== 地面检测 ====================

        /// <summary>
        /// 检测角色是否在地面，并维护土狼计时器
        /// </summary>
        private void CheckGround()
        {
            bool bWasGrounded = _bIsGrounded;
            Transform gc = _ctx.GroundCheck;
            _bIsGrounded = gc != null && Physics2D.OverlapCircle(
                gc.position, _ctx.GroundCheckRadius, _ctx.GroundLayer);

            if (_bIsGrounded)
            {
                _coyoteTimer = _ctx.CoyoteTime;
                if (!bWasGrounded)
                {
                    _bIsJumping = false;
                }
            }
            else
            {
                _coyoteTimer -= Time.deltaTime;
            }
        }

        // ==================== 跳跃 ====================

        /// <summary>
        /// 处理起跳：按下跳跃键且（在地面或土狼时间内）且未在跳跃中
        /// </summary>
        private void HandleJumpStart()
        {
            if (_ctx.JumpPressed && (_bIsGrounded || _coyoteTimer > 0f) && !_bIsJumping)
            {
                _ctx.Rb.velocity = new Vector2(_ctx.Rb.velocity.x, _ctx.JumpVelocity);
                _bIsJumping = true;
                _jumpHoldTimer = 0f;
                // 起跳消耗土狼时间，避免连跳
                _coyoteTimer = 0f;
            }
        }

        /// <summary>
        /// 处理松手短跳：上升过程中松手削减竖直速度
        /// </summary>
        private void HandleJumpCut()
        {
            if (_bIsJumping && !_ctx.JumpHeld && _ctx.Rb.velocity.y > 0f)
            {
                _ctx.Rb.velocity = new Vector2(
                    _ctx.Rb.velocity.x,
                    _ctx.Rb.velocity.y * _ctx.JumpCutMultiplier);
                _bIsJumping = false;
            }
        }

        // ==================== 移动 ====================

        /// <summary>
        /// 水平移动：朝目标速度平滑插值
        /// </summary>
        private void ApplyHorizontalMovement()
        {
            float targetVelocity = _ctx.HorizontalInput * _ctx.MoveSpeed;
            float rate = Mathf.Abs(_ctx.HorizontalInput) > 0.01f
                ? _ctx.Acceleration
                : _ctx.Deceleration;
            if (!_bIsGrounded)
            {
                rate *= _ctx.AirControlMultiplier;
            }

            _currentHorizontalVelocity = Mathf.MoveTowards(
                _currentHorizontalVelocity, targetVelocity, rate * Time.fixedDeltaTime);

            _ctx.Rb.velocity = new Vector2(_currentHorizontalVelocity, _ctx.Rb.velocity.y);
        }

        /// <summary>
        /// 可变跳跃高度：长按时持续追加向上速度
        /// </summary>
        private void ApplyVariableJumpHeight()
        {
            if (_bIsJumping && _ctx.JumpHeld && _jumpHoldTimer < _ctx.MaxJumpHoldTime)
            {
                _ctx.Rb.velocity += Vector2.up * (_ctx.JumpHoldAccel * Time.fixedDeltaTime);
                _jumpHoldTimer += Time.fixedDeltaTime;
            }
            else if (_jumpHoldTimer >= _ctx.MaxJumpHoldTime)
            {
                _bIsJumping = false;
            }
        }

        /// <summary>
        /// 下落手感优化：下落加重力、松手上升补重力、限制最大下落速度
        /// </summary>
        private void ApplyBetterFallGravity()
        {
            float effectiveGravity = Physics2D.gravity.y * _ctx.GravityScale;
            Vector2 vel = _ctx.Rb.velocity;

            if (vel.y < 0f)
            {
                // 下落时加重力，落点更干脆
                vel.y += effectiveGravity * (_ctx.FallMultiplier - 1f) * Time.fixedDeltaTime;
            }
            else if (vel.y > 0f && !_ctx.JumpHeld)
            {
                // 松手上升时补重力，配合 jumpCut 让短跳更明确
                vel.y += effectiveGravity * (_ctx.LowJumpMultiplier - 1f) * Time.fixedDeltaTime;
            }

            // 限制最大下落速度
            if (vel.y < _ctx.MaxFallSpeed)
            {
                vel.y = _ctx.MaxFallSpeed;
            }

            _ctx.Rb.velocity = vel;
        }

        // ==================== 存活边界约束 ====================

        /// <summary>
        /// 存活状态边界约束：左右夹紧、掉落死亡
        /// 存活状态不约束上边界（可跳跃超出地图上方），不约束下边界（用死亡判定代替）
        /// </summary>
        private void ClampToMapBoundary()
        {
            MapBoundary boundary = _ctx.MapBoundary;
            if (boundary == null) return;

            Vector2 pos = _ctx.Rb.position;

            // 左右边界夹紧，不允许超出
            pos = boundary.ClampHorizontal(pos);

            // 下边界：掉落死亡
            if (boundary.IsBelowBoundary(pos.y))
            {
                _ctx.PlayerDie();
                return;
            }

            // 仅在位置被修正时才写入，避免无谓赋值
            if (pos != _ctx.Rb.position)
            {
                _ctx.Rb.position = pos;
            }
        }

        // ==================== 提前放弃长按检测 ====================

        /// <summary>
        /// 检测提前放弃操作
        /// 当场上只剩一名存活玩家时，长按 Down Key 达到指定时长后触发放弃
        /// 松手或条件不满足时重置计时器，进度不可累积到无限生命
        /// </summary>
        private void HandleEarlyQuit()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            // 只有最后一名存活玩家才能触发提前放弃
            if (!registry.BIsLastPlayerStanding)
            {
                if (_bIsEarlyQuitHolding)
                {
                    // 条件不再满足（中途又有其他人变回存活等），重置进度
                    _earlyQuitHoldTimer = 0f;
                    _bIsEarlyQuitHolding = false;
                }
                return;
            }

            // 确认自己是最后一名存活玩家
            PlayerController lastAlive = registry.GetLastAlivePlayer();
            if (lastAlive != _ctx)
            {
                return;
            }

            // 检测 Down Key 是否被持续按住
            if (Input.GetKey(_ctx.DownKey))
            {
                _bIsEarlyQuitHolding = true;
                _earlyQuitHoldTimer += Time.deltaTime;

                if (_earlyQuitHoldTimer >= registry.EarlyQuitHoldDuration)
                {
                    // 长按达标，触发提前放弃
                    _earlyQuitHoldTimer = 0f;
                    _bIsEarlyQuitHolding = false;
                    registry.TriggerEarlyQuit(_ctx);
                }
            }
            else
            {
                // 松手则重置计时器，进度不保留
                if (_bIsEarlyQuitHolding)
                {
                    _earlyQuitHoldTimer = 0f;
                    _bIsEarlyQuitHolding = false;
                }
            }
        }
    }
}

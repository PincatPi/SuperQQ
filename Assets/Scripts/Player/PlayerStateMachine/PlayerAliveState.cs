using UnityEngine;
using SuperQQ.Map;

namespace SuperQQ.Player
{
    /// <summary>
    /// 存活状态：左右移动、可变高度跳跃、下落手感优化、地图边界约束
    /// 边界行为：左右夹紧不允许水平越界；上方开放可跳出地图顶部；
    /// 下方不做位置夹紧，y 越过下边界时触发死亡（PlayerDie）
    /// 所有运行时数据（土狼计时、跳跃保持计时等）归本状态私有
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
        /// 是否滞空（离地）：跳跃或自然坠落均为 true，由地面检测直接派生
        /// </summary>
        public bool BIsJumpAirborne => !_bIsGrounded;

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

            // 击退压制中：跳过起跳/短跳等输入驱动的速度改写，让击退速度纯物理飞行
            // 飞行模式（如"中国人能飞"咒语）中：跳跃输入由飞行逻辑接管，无需普通起跳/短跳处理
            if (!_ctx.BIsKnockbackStunned && !_ctx.BIsFlying)
            {
                HandleJumpStart();
                HandleJumpCut();
            }
            HandleEarlyQuit();
        }

        /// <summary>
        /// 物理帧更新：水平移动、可变跳跃高度、下落手感、地图边界约束
        /// </summary>
        public void FixedUpdate()
        {
            // 击退压制中：跳过输入驱动的移动改写（击退速度自然飞行），仅保留边界约束
            if (_ctx.BIsKnockbackStunned)
            {
                ClampToLevelBounds();
                return;
            }

            ApplyHorizontalMovement();

            // 飞行模式：跳跃逻辑替换为"按住跳跃键持续向上飞行"，其余物理照常
            if (_ctx.BIsFlying)
            {
                ApplyFlight();
            }
            else
            {
                ApplyVariableJumpHeight();
                ApplyBetterFallGravity();
            }
            ClampToLevelBounds();
        }

        /// <summary>
        /// 飞行（如"中国人能飞"咒语生效期间）：
        /// 按住跳跃键持续向上加速（封顶最大飞行速度）；松开按键按普通手感自然减速/下落
        /// 水平移动由 ApplyHorizontalMovement 照常处理，与普通状态一致
        /// </summary>
        private void ApplyFlight()
        {
            float effectiveGravity = Physics2D.gravity.y * _ctx.GravityScale;
            Vector2 velocity = _ctx.Rb.velocity;

            if (_ctx.JumpHeld)
            {
                // 按住跳跃键：持续向上加速，封顶最大飞行速度
                velocity.y = Mathf.Min(velocity.y + _ctx.FlyAcceleration * Time.fixedDeltaTime, _ctx.FlyMaxSpeed);
            }
            else if (velocity.y >= 0f)
            {
                // 松开按键仍在上行：按普通重力自然减速
                velocity.y += effectiveGravity * Time.fixedDeltaTime;
            }
            else
            {
                // 松开按键且下落：按普通下落手感加速下落
                velocity.y += effectiveGravity * _ctx.FallMultiplier * Time.fixedDeltaTime;
                velocity.y = Mathf.Max(velocity.y, _ctx.MaxFallSpeed);
            }

            _ctx.Rb.velocity = velocity;
        }

        // ==================== 地图边界 ====================

        /// <summary>
        /// 边界约束：水平夹紧（左右不允许越界），上下开放；
        /// y 越过下边界时触发死亡。未配置 LevelBounds 时静默跳过
        /// </summary>
        private void ClampToLevelBounds()
        {
            LevelBounds bounds = _ctx.LevelBounds;
            if (bounds == null)
            {
                return;
            }

            // 先水平夹紧写回（仅在产生修正时写入），死亡判定使用钳制后的位置
            Vector2 pos = _ctx.Rb.position;
            Vector2 clamped = bounds.ClampHorizontal(pos);
            if (clamped != pos)
            {
                _ctx.Rb.position = clamped;
            }

            // 越过下边界：掉落死亡（不可豁免，无视无敌金身等无敌保护）
            if (bounds.IsBelow(clamped.y))
            {
                _ctx.PlayerForceDie();
            }
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

                // 联机：上报起跳事件（远端播音效/尘土），离线时为空操作
                SuperQQ.Network.NetEventSync.ReportEvent(
                    Minigame.Room.V1.PlayerEventType.Jump, _ctx.transform.position);
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
            float rate;
            if (_ctx.Frictionless && _bIsGrounded)
            {
                // 肥皂表面：滑行完全不可控——只有静止起步时输入才决定滑动方向（满速），
                // 一旦滑起来就忽略一切输入（不能转向/刹车/加速），仅按减阻缓慢衰减（0=匀速）
                rate = _ctx.SlideDrag;
                if (Mathf.Abs(_currentHorizontalVelocity) < 0.01f
                    && Mathf.Abs(_ctx.HorizontalInput) > 0.01f)
                {
                    _currentHorizontalVelocity = Mathf.Sign(_ctx.HorizontalInput) * _ctx.MoveSpeed;
                }
            }
            else
            {
                rate = Mathf.Abs(_ctx.HorizontalInput) > 0.01f
                    ? _ctx.Acceleration
                    : _ctx.Deceleration;
                if (!_bIsGrounded)
                {
                    rate *= _ctx.AirControlMultiplier;
                }
            }

            _currentHorizontalVelocity = Mathf.MoveTowards(
                _currentHorizontalVelocity, targetVelocity, rate * Time.fixedDeltaTime);

            // 外部推力（排气扇风力）：独立累积风速分量，不被输入覆盖。
            // 风速按响应时间封顶 → 逆风时净速度 = 输入速度 + 风速，
            // 风力封顶值接近/超过移速时逆风寸步难行甚至被吹回去；顺风助推跳远；
            // 出风区后风速逐渐衰减，不会永久残留
            _ctx.Rb.velocity = new Vector2(
                _currentHorizontalVelocity + UpdateWindVelocity(),
                _ctx.Rb.velocity.y + _ctx.WindForce.y * Time.fixedDeltaTime);
        }

        private float _windVelocity;
        private const float WindResponseTime = 0.35f;  // 风速累积到风力全量所需时间（秒）
        private const float WindDecayRate = 30f;       // 出风区后风速衰减速率

        /// <summary>
        /// 积分风力速度分量：风区内累积（封顶 windForce * 响应时间），风区外衰减回 0
        /// </summary>
        private float UpdateWindVelocity()
        {
            float windX = _ctx.WindForce.x;
            if (Mathf.Abs(windX) > 0.01f)
            {
                _windVelocity = Mathf.Clamp(
                    _windVelocity + windX * Time.fixedDeltaTime,
                    -Mathf.Abs(windX) * WindResponseTime,
                    Mathf.Abs(windX) * WindResponseTime);
            }
            else
            {
                _windVelocity = Mathf.MoveTowards(_windVelocity, 0f, WindDecayRate * Time.fixedDeltaTime);
            }
            return _windVelocity;
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

            // 检测"下"输入是否被持续按住（走输入抽象层，键盘/触屏通用）
            if (_ctx.VerticalInput < -0.5f)
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

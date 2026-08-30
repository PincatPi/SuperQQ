using UnityEngine;
using SuperQQ.Grid;
using SuperQQ.Map;

namespace SuperQQ.Player
{
    /// <summary>
    /// 存活状态：左右移动、可变高度跳跃、下落手感优化、地图边界约束
    /// 边界行为：左/右/上边界夹紧不允许越界（类似碰撞体）；下方开放，越过下边界触发掉落死亡；
    /// 大幅越过任意边界（异常情况）触发兜底强制死亡（PlayerForceDie），随后进入幽灵状态重生在地图中央
    /// 所有运行时数据（土狼计时、跳跃保持计时等）归本状态私有
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
        }

        /// <summary>
        /// 退出存活状态
        /// </summary>
        public void Exit() { }

        /// <summary>
        /// 清零运动积分器（传送等瞬移后调用）：
        /// 水平速度/风力分量是状态内部累积量，仅清刚体速度会被下一帧 FixedUpdate 写回
        /// </summary>
        public void ResetMotion()
        {
            _currentHorizontalVelocity = 0f;
            _windVelocity = 0f;
        }

        /// <summary>
        /// 每帧更新：地面检测、跳跃起跳、跳跃截断
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
            CheckWaterDeath();
        }

        // ==================== 水域淹没判定 ====================

        /// <summary>
        /// 淹没判定：玩家所在格子命中 Water 区域即死亡（不播放受击音效——非命中型死亡）。
        /// 判定走 GridManager.GetZonesAt，已内置夜晚水位偏移（WaterYOffsetCells），
        /// 因此黑夜水面上升后站在新水格子里同样会判死，昼夜切换无需额外处理。
        /// 未配置区域（GridManager/ZoneConfig 缺失）时静默跳过。
        /// </summary>
        private void CheckWaterDeath()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return;
            }

            // 玩家 1x2：取脚底格与头顶格，两格都被水淹没（完全没入）才判死。
            // 半淹（脚踩水、头在水面上）不判——跳上船/踩浅水平台等场景因此合法
            float cs = grid.PublicCellSize;
            Vector2 pos = _ctx.Rb.position;
            Vector2Int bottomCell = grid.WorldToCell(pos + Vector2.down * cs);
            Vector2Int topCell = grid.WorldToCell(pos + Vector2.up * cs);
            bool bottomInWater = (grid.GetZonesAt(bottomCell) & GridZoneType.Water) != 0;
            bool topInWater = (grid.GetZonesAt(topCell) & GridZoneType.Water) != 0;
            if (bottomInWater && topInWater)
            {
                _ctx.PlayerDie(playHitSfx: false);
            }
        }

        /// <summary>
        /// 飞行（如"中国人能飞"咒语生效期间）：
        /// 按住跳跃键持续向上加速（封顶最大飞行速度）；松开按键按普通手感自然减速/下落
        /// 水平移动由 ApplyHorizontalMovement 照常处理，与普通状态一致
        /// </summary>
        private void ApplyFlight()
        {
            // 全运动减速（蛛网等）：飞行加速度/极速、松手重力与下落限速同比例降低
            float slow = _ctx.MotionSlowFactor;
            float effectiveGravity = Physics2D.gravity.y * _ctx.GravityScale * slow;
            Vector2 velocity = _ctx.Rb.velocity;

            if (_ctx.JumpHeld)
            {
                // 按住跳跃键：持续向上加速，封顶最大飞行速度
                velocity.y = Mathf.Min(
                    velocity.y + _ctx.FlyAcceleration * slow * Time.fixedDeltaTime,
                    _ctx.FlyMaxSpeed * slow);
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
                velocity.y = Mathf.Max(velocity.y, _ctx.MaxFallSpeed * slow);
            }

            _ctx.Rb.velocity = velocity;
        }

        // ==================== 地图边界 ====================

        /// <summary>
        /// 边界约束：左/右/上边界像碰撞体一样夹紧位置，正常游玩无法越界；
        /// 下方不夹紧，y 越过下边界时触发掉落死亡。
        /// 兜底：位置大幅越过任意边界（超出容差，属异常情况：超大击退、出生在外等）时强制死亡，
        /// 均不可豁免（无视无敌金身等无敌保护）。未配置 LevelBounds 时静默跳过
        /// fellOutOfBounds: true → 幽灵重生在地图中央（尸体已跌出地图，保持原位无意义）
        /// </summary>
        private void ClampToLevelBounds()
        {
            LevelBounds bounds = _ctx.LevelBounds;
            if (bounds == null)
            {
                return;
            }

            Vector2 pos = _ctx.Rb.position;

            // 越过下边界：掉落死亡（不可豁免，无视无敌金身等无敌保护）
            if (bounds.IsBelow(pos.y))
            {
                _ctx.PlayerForceDie(fellOutOfBounds: true);
                return;
            }

            // 兜底：大幅越过左/右/上边界（正常会被夹紧，只有异常情况才可能到这里）→ 强制死亡
            if (bounds.IsDeeplyOutOfBounds(pos))
            {
                _ctx.PlayerForceDie(fellOutOfBounds: true);
                return;
            }

            // 正常情况：左/右/上边界夹紧写回（仅在产生修正时写入），玩家无法越界
            Vector2 clamped = bounds.ClampHorizontalAndTop(pos);
            if (clamped != pos)
            {
                _ctx.Rb.position = clamped;
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
                    _ctx.PlayLandSfx();   // 滞空→着地边沿：落地音效
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
                // 全运动减速（蛛网等）：起跳速度同比例降低
                _ctx.Rb.velocity = new Vector2(_ctx.Rb.velocity.x, _ctx.JumpVelocity * _ctx.MotionSlowFactor);
                _bIsJumping = true;
                _jumpHoldTimer = 0f;
                // 起跳消耗土狼时间，避免连跳
                _coyoteTimer = 0f;

                _ctx.PlayJumpSfx();

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
            // 全运动减速（蛛网等）：与跳跃/下落同比例减慢水平移动
            float targetVelocity = _ctx.HorizontalInput * _ctx.MoveSpeed * _ctx.MotionSlowFactor;
            float rate;
            if (_ctx.Frictionless && _bIsGrounded)
            {
                // 肥皂表面：滑行完全不可控——只有静止起步时输入才决定滑动方向（满速），
                // 一旦滑起来就忽略一切输入（不能转向/刹车/加速），仅按减阻缓慢衰减（0=匀速）
                rate = _ctx.SlideDrag;
                if (Mathf.Abs(_currentHorizontalVelocity) < 0.01f
                    && Mathf.Abs(_ctx.HorizontalInput) > 0.01f)
                {
                    _currentHorizontalVelocity = Mathf.Sign(_ctx.HorizontalInput) * _ctx.MoveSpeed * _ctx.MotionSlowFactor;
                }
            }
            else
            {
                bool hasInput = Mathf.Abs(_ctx.HorizontalInput) > 0.01f;
                rate = hasInput ? _ctx.Acceleration : _ctx.Deceleration;
                if (!_bIsGrounded)
                {
                    // 空中：有输入按操控系数变向/加速；无输入走空气阻尼（远小于地面减速，
                    // 保住跳跃水平动量，跳距/落点更自然——原逻辑空中松手按地面减速急刹，跳距损失大）
                    rate = hasInput ? rate * _ctx.AirControlMultiplier : _ctx.AirDrag;
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
        /// 注意：_bIsJumping 不随长按窗口结束而解除——它同时是"短跳截断待命"标记。
        /// 若窗口（0.03s）结束就解除，真实点按（约0.08~0.12s）松手时 jumpCut 永不触发，
        /// 大小跳高度几乎无差别。现仅由松手截断（HandleJumpCut）或落地（CheckGround）解除
        /// </summary>
        private void ApplyVariableJumpHeight()
        {
            if (_bIsJumping && _ctx.JumpHeld && _jumpHoldTimer < _ctx.MaxJumpHoldTime)
            {
                // 全运动减速（蛛网等）：长按追加的跳跃加速度同比例降低
                _ctx.Rb.velocity += Vector2.up * (_ctx.JumpHoldAccel * _ctx.MotionSlowFactor * Time.fixedDeltaTime);
                _jumpHoldTimer += Time.fixedDeltaTime;
            }
        }

        /// <summary>
        /// 下落手感优化：下落加重力、松手上升补重力、限制最大下落速度
        /// </summary>
        private void ApplyBetterFallGravity()
        {
            // 全运动减速（蛛网等）：重力加速度与最大下落速度同比例降低（下落变慢）
            float slow = _ctx.MotionSlowFactor;
            float effectiveGravity = Physics2D.gravity.y * _ctx.GravityScale * slow;
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

            // 限制最大下落速度（减速比例同步作用于限速）
            float maxFallSpeed = _ctx.MaxFallSpeed * slow;
            if (vel.y < maxFallSpeed)
            {
                vel.y = maxFallSpeed;
            }

            _ctx.Rb.velocity = vel;
        }
    }
}

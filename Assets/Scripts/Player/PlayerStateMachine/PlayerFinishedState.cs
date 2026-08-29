using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 通关状态：角色到达终点后进入此状态
    /// 角色保持可见（循环播放胜利动画）、禁用碰撞、屏蔽移动输入；
    /// 空中通关时手动积分重力正常落地，着地后停住。
    /// 胜利动画由 PlayerAnimationController 读取 BIsFinished 驱动 Animator bIsVictory 参数；
    /// 联机下 InputReporter 照常上报 player_state=2，远端由 RemotePlayerSync 驱动同一参数。
    /// </summary>
    public class PlayerFinishedState : IPlayerState
    {
        private readonly PlayerController _ctx;

        // Enter 时保存，Exit 时恢复
        private float _savedGravityScale;
        private IPlayerInput _savedInput;

        // 落地模拟：碰撞体已禁用无法靠物理引擎支撑，由状态手动积分重力下落，地面检测判定着地
        private bool _bGrounded;
        private float _fallSpeed;

        public PlayerFinishedState(PlayerController ctx) => _ctx = ctx;

        // ==================== IPlayerState 查询 ====================

        /// <summary>
        /// 通关状态是否在地面（空中通关时先下落，着地后为 true）
        /// </summary>
        public bool BIsGrounded => _bGrounded;

        /// <summary>
        /// 通关状态不跳跃
        /// </summary>
        public bool BIsJumping => false;

        /// <summary>
        /// 通关状态无跳跃滞空期
        /// </summary>
        public bool BIsJumpAirborne => false;

        /// <summary>
        /// 通关状态无水平速度
        /// </summary>
        public float HorizontalVelocity => 0f;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 进入通关状态：禁用碰撞体、取消重力、停止速度、屏蔽移动输入
        /// 角色保持可见，由动画层循环播放胜利动画
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

            // 落地模拟初始化：地面通关直接着地；空中通关由 FixedUpdate 手动积分重力下落至着地
            _fallSpeed = 0f;
            _bGrounded = CheckGrounded();

            // 屏蔽移动输入：注入空输入源（与 PlayingPhase 开局保护同一套模式），复活时还原
            _savedInput = _ctx.InputSource;
            _ctx.SetInputSource(NullPlayerInput.Instance);
        }

        /// <summary>
        /// 退出通关状态：恢复碰撞体、重力、物理状态与输入源
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

            // 还原输入源：仅在输入源仍为本状态注入的空输入时还原，
            // 避免覆盖通关期间其他系统（如开局保护解除）替换的输入源
            if (_savedInput != null && _ctx.InputSource == NullPlayerInput.Instance)
            {
                _ctx.SetInputSource(_savedInput);
            }
            _savedInput = null;
        }

        /// <summary>
        /// 通关状态无 Update 逻辑
        /// </summary>
        public void Update() { }

        /// <summary>
        /// 物理帧更新：空中通关时手动积分重力下落（碰撞体已禁用，无法靠物理引擎支撑），
        /// 地面检测命中后着地停住，原地循环播放胜利动画
        /// </summary>
        public void FixedUpdate()
        {
            if (_bGrounded || _ctx.Rb == null)
            {
                return;
            }

            // 兜底：检测圆已与地面重叠（如通关瞬间贴地）时直接着地，不再移动
            if (CheckGrounded())
            {
                _fallSpeed = 0f;
                _bGrounded = true;
                return;
            }

            // 下落手感与存活状态一致：整体重力倍率 × 下落额外倍数，封顶最大下落速度
            float gravity = Physics2D.gravity.y * _ctx.GravityScale * _ctx.FallMultiplier;
            _fallSpeed = Mathf.Max(_fallSpeed + gravity * Time.fixedDeltaTime, _ctx.MaxFallSpeed);
            float step = -_fallSpeed * Time.fixedDeltaTime; // 本帧下落距离（正值）

            // 用 CircleCast 预先探测本帧落点（而非先穿透再检测重叠）：
            // 命中地面时把检测点下移 命中距离+半径，即检测圆中心停在地表、脚底恰好贴地，杜绝穿模
            RaycastHit2D hit = Physics2D.CircleCast(
                _ctx.GroundCheck.position, _ctx.GroundCheckRadius, Vector2.down,
                step + _ctx.GroundCheckRadius, _ctx.GroundLayer);

            if (hit.collider != null)
            {
                _ctx.Rb.MovePosition(_ctx.Rb.position + Vector2.down * (hit.distance + _ctx.GroundCheckRadius));
                _fallSpeed = 0f;
                _bGrounded = true;
                return;
            }

            _ctx.Rb.MovePosition(_ctx.Rb.position + Vector2.down * step);

            // 兜底：跌出关卡下边界时直接停住，避免终点架在虚空上时无限坠落
            if (IsBelowLevelBounds())
            {
                _fallSpeed = 0f;
                _bGrounded = true;
            }
        }

        /// <summary>
        /// 地面检测：与存活状态同一判定口径（groundCheck 未配置时视为在地面，退化为原地冻结）
        /// </summary>
        private bool CheckGrounded()
        {
            Transform gc = _ctx.GroundCheck;
            return gc == null || Physics2D.OverlapCircle(gc.position, _ctx.GroundCheckRadius, _ctx.GroundLayer);
        }

        /// <summary>是否跌出关卡下边界（兜底用，防止终点下方无地面时无限坠落）</summary>
        private bool IsBelowLevelBounds()
        {
            SuperQQ.Map.LevelBounds bounds = _ctx.LevelBounds;
            return bounds != null && bounds.IsBelow(_ctx.Rb.position.y);
        }
    }
}

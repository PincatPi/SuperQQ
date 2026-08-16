using UnityEngine;
using SuperQQ.Map;
using SuperQQ.UI;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家控制器 — 状态机持有者
    /// 管理组件引用、输入读取和状态切换
    /// 存活/幽灵的具体行为委托给 IPlayerState 实现
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动设置")]
        [SerializeField] private float moveSpeed = 6f;                 // 左右移动速度（单位/秒）
        [SerializeField] private float acceleration = 80f;             // 水平加速速率
        [SerializeField] private float deceleration = 100f;            // 水平减速速率（松手回停）
        [SerializeField] private float airControlMultiplier = 0.85f;   // 空中操控系数

        [Header("重力设置")]
        [SerializeField] private float gravityScale = 1.5f;             // 整体重力倍率（>1 跳跃更紧凑、上升下落更快）

        [Header("跳跃设置")]
        [SerializeField] private float jumpVelocity = 6.5f;             // 起跳初始竖直速度
        [SerializeField] private float jumpHoldAccel = 55f;             // 长按时额外向上加速度
        [SerializeField] private float maxJumpHoldTime = 0.03f;         // 最大长按持续时间（秒）
        [SerializeField] private float jumpCutMultiplier = 0.82f;       // 松手时竖直速度保留比例（短跳）
        [SerializeField] private float coyoteTime = 0.1f;               // 离地后仍可跳跃的宽容时间

        [Header("下落手感")]
        [SerializeField] private float fallMultiplier = 2.5f;           // 下落时额外重力倍数
        [SerializeField] private float lowJumpMultiplier = 1f;          // 松手上升时额外重力倍数
        [SerializeField] private float maxFallSpeed = -15f;             // 最大下落速度

        [Header("地面检测")]
        [SerializeField] private Transform groundCheck;                 // 脚下检测点
        [SerializeField] private float groundCheckRadius = 0.16f;       // 检测半径
        [SerializeField] private LayerMask groundLayer;                 // 地面Layer

        [Header("死亡设置")]
        [SerializeField] private float deathDuration = 0.6f;           // 死亡过渡时长（秒），结束后自动进入幽灵状态

        [Header("幽灵设置")]
        [SerializeField] private float ghostMoveSpeed = 6f;             // 幽灵四向移动速度
        [SerializeField] private float ghostAcceleration = 80f;         // 幽灵加速速率
        [SerializeField] private float ghostDeceleration = 100f;        // 幽灵减速速率
        [Range(0f, 1f)]
        [SerializeField] private float ghostAlpha = 0.5f;               // 幽灵透明度
        [SerializeField] private Vector3 ghostSpawnPosition = Vector3.zero; // 幽灵初始位置

        [Header("外部引用")]
        [SerializeField] private LevelBounds levelBounds;                  // 关卡边界，留空则自动使用场景中的 LevelBounds.Instance

        [Header("玩家信息")]
        [SerializeField] private string playerId = "";                     // 网络唯一ID（联机主键，单机可为空）
        [SerializeField] private bool isLocal = true;                      // 是否本机控制（false=远程玩家由网络驱动）
        [SerializeField] private string playerName = "P1";                 // 玩家名称
        [SerializeField] private Color playerColor = Color.white;          // 玩家专属颜色
        [SerializeField] private Sprite cursorMarkerSprite;                // 放置阶段跟随光标的玩家标识图，留空则回退用角色本体 Sprite

        [Header("输入键位")]
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode jumpKeyAlt = KeyCode.W;         // 备用跳跃键（存活状态）
        [SerializeField] private KeyCode downKey = KeyCode.S;            // 下蹲/幽灵下移键

        // ---------- 组件缓存 ----------
        private Rigidbody2D _rb;
        private SpriteRenderer _spriteRenderer;
        private Collider2D _collider;

        // ---------- 状态机 ----------
        private IPlayerState _currentState;

        // ---------- 外部速度修正（道具表面效果等，1=无影响） ----------
        private float _speedMultiplier = 1f;

        // ---------- 无摩擦状态（肥皂表面等） ----------
        private bool _frictionless;
        private float _slideDrag;

        // ---------- 输入来源（本地键盘 / 联机远程快照） ----------
        private IPlayerInput _input;

        // ---------- 输入（Update 读取，状态消费） ----------
        private float _horizontalInput;
        private float _verticalInput;
        private bool _jumpPressed;
        private bool _jumpHeld;

        // ==================== 公开访问器（供状态使用） ====================

        public Rigidbody2D Rb => _rb;
        public SpriteRenderer Renderer => _spriteRenderer;
        public Collider2D Collider => _collider;
        public float GravityScale => gravityScale;
        public Transform GroundCheck => groundCheck;
        public float GroundCheckRadius => groundCheckRadius;
        public LayerMask GroundLayer => groundLayer;

        // 关卡边界：优先使用 Inspector 显式指定，未指定时惰性回退到场景单例
        // 访问时机在 FixedUpdate（晚于所有 Awake），无脚本执行顺序问题；为 null 时状态跳过钳制
        public LevelBounds LevelBounds => levelBounds != null ? levelBounds : LevelBounds.Instance;

        // 移动
        public float MoveSpeed => moveSpeed * _speedMultiplier;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float AirControlMultiplier => airControlMultiplier;

        // 无摩擦（肥皂：加/减速率压到 0，初速度保留）
        public bool Frictionless => _frictionless;
        public float SlideDrag => _slideDrag;

        // 跳跃
        public float JumpVelocity => jumpVelocity;
        public float JumpHoldAccel => jumpHoldAccel;
        public float MaxJumpHoldTime => maxJumpHoldTime;
        public float JumpCutMultiplier => jumpCutMultiplier;
        public float CoyoteTime => coyoteTime;

        // 下落
        public float FallMultiplier => fallMultiplier;
        public float LowJumpMultiplier => lowJumpMultiplier;
        public float MaxFallSpeed => maxFallSpeed;

        // 死亡
        public float DeathDuration => deathDuration;

        // 幽灵
        public float GhostMoveSpeed => ghostMoveSpeed;
        public float GhostAcceleration => ghostAcceleration;
        public float GhostDeceleration => ghostDeceleration;
        public float GhostAlpha => ghostAlpha;
        public Vector3 GhostSpawnPosition => ghostSpawnPosition;

        // 玩家信息
        public string PlayerId => playerId;
        public bool BIsLocal => isLocal;
        public string PlayerName => playerName;
        public Color PlayerColor => playerColor;
        /// <summary>身份主键：联机为 PlayerId，单机回退为 PlayerName</summary>
        public string IdentityKey => string.IsNullOrEmpty(playerId) ? playerName : playerId;

        /// <summary>
        /// 放置阶段跟随光标的玩家标识图；未配置时回退为角色本体 Sprite
        /// </summary>
        public Sprite CursorMarkerSprite => cursorMarkerSprite != null
            ? cursorMarkerSprite
            : (_spriteRenderer != null ? _spriteRenderer.sprite : null);

        // 输入键位
        public KeyCode DownKey => downKey;

        // 输入
        public float HorizontalInput => _horizontalInput;
        public float VerticalInput => _verticalInput;
        public bool JumpPressed => _jumpPressed;
        public bool JumpHeld => _jumpHeld;

        // ==================== 公开状态查询 ====================

        public bool BIsGrounded => _currentState?.BIsGrounded ?? false;
        public bool BIsJumping => _currentState?.BIsJumping ?? false;
        // 跳跃滞空期：起跳 true、落地 false，供动画层驱动跳跃动画
        public bool BIsJumpAirborne => _currentState?.BIsJumpAirborne ?? false;
        // 仅死亡过渡（Dying）中视为已死亡，进入幽灵后置回 false
        public bool BIsDead => _currentState is PlayerDyingState;
        // 仅幽灵状态中视为幽灵，与 BIsDead 互斥
        public bool BIsGhost => _currentState is PlayerGhostState;
        public bool BIsFinished => _currentState is PlayerFinishedState;
        public float HorizontalVelocity => _currentState?.HorizontalVelocity ?? 0f;

        /// <summary>
        /// 面朝方向（+1 朝右 / -1 朝左）：按水平速度更新，低于翻转阈值保持原朝向，
        /// 判定与 PlayerAnimationController 的精灵翻转同一套阈值，表现层无需读 flipX
        /// </summary>
        public float FacingDir
        {
            get
            {
                float velocity = HorizontalVelocity;
                if (velocity > FACING_FLIP_THRESHOLD)
                {
                    _facingDir = 1f;
                }
                else if (velocity < -FACING_FLIP_THRESHOLD)
                {
                    _facingDir = -1f;
                }
                return _facingDir;
            }
        }
        private float _facingDir = 1f;
        private const float FACING_FLIP_THRESHOLD = 0.1f;   // 与 PlayerAnimationController.runEnterThreshold 默认值一致

        // ==================== 生命周期 ====================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            // SpriteRenderer 挂在子物体 Visual 上，需从子级查找
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();

            if (_spriteRenderer == null)
            {
                Debug.LogWarning("[PlayerController] 未找到 SpriteRenderer，颜色/透明度/朝向翻转将失效。请确认子物体 Visual 上挂载了 SpriteRenderer。", this);
            }

            _rb.gravityScale = gravityScale;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            if (groundCheck == null)
            {
                Debug.LogWarning("[PlayerController] 未指定 groundCheck，地面检测将失效。请在 Inspector 中设置脚下检测点。", this);
            }

            // 默认使用本地键盘输入；联机模式下由外部通过 SetInputSource 替换为远程输入
            _input = new LocalPlayerInput(leftKey, rightKey, jumpKey, jumpKeyAlt, downKey);

            _currentState = new PlayerAliveState(this);
            _currentState.Enter();
        }

        private void Start()
        {
            // 注册到关卡玩家注册表（场景级）
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.RegisterPlayer(this);
            }

            // 注册到名称标签管理器
            if (PlayerNameLabelManager.Instance != null)
            {
                PlayerNameLabelManager.Instance.RegisterPlayer(this);
            }
        }

        private void OnDestroy()
        {
            // 从关卡玩家注册表注销
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.UnregisterPlayer(this);
            }

            // 从名称标签管理器注销
            if (PlayerNameLabelManager.Instance != null)
            {
                PlayerNameLabelManager.Instance.UnregisterPlayer(this);
            }
        }

        // ==================== 档案应用 ====================

        /// <summary>
        /// 应用玩家档案配置
        /// 由 LevelPlayerRegistry 在实例化玩家后调用
        /// 同步设置名称、颜色和键位，并立即刷新精灵颜色
        /// </summary>
        /// <param name="profile">玩家档案</param>
        public void ApplyProfile(PlayerProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            playerId = profile.PlayerId;
            isLocal = profile.IsLocal;
            playerName = profile.PlayerName;
            playerColor = profile.PlayerColor;
            leftKey = profile.LeftKey;
            rightKey = profile.RightKey;
            jumpKey = profile.JumpKey;
            jumpKeyAlt = profile.JumpKeyAlt;
            downKey = profile.DownKey;

            // 同步键位到本地输入源
            if (_input is LocalPlayerInput localInput)
            {
                localInput.SetKeys(leftKey, rightKey, jumpKey, jumpKeyAlt, downKey);
            }

            // 立即刷新精灵颜色（Awake 已缓存 _spriteRenderer）
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = playerColor;
            }
        }

        /// <summary>
        /// 当前输入来源。临时替换输入源的流程（如道具放置阶段屏蔽移动操作）可先缓存本值，结束后原样还原
        /// </summary>
        public IPlayerInput InputSource => _input;

        /// <summary>
        /// 替换输入来源。联机模式下远程玩家应传入 RemotePlayerInput，
        /// 调用后状态机行为不变，仅输入来源切换
        /// </summary>
        public void SetInputSource(IPlayerInput input)
        {
            if (input != null)
            {
                _input = input;
            }
        }

        /// <summary>
        /// 根据当前配置构建玩家档案
        /// 用于将场景中预置的 PlayerController 信息同步到 PlayerSessionManager
        /// 使手动放置的玩家也能进入结算页的玩家列表与得分记录
        /// </summary>
        /// <returns>包含当前名称、颜色和键位的 PlayerProfile</returns>
        public PlayerProfile BuildProfile()
        {
            return new PlayerProfile
            {
                PlayerId = playerId,
                IsLocal = isLocal,
                PlayerName = playerName,
                PlayerColor = playerColor,
                LeftKey = leftKey,
                RightKey = rightKey,
                JumpKey = jumpKey,
                JumpKeyAlt = jumpKeyAlt,
                DownKey = downKey
            };
        }

        private void Update()
        {
            ReadInput();
            _currentState.Update();
        }

        private void FixedUpdate()
        {
            _currentState.FixedUpdate();
        }

        // ==================== 输入读取 ====================

        /// <summary>
        /// 读取玩家操作输入（委托给当前输入源）
        /// </summary>
        private void ReadInput()
        {
            _input.ReadInput();
            _horizontalInput = _input.Horizontal;
            _verticalInput = _input.Vertical;
            _jumpPressed = _input.JumpPressed;
            _jumpHeld = _input.JumpHeld;
        }

        // ==================== 外部速度修正 ====================

        /// <summary>
        /// 设置移动速度倍率（由道具表面效果调用，如黄油块减速0.5）
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// 恢复原始移动速度
        /// </summary>
        public void ResetSpeedMultiplier()
        {
            _speedMultiplier = 1f;
        }

        /// <summary>
        /// 进入/离开无摩擦状态（肥皂表面：滑行不可控）
        /// </summary>
        /// <param name="active">true=无摩擦滑行</param>
        /// <param name="drag">滑行减阻（0=完全无摩擦匀速滑行）</param>
        public void SetFrictionless(bool active, float drag = 0f)
        {
            _frictionless = active;
            _slideDrag = active ? Mathf.Max(0f, drag) : 0f;
        }

        // ==================== 状态切换 ====================

        /// <summary>
        /// 切换到新状态（先 Exit 旧状态，再 Enter 新状态），并通知 LevelPlayerRegistry 更新状态记录
        /// 调用约定：外部事件驱动的转换走本类的公共事件方法（PlayerDie/PlayerKnockbackDie/PlayerFinish）；
        /// 状态自主驱动的转换由各状态内部直接调用本方法
        /// </summary>
        public void TransitionTo(IPlayerState newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();

            // 通知 LevelPlayerRegistry 同步状态
            NotifyStateChanged();
        }

        /// <summary>
        /// 将当前状态同步到 LevelPlayerRegistry
        /// </summary>
        private void NotifyStateChanged()
        {
            if (LevelPlayerRegistry.Instance == null)
            {
                return;
            }

            // 死亡过渡视为 Ghost 记录（对外等价于已死亡）
            PlayerStateType stateType = _currentState is PlayerGhostState || _currentState is PlayerDyingState ? PlayerStateType.Ghost
                : _currentState is PlayerFinishedState ? PlayerStateType.Finished
                : PlayerStateType.Alive;
            LevelPlayerRegistry.Instance.UpdatePlayerState(this, stateType);
        }

        /// <summary>
        /// 死亡，进入死亡过渡状态（倒计时结束后自动切换为幽灵状态）
        /// </summary>
        public void PlayerDie()
        {
            if (BIsDead || BIsGhost)
            {
                return;
            }
            TransitionTo(new PlayerDyingState(this));
        }

        /// <summary>
        /// 被击飞死亡：强制一个击飞速度并进入死亡过渡状态
        /// 过渡期间保留击飞动量、无法操作，倒计时结束后自动进入幽灵状态
        /// </summary>
        /// <param name="knockbackVelocity">击飞速度（世界方向）</param>
        /// <param name="ghostDelay">过渡时长（秒），覆盖默认死亡过渡时长</param>
        public void PlayerKnockbackDie(Vector2 knockbackVelocity, float ghostDelay = 0.6f)
        {
            if (BIsDead || BIsGhost)
            {
                return;
            }
            if (_rb != null)
            {
                _rb.velocity = knockbackVelocity;
            }
            TransitionTo(new PlayerDyingState(this, ghostDelay));
        }

        /// <summary>
        /// 通关，进入通关状态
        /// 已通关或已死亡的玩家不可再次通关
        /// </summary>
        public void PlayerFinish()
        {
            if (BIsFinished || BIsDead || BIsGhost) 
            {
                return;
            }
            TransitionTo(new PlayerFinishedState(this));
        }

        // ==================== 调试可视化 ====================

        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = BIsGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }
        }
    }
}

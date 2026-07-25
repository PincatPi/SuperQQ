using UnityEngine;
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

        [Header("外部引用")]
        [SerializeField] private MapBoundary mapBoundary;              // 地图边界组件引用

        [Header("幽灵设置")]
        [SerializeField] private float ghostMoveSpeed = 6f;             // 幽灵四向移动速度
        [SerializeField] private float ghostAcceleration = 80f;         // 幽灵加速速率
        [SerializeField] private float ghostDeceleration = 100f;        // 幽灵减速速率
        [Range(0f, 1f)]
        [SerializeField] private float ghostAlpha = 0.5f;               // 幽灵透明度
        [SerializeField] private Vector3 ghostSpawnPosition = Vector3.zero; // 幽灵初始位置

        [Header("玩家信息")]
        [SerializeField] private string playerName = "P1";                 // 玩家名称
        [SerializeField] private Color playerColor = Color.white;          // 玩家专属颜色

        [Header("输入键位")]
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode jumpKeyAlt = KeyCode.W;         // 备用跳跃键（存活状态）
        [SerializeField] private KeyCode downKey = KeyCode.S;            // 下蹲/幽灵下移键

        [Header("调试用参数")]
        [SerializeField] private Vector3 rebornPosition;              // 复活出生点

        // ---------- 组件缓存 ----------
        private Rigidbody2D _rb;
        private SpriteRenderer _spriteRenderer;
        private Collider2D _collider;

        // ---------- 状态机 ----------
        private IPlayerState _currentState;

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
        public Vector3 RebornPosition => rebornPosition;

        // 外部引用
        public MapBoundary MapBoundary => mapBoundary;

        // 移动
        public float MoveSpeed => moveSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float AirControlMultiplier => airControlMultiplier;

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

        // 幽灵
        public float GhostMoveSpeed => ghostMoveSpeed;
        public float GhostAcceleration => ghostAcceleration;
        public float GhostDeceleration => ghostDeceleration;
        public float GhostAlpha => ghostAlpha;
        public Vector3 GhostSpawnPosition => ghostSpawnPosition;

        // 玩家信息
        public string PlayerName => playerName;
        public Color PlayerColor => playerColor;

        // 输入
        public float HorizontalInput => _horizontalInput;
        public float VerticalInput => _verticalInput;
        public bool JumpPressed => _jumpPressed;
        public bool JumpHeld => _jumpHeld;

        // ==================== 公开状态查询 ====================

        public bool BIsGrounded => _currentState?.BIsGrounded ?? false;
        public bool BIsJumping => _currentState?.BIsJumping ?? false;
        public bool BIsDead => _currentState is PlayerGhostState;
        public bool BIsFinished => _currentState is PlayerFinishedState;
        public float HorizontalVelocity => _currentState?.HorizontalVelocity ?? 0f;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();

            _rb.gravityScale = gravityScale;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            if (groundCheck == null)
            {
                Debug.LogWarning("[PlayerController] 未指定 groundCheck，地面检测将失效。请在 Inspector 中设置脚下检测点。", this);
            }

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

            playerName = profile.PlayerName;
            playerColor = profile.PlayerColor;
            leftKey = profile.LeftKey;
            rightKey = profile.RightKey;
            jumpKey = profile.JumpKey;
            jumpKeyAlt = profile.JumpKeyAlt;
            downKey = profile.DownKey;

            // 立即刷新精灵颜色（Awake 已缓存 _spriteRenderer）
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = playerColor;
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
            ReadDebugInput();
            _currentState.Update();
        }

        private void FixedUpdate()
        {
            _currentState.FixedUpdate();
        }

        // ==================== 输入读取 ====================

        /// <summary>
        /// 读取玩家操作输入
        /// </summary>
        private void ReadInput()
        {
            _horizontalInput = 0f;
            _verticalInput = 0f;

            if (Input.GetKey(leftKey))
            {
                _horizontalInput -= 1f;
            }
            if (Input.GetKey(rightKey))
            {
                _horizontalInput += 1f;
            }
            if (Input.GetKey(jumpKey) || Input.GetKey(jumpKeyAlt))
            {
                _verticalInput += 1f;
            }
            if (Input.GetKey(downKey))
            {
                _verticalInput -= 1f;
            }

            _jumpPressed = Input.GetKeyDown(jumpKey) || Input.GetKeyDown(jumpKeyAlt);
            _jumpHeld = Input.GetKey(jumpKey) || Input.GetKey(jumpKeyAlt);
        }

        /// <summary>
        /// 读取调试输入：K键击杀、R键复活
        /// </summary>
        private void ReadDebugInput()
        {
            if (Input.GetKeyDown(KeyCode.K))
            {
                PlayerDie();
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                Revive();
            }
        }

        // ==================== 状态切换 ====================

        /// <summary>
        /// 切换到新状态（先 Exit 旧状态，再 Enter 新状态），并通知 LevelPlayerRegistry 更新状态记录
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

            PlayerStateType stateType = _currentState is PlayerGhostState ? PlayerStateType.Ghost
                : _currentState is PlayerFinishedState ? PlayerStateType.Finished
                : PlayerStateType.Alive;
            LevelPlayerRegistry.Instance.UpdatePlayerState(this, stateType);
        }

        /// <summary>
        /// 死亡，进入幽灵状态
        /// </summary>
        public void PlayerDie()
        {
            if (BIsDead)
            {
                return;
            }
            TransitionTo(new PlayerGhostState(this));
        }

        /// <summary>
        /// 复活，回到存活状态
        /// </summary>
        public void Revive()
        {
            if (!BIsDead)
            {
                return;
            }
            TransitionTo(new PlayerAliveState(this));
            // 重置出生点
            if (rebornPosition != null)
            {
                transform.position = rebornPosition;
            }
        }

        /// <summary>
        /// 通关，进入通关状态
        /// 已通关或已死亡的玩家不可再次通关
        /// </summary>
        public void PlayerFinish()
        {
            if (BIsFinished || BIsDead) 
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

using UnityEngine;

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

        // 输入
        public float HorizontalInput => _horizontalInput;
        public float VerticalInput => _verticalInput;
        public bool JumpPressed => _jumpPressed;
        public bool JumpHeld => _jumpHeld;

        // ==================== 公开状态查询 ====================

        public bool BIsGrounded => _currentState?.BIsGrounded ?? false;
        public bool BIsJumping => _currentState?.BIsJumping ?? false;
        public bool BIsDead => _currentState is PlayerGhostState;
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
        /// 切换到新状态（先 Exit 旧状态，再 Enter 新状态）
        /// </summary>
        public void TransitionTo(IPlayerState newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        /// <summary>
        /// 死亡，进入幽灵状态
        /// </summary>
        public void PlayerDie()
        {
            if (BIsDead) return;
            TransitionTo(new PlayerGhostState(this));
        }

        /// <summary>
        /// 复活，回到存活状态
        /// </summary>
        public void Revive()
        {
            if (!BIsDead) return;
            TransitionTo(new PlayerAliveState(this));
            // 重置出生点
            if (rebornPosition != null)
            {
                transform.position = rebornPosition;
            }
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

        /// <summary>
        /// 屏幕调试信息：显示接地状态、跳跃状态、当前状态类型、位置
        /// </summary>
        private void OnGUI()
        {
            string stateName = _currentState is PlayerGhostState ? "Ghost" : "Alive";
            Vector2 pos = _rb.position;
            string info = $"State: {stateName}  |  Grounded: {BIsGrounded}  |  Jumping: {BIsJumping}\n"
                        + $"Pos: ({pos.x:F1}, {pos.y:F1})";
            GUI.Label(new Rect(10, 10, 500, 40), info);
        }
    }
}

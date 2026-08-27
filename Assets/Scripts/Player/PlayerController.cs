using UnityEngine;
using SuperQQ.Audio;
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
        [SerializeField] private float airControlMultiplier = 0.85f;   // 空中操控系数（空中有输入时的变向/加速速率 = 地面加速度 × 该系数）
        [SerializeField] private float airDrag = 28f;                  // 空中无输入水平阻尼（控制松手滑翔距离：越大滑翔越短，0=完全不减速）

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
        [SerializeField] private float deathDuration = 2f;             // 死亡过渡时长（秒），结束后自动进入幽灵状态

        [Header("音效")]
        [Tooltip("被命中音效：被伤害型道具/事件命中致死或击飞时在玩家位置 3D 播放（坠落出界不播放）；None 表示静默")]
        [SerializeField] private SfxId hitSfx = SfxId.PlayerHit;

        [Tooltip("起跳音效：起跳瞬间在玩家位置 3D 播放；None 表示静默")]
        [SerializeField] private SfxId jumpSfx = SfxId.Jump;

        [Tooltip("落地音效：滞空后着地瞬间在玩家位置 3D 播放；None 表示静默")]
        [SerializeField] private SfxId landSfx = SfxId.Land;

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
        [SerializeField] private Sprite selectionIconSprite;               // 选择阶段面板上的玩家图标，留空则回退用光标标识图

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

        // ---------- 死亡信息（进入死亡时记录，供幽灵状态决定出生位置） ----------
        private Vector2 _deathPosition;              // 死亡瞬间位置
        private bool _ghostSpawnAtFixedPosition;     // 幽灵是否出生在固定初始位置（仅跌落下边界死亡为 true）

        // ---------- 外部速度修正（道具表面效果等，1=无影响） ----------
        private float _speedMultiplier = 1f;

        // ---------- 无摩擦状态（肥皂表面等） ----------
        private bool _frictionless;
        private float _slideDrag;

        // ---------- 外部推力（排气扇风力等，多来源向量累加） ----------
        private Vector2 _windForce;


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
        public float AirDrag => airDrag;

        // 无摩擦（肥皂：加/减速率压到 0，初速度保留）
        public bool Frictionless => _frictionless;
        public float SlideDrag => _slideDrag;

        // 外部推力（排气扇风力，单位/秒²）
        public Vector2 WindForce => _windForce;

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
        /// <summary>死亡瞬间位置（进入死亡过渡时记录）</summary>
        public Vector2 DeathPosition => _deathPosition;
        /// <summary>幽灵是否出生在固定初始位置：仅跌落下边界死亡为 true，其余死亡保持死亡位置</summary>
        public bool GhostSpawnAtFixedPosition => _ghostSpawnAtFixedPosition;

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

        /// <summary>
        /// 选择阶段面板上的玩家图标；未配置时回退为光标标识图（其自身再回退角色本体 Sprite）
        /// </summary>
        public Sprite SelectionIconSprite => selectionIconSprite != null
            ? selectionIconSprite
            : CursorMarkerSprite;

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
        // 冻结状态：无法操作但仍视为在场，可被击杀，解冻后恢复存活
        public bool BIsFrozen => _currentState is PlayerFrozenState;

        // 无敌标记的引用计数（支持多个无敌来源叠加，全部解除后才失去无敌）
        private int _invincibilityCount;

        /// <summary>
        /// 无敌状态：免疫伤害，不会进入死亡/幽灵状态（物理效果仍正常作用于自身）
        /// </summary>
        public bool BIsInvincible => _invincibilityCount > 0;

        /// <summary>
        /// 添加一个无敌来源（如无敌金身护盾）；需与 RemoveInvincibility 成对调用
        /// </summary>
        public void AddInvincibility()
        {
            _invincibilityCount++;
        }

        /// <summary>
        /// 移除一个无敌来源；计数归零后恢复可死亡
        /// </summary>
        public void RemoveInvincibility()
        {
            _invincibilityCount = Mathf.Max(0, _invincibilityCount - 1);
        }

        // 击退压制窗口的剩余时间（>0 时存活状态跳过输入驱动的移动改写，让击退速度纯物理飞行）
        private float _knockbackStunTimer;

        /// <summary>
        /// 击退压制窗口的默认时长（秒）：免疫死亡但保留击退时，恢复操控前的物理飞行时间
        /// </summary>
        [Header("击退")]
        [Tooltip("免疫死亡但保留击退效果时，被击退后恢复操控所需的时间（秒），期间击退速度自然飞行不受输入改写")]
        [SerializeField] private float _knockbackStunDuration = 0.6f;

        /// <summary>
        /// 是否处于击退压制中：存活状态在该窗口内不执行输入驱动的移动改写（击退速度自然飞行）
        /// </summary>
        public bool BIsKnockbackStunned => _knockbackStunTimer > 0f;

        /// <summary>
        /// 开启击退压制窗口（免疫死亡但保留击退物理时使用）；不切换状态，不影响出局判定
        /// </summary>
        public void BeginKnockbackStun()
        {
            _knockbackStunTimer = _knockbackStunDuration;
        }

        // ==================== 飞行（咒语效果注入，如"中国人能飞"） ====================

        // 飞行模式标记与参数（由咒语效果激活时注入，结束时复位）
        private bool _bIsFlying;
        private float _flyAcceleration;
        private float _flyMaxSpeed;

        /// <summary>
        /// 是否处于飞行模式：按住跳跃键持续向上飞行（替代普通跳跃逻辑），左右移动不变
        /// </summary>
        public bool BIsFlying => _bIsFlying;

        /// <summary>飞行上升加速度（单位/秒²）</summary>
        public float FlyAcceleration => _flyAcceleration;

        /// <summary>飞行最大上升速度（单位/秒）</summary>
        public float FlyMaxSpeed => _flyMaxSpeed;

        /// <summary>
        /// 开关飞行模式（由咒语效果调用，如"中国人能飞"）
        /// 开启时注入飞行参数；关闭后恢复普通跳跃逻辑
        /// </summary>
        /// <param name="flying">是否开启飞行</param>
        /// <param name="flyAcceleration">飞行上升加速度</param>
        /// <param name="maxFlySpeed">飞行最大上升速度</param>
        public void SetFlying(bool flying, float flyAcceleration = 0f, float maxFlySpeed = 0f)
        {
            _bIsFlying = flying;
            if (flying)
            {
                _flyAcceleration = flyAcceleration;
                _flyMaxSpeed = maxFlySpeed;
            }
        }
        // 是否可被道具效果影响（控制类：风力/磁力/减速/传送/震屏等）：死亡过渡与幽灵不受影响
        public bool BAffectedByItems => !BIsDead && !BIsGhost;
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
            // 档案未配置键位（None）时保留 prefab 序列化键位：
            // 联机补注册/纯标识档案可能不带键位，无条件覆盖会把按键清成 None（角色无法操控）
            if (profile.LeftKey != KeyCode.None) leftKey = profile.LeftKey;
            if (profile.RightKey != KeyCode.None) rightKey = profile.RightKey;
            if (profile.JumpKey != KeyCode.None) jumpKey = profile.JumpKey;
            if (profile.JumpKeyAlt != KeyCode.None) jumpKeyAlt = profile.JumpKeyAlt;
            if (profile.DownKey != KeyCode.None) downKey = profile.DownKey;

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
            if (_knockbackStunTimer > 0f)
            {
                _knockbackStunTimer -= Time.deltaTime;
            }
            // 状态机在 Start 初始化；晚生成的远端化身首帧可能先于 Start 执行 Update
            if (_currentState != null)
            {
                _currentState.Update();
            }
        }

        private void FixedUpdate()
        {
            if (_currentState != null)
            {
                _currentState.FixedUpdate();
            }
        }

        // ==================== 输入读取 ====================

        /// <summary>
        /// 读取玩家操作输入（委托给当前输入源）
        /// 输入源在 Start 初始化；晚生成的远端化身首帧 Update 可能先于 Start 执行，此时跳过
        /// </summary>
        private void ReadInput()
        {
            if (_input == null)
            {
                return;
            }
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

        // 全运动减速系数（蛛网等环境效果：水平移动/跳跃/下落/飞行统一生效）
        private float _motionSlowFactor = 1f;

        /// <summary>
        /// 全运动减速系数（1=正常，0.1=仅保留 10%）：
        /// 与 _speedMultiplier 的区别——后者仅作用于水平移速（黄油块等表面效果），
        /// 本系数作用于全部运动通道（移动/跳跃/下落/飞行），由状态机在对应通道乘算
        /// </summary>
        public float MotionSlowFactor => _motionSlowFactor;

        /// <summary>
        /// 设置全运动减速系数（如蛛网减速），自动夹紧到 0~1
        /// </summary>
        public void SetMotionSlow(float factor)
        {
            _motionSlowFactor = Mathf.Clamp01(factor);
        }

        /// <summary>
        /// 恢复全运动减速系数为 1
        /// </summary>
        public void ResetMotionSlow()
        {
            _motionSlowFactor = 1f;
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

        /// <summary>
        /// 累加外部推力（排气扇风力等；进入风区传正向量，离开传反向量抵消）
        /// </summary>
        public void AddWindForce(Vector2 force)
        {
            _windForce += force;
        }

        /// <summary>
        /// 清空外部推力（复活/状态重置时兜底用）
        /// </summary>
        public void ClearWindForce()
        {
            _windForce = Vector2.zero;
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
                : _currentState is PlayerFrozenState ? PlayerStateType.Frozen
                : PlayerStateType.Alive;
            LevelPlayerRegistry.Instance.UpdatePlayerState(this, stateType);
        }

        /// <summary>
        /// 死亡，进入死亡过渡状态（倒计时结束后自动切换为幽灵状态）
        /// 伤害型死亡（道具/事件命中）：播放命中音效
        /// </summary>
        /// <param name="playHitSfx">是否播放命中音效；落水等非命中死亡传 false</param>
        public void PlayerDie(bool playHitSfx = true)
        {
            // 命中音效先于免疫判定播放：无敌时虽免疫死亡，但被命中事实成立，仍需命中反馈
            if (playHitSfx)
            {
                PlayHitSfx();
            }

            // 无敌状态免疫伤害性死亡（掉落出界等不可豁免的死亡走 PlayerForceDie）
            if (BIsInvincible)
            {
                return;
            }
            PlayerForceDie(playHitSfx: false);   // 音效已在上方播放，避免重复
        }

        /// <summary>
        /// 强制死亡：无视无敌状态，立即进入死亡过渡
        /// 用于掉落出界等不可豁免的死亡场景（无敌金身等护盾不提供保护）
        /// </summary>
        /// <param name="playHitSfx">是否播放命中音效；坠落出界等非命中死亡传 false</param>
        /// <param name="fellOutOfBounds">是否跌落下边界死亡：true 时幽灵出生在固定初始位置，false 时保持死亡位置</param>
        public void PlayerForceDie(bool playHitSfx = false, bool fellOutOfBounds = false)
        {
            if (BIsDead || BIsGhost)
            {
                return;
            }
            if (playHitSfx)
            {
                PlayHitSfx();
            }
            // 记录死亡信息，供幽灵状态决定出生位置
            _deathPosition = _rb != null ? _rb.position : (Vector2)transform.position;
            _ghostSpawnAtFixedPosition = fellOutOfBounds;
            // 联机：上报死亡瞬间事件（远端播死亡表现），离线时为空操作
            SuperQQ.Network.NetEventSync.ReportEvent(
                Minigame.Room.V1.PlayerEventType.Die, transform.position);
            TransitionTo(new PlayerDyingState(this));
        }

        /// <summary>
        /// 被击飞死亡：强制一个击飞速度并进入死亡过渡状态
        /// 过渡期间保留击飞动量、无法操作，倒计时结束后自动进入幽灵状态
        /// 过渡时长统一使用 DeathDuration 配置，不允许外部覆盖
        /// </summary>
        /// <param name="knockbackVelocity">击飞速度（世界方向）</param>
        public void PlayerKnockbackDie(Vector2 knockbackVelocity)
        {
            if (BIsDead || BIsGhost)
            {
                return;
            }
            if (_rb != null)
            {
                _rb.velocity = knockbackVelocity;
            }

            // 命中音效先于免疫判定播放：无敌时免疫死亡但保留击退，被命中反馈照常
            PlayHitSfx();

            // 无敌：免疫死亡但仍保留击退——速度已施加，开启击退压制窗口让击退纯物理飞行
            // （否则存活状态的输入驱动会在下一物理帧改写击退速度，表现为击退力度骤减）
            if (BIsInvincible)
            {
                BeginKnockbackStun();
                return;
            }

            // 记录死亡信息：击飞死亡保持死亡位置进入幽灵
            _deathPosition = _rb != null ? _rb.position : (Vector2)transform.position;
            _ghostSpawnAtFixedPosition = false;

            // 联机：受击+死亡事件（远端播受击闪色与死亡表现）
            SuperQQ.Network.NetEventSync.ReportEvent(
                Minigame.Room.V1.PlayerEventType.Hit, transform.position);
            SuperQQ.Network.NetEventSync.ReportEvent(
                Minigame.Room.V1.PlayerEventType.Die, transform.position);
            TransitionTo(new PlayerDyingState(this));
        }

        /// <summary>播放被命中音效（3D 定位在玩家位置，走 SFX 总线）；未配置时静默</summary>
        private void PlayHitSfx()
        {
            if (hitSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(hitSfx, transform.position);
            }
        }

        /// <summary>播放起跳音效；由存活状态的起跳处理调用（飞行模式不走该路径，其音效由咒语自身控制）</summary>
        internal void PlayJumpSfx()
        {
            if (jumpSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(jumpSfx, transform.position);
            }
        }

        /// <summary>播放落地音效；由存活状态的地面检测在滞空→着地的边沿调用</summary>
        internal void PlayLandSfx()
        {
            if (landSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(landSfx, transform.position);
            }
        }

        /// <summary>
        /// 冻结：进入冻结状态（无法操作、刚体全约束，但仍可被击杀）
        /// 已死亡/幽灵/通关/已冻结的玩家不重复进入
        /// </summary>
        public void Freeze()
        {
            if (BIsDead || BIsGhost || BIsFinished || BIsFrozen)
            {
                return;
            }
            TransitionTo(new PlayerFrozenState(this));
        }

        /// <summary>
        /// 解冻：从冻结状态恢复为正常存活状态
        /// 非冻结状态下调用为空操作
        /// </summary>
        public void Unfreeze()
        {
            if (!BIsFrozen)
            {
                return;
            }
            TransitionTo(new PlayerAliveState(this));
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

        /// <summary>
        /// 复活：从死亡/幽灵/通关状态回到存活状态（新一轮开始时调用）。
        /// 联机模式同场景跨轮复用玩家实例，必须显式复活；单机每轮换场景重生实例，此处为空操作。
        /// 状态 Exit 负责恢复碰撞体/重力/透明度，此处只需清零速度并切换状态。
        /// </summary>
        public void Revive()
        {
            if (!BIsDead && !BIsGhost && !BIsFinished)
            {
                return; // 已存活，无需复活
            }

            if (_rb != null)
            {
                _rb.velocity = Vector2.zero;
            }
            ClearWindForce();
            TransitionTo(new PlayerAliveState(this));
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

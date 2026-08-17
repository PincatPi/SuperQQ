using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家动画驱动器 — 逻辑状态到 Animator 的桥接层
    /// 单向依赖：只读取 PlayerController 的公开查询属性，驱动 Animator 参数
    /// 动画层不反向影响逻辑层，逻辑层无需感知动画存在
    ///
    /// Animator 参数约定：
    ///   VelocityX (Float)— 水平速度绝对值，Idle/Run 切换条件（阈值见 runEnterThreshold）
    ///   VelocityY (Float)— 竖直速度（带符号，上升为正），Jump BlendTree 的混合输入
    ///   bIsDead (Bool)  — 死亡过渡标记，配合 Any State → Die 转换（进入幽灵后置回 false）
    ///   bIsJumping (Bool)— 滞空标记，离地（跳跃或自然坠落）为 true、落地为 false，驱动跳跃动画进出
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimationController : MonoBehaviour
    {
        [Header("组件引用")]
        [SerializeField] private Animator animator;                  // 目标 Animator（不填则自动从子物体获取）

        [Header("Idle/Run 切换")]
        [SerializeField] private float runEnterThreshold = 0.1f;     // VelocityX 大于此值进入 Run（需与 Animator 条件一致）

        [Header("朝向")]
        [SerializeField] private bool flipWithVelocity = true;       // 根据水平速度方向翻转精灵

        // ---------- Animator 参数哈希（避免每帧字符串查找） ----------
        private static readonly int VelocityXHash = Animator.StringToHash("VelocityX");
        private static readonly int VelocityYHash = Animator.StringToHash("VelocityY");
        private static readonly int IsDeadHash = Animator.StringToHash("bIsDead");
        private static readonly int IsJumpingHash = Animator.StringToHash("bIsJumping");

        // ---------- 组件缓存 ----------
        private PlayerController _player;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                Debug.LogWarning("[PlayerAnimationController] 未找到 Animator，动画驱动将失效。请在 Inspector 中指定或在子物体上挂载 Animator。", this);
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            UpdateLocomotion();
            UpdateDie();
            UpdateJump();
            UpdateFacing();
        }

        // ==================== 移动动画（Idle / Run） ====================

        /// <summary>
        /// 将水平速度绝对值写入 VelocityX 参数，将竖直速度写入 VelocityY 参数
        /// Idle → Run：VelocityX Greater runEnterThreshold
        /// Run → Idle：VelocityX Less runEnterThreshold
        /// VelocityY 带符号（上升为正、下落为负），直接读刚体实际速度，供 Jump BlendTree 混合上升/下落 Motion
        /// </summary>
        private void UpdateLocomotion()
        {
            animator.SetFloat(VelocityXHash, Mathf.Abs(_player.HorizontalVelocity));
            animator.SetFloat(VelocityYHash, _player.Rb != null ? _player.Rb.velocity.y : 0f);
        }

        // ==================== 死亡动画 ====================

        /// <summary>
        /// 将逻辑层 BIsDead 写入 Animator Bool 参数，配合 Any State → Die 转换
        /// 死亡过渡期间为 true 播放死亡动画，进入幽灵后自动置回 false
        /// 每帧幂等写入，无需额外的触发标记
        /// </summary>
        private void UpdateDie()
        {
            animator.SetBool(IsDeadHash, _player.BIsDead);
        }

        // ==================== 跳跃动画 ====================

        /// <summary>
        /// 将逻辑层 BIsJumpAirborne 写入 Animator Bool 参数
        /// 离地（跳跃或自然坠落）为 true 进入跳跃动画，落地后置回 false 退出跳跃动画
        /// 每帧幂等写入
        /// </summary>
        private void UpdateJump()
        {
            animator.SetBool(IsJumpingHash, _player.BIsJumpAirborne);
        }

        // ==================== 朝向翻转 ====================

        /// <summary>
        /// 根据水平速度方向翻转精灵（朝右为正方向）
        /// 速度在阈值附近抖动时不翻转，避免左右闪动
        /// </summary>
        private void UpdateFacing()
        {
            if (!flipWithVelocity || _player.Renderer == null)
            {
                return;
            }

            float velocity = _player.HorizontalVelocity;
            if (velocity > runEnterThreshold)
            {
                _player.Renderer.flipX = false;
            }
            else if (velocity < -runEnterThreshold)
            {
                _player.Renderer.flipX = true;
            }
        }
    }
}

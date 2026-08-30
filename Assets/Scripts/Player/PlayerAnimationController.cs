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
    ///   bIsVictory (Bool)— 通关标记，配合 Any State → Victory 转换（循环播放胜利动画，复活回到存活后自动置回 false）
    ///   bIsGhost (Bool) — 幽灵标记，配合 Ghost 动画转换（进入幽灵后循环播放幽灵动画，复活回到存活后自动置回 false）
    ///   Taunt (Trigger) — 嘲讽表情触发标记：PC 端由本地键盘嘲讽键（PlayerController.TauntPressed）驱动，
    ///                     移动端由 MobileInputPanel 嘲讽按钮直接调用 PlayTaunt()；
    ///                     打断由 Animator 过渡实现：Taunt 状态配自身过渡（可被再次嘲讽打断），
    ///                     移动/跳跃经 VelocityX/bIsJumping 条件过渡打断。
    ///                     注意：仅存活状态走本 Trigger；嘲讽表情包（TauntEmojiController）存活/幽灵均播放
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
        private static readonly int IsVictoryHash = Animator.StringToHash("bIsVictory");
        private static readonly int IsGhostHash = Animator.StringToHash("bIsGhost");
        private static readonly int TauntHash = Animator.StringToHash("Taunt");

        // ---------- 组件缓存 ----------
        private PlayerController _player;
        private TauntEmojiController _emojiCtrl;             // 幽灵嘲讽表情包播放器（可空，未挂载时幽灵嘲讽仅上报）

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _emojiCtrl = GetComponent<TauntEmojiController>();

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
            UpdateVictory();
            UpdateGhost();
            UpdateTaunt();
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

        // ==================== 通关胜利动画 ====================

        /// <summary>
        /// 将逻辑层 BIsFinished 写入 Animator Bool 参数，配合 Any State → Victory 转换
        /// 通关期间为 true 循环播放胜利动画，复活回到存活状态后自动置回 false
        /// 每帧幂等写入，无需额外的触发标记
        /// </summary>
        private void UpdateVictory()
        {
            animator.SetBool(IsVictoryHash, _player.BIsFinished);
        }

        // ==================== 幽灵动画 ====================

        /// <summary>
        /// 将逻辑层 BIsGhost 写入 Animator Bool 参数，配合 Ghost 动画转换
        /// 死亡过渡（Dying）期间为 false 先播死亡动画，进入幽灵状态后为 true 循环播放幽灵动画，
        /// 复活回到存活状态后自动置回 false 回到 Idle。每帧幂等写入，无需额外的触发标记
        /// </summary>
        private void UpdateGhost()
        {
            animator.SetBool(IsGhostHash, _player.BIsGhost);
        }

        // ==================== 嘲讽表情 ====================

        /// <summary>
        /// PC 端键盘嘲讽：读取逻辑层 TauntPressed（沿触发，仅本地键盘输入源有效），按下即播放嘲讽
        /// 远程玩家/输入屏蔽期间（NullPlayerInput/JoystickPlayerInput）恒为 false，不会误触发
        /// </summary>
        private void UpdateTaunt()
        {
            if (_player.TauntPressed)
            {
                PlayTaunt();
            }
        }

        /// <summary>
        /// 播放嘲讽：存活/幽灵均弹表情包（TauntEmojiController，固定时长 + Close 收尾动画，
        /// 重复嘲讽立即打断重播）；存活状态额外向 Animator 写入 Taunt Trigger 播放玩家嘲讽动画
        /// （可重复写入，再次触发重启动作需 Animator 配 Taunt 自过渡；
        /// 移动/跳跃打断由 Taunt → Idle/Run/Jump 条件过渡实现，代码侧无需处理）。
        /// 冻结状态禁止嘲讽：不播也不上报，远端因此不会收到冻结玩家的嘲讽事件。
        /// 联机时经 NetEventSync 上报一次性事件（服务器透传广播），远端化身收到后按同样的规则播放；
        /// 仅本地玩家会走到这里（远端化身的本组件已被 RemotePlayerSync 禁用），离线时上报为空操作
        /// </summary>
        public void PlayTaunt()
        {
            if (_player.BIsFrozen)
            {
                return;
            }

            // 存活/幽灵均弹表情包
            if (_emojiCtrl != null)
            {
                _emojiCtrl.Play();
            }
            else
            {
                Debug.LogWarning("[PlayerAnimationController] 嘲讽表情包需要挂载 TauntEmojiController 并配置表情包 prefab。", this);
            }

            // 存活状态额外播放玩家嘲讽动画（幽灵只弹表情包）
            if (!_player.BIsGhost && animator != null)
            {
                animator.SetTrigger(TauntHash);
            }

            SuperQQ.Network.NetEventSync.ReportEvent(
                Minigame.Room.V1.PlayerEventType.Taunt, _player.transform.position);
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

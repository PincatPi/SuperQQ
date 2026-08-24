using SuperQQ.Audio;
using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 脚步音效转发器 — 帧动画事件到 AudioManager 的薄桥接层
    /// 挂在 Animator 所在物体（Player/Visual）上：动画事件的接收组件必须与 Animator 同物体，
    /// 走路（Run）帧动画在两只脚落地帧各配置一个动画事件指向 PlayFootstep()，每次触发播放一次脚步声。
    ///
    /// 分层说明：本组件属动画表现层，仅向 AudioManager 门面转发，不含任何玩法逻辑；
    /// 与 PlayerAnimationController 单向依赖逻辑层的设计一致，逻辑层无需感知音效存在。
    ///
    /// 配置步骤：
    ///   1. 本组件挂到 Player/Visual（Animator 所在物体）；
    ///   2. 打开走路动画 Clip（Animation 窗口），在两只脚落地帧各添加一个 Animation Event，
    ///      Function 选 PlayFootstep；
    ///   3. AudioCatalog 中注册 Footstep 条目并拖配脚步 Clip（Bus = SFX）。
    /// </summary>
    public class FootstepSfxRelay : MonoBehaviour
    {
        [Tooltip("脚步音效（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId footstepSfx = SfxId.Footstep;

        /// <summary>
        /// 动画事件回调：走路动画的落地帧触发，在玩家位置 3D 播放一次脚步声
        /// （走 SFX 总线；连走触发频繁，受条目 MinReplayInterval 限频保护）
        /// </summary>
        public void PlayFootstep()
        {
            if (footstepSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(footstepSfx, transform.position);
            }
        }
    }
}

using SuperQQ.Audio;
using SuperQQ.Microphone;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 正式游玩阶段。
    /// 全员出局条件成立后，结算本轮得分并进入配置的下一阶段。
    /// 转移配置建议：[0] 全员出局条件 -> 回合结算阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Playing Phase")]
    public class PlayingPhase : GamePhaseBase
    {
        [Header("音效")]
        [Tooltip("进入正式游玩阶段时播放的开始音效（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _roundStartSfx = SfxId.RoundStart;

        [Tooltip("离开正式游玩阶段时播放的结束音效（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _roundFinishSfx = SfxId.RoundFinish;

        private bool _bRoundSettled;

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bRoundSettled = false;

            // 阶段开始音效（2D 全局，走 SFX 总线）
            if (_roundStartSfx != SfxId.None)
            {
                AudioManager.PlaySfx(_roundStartSfx);
            }

            // 兜底复活：正常路径下玩家已在选择阶段开始时复活（PropSelectionPhase.OnEnter），
            // 此处为幂等二次调用（已存活为空操作），覆盖联机迟到/单机独立进入游玩阶段等路径。
            SuperQQ.Player.LevelPlayerRegistry.Instance?.ReviveLocalPlayersForNewRound();

            // 进入游玩阶段开始接收本地玩家麦克风输入，实时检测分贝
            MicVolumeManager.EnsureExists().StartMic();
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);
            _bRoundSettled = false;

            // 阶段结束音效（2D 全局，走 SFX 总线）
            if (_roundFinishSfx != SfxId.None)
            {
                AudioManager.PlaySfx(_roundFinishSfx);
            }

            // 离开游玩阶段停止麦克风输入接收
            if (MicVolumeManager.Instance != null)
            {
                MicVolumeManager.Instance.StopMic();
            }
        }

        /// <summary>
        /// 转移选中时结算本轮得分（防重）。
        /// </summary>
        protected override void OnTransitionSelected(GamePhaseContext context, GamePhaseTransition transition)
        {
            if (_bRoundSettled)
            {
                return;
            }

            if (context.ScoreManager != null)
            {
                context.ScoreManager.SettleCurrentRound();
            }
            else
            {
                Debug.LogWarning("[PlayingPhase] PlayerScoreManager 不存在，本轮得分不会被结算。");
            }

            _bRoundSettled = true;
        }
    }
}

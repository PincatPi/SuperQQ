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
        private bool _bRoundSettled;

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bRoundSettled = false;

            // 兜底复活：正常路径下玩家已在选择阶段开始时复活（PropSelectionPhase.OnEnter），
            // 此处为幂等二次调用（已存活为空操作），覆盖联机迟到/单机独立进入游玩阶段等路径。
            SuperQQ.Player.LevelPlayerRegistry.Instance?.ReviveLocalPlayersForNewRound();
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);
            _bRoundSettled = false;
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

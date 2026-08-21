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

            // 新一轮开始：复活本地玩家并回出生点。
            // 联机模式同场景跨轮复用玩家实例，上一轮死亡/通关的玩家必须显式复活回 Alive；
            // 远端玩家由各端自己复活后经状态上报同步；单机新场景新实例为空操作。
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

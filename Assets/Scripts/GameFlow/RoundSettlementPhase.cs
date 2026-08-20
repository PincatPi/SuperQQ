using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 单轮结算阶段。
    /// 等待结算展示完成后，根据是否达到胜利线进入配置的最终结算或下一轮阶段。
    /// 转移配置建议：[0] 胜利线条件 -> 最终结算阶段；[1] 无条件 -> 下一轮阶段（数组下标即优先级）。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Round Settlement Phase")]
    public class RoundSettlementPhase : GamePhaseBase
    {
        private bool _bSettlementFinished;
        private bool _bAdvancedToNextRound;
        private bool _bResultsPanelOpened;

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bSettlementFinished = false;
            _bAdvancedToNextRound = false;

            // 结算为场景内覆盖层（不切场景），RoundResultsDirector 已在场，直接弹出结算面板
            _bResultsPanelOpened = false;
            TryOpenResultsPanel(context);
        }

        public override void RefreshSceneRuntimeBindings(GamePhaseContext context)
        {
            base.RefreshSceneRuntimeBindings(context);

            // 场景异步加载完成后 Director 才存在，此处补开启（由 _bResultsPanelOpened 保证不重复）
            TryOpenResultsPanel(context);
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);

            context.RoundResultsDirector?.EndPhase();

            _bSettlementFinished = false;
            _bAdvancedToNextRound = false;
            _bResultsPanelOpened = false;
        }

        public override bool TryGetNextPhase(GamePhaseContext context, out GamePhaseBase nextPhase, out string reason)
        {
            if (!_bSettlementFinished)
            {
                nextPhase = null;
                reason = string.Empty;
                return false;
            }

            if (context.ScoreManager == null)
            {
                Debug.LogError("[RoundSettlementPhase] PlayerScoreManager 不存在，无法判断结算后的下一阶段。");
                nextPhase = null;
                reason = string.Empty;
                return false;
            }

            return base.TryGetNextPhase(context, out nextPhase, out reason);
        }

        /// <summary>
        /// 结算表现层展示完成后，经 GamePhaseManager.NotifyCurrentPhaseEvent 通知到此处。
        /// </summary>
        public override void NotifyPhaseEvent()
        {
            base.NotifyPhaseEvent();
            _bSettlementFinished = true;
        }

        /// <summary>
        /// 尝试弹出单轮结算面板（幂等）。Director 不在场时仅告警，不阻断结算流程（柱体动画仍照常推进）。
        /// </summary>
        private void TryOpenResultsPanel(GamePhaseContext context)
        {
            if (_bResultsPanelOpened)
            {
                return;
            }

            if (context.RoundResultsDirector == null)
            {
                Debug.LogWarning($"[{LogName}] 场景中缺少 RoundResultsDirector，结算面板不会弹出（结算流程仍照常推进）。");
                return;
            }

            context.RoundResultsDirector.BeginPhase();
            _bResultsPanelOpened = true;
        }

        /// <summary>
        /// 转移选中时，若未达胜利线则推进下一轮（防重）；达胜利线则无副作用。
        /// </summary>
        protected override void OnTransitionSelected(GamePhaseContext context, GamePhaseTransition transition)
        {
            if (context.ScoreManager.BHasPlayerReachedVictoryLine())
            {
                return;
            }

            if (!_bAdvancedToNextRound)
            {
                context.ScoreManager.AdvanceToNextRound();
                _bAdvancedToNextRound = true;
            }
        }
    }
}

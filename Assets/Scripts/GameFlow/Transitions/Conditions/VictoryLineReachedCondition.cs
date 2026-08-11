using System;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 胜利线条件。
    /// 任意玩家得分达到胜利线后条件成立。
    /// </summary>
    [CreateAssetMenu(fileName = "VictoryLineReachedCondition", menuName = "SuperQQ/Game Flow/Conditions/Victory Line Reached Condition")]
    public class VictoryLineReachedCondition : GamePhaseCondition
    {
        private bool _bScoreManagerWarned;
        [SerializeField] private bool _bIsInvert;

        public override bool Evaluate(GamePhaseContext context)
        {
            if (context == null || context.ScoreManager == null)
            {
                if (!_bScoreManagerWarned)
                {
                    Debug.LogWarning("[VictoryLineReachedCondition] PlayerScoreManager 不存在，条件不成立。");
                    _bScoreManagerWarned = true;
                }

                return false;
            }

            return context.ScoreManager.BHasPlayerReachedVictoryLine();
        }

        public override void OnPhaseEnter(GamePhaseContext context)
        {
            _bScoreManagerWarned = false;
        }

        public override string GetReason()
        {
            return "已有玩家达到胜利线";
        }
    }
}

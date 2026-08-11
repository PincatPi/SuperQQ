using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 道具摆放阶段条件。
    /// 倒计时结束后条件成立；倒计时未结束时，若提前完成判定满足则立即成立。
    /// 倒计时时长在资产上配置。
    /// </summary>
    [CreateAssetMenu(fileName = "PropPlacementCondition", menuName = "SuperQQ/Game Flow/Conditions/Prop Placement Condition")]
    public class PropPlacementCondition : TimerElapsedCondition
    {
        /// <summary>
        /// 提前完成判定。倒计时未结束时调用，返回 true 则不等待倒计时结束，直接视为本条件成立。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <returns>提前完成返回 true。</returns>
        protected override bool EvaluateEarlyComplete(GamePhaseContext context)
        {
            // TODO: 根据道具摆放阶段的业务需求实现提前完成判定逻辑。
            return false;
        }
    }
}

using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 道具选择阶段条件。
    /// 倒计时结束后条件成立；倒计时未结束时，若本地玩家已完成选择则立即成立。
    /// 倒计时时长在资产上配置。
    /// </summary>
    [CreateAssetMenu(fileName = "PropSelectCondition", menuName = "SuperQQ/Game Flow/Conditions/Prop Select Condition")]
    public class PropSelectCondition : TimerElapsedCondition
    {
        /// <summary>
        /// 提前完成判定：本地玩家已确认选中道具时提前结束阶段。
        /// 本方法每帧被调用，只做只读查询、不产生副作用。
        /// TODO: 联机接入后升级为「全员选择完毕」判定（当前拿不到远端玩家的选择状态，只能判定本地）。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <returns>提前完成返回 true。</returns>
        protected override bool EvaluateEarlyComplete(GamePhaseContext context)
        {
            // Director 不在场或选择玩法未开启时不提前结束，交由倒计时兜底
            return context.SelectionDirector != null && context.SelectionDirector.BIsLocalSelectionDone;
        }

        public override string GetReason()
        {
            return $"倒计时结束或本地道具选择完毕（时长 {Duration:F1}s）";
        }
    }
}

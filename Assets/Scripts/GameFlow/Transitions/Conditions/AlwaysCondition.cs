using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 恒真条件。
    /// 始终成立，用于兜底转移或进入阶段后立即切换。
    /// </summary>
    [CreateAssetMenu(fileName = "AlwaysCondition", menuName = "SuperQQ/Game Flow/Conditions/Always Condition")]
    public class AlwaysCondition : GamePhaseCondition
    {
        public override bool Evaluate(GamePhaseContext context)
        {
            return true;
        }

        public override string GetReason()
        {
            return "无条件转移";
        }
    }
}

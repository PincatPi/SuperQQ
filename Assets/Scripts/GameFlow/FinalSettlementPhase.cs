using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 最终结算阶段。
    /// 作为整局游戏流程的终止阶段，转移数组保持为空即不再跳转到后续阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Final Settlement Phase")]
    public class FinalSettlementPhase : GamePhaseBase
    {
    }
}

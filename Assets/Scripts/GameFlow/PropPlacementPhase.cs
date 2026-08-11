using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 道具放置阶段。
    /// 倒计时结束或外部确认后进入配置的下一阶段。
    /// 转移配置建议：[0] 外部事件条件 -> 下一阶段；[1] 倒计时条件 -> 下一阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Prop Placement Phase")]
    public class PropPlacementPhase : GamePhaseBase
    {
    }
}

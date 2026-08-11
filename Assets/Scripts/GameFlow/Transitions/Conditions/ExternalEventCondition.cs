using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 外部事件条件。
    /// 所属阶段收到外部事件通知（GamePhaseBase.NotifyPhaseEvent）后条件成立。
    /// 适用于“外部确认提前完成”“结算展示完成”等由表现层驱动的切换。
    /// </summary>
    [CreateAssetMenu(fileName = "ExternalEventCondition", menuName = "SuperQQ/Game Flow/Conditions/External Event Condition")]
    public class ExternalEventCondition : GamePhaseCondition
    {
        private bool _bTriggered;

        public override bool Evaluate(GamePhaseContext context)
        {
            return _bTriggered;
        }

        public override void OnPhaseEnter(GamePhaseContext context)
        {
            _bTriggered = false;
        }

        public override void OnPhaseExit(GamePhaseContext context)
        {
            _bTriggered = false;
        }

        public override void OnPhaseEvent()
        {
            _bTriggered = true;
        }

        public override string GetReason()
        {
            return "外部事件触发";
        }
    }
}

using System;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 阶段转移数据。
    /// 表示“切换条件成立时，转移到目标阶段”。
    /// 条件为空视为无条件转移（恒真）；同一阶段的多个转移按数组下标顺序评估，先命中者优先。
    /// </summary>
    [Serializable]
    public class GamePhaseTransition
    {
        [Tooltip("条件成立时要转移到的目标阶段")]
        [SerializeField] private GamePhaseBase _targetPhase;

        [Tooltip("切换条件资产，为空表示无条件转移（恒真）")]
        [SerializeField] private GamePhaseCondition _condition;

        [Tooltip("勾选后对条件判定结果取反。无条件时勾选视为不转移")]
        [SerializeField] private bool _bIsInvert;

        /// <summary>
        /// 运行时条件实例。阶段进入时由配置资产实例化而来，避免共享资产的运行时状态互相污染。
        /// </summary>
        [NonSerialized] private GamePhaseCondition _runtimeCondition;

        /// <summary>
        /// 目标阶段。
        /// </summary>
        public GamePhaseBase TargetPhase => _targetPhase;

        /// <summary>
        /// 切换条件。运行时返回实例化副本；未进入阶段时返回配置资产。为空表示无条件转移。
        /// </summary>
        public GamePhaseCondition Condition => _runtimeCondition != null ? _runtimeCondition : _condition;

        /// <summary>
        /// 评估该转移的切换条件是否成立，并按配置对结果取反。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <returns>条件成立或无条件时返回 true；勾选取反后返回其反值。</returns>
        public bool Evaluate(GamePhaseContext context)
        {
            GamePhaseCondition condition = Condition;
            bool bResult = condition == null || condition.Evaluate(context);
            return _bIsInvert ? !bResult : bResult;
        }

        /// <summary>
        /// 所属阶段进入时调用，实例化运行时条件副本并转发进入通知。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public void OnPhaseEnter(GamePhaseContext context)
        {
            _runtimeCondition = _condition != null ? UnityEngine.Object.Instantiate(_condition) : null;
            _runtimeCondition?.OnPhaseEnter(context);
        }

        /// <summary>
        /// 所属阶段退出时调用，转发给切换条件并销毁运行时副本。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public void OnPhaseExit(GamePhaseContext context)
        {
            if (_runtimeCondition == null)
            {
                return;
            }

            _runtimeCondition.OnPhaseExit(context);
            UnityEngine.Object.Destroy(_runtimeCondition);
            _runtimeCondition = null;
        }

        /// <summary>
        /// 所属阶段每帧更新时调用，转发给切换条件。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <param name="deltaTime">帧间隔时间。</param>
        public void OnPhaseTick(GamePhaseContext context, float deltaTime)
        {
            Condition?.OnPhaseTick(context, deltaTime);
        }

        /// <summary>
        /// 场景运行时绑定刷新时调用，转发给切换条件。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public void OnSceneBindingsRefresh(GamePhaseContext context)
        {
            Condition?.OnSceneBindingsRefresh(context);
        }

        /// <summary>
        /// 所属阶段收到外部事件通知时调用，转发给切换条件。
        /// </summary>
        public void OnPhaseEvent()
        {
            Condition?.OnPhaseEvent();
        }

        /// <summary>
        /// 切换原因描述，用于阶段切换日志。
        /// </summary>
        public string GetReason()
        {
            GamePhaseCondition condition = Condition;
            string reason = condition != null ? condition.GetReason() : "无条件转移";
            return _bIsInvert ? $"{reason}（取反）" : reason;
        }
    }
}

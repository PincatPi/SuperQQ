using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 游戏阶段基类。
    /// 每个阶段负责维护自身进入、退出、更新逻辑。
    /// 阶段通过转移数组配置后续阶段：每个转移由目标阶段与切换条件组成，
    /// 按数组下标顺序评估，首个条件成立的转移胜出；数组为空表示最终阶段。
    /// </summary>
    public abstract class GamePhaseBase : ScriptableObject
    {
        [Header("阶段基础信息")]
        [SerializeField] private string _displayName = "";
        [SerializeField] private SceneReference _scene = new();
        [TextArea]
        [SerializeField] private string _description = "";

        [Header("阶段转移")]
        [Tooltip("按数组下标顺序评估，首个条件成立的转移胜出；数组为空表示最终阶段")]
        [SerializeField] private GamePhaseTransition[] _transitions = Array.Empty<GamePhaseTransition>();

        /// <summary>
        /// 阶段展示名称。
        /// </summary>
        public string DisplayName => _displayName;

        /// <summary>
        /// 进入该阶段时需要加载的场景引用。未配置表示不切换场景。
        /// </summary>
        public SceneReference Scene => _scene;

        /// <summary>
        /// 进入该阶段时需要加载的场景名。为空表示不切换场景。
        /// </summary>
        public string SceneName => _scene != null ? _scene.SceneName : string.Empty;

        /// <summary>
        /// 阶段说明。
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// 阶段日志名称。
        /// </summary>
        public string LogName => !string.IsNullOrWhiteSpace(_displayName) ? _displayName : name;

        /// <summary>
        /// 阶段转移数组。数组为空表示最终阶段。
        /// </summary>
        public IReadOnlyList<GamePhaseTransition> Transitions => _transitions;

        /// <summary>
        /// 进入阶段。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void OnEnter(GamePhaseContext context)
        {
            string sceneName = SceneName;
            if (!string.IsNullOrEmpty(sceneName))
            {
                context.LoadScene(sceneName);
            }

            ForEachTransition(transition => transition.OnPhaseEnter(context));
        }

        /// <summary>
        /// 退出阶段。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void OnExit(GamePhaseContext context)
        {
            ForEachTransition(transition => transition.OnPhaseExit(context));
        }

        /// <summary>
        /// 阶段运行中每帧更新。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <param name="deltaTime">帧间隔时间。</param>
        public virtual void OnUpdate(GamePhaseContext context, float deltaTime)
        {
            ForEachTransition(transition => transition.OnPhaseTick(context, deltaTime));
        }

        /// <summary>
        /// 场景加载完成后刷新阶段依赖的场景运行时对象。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void RefreshSceneRuntimeBindings(GamePhaseContext context)
        {
            ForEachTransition(transition => transition.OnSceneBindingsRefresh(context));
        }

        /// <summary>
        /// 收集该阶段可能引用的目标阶段，用于流程配置校验。
        /// </summary>
        /// <param name="phases">目标阶段集合。</param>
        public virtual void CollectReferencedPhases(List<GamePhaseBase> phases)
        {
            if (_transitions == null)
            {
                return;
            }

            for (int i = 0; i < _transitions.Length; i++)
            {
                GamePhaseTransition transition = _transitions[i];
                phases.Add(transition != null ? transition.TargetPhase : null);
            }
        }

        /// <summary>
        /// 尝试获取下一阶段。
        /// 按数组下标顺序评估各转移的切换条件，首个条件成立且目标非空的转移胜出。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <param name="nextPhase">目标阶段。</param>
        /// <param name="reason">切换原因，用于日志。</param>
        public virtual bool TryGetNextPhase(GamePhaseContext context, out GamePhaseBase nextPhase, out string reason)
        {
            if (_transitions != null)
            {
                for (int i = 0; i < _transitions.Length; i++)
                {
                    GamePhaseTransition transition = _transitions[i];
                    if (transition == null || transition.TargetPhase == null)
                    {
                        continue;
                    }

                    if (!transition.Evaluate(context))
                    {
                        continue;
                    }

                    OnTransitionSelected(context, transition);
                    nextPhase = transition.TargetPhase;
                    reason = transition.GetReason();
                    return true;
                }
            }

            nextPhase = null;
            reason = string.Empty;
            return false;
        }

        /// <summary>
        /// 转移被选中时调用，供子类执行切换副作用（如结算得分、推进轮次）。
        /// 注意：转移条件成立后每帧都会命中，副作用需由子类自行防重。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <param name="transition">被选中的转移。</param>
        protected virtual void OnTransitionSelected(GamePhaseContext context, GamePhaseTransition transition)
        {
        }

        /// <summary>
        /// 通知当前阶段的外部事件（如结算展示完成、外部确认提前完成）。
        /// 基类默认转发给所有转移的切换条件，子类可重写以实现自身的事件响应。
        /// </summary>
        public virtual void NotifyPhaseEvent()
        {
            ForEachTransition(transition => transition.OnPhaseEvent());
        }

        private void ForEachTransition(Action<GamePhaseTransition> action)
        {
            if (_transitions == null)
            {
                return;
            }

            for (int i = 0; i < _transitions.Length; i++)
            {
                GamePhaseTransition transition = _transitions[i];
                if (transition != null)
                {
                    action(transition);
                }
            }
        }
    }
}

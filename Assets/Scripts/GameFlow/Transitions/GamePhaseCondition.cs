using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 阶段切换条件抽象基类。
    /// 以 ScriptableObject 资产形式配置，可直接拖拽挂载到阶段转移数据中。
    /// 阶段进入时会实例化运行时副本，各阶段运行时状态互不污染。
    /// 条件只负责判断“能否切换”，切换时的副作用由阶段子类的转移选中钩子处理。
    /// </summary>
    public abstract class GamePhaseCondition : ScriptableObject
    {
        /// <summary>
        /// 评估切换条件是否成立。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <returns>条件成立返回 true。</returns>
        public abstract bool Evaluate(GamePhaseContext context);

        /// <summary>
        /// 所属阶段进入时调用，用于重置运行时状态。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void OnPhaseEnter(GamePhaseContext context)
        {
        }

        /// <summary>
        /// 所属阶段退出时调用，用于清理运行时状态。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void OnPhaseExit(GamePhaseContext context)
        {
        }

        /// <summary>
        /// 所属阶段每帧更新时调用，用于累计时间等逐帧状态。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <param name="deltaTime">帧间隔时间。</param>
        public virtual void OnPhaseTick(GamePhaseContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 场景运行时绑定刷新时调用，用于重新订阅场景对象事件。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        public virtual void OnSceneBindingsRefresh(GamePhaseContext context)
        {
        }

        /// <summary>
        /// 所属阶段收到外部事件通知时调用。
        /// </summary>
        public virtual void OnPhaseEvent()
        {
        }

        /// <summary>
        /// 切换原因描述，用于阶段切换日志。
        /// </summary>
        public virtual string GetReason()
        {
            return GetType().Name;
        }
    }
}

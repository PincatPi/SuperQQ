using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件修饰符抽象基类 — ScriptableObject 资产
    /// 每个具体事件（如 ColdSnap、BossPatrol）继承此类，实现自己的事件逻辑
    /// 作为纯逻辑资产，不持有场景引用，场景物体通过 LevelEventContext 传递
    /// 新增事件只需：继承此类 → 实现 Activate/Deactivate → 创建 SO 资产 → 拖入 LevelEventEntry.Modifier
    /// </summary>
    public abstract class LevelEventModifier : ScriptableObject
    {
        /// <summary>
        /// 激活事件：启动事件逻辑
        /// 由 LevelEventAnnouncer 在事件被选中时调用
        /// </summary>
        /// <param name="context">运行时上下文，提供协程宿主和场景引用</param>
        public abstract void Activate(LevelEventContext context);

        /// <summary>
        /// 停用事件：停止事件逻辑，用于场景切换或强制中断时的清理
        /// 由 LevelEventAnnouncer 在场景销毁时调用
        /// </summary>
        /// <param name="context">运行时上下文</param>
        public abstract void Deactivate(LevelEventContext context);
    }
}

using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果抽象基类 — ScriptableObject 资产，纯配置载体（预制体/时长等参数），不持有运行时状态
    /// 契约：Activate(SpellEffectContext) 创建并返回一个运行时效果实例
    /// 新增效果（护盾/加速/变大等）= 继承本类实现 Activate，策划侧纯配置扩展
    /// </summary>
    public abstract class SpellEffect : ScriptableObject
    {
        /// <summary>
        /// 激活效果：按自身配置创建运行时效果实例并使其生效
        /// </summary>
        /// <param name="context">效果上下文（触发玩家、协程宿主等运行时引用）</param>
        /// <returns>运行时效果实例；上下文无效（如触发玩家缺失）时返回 null</returns>
        public abstract SpellEffectInstance Activate(SpellEffectContext context);
    }
}

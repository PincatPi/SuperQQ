namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果运行时实例基类 — 管理单次生效的生命周期
    /// 与 SpellEffect（SO 配置）分离：实例持有运行时状态（特效实例、协程、事件订阅），
    /// 支持计时结束、提前结束（如玩家出局）与事件停用时统一清理
    /// 生命周期契约：创建即生效 → End() 幂等结束（OnEnd 中做具体清理）
    /// </summary>
    public abstract class SpellEffectInstance
    {
        /// <summary>效果上下文（触发玩家、协程宿主等）</summary>
        protected SpellEffectContext Context { get; }

        /// <summary>效果目标玩家的便捷访问</summary>
        protected SuperQQ.Player.PlayerController Target => Context.Target;

        /// <summary>协程宿主的便捷访问</summary>
        protected UnityEngine.MonoBehaviour Runner => Context.CoroutineRunner;

        /// <summary>效果是否仍在生效（End 后为 false）</summary>
        public bool BIsActive { get; private set; } = true;

        protected SpellEffectInstance(SpellEffectContext context)
        {
            Context = context;
        }

        /// <summary>
        /// 结束效果：幂等，重复调用为空操作；实际清理由子类在 OnEnd 中实现
        /// </summary>
        public void End()
        {
            if (!BIsActive)
            {
                return;
            }

            BIsActive = false;
            OnEnd();
        }

        /// <summary>
        /// 结束时的具体清理：销毁特效、停止协程、退订事件等
        /// </summary>
        protected abstract void OnEnd();
    }
}

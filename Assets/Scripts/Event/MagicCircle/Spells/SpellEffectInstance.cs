namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果运行时实例基类 — 管理单次生效的生命周期
    /// 与 SpellEffect（SO 配置）分离：实例持有运行时状态（特效实例、协程、事件订阅），
    /// 支持计时结束、提前结束（如玩家出局）与事件停用时统一清理
    /// 生命周期契约：创建即生效 → End() 幂等结束（OnEnd 中做具体清理）
    ///
    /// Tick 驱动：实例构造时自动创建一个内置 MonoBehaviour 驱动器，每帧回调 Tick()；
    /// End() 时自动销毁。效果无论由谁激活（事件 Modifier 或测试热键）都能被正常驱动，
    /// 不依赖外部的协程/Update 驱动
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

        /// <summary>效果结束事件：End() 时触发一次（幂等，不会重复触发），供冷却计时等外部逻辑监听</summary>
        public event System.Action<SpellEffectInstance> OnEnded;

        // 内置 Tick 驱动器（构造时创建，End 时销毁）
        private TickDriver _tickDriver;

        protected SpellEffectInstance(SpellEffectContext context)
        {
            Context = context;

            // 创建内置 Tick 驱动器，挂到目标玩家下（玩家销毁时随之销毁，不留场景残留）；
            // 玩家可能为空（罕见），此时驱动器作为独立物体存在，End 时销毁
            var driverObj = new UnityEngine.GameObject($"{GetType().Name}_TickDriver");
            if (context != null && context.Target != null)
            {
                driverObj.transform.SetParent(context.Target.transform, false);
            }
            _tickDriver = driverObj.AddComponent<TickDriver>();
            _tickDriver.OnTick = Tick;
        }

        /// <summary>
        /// 每帧驱动（由内置驱动器每帧回调）：
        /// 供需要逐帧轮询的效果使用（如飞行音效的按键边沿检测）
        /// 基类为空实现，无需轮询的效果不覆写
        /// </summary>
        public virtual void Tick() { }

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
            OnEnded?.Invoke(this);

            // 销毁 Tick 驱动器
            if (_tickDriver != null)
            {
                _tickDriver.OnTick = null;
                UnityEngine.Object.Destroy(_tickDriver.gameObject);
                _tickDriver = null;
            }
        }

        /// <summary>
        /// 结束时的具体清理：销毁特效、停止协程、退订事件等
        /// </summary>
        protected abstract void OnEnd();

        /// <summary>
        /// Tick 驱动器 — 内置 MonoBehaviour，每帧回调实例的 Tick
        /// 随 SpellEffectInstance 创建而创建、End 而销毁，生命周期完全自治
        /// </summary>
        private sealed class TickDriver : UnityEngine.MonoBehaviour
        {
            public System.Action OnTick;

            private void Update()
            {
                OnTick?.Invoke();
            }
        }
    }
}

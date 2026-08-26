using SuperQQ.Audio;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果抽象基类 — ScriptableObject 资产，纯配置载体（预制体/时长等参数），不持有运行时状态
    /// 契约：Activate(SpellEffectContext) 创建并返回一个运行时效果实例；
    /// 效果激活成功（返回非空实例）时统一播放咒语生效音效（全咒语共用，子类无需关心）
    /// 新增效果（护盾/加速/变大等）= 继承本类实现 OnActivate，策划侧纯配置扩展
    /// </summary>
    public abstract class SpellEffect : ScriptableObject
    {
        [Header("音效")]
        [Tooltip("咒语生效音效：效果激活成功时在目标玩家位置 3D 播放，全咒语共用（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _castSfx = SfxId.SpellCast;

        /// <summary>
        /// 激活效果（模板方法，密封）：委托子类创建运行时实例，成功后统一播放生效音效。
        /// 任何触发路径（法阵语音、测试热键、网络回放等）行为一致
        /// </summary>
        /// <param name="context">效果上下文（触发玩家、协程宿主等运行时引用）</param>
        /// <returns>运行时效果实例；上下文无效（如触发玩家缺失）时返回 null</returns>
        public SpellEffectInstance Activate(SpellEffectContext context)
        {
            SpellEffectInstance instance = OnActivate(context);
            if (instance != null && context != null && context.Target != null)
            {
                PlayCastSfx(context.Target.transform.position);
            }
            return instance;
        }

        /// <summary>
        /// 子类实现：按自身配置创建运行时效果实例并使其生效（不播音效，由基类统一处理）
        /// </summary>
        protected abstract SpellEffectInstance OnActivate(SpellEffectContext context);

        /// <summary>
        /// 播放生效音效（服务端驱动等无运行时实例的模式下由子类手动调用）
        /// </summary>
        protected void PlayCastSfx(UnityEngine.Vector3 position)
        {
            if (_castSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(_castSfx, position);
            }
        }

        /// <summary>
        /// 联机：应用服务端下发的事件3玩家状态（每次快照到达都可能调用，全量重复下发）。
        /// 基类空实现；需要服务端同步的效果（如雷公助我）重写，内部自行保证幂等（边沿触发/去重）
        /// </summary>
        /// <param name="states">player_id -> Event3PlayerState（子类型/剩余时间/检测声音/劈/音量超标玩家列表）</param>
        /// <param name="eventContext">事件运行时上下文（协程宿主/场景根节点）</param>
        public virtual void ApplyServerEvent3States(
            System.Collections.Generic.IDictionary<string, Minigame.Room.V1.Event3PlayerState> states,
            LevelEventContext eventContext)
        {
        }

        /// <summary>
        /// 联机：事件结束时清理服务端同步产生的运行时表现。基类空实现
        /// </summary>
        public virtual void EndServerDrivenEffects()
        {
        }
    }
}

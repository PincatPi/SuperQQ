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
            if (instance != null && _castSfx != SfxId.None && context != null && context.Target != null)
            {
                AudioManager.PlaySfxAt(_castSfx, context.Target.transform.position);
            }
            return instance;
        }

        /// <summary>
        /// 子类实现：按自身配置创建运行时效果实例并使其生效（不播音效，由基类统一处理）
        /// </summary>
        protected abstract SpellEffectInstance OnActivate(SpellEffectContext context);
    }
}

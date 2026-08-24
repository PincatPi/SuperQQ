using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语定义 — ScriptableObject 资产，策划侧的"咒语条目"
    /// 持有咒语文本（与语音识别结果匹配用）、显示名与效果引用
    /// 新增咒语 = 新建本资产并拖入事件的咒语列表，无需改动代码
    /// </summary>
    [CreateAssetMenu(fileName = "SpellDefinition", menuName = "SuperQQ/Event/Spells/Spell Definition")]
    public class SpellDefinition : ScriptableObject
    {
        [Tooltip("咒语文本：语音识别结果与该文本做覆盖率匹配（忽略大小写/空白/标点）")]
        [SerializeField] private string _spellText = "";

        [Tooltip("显示名：GUI/日志展示用；留空则回退为咒语文本")]
        [SerializeField] private string _displayName = "";

        [Tooltip("咒语效果：匹配命中后执行；留空则仅做匹配打印，无玩法效果")]
        [SerializeField] private SpellEffect _effect;

        /// <summary>咒语文本（匹配用）</summary>
        public string SpellText => _spellText;

        /// <summary>显示名：未配置时回退为咒语文本</summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _spellText : _displayName;

        /// <summary>咒语效果（可为 null，表示纯匹配咒语）</summary>
        public SpellEffect Effect => _effect;
    }
}

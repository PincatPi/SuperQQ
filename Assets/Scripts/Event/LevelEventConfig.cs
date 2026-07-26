using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件配置表 — ScriptableObject 资产
    /// 集中管理所有可随机抽取的关卡事件条目
    /// 由 LevelEventAnnouncer 读取，从中随机选择一个事件进行播报
    /// 多关卡可复用同一 Config，也可为不同关卡配置不同事件池
    /// </summary>
    [CreateAssetMenu(fileName = "LevelEventConfig", menuName = "SuperQQ/LevelEventConfig")]
    public class LevelEventConfig : ScriptableObject
    {
        /// <summary>
        /// 所有可随机抽取的事件条目
        /// 在 Inspector 中配置，运行时只读
        /// </summary>
        [SerializeField] private LevelEventEntry[] _events;

        /// <summary>
        /// 所有事件条目（只读视图）
        /// </summary>
        public IReadOnlyList<LevelEventEntry> Events => _events;

        /// <summary>
        /// 事件条目总数
        /// </summary>
        public int EventCount => _events != null ? _events.Length : 0;

        /// <summary>
        /// 按事件类型查找对应条目
        /// </summary>
        /// <param name="eventType">目标事件类型</param>
        /// <returns>匹配的事件条目；未找到时返回默认值</returns>
        public LevelEventEntry FindEntry(LevelEventType eventType)
        {
            if (_events == null)
            {
                return default;
            }

            for (int i = 0; i < _events.Length; i++)
            {
                if (_events[i].EventType == eventType)
                {
                    return _events[i];
                }
            }

            return default;
        }
    }
}

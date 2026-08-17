using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件配置表 — ScriptableObject 资产
    /// 集中管理所有可参与关卡选取的事件条目
    /// 由 LevelEventAnnouncer 读取，经 LevelEventSelector 选定本关事件
    /// 多关卡可复用同一 Config，也可为不同关卡配置不同事件池
    /// </summary>
    [CreateAssetMenu(fileName = "LevelEventConfig", menuName = "SuperQQ/Event/LevelEventConfig")]
    public class LevelEventConfig : ScriptableObject
    {
        /// <summary>
        /// 所有可参与选取的事件条目
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
        /// 按事件类型标识查找对应条目
        /// 供日志展示与联机同步按枚举还原条目使用；事件选取流程不依赖此查找
        /// </summary>
        /// <param name="eventType">目标事件类型标识</param>
        /// <returns>匹配的事件条目；未找到时返回 null</returns>
        public LevelEventEntry FindEntry(LevelEventType eventType)
        {
            if (_events == null)
            {
                return null;
            }

            for (int i = 0; i < _events.Length; i++)
            {
                if (_events[i] != null && _events[i].EventType == eventType)
                {
                    return _events[i];
                }
            }

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器校验：同一事件类型重复配置时输出警告，避免身份标识歧义
        /// </summary>
        private void OnValidate()
        {
            if (_events == null)
            {
                return;
            }

            for (int i = 0; i < _events.Length; i++)
            {
                if (_events[i] == null)
                {
                    continue;
                }

                for (int j = i + 1; j < _events.Length; j++)
                {
                    if (_events[j] != null && _events[j].EventType == _events[i].EventType)
                    {
                        Debug.LogWarning(
                            $"[LevelEventConfig] 事件类型 {_events[i].EventType} 重复配置（第 {i}、{j} 项），请检查。", this);
                    }
                }
            }
        }
#endif
    }
}

using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件条目 — 事件类型与对应弹窗资源的映射数据
    /// 用于 LevelEventConfig 中配置每个事件的展示信息
    /// 作为 ScriptableObject 的可序列化数组成员使用
    /// </summary>
    [System.Serializable]
    public struct LevelEventEntry
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public LevelEventType EventType;

        /// <summary>
        /// 事件说明弹窗的 Prefab
        /// 由 PopupManager 实例化并自动关闭
        /// </summary>
        public GameObject PopupPrefab;

        /// <summary>
        /// 事件的中文显示名称，用于日志和调试
        /// </summary>
        public string DisplayName;
    }
}

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

        /// <summary>
        /// 是否为固定事件
        /// 为 true 时该事件不参与随机抽取，每次进入关卡都会执行
        /// 为 false 时该事件参与随机抽取，每次进入关卡按随机结果决定是否执行
        /// </summary>
        public bool BIsFixed;

        /// <summary>
        /// 事件逻辑修饰符：事件被选中时调用其 Activate 方法启动事件逻辑
        /// 为空时该事件仅有弹窗播报，无逻辑执行
        /// 作为 ScriptableObject 资产，不持有场景引用，场景物体通过 LevelEventContext 传递
        /// </summary>
        public LevelEventModifier Modifier;
    }
}

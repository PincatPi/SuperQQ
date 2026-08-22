using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件条目 — 一个可参与关卡选取的事件的完整配置
    /// 事件的事实身份由 Modifier 资产承载，代码中直接持有条目引用，不按枚举查找
    /// EventType 仅作为日志输出与联机同步的紧凑标识
    /// 作为 LevelEventConfig 的可序列化数组成员使用
    /// </summary>
    [System.Serializable]
    public class LevelEventEntry
    {
        /// <summary>
        /// 事件类型标识，用于日志输出和联机同步
        /// 逻辑层面不作为查找依据（选取结果直接持有条目引用）
        /// </summary>
        public LevelEventType EventType;

        /// <summary>
        /// 事件的中文显示名称，用于日志和调试
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// 事件说明弹窗类型（PopupManager 注册表的索引键，PopupType.None 表示无弹窗）
        /// 由 PopupManager 实例化并自动关闭
        /// </summary>
        public PopupType IntroPopup;

        /// <summary>
        /// 是否为固定事件
        /// 为 true 时该事件不参与随机抽取，每次进入关卡都会执行
        /// 为 false 时该事件参与随机抽取，每次进入关卡按权重决定是否执行
        /// </summary>
        public bool BIsFixed;

        /// <summary>
        /// 随机抽取权重，仅非固定事件生效
        /// 权重越大被抽中的概率越高；为 0 时永远不会被随机抽中
        /// </summary>
        [Min(0f)]
        public float Weight = 1f;

        /// <summary>
        /// 事件逻辑修饰符：事件被选中时调用其 Activate 方法启动事件逻辑
        /// 为空时该事件仅有弹窗播报，无逻辑执行
        /// 作为 ScriptableObject 资产，不持有场景引用，场景物体通过 LevelEventContext 传递
        /// </summary>
        public LevelEventModifier Modifier;
    }
}

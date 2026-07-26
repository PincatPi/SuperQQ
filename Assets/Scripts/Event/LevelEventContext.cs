using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件运行时上下文
    /// 在事件激活时由 LevelEventAnnouncer 创建，传递给 LevelEventModifier
    /// 提供场景相关引用，避免 ScriptableObject 持有过期的场景引用
    /// （场景卸载后 SO 中的场景引用会失效，因此通过 Context 在每次激活时注入）
    /// </summary>
    public class LevelEventContext
    {
        /// <summary>
        /// 协程宿主：Modifier 通过此宿主启动和停止协程
        /// 通常为 LevelEventAnnouncer 自身
        /// </summary>
        public MonoBehaviour CoroutineRunner { get; set; }

        /// <summary>
        /// 场景根节点：用于查找场景中的物体
        /// 可为空，Modifier 可自行通过 FindObjectsByType 查找场景组件
        /// </summary>
        public Transform SceneRoot { get; set; }
    }
}

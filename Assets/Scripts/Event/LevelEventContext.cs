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

        /// <summary>
        /// 本轮随机种子（联机由服务器 GamePhaseSync.random_seed 下发）。
        /// 非 0 时 Modifier 应以此初始化随机源，保证各端事件内部随机过程一致（如陨石落点序列）。
        /// 0 表示未指定，Modifier 回退到自身固定种子/时间种子。
        /// </summary>
        public int RandomSeed { get; set; }

        /// <summary>
        /// 是否等待服务器触发信号再启动事件逻辑（联机模式为 true）。
        /// 为 true 时 Activate 只做准备工作，真正的启动等待 OnServerTrigger 回调
        /// （由服务器 RoomSnapshot.event_triggered 翻牌驱动，触发时刻全端以服务器时钟为锚点对齐）。
        /// </summary>
        public bool WaitForTrigger { get; set; }
    }
}

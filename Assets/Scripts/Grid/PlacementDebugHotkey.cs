using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 摆放调试快捷键 — 场景级调试工具
    /// P：激活场景中所有 PlacementController 的可拖拽状态（EnterDraggableState）
    /// 仅用于编辑器/开发期验证拖拽吸附，正式流程由阶段系统调用接口
    /// </summary>
    public class PlacementDebugHotkey : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                ActivateAll();
            }
        }

        /// <summary>
        /// 激活场景中所有（含未激活物体上的）PlacementController 的可拖拽状态
        /// </summary>
        public void ActivateAll()
        {
            PlacementController[] controllers = FindObjectsOfType<PlacementController>(true);
            foreach (PlacementController pc in controllers)
            {
                pc.EnterDraggableState();
            }
            Debug.Log($"[PlacementDebugHotkey] 已激活 {controllers.Length} 个道具的可拖拽状态");
        }
    }
}

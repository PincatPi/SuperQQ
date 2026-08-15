using UnityEngine;
using SuperQQ.Player;

namespace SuperQQ.Map
{
    /// <summary>
    /// 终点触发器
    /// 玩家触碰终点后触发通关，角色消失并进入通关状态
    /// 挂载到终点 GameObject 上，需将 Collider2D 设为 isTrigger
    /// </summary>
    public class Final : MonoBehaviour
    {
        /// <summary>
        /// 触发器检测：带 Player 标签的对象进入终点区域时调用通关
        /// </summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            PlayerController player = other.GetComponent<PlayerController>();
            if (player == null)
            {
                return;
            }

            // 只有存活状态的玩家可以通关，已死亡（含幽灵）或已通关的不再触发
            if (player.BIsDead || player.BIsGhost || player.BIsFinished)
            {
                return;
            }

            player.PlayerFinish();
        }
    }
}

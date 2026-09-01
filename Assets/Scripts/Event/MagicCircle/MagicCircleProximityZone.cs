using System;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 法阵外围区域触发器 — 挂在法阵 Prefab 的子节点上（与根节点的吟唱触发碰撞体相互独立）
    /// 依赖所在节点上配置的 Trigger 碰撞体（形状与大小即"周围区域"范围，由 Prefab 制作者配置；
    /// 通常比吟唱触发范围大一圈且包含法阵本体；移动玩家自带 Rigidbody2D 会驱动触发回调，自身无需刚体）
    /// 只负责检测"Player 标签物体的进出"并转发为事件，不含任何提示/玩法逻辑
    /// </summary>
    public class MagicCircleProximityZone : MonoBehaviour
    {
        /// <summary>
        /// 玩家进入外围区域事件（参数为进入的玩家控制器）
        /// </summary>
        public event Action<PlayerController> OnPlayerEntered;

        /// <summary>
        /// 玩家离开外围区域事件（参数为离开的玩家控制器）
        /// </summary>
        public event Action<PlayerController> OnPlayerExited;

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = MagicCircle.ResolvePlayer(other);
            if (player != null)
            {
                OnPlayerEntered?.Invoke(player);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = MagicCircle.ResolvePlayer(other);
            if (player != null)
            {
                OnPlayerExited?.Invoke(player);
            }
        }
    }
}

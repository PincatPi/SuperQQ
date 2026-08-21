using System;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 法阵触发器 — 挂在言出法随事件法阵 Prefab 的根节点上
    /// 依赖法阵 Prefab 上配置的 Trigger 碰撞体（形状与大小即法阵触发范围，由 Prefab 制作者配置；
    /// 移动玩家自带 Rigidbody2D 会驱动触发回调，法阵自身无需刚体）
    /// 只负责检测"Player 标签物体的进出"并转发为事件，不含任何提示/玩法逻辑
    /// </summary>
    public class MagicCircle : MonoBehaviour
    {
        // 玩家标签（与场景中玩家化身一致）
        private const string PLAYER_TAG = "Player";

        /// <summary>
        /// 玩家进入法阵触发范围事件（参数为进入的玩家控制器）
        /// </summary>
        public event Action<PlayerController> OnPlayerEntered;

        /// <summary>
        /// 玩家离开法阵触发范围事件（参数为离开的玩家控制器）
        /// </summary>
        public event Action<PlayerController> OnPlayerExited;

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = ResolvePlayer(other);
            if (player != null)
            {
                OnPlayerEntered?.Invoke(player);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = ResolvePlayer(other);
            if (player != null)
            {
                OnPlayerExited?.Invoke(player);
            }
        }

        /// <summary>
        /// 从触发碰撞体解析玩家控制器：仅接受 Player 标签物体，碰撞体可在玩家根节点或其子级上
        /// </summary>
        private static PlayerController ResolvePlayer(Collider2D other)
        {
            if (other == null || !other.CompareTag(PLAYER_TAG))
            {
                return null;
            }
            return other.GetComponentInParent<PlayerController>();
        }
    }
}

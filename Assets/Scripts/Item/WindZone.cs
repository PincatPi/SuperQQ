using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 风力区域 — 持续吹风，推动其中的玩家向一个方向位移
    /// 挂在 HitZones 下的 Trigger 物体上（WindZone），区域即吹风范围
    /// 吹风方向取 transform.right：排气扇旋转时自动联动，无需额外配置
    /// 对地面和空中玩家都生效：可助推跳远，也可以把人吹下悬崖
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class WindZone : MonoBehaviour
    {
        [Header("风力")]
        [Tooltip("对玩家的推动加速度（单位/秒²）")]
        [SerializeField] private float windForce = 20f;

        // 区域内玩家计数（多风区重叠时直接累加风力，离开时按进入次数抵消）
        private readonly Dictionary<PlayerController, int> _players = new Dictionary<PlayerController, int>();

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[WindZone] 所在碰撞体应为 Trigger", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }
            _players.TryGetValue(player, out int count);
            player.AddWindForce(transform.right * windForce);
            _players[player] = count + 1;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }
            if (!_players.TryGetValue(player, out int count))
            {
                return;
            }
            player.AddWindForce(-transform.right * windForce);
            if (count <= 1)
            {
                _players.Remove(player);
            }
            else
            {
                _players[player] = count - 1;
            }
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<PlayerController, int> pair in _players)
            {
                if (pair.Key != null)
                {
                    pair.Key.AddWindForce(-transform.right * (windForce * pair.Value));
                }
            }
            _players.Clear();
        }
    }
}

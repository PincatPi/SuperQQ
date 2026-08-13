using SuperQQ.Player;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 危险判定区 — 玩家接触即注定死亡：alive → dying(延迟) → ghost
    /// 挂在 HitZones 下的 Trigger 物体上（如玻璃球的圆形判定区）
    /// 一旦触碰死亡不可撤销，延迟仅为 dying 表现时长；离开判定区不会取消
    /// 适用道具：玻璃球、玻璃刺、剪刀、电击枪等接触即杀类陷阱
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class KillZone : MonoBehaviour
    {
        [Header("致死流程")]
        [Tooltip("死亡延迟（秒）： dying 阶段时长，到期后进入 ghost")]
        [SerializeField, Range(0f, 2f)] private float killDelay = 0.35f;

        /// <summary>正在 dying 中的玩家及其到期时间（Time.time + killDelay）</summary>
        private readonly Dictionary<PlayerController, float> _dyingPlayers = new Dictionary<PlayerController, float>();

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[KillZone] 所在碰撞体应为 Trigger", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.BIsDead)
            {
                return;
            }

            if (!_dyingPlayers.ContainsKey(player))
            {
                _dyingPlayers[player] = Time.time + killDelay;
            }
        }

        // 不提供 OnTriggerExit2D 取消逻辑：触碰后死亡已确定，离开不豁免

        private void Update()
        {
            if (_dyingPlayers.Count == 0)
            {
                return;
            }

            _expiredBuffer.Clear();
            foreach (KeyValuePair<PlayerController, float> pair in _dyingPlayers)
            {
                PlayerController player = pair.Key;
                // 玩家已死亡（被其他方式杀死）或组件失效：无需再处理
                if (player == null || player.BIsDead)
                {
                    continue;
                }
                if (Time.time >= pair.Value)
                {
                    _expiredBuffer.Add(player);
                }
            }

            foreach (PlayerController player in _expiredBuffer)
            {
                _dyingPlayers.Remove(player);
                player.PlayerDie();
            }
        }

        private void OnDisable()
        {
            _dyingPlayers.Clear();
        }

        private static readonly List<PlayerController> _expiredBuffer = new List<PlayerController>();
    }
}

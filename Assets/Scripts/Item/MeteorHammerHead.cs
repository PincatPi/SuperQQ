using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 流星锤锤头 — 挂在 Arm 末端的 Trigger 碰撞体上
    /// 命中玩家即死亡，并沿锤头运动方向弹飞（弹飞仅作死亡表现）
    /// 只有锤头挂此脚本；链条与底座不挂（安全区）
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class MeteorHammerHead : MonoBehaviour
    {
        private MeteorHammer hammer;

        private void Awake()
        {
            hammer = GetComponentInParent<MeteorHammer>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.BIsDead || player.BIsGhost)
            {
                return;
            }

            if (hammer == null)
            {
                player.PlayerDie();
                return;
            }

            // 弹飞死亡：沿锤头运动方向击飞（死亡表现），短暂延迟后进入幽灵状态
            Vector2 knockback = hammer.HeadVelocityDir.normalized * hammer.KnockbackSpeed
                              + Vector2.up * (hammer.KnockbackSpeed * 0.5f);
            player.PlayerKnockbackDie(knockback, hammer.GhostDelay);
        }
    }
}

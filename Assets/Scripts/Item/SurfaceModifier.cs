using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 表面属性修改器 — 站在其上时改变玩家移动速度
    /// 挂在 HitZones 下的 Trigger 物体上（如 StandZone），
    /// Trigger 应略高于平台顶面，玩家踏入时生效、离开时恢复
    /// 适用道具：黄油块（减速0.5）、肥皂（可扩展为无摩擦）
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SurfaceModifier : MonoBehaviour
    {
        [Header("表面效果")]
        [Tooltip("速度倍率：0.5=减速一半，1=无影响")]
        [SerializeField, Range(0f, 2f)] private float speedMultiplier = 0.5f;

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[SurfaceModifier] 所在碰撞体应为 Trigger", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.SetSpeedMultiplier(speedMultiplier);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ResetSpeedMultiplier();
            }
        }
    }
}

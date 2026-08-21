using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 肥皂表面 — 站在其上时玩家完全无摩擦：松手不减速，有输入也无法驱动（滑行不可控）
    /// 挂在 HitZones 下的 Trigger 物体上（StandZone），
    /// Trigger 应略高于平台顶面，玩家踏入时生效、离开时恢复
    /// 与 SurfaceModifier 的区别：不乘算速度倍率，而是把加/减速率压到 0，
    /// 踩上瞬间的初速度原样保留 → 可利用助跑滑行加速冲远，也可能滑进陷阱
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SoapSurface : MonoBehaviour
    {
        [Header("滑行参数")]
        [Tooltip("滑行减阻：0=完全无摩擦（匀速滑行），略微增大可让滑行缓慢衰减")]
        [SerializeField, Min(0f)] private float slideDrag;

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[SoapSurface] 所在碰撞体应为 Trigger", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && player.BAffectedByItems)   // 死亡过渡/幽灵不受无摩擦影响
            {
                player.SetFrictionless(true, slideDrag);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.SetFrictionless(false);
            }
        }
    }
}

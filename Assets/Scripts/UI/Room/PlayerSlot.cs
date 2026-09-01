using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 房间玩家槽位（UI/Room 场景，挂在 PlayerSlot 物体上）。
    /// 职责：本槽位是本地客户端自身时，将槽位自身的 Image 高亮为可配置颜色。
    ///
    /// 驱动方式：由外部（RoomView.SetSlotPlayer → UIRoomController 按 playerId 比对）
    /// 调用 SetLocalHighlight 告知身份，本脚本不自行判定，可与 RoomSlotView 共存。
    ///
    /// Editor 接线：
    ///   slotImage ← PlayerSlot 自身的 Image；留空则自动取本物体上的 Image
    /// </summary>
    public class PlayerSlot : MonoBehaviour
    {
        [Header("槽位背景 Image（留空则自动取本物体上的 Image）")]
        [SerializeField] private Image slotImage;

        [Header("本地玩家高亮颜色（本槽位是自己时生效）")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.914f, 0.780f, 1f); // FFE9C7

        // Image 初始颜色（非高亮时恢复用）
        private Color _normalColor;
        private bool _highlighted;

        private void Awake()
        {
            if (slotImage == null)
            {
                slotImage = GetComponent<Image>();
            }
            if (slotImage != null)
            {
                _normalColor = slotImage.color;
            }
        }

        /// <summary>设置本槽位是否为本地玩家：是则 Image 切高亮色，否则恢复初始颜色（状态未变时不重复赋值）</summary>
        public void SetLocalHighlight(bool isLocal)
        {
            if (slotImage == null || isLocal == _highlighted) return;
            _highlighted = isLocal;
            slotImage.color = isLocal ? highlightColor : _normalColor;
        }
    }
}

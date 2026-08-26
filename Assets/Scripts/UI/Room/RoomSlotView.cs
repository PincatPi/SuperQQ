using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 房间玩家槽位视图（UI/Room 场景 Slot_1~4，挂在槽位根物体上）。
    /// 纯展示：玩家昵称 + 准备状态文本；槽位整体显隐由 RoomView 控制。
    ///
    /// 子物体引用留空时按名字自动查找：
    ///   playerNameLabel  ← "PlayerName"
    ///   readyStateLabel  ← "BtnReady/Label"（槽位内的 BtnReady 仅作状态展示，点击会被禁用）
    /// 无需手动挂组件：RoomView 驱动槽位时会自动 AddComponent。
    /// </summary>
    public class RoomSlotView : MonoBehaviour
    {
        [Header("子物体引用（留空则按名字自动查找）")]
        [SerializeField] private TMP_Text playerNameLabel;
        [SerializeField] private TMP_Text readyStateLabel;

        [Header("准备状态文案")]
        [SerializeField] private string readyText = "已准备";
        [SerializeField] private string notReadyText = "未准备";

        [Header("BtnReady 背景颜色")]
        [SerializeField] private Color readyBgColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        [SerializeField] private Color notReadyBgColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        private Image btnReadyBg;

        private void Awake()
        {
            if (playerNameLabel == null)
            {
                Transform t = transform.Find("PlayerName");
                if (t != null) playerNameLabel = t.GetComponent<TMP_Text>();
            }

            Transform btn = transform.Find("BtnReady");
            if (btn != null)
            {
                if (readyStateLabel == null)
                {
                    Transform label = btn.Find("Label");
                    if (label != null) readyStateLabel = label.GetComponent<TMP_Text>();
                }
                btnReadyBg = btn.GetComponent<Image>();
            }

            // 槽位内的 BtnReady 仅作状态展示，禁用点击交互
            if (btn != null)
            {
                Button button = btn.GetComponent<Button>();
                if (button != null) button.interactable = false;
            }
        }

        /// <summary>填充玩家信息：昵称 + 准备状态文本，并按准备状态切换 BtnReady 背景颜色</summary>
        public void SetPlayer(string playerName, bool isReady)
        {
            if (playerNameLabel != null) playerNameLabel.text = playerName;
            if (readyStateLabel != null) readyStateLabel.text = isReady ? readyText : notReadyText;
            if (btnReadyBg != null) btnReadyBg.color = isReady ? readyBgColor : notReadyBgColor;
        }
    }
}

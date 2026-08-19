using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 房间等待界面视图（UI/Room 场景）— 纯展示层。
    /// 只负责把数据渲染到场景 UI，不含任何网络/业务逻辑；
    /// 由控制器（UIRoomController）单向驱动，按钮点击通过 ReadyClicked 事件上抛。
    ///
    /// Editor 接线：
    ///   roomCodeText     ← Canvas/TopArea/TopRight/RoomCodeText
    ///   progressText     ← Canvas/BottomArea/ProgressPanel/ProgressText
    ///   barFill          ← ProgressPanel 下的 BarFill（Image Type 需设为 Filled / Horizontal）
    ///   readyButton      ← Canvas/BottomArea/BtnReady
    ///   readyButtonLabel ← BtnReady 下的 Label（TMP）
    /// </summary>
    public class RoomView : MonoBehaviour
    {
        [Header("房间码")]
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private string roomCodePrefix = "房间码 ";

        [Header("准备进度")]
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image barFill;
        [SerializeField] private string progressFormat = "{0} / {1} 已准备";

        [Header("准备按钮")]
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private string notReadyText = "点击准备";
        [SerializeField] private string readyText = "取消准备";

        /// <summary>准备按钮被点击（由控制器订阅，处理网络逻辑）</summary>
        public event Action ReadyClicked;

        private void Awake()
        {
            if (readyButton != null)
            {
                readyButton.onClick.AddListener(OnReadyButtonClicked);
            }
        }

        private void OnDestroy()
        {
            if (readyButton != null)
            {
                readyButton.onClick.RemoveListener(OnReadyButtonClicked);
            }
        }

        /// <summary>显示房间码</summary>
        public void SetRoomCode(string roomCode)
        {
            if (roomCodeText != null) roomCodeText.text = roomCodePrefix + roomCode;
        }

        /// <summary>刷新准备进度：文本 "n / m 已准备" + 进度条填充比例</summary>
        public void SetReadyProgress(int readyCount, int totalCount)
        {
            if (progressText != null)
            {
                progressText.text = string.Format(progressFormat, readyCount, totalCount);
            }
            if (barFill != null)
            {
                barFill.fillAmount = totalCount > 0 ? Mathf.Clamp01((float)readyCount / totalCount) : 0f;
            }
        }

        /// <summary>刷新准备按钮文案（未准备：点击准备 / 已准备：取消准备）</summary>
        public void SetSelfReady(bool isReady)
        {
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = isReady ? readyText : notReadyText;
            }
        }

        /// <summary>设置准备按钮是否可交互（未进房时禁用）</summary>
        public void SetReadyInteractable(bool interactable)
        {
            if (readyButton != null) readyButton.interactable = interactable;
        }

        private void OnReadyButtonClicked()
        {
            ReadyClicked?.Invoke();
        }
    }
}

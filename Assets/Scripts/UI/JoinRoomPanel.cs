using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>加入房间 UI 的通用接口（美术面板与代码动态弹窗共用）</summary>
    public interface IJoinRoomUI
    {
        void SetStatus(string message, bool isError = false);
        void SetConfirmInteractable(bool interactable);
        void ClosePopup();
    }

    /// <summary>
    /// 加入房间面板（美术版）：挂在 Lobby 场景中搭好的加入房间 Panel 上。
    /// 逻辑与 JoinRoomPopup 一致：输入房间码 → 确认回调；错误信息显示在面板内。
    /// 由 LobbyController 持有引用，通过 Open/ClosePopup 控制显隐。
    /// </summary>
    public class JoinRoomPanel : MonoBehaviour, IJoinRoomUI
    {
        [Header("UI 引用")]
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        private Action<string> _onConfirm;
        private Action _onCancel;
        private bool _bound;
        private bool _destroyOnClose;

        /// <summary>Prefab 方式打开：在 parent（一般为 Canvas）下实例化面板</summary>
        public static JoinRoomPanel Show(JoinRoomPanel prefab, Transform parent, Action<string> onConfirm, Action onCancel = null)
        {
            JoinRoomPanel panel = Instantiate(prefab, parent, false);
            panel._destroyOnClose = true;
            panel.Open(onConfirm, onCancel);
            return panel;
        }

        /// <summary>打开面板并绑定本次的确认/取消回调</summary>
        public void Open(Action<string> onConfirm, Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            gameObject.SetActive(true);
            BindEvents();
            SetStatus("");

            // 清空上次输入并聚焦输入框
            if (roomCodeInput != null)
            {
                roomCodeInput.text = "";
                roomCodeInput.Select();
                roomCodeInput.ActivateInputField();
            }
        }

        /// <summary>设置面板内提示文字（isError=true 红色，否则灰色）</summary>
        public void SetStatus(string message, bool isError = false)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = isError ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.4f, 0.4f, 0.4f);
        }

        /// <summary>设置确认按钮是否可交互（请求发送后临时禁用防连点）</summary>
        public void SetConfirmInteractable(bool interactable)
        {
            if (confirmButton != null) confirmButton.interactable = interactable;
        }

        /// <summary>关闭面板：实例化的副本直接销毁，场景对象则隐藏</summary>
        public void ClosePopup()
        {
            if (_destroyOnClose)
            {
                Destroy(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ==================== 内部逻辑 ====================

        private void BindEvents()
        {
            if (_bound) return;
            _bound = true;

            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);

            // 输入框回车等同点击确认
            if (roomCodeInput != null) roomCodeInput.onSubmit.AddListener(_ => OnConfirmClicked());
        }

        private void OnConfirmClicked()
        {
            string roomCode = roomCodeInput != null ? (roomCodeInput.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(roomCode))
            {
                SetStatus("请输入房间码", true);
                return;
            }
            _onConfirm?.Invoke(roomCode);
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            ClosePopup();
        }
    }
}

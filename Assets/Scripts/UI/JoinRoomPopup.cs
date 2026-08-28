using System;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 加入房间弹窗（纯代码动态构建 uGUI，无需预制体/场景配置）。
    /// 输入房间码 → 确认回调；错误信息可显示在弹窗内。
    ///
    /// 用法：
    ///   JoinRoomPopup popup = JoinRoomPopup.Show(canvas.transform, OnConfirm, OnCancel);
    ///   popup.SetStatus("房间不存在", true);
    ///   popup.ClosePopup();
    /// </summary>
    public class JoinRoomPopup : MonoBehaviour, IJoinRoomUI
    {
        private InputField _roomCodeInput;
        private Button _confirmButton;
        private Text _statusText;

        private Action<string> _onConfirm;
        private Action _onCancel;

        /// <summary>在指定父级（通常为 Canvas）下弹出房间码输入框</summary>
        public static JoinRoomPopup Show(Transform parent, Action<string> onConfirm, Action onCancel = null)
        {
            if (parent == null)
            {
                Debug.LogError("[JoinRoomPopup] 父级为空，无法创建弹窗");
                return null;
            }

            var root = new GameObject("JoinRoomPopup", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Stretch((RectTransform)root.transform);

            JoinRoomPopup popup = root.AddComponent<JoinRoomPopup>();
            popup._onConfirm = onConfirm;
            popup._onCancel = onCancel;
            popup.Build();
            return popup;
        }

        /// <summary>设置弹窗底部提示文字（isError=true 红色，否则灰色）</summary>
        public void SetStatus(string message, bool isError = false)
        {
            if (_statusText == null) return;
            _statusText.text = message;
            _statusText.color = isError ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.4f, 0.4f, 0.4f);
        }

        /// <summary>设置确认按钮是否可交互（请求发送后临时禁用防连点）</summary>
        public void SetConfirmInteractable(bool interactable)
        {
            if (_confirmButton != null) _confirmButton.interactable = interactable;
        }

        /// <summary>关闭并销毁弹窗</summary>
        public void ClosePopup()
        {
            Destroy(gameObject);
        }

        private void OnConfirmClicked()
        {
            string roomCode = _roomCodeInput != null ? _roomCodeInput.text.Trim() : "";
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

        // ==================== 动态构建 UI ====================

        private void Build()
        {
            Font font = LoadBuiltinFont();

            // 全屏遮罩
            Image mask = gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.6f);

            // 中央面板
            GameObject panel = CreateUIObject("Panel", transform);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 340f);

            // 标题
            Text title = CreateText(panel.transform, "Title", font, 34, FontStyle.Bold, Color.white);
            title.text = "加入房间";
            RectTransform titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(-40f, 48f);

            // 房间码输入框
            _roomCodeInput = CreateInputField(panel.transform, "RoomCodeInput", font, "请输入房间码");
            RectTransform inputRect = (RectTransform)_roomCodeInput.transform;
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.anchoredPosition = new Vector2(0f, -104f);
            inputRect.sizeDelta = new Vector2(-80f, 56f);
            _roomCodeInput.onSubmit.AddListener(_ => OnConfirmClicked());

            // 提示文字
            _statusText = CreateText(panel.transform, "StatusText", font, 20, FontStyle.Normal, new Color(0.4f, 0.4f, 0.4f));
            _statusText.text = "";
            RectTransform statusRect = (RectTransform)_statusText.transform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -176f);
            statusRect.sizeDelta = new Vector2(-80f, 32f);

            // 按钮行：加入 / 取消
            _confirmButton = CreateButton(panel.transform, "ConfirmButton", font, "加 入",
                new Color(0.25f, 0.55f, 0.95f), OnConfirmClicked);
            RectTransform confirmRect = (RectTransform)_confirmButton.transform;
            confirmRect.anchorMin = new Vector2(0f, 0f);
            confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(-12f, 32f);
            confirmRect.sizeDelta = new Vector2(-30f, 60f);

            Button cancelButton = CreateButton(panel.transform, "CancelButton", font, "取 消",
                new Color(0.35f, 0.37f, 0.42f), OnCancelClicked);
            RectTransform cancelRect = (RectTransform)cancelButton.transform;
            cancelRect.anchorMin = new Vector2(0.5f, 0f);
            cancelRect.anchorMax = new Vector2(1f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(12f, 32f);
            cancelRect.sizeDelta = new Vector2(-30f, 60f);

            if (_roomCodeInput != null)
            {
                _roomCodeInput.Select();
                _roomCodeInput.ActivateInputField();
            }
        }

        private static InputField CreateInputField(Transform parent, string name, Font font, string placeholder)
        {
            GameObject obj = CreateUIObject(name, parent);
            Image bg = obj.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.95f, 0.95f);

            InputField input = obj.AddComponent<InputField>();

            Text text = CreateText(obj.transform, "Text", font, 24, FontStyle.Normal, Color.black);
            text.supportRichText = false;
            Stretch((RectTransform)text.transform, 14f, 2f);

            Text placeholderText = CreateText(obj.transform, "Placeholder", font, 24, FontStyle.Italic, new Color(0.55f, 0.55f, 0.55f));
            placeholderText.text = placeholder;
            Stretch((RectTransform)placeholderText.transform, 14f, 2f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
        }

        private static Button CreateButton(Transform parent, string name, Font font, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject obj = CreateUIObject(name, parent);
            Image bg = obj.AddComponent<Image>();
            bg.color = color;

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            Text text = CreateText(obj.transform, "Text", font, 26, FontStyle.Bold, Color.white);
            text.text = label;
            Stretch((RectTransform)text.transform);

            return button;
        }

        private static Text CreateText(Transform parent, string name, Font font, int fontSize, FontStyle style, Color color)
        {
            GameObject obj = CreateUIObject(name, parent);
            Text text = obj.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private static void Stretch(RectTransform rect, float offsetX = 0f, float offsetY = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(offsetX, offsetY);
            rect.offsetMax = new Vector2(-offsetX, -offsetY);
        }

        private static Font LoadBuiltinFont()
        {
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            return font;
        }
    }
}

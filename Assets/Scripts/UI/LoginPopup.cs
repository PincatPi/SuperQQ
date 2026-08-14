using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 账号密码登录弹窗（纯代码动态构建 uGUI，无需预制体/场景配置）。
    ///
    /// 用法：
    ///   LoginPopup popup = LoginPopup.Show(canvas.transform, OnConfirm, OnCancel);
    ///   popup.SetStatus("账号或密码错误", true);
    ///   popup.ClosePopup();
    ///
    /// 确认回调参数为 (username, password)；取消/关闭回调无参。
    /// 弹窗自带全屏遮罩，打开期间阻挡对背后 UI 的点击。
    /// </summary>
    public class LoginPopup : MonoBehaviour
    {
        private InputField _usernameInput;
        private InputField _passwordInput;
        private Button _confirmButton;
        private Text _statusText;

        private Action<string, string> _onConfirm;
        private Action _onCancel;

        /// <summary>在指定父级（通常为 Canvas）下弹出登录框</summary>
        public static LoginPopup Show(Transform parent, Action<string, string> onConfirm, Action onCancel = null)
        {
            if (parent == null)
            {
                Debug.LogError("[LoginPopup] 父级为空，无法创建弹窗");
                return null;
            }

            var root = new GameObject("LoginPopup", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Stretch((RectTransform)root.transform);

            LoginPopup popup = root.AddComponent<LoginPopup>();
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

        /// <summary>设置确认按钮是否可交互（登录请求发送后可临时禁用防连点）</summary>
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
            string username = _usernameInput != null ? _usernameInput.text.Trim() : "";
            string password = _passwordInput != null ? _passwordInput.text : "";

            if (string.IsNullOrEmpty(username))
            {
                SetStatus("请输入账号", true);
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                SetStatus("请输入密码", true);
                return;
            }

            _onConfirm?.Invoke(username, password);
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

            // 全屏遮罩：半透明黑底，阻挡背后点击；点击遮罩不关闭（防止误触丢输入）
            Image mask = gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.6f);

            // 中央面板
            GameObject panel = CreateUIObject("Panel", transform);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 420f);

            // 标题
            Text title = CreateText(panel.transform, "Title", font, 34, FontStyle.Bold, Color.white);
            title.text = "账号登录";
            RectTransform titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(-40f, 48f);

            // 账号输入框
            _usernameInput = CreateInputField(panel.transform, "UsernameInput", font, "请输入账号", false);
            RectTransform userRect = (RectTransform)_usernameInput.transform;
            userRect.anchorMin = new Vector2(0f, 1f);
            userRect.anchorMax = new Vector2(1f, 1f);
            userRect.pivot = new Vector2(0.5f, 1f);
            userRect.anchoredPosition = new Vector2(0f, -100f);
            userRect.sizeDelta = new Vector2(-80f, 56f);

            // 密码输入框
            _passwordInput = CreateInputField(panel.transform, "PasswordInput", font, "请输入密码", true);
            RectTransform pwdRect = (RectTransform)_passwordInput.transform;
            pwdRect.anchorMin = new Vector2(0f, 1f);
            pwdRect.anchorMax = new Vector2(1f, 1f);
            pwdRect.pivot = new Vector2(0.5f, 1f);
            pwdRect.anchoredPosition = new Vector2(0f, -176f);
            pwdRect.sizeDelta = new Vector2(-80f, 56f);
            // 密码框回车直接提交
            _passwordInput.onSubmit.AddListener(_ => OnConfirmClicked());

            // 提示文字
            _statusText = CreateText(panel.transform, "StatusText", font, 20, FontStyle.Normal, new Color(0.4f, 0.4f, 0.4f));
            _statusText.text = "";
            RectTransform statusRect = (RectTransform)_statusText.transform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -248f);
            statusRect.sizeDelta = new Vector2(-80f, 32f);

            // 按钮行：登录 / 取消
            _confirmButton = CreateButton(panel.transform, "ConfirmButton", font, "登 录",
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

            // 默认聚焦账号框
            if (_usernameInput != null)
            {
                _usernameInput.Select();
                _usernameInput.ActivateInputField();
            }
        }

        private static InputField CreateInputField(Transform parent, string name, Font font, string placeholder, bool isPassword)
        {
            GameObject obj = CreateUIObject(name, parent);
            Image bg = obj.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.95f, 0.95f);

            InputField input = obj.AddComponent<InputField>();

            Text text = CreateText(obj.transform, "Text", font, 24, FontStyle.Normal, Color.black);
            text.supportRichText = false;
            RectTransform textRect = (RectTransform)text.transform;
            Stretch(textRect, 14f, 2f);

            Text placeholderText = CreateText(obj.transform, "Placeholder", font, 24, FontStyle.Italic, new Color(0.55f, 0.55f, 0.55f));
            placeholderText.text = placeholder;
            RectTransform placeholderRect = (RectTransform)placeholderText.transform;
            Stretch(placeholderRect, 14f, 2f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            if (isPassword)
            {
                input.contentType = InputField.ContentType.Password;
            }

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

        /// <summary>内置动态字体：优先 LegacyRuntime（2022+），回退 Arial（旧版编辑器）</summary>
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

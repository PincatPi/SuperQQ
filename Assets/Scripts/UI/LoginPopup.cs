using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace SuperQQ.UI
{
    /// <summary>
    /// 账号密码登录弹窗。支持两种构建方式：
    /// 1) Prefab 方式（推荐）：预先在场景里搭好并保存为 Prefab，字段拖引用。
    ///    LoginPopup.Show(prefab, canvas, onConfirm, onCancel);
    /// 2) 纯代码兜底：找不到 Prefab 时代码动态构建（旧逻辑保留）。
    ///    LoginPopup.Show(canvas, onConfirm, onCancel);
    /// </summary>
    public class LoginPopup : MonoBehaviour
    {
        [Header("Prefab 模式下需拖入的 UI 引用（代码兜底时自动创建）")]
        [SerializeField] private InputField _usernameInput;
        [SerializeField] private InputField _passwordInput;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private TMP_Text _statusTextTMP;
        [SerializeField] private TMP_InputField _usernameInputTMP;
        [SerializeField] private TMP_InputField _passwordInputTMP;

        private Action<string, string> _onConfirm;
        private Action _onCancel;

        // ==================== 对外接口 ====================

        /// <summary>Prefab 方式弹出（推荐）</summary>
        public static LoginPopup Show(LoginPopup prefab, Transform parent, Action<string, string> onConfirm, Action onCancel = null)
        {
            if (prefab == null) return Show(parent, onConfirm, onCancel);
            if (parent == null) { Debug.LogError("[LoginPopup] parent is null"); return null; }
            LoginPopup popup = Instantiate(prefab, parent, false);
            popup.name = prefab.name;
            popup._onConfirm = onConfirm;
            popup._onCancel = onCancel;
            popup.BindPrefabEvents();
            return popup;
        }

        /// <summary>纯代码兜底方式</summary>
        public static LoginPopup Show(Transform parent, Action<string, string> onConfirm, Action onCancel = null)
        {
            if (parent == null) { Debug.LogError("[LoginPopup] parent is null"); return null; }
            var root = new GameObject("LoginPopup", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Stretch((RectTransform)root.transform);
            LoginPopup popup = root.AddComponent<LoginPopup>();
            popup._onConfirm = onConfirm;
            popup._onCancel = onCancel;
            popup.Build();
            return popup;
        }

        public void SetStatus(string message, bool isError = false)
        {
            Color c = isError ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.4f, 0.4f, 0.4f);
            if (_statusText != null) { _statusText.text = message; _statusText.color = c; }
            if (_statusTextTMP != null) { _statusTextTMP.text = message; _statusTextTMP.color = c; }
        }

        public void SetConfirmInteractable(bool interactable)
        {
            if (_confirmButton != null) _confirmButton.interactable = interactable;
        }

        public void ClosePopup()
        {
            Destroy(gameObject);
        }

        // ==================== 内部逻辑 ====================

        private string GetUsername()
        {
            if (_usernameInputTMP != null) return (_usernameInputTMP.text ?? "").Trim();
            if (_usernameInput != null) return (_usernameInput.text ?? "").Trim();
            return "";
        }

        private string GetPassword()
        {
            if (_passwordInputTMP != null) return _passwordInputTMP.text ?? "";
            if (_passwordInput != null) return _passwordInput.text ?? "";
            return "";
        }

        private void BindPrefabEvents()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(OnConfirmClicked);
                _confirmButton.onClick.AddListener(OnConfirmClicked);
            }
            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(OnCancelClicked);
                _cancelButton.onClick.AddListener(OnCancelClicked);
            }
            if (_passwordInputTMP != null)
                _passwordInputTMP.onSubmit.AddListener(_ => OnConfirmClicked());
            else if (_passwordInput != null)
                _passwordInput.onSubmit.AddListener(_ => OnConfirmClicked());

            if (_statusText != null) _statusText.text = "";
            if (_statusTextTMP != null) _statusTextTMP.text = "";

            if (_usernameInputTMP != null) { _usernameInputTMP.Select(); _usernameInputTMP.ActivateInputField(); }
            else if (_usernameInput != null) { _usernameInput.Select(); _usernameInput.ActivateInputField(); }
        }

        private void OnConfirmClicked()
        {
            string username = GetUsername();
            string password = GetPassword();
            if (string.IsNullOrEmpty(username)) { SetStatus("请输入账号", true); return; }
            if (string.IsNullOrEmpty(password)) { SetStatus("请输入密码", true); return; }
            _onConfirm?.Invoke(username, password);
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            ClosePopup();
        }

        // ==================== 纯代码构建（兜底） ====================

        private void Build()
        {
            Font font = LoadBuiltinFont();

            Image mask = gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.6f);

            GameObject panel = CreateUIObject("Panel", transform);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 420f);

            Text title = CreateText(panel.transform, "Title", font, 34, FontStyle.Bold, Color.white);
            title.text = "账号登录";
            RectTransform titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f); titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(-40f, 48f);

            _usernameInput = CreateInputField(panel.transform, "UsernameInput", font, "请输入账号", false);
            RectTransform userRect = (RectTransform)_usernameInput.transform;
            userRect.anchorMin = new Vector2(0f, 1f); userRect.anchorMax = new Vector2(1f, 1f);
            userRect.pivot = new Vector2(0.5f, 1f);
            userRect.anchoredPosition = new Vector2(0f, -100f);
            userRect.sizeDelta = new Vector2(-80f, 56f);

            _passwordInput = CreateInputField(panel.transform, "PasswordInput", font, "请输入密码", true);
            RectTransform pwdRect = (RectTransform)_passwordInput.transform;
            pwdRect.anchorMin = new Vector2(0f, 1f); pwdRect.anchorMax = new Vector2(1f, 1f);
            pwdRect.pivot = new Vector2(0.5f, 1f);
            pwdRect.anchoredPosition = new Vector2(0f, -176f);
            pwdRect.sizeDelta = new Vector2(-80f, 56f);
            _passwordInput.onSubmit.AddListener(_ => OnConfirmClicked());

            _statusText = CreateText(panel.transform, "StatusText", font, 20, FontStyle.Normal, new Color(0.4f, 0.4f, 0.4f));
            _statusText.text = "";
            RectTransform statusRect = (RectTransform)_statusText.transform;
            statusRect.anchorMin = new Vector2(0f, 1f); statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -248f);
            statusRect.sizeDelta = new Vector2(-80f, 32f);

            _confirmButton = CreateButton(panel.transform, "ConfirmButton", font, "登 录",
                new Color(0.25f, 0.55f, 0.95f), OnConfirmClicked);
            RectTransform confirmRect = (RectTransform)_confirmButton.transform;
            confirmRect.anchorMin = new Vector2(0f, 0f); confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(-12f, 32f);
            confirmRect.sizeDelta = new Vector2(-30f, 60f);

            Button cancelButton = CreateButton(panel.transform, "CancelButton", font, "取 消",
                new Color(0.35f, 0.37f, 0.42f), OnCancelClicked);
            _cancelButton = cancelButton;
            RectTransform cancelRect = (RectTransform)cancelButton.transform;
            cancelRect.anchorMin = new Vector2(0.5f, 0f); cancelRect.anchorMax = new Vector2(1f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(12f, 32f);
            cancelRect.sizeDelta = new Vector2(-30f, 60f);

            if (_usernameInput != null) { _usernameInput.Select(); _usernameInput.ActivateInputField(); }
        }

        private static InputField CreateInputField(Transform parent, string name, Font font, string placeholder, bool isPassword)
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
            if (isPassword) input.contentType = InputField.ContentType.Password;
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
            text.font = font; text.fontSize = fontSize; text.fontStyle = style; text.color = color;
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
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(offsetX, offsetY);
            rect.offsetMax = new Vector2(-offsetX, -offsetY);
        }

        private static Font LoadBuiltinFont()
        {
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            return font;
        }
    }
}

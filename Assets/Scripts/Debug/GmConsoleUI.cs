using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperQQ.Debugging
{
    /// <summary>
    /// GM 调试控制台（纯代码动态构建 uGUI，无需预制体/场景配置）。
    /// 由 GmCommandService.AutoCreate 与之一同创建，DontDestroyOnLoad 常驻。
    ///
    /// 交互：
    ///   - 反引号 ` （Tab 上方）或左上角 GM 按钮开合控制台；
    ///   - 回车提交指令，↑/↓ 翻阅历史指令，Esc 关闭；
    ///   - 打开期间本地玩家输入切换为 NullPlayerInput（避免打字驱动角色），关闭自动还原。
    /// </summary>
    public class GmConsoleUI : MonoBehaviour
    {
        private const int MaxLines = 60;

        private GameObject _panel;
        private GameObject _blocker;
        private InputField _input;
        private Text _historyText;

        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex;
        private bool _visible;

        // 打开控制台期间被屏蔽输入的本地玩家（关闭时还原）
        private PlayerController _mutedPlayer;
        private IPlayerInput _mutedInput;

        private void Start()
        {
            BuildUI();
            SetVisible(false);

            if (GmCommandService.Instance != null)
            {
                GmCommandService.Instance.Output += AppendLine;
            }
            AppendLine("GM 控制台就绪：按 ` 或点击左上角 GM 按钮打开；help 查看本地指令，其余指令由服务器响应");
        }

        private void OnDestroy()
        {
            if (GmCommandService.Instance != null)
            {
                GmCommandService.Instance.Output -= AppendLine;
            }
            RestorePlayerInput();
        }

        private void Update()
        {
            bool inputFocused = _input != null && _input.isFocused;
            if (Input.GetKeyDown(KeyCode.BackQuote) && (!_visible || !inputFocused))
            {
                SetVisible(!_visible);
                return;
            }
            if (!_visible) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetVisible(false);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                Recall(-1);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                Recall(1);
            }
        }

        // ==================== 对外行为 ====================

        /// <summary>追加一行回显（线程调用方需保证在主线程，网络回调已由 NetworkManager 排队到主线程）</summary>
        public void AppendLine(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }
            if (_historyText != null)
            {
                _historyText.text = string.Join("\n", _lines);
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_panel != null) _panel.SetActive(visible);
            if (_blocker != null) _blocker.SetActive(visible);

            if (visible)
            {
                EnsureEventSystem();
                MutePlayerInput();
                RefocusInput();
            }
            else
            {
                RestorePlayerInput();
            }
        }

        private void OnSubmit(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                AppendLine("> " + text);
                _commandHistory.Add(text);
                _historyIndex = _commandHistory.Count;
                if (GmCommandService.Instance != null)
                {
                    GmCommandService.Instance.Submit(text);
                }
            }
            if (_input != null) _input.text = "";
            RefocusInput();
        }

        private void Recall(int direction)
        {
            if (_commandHistory.Count == 0 || _input == null) return;
            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _commandHistory.Count);
            _input.text = _historyIndex < _commandHistory.Count ? _commandHistory[_historyIndex] : "";
            _input.caretPosition = _input.text.Length;
        }

        private void RefocusInput()
        {
            if (_input == null) return;
            _input.Select();
            _input.ActivateInputField();
        }

        // ==================== 输入屏蔽 ====================

        private void MutePlayerInput()
        {
            RestorePlayerInput();
            PlayerController player = FindLocalPlayer();
            if (player == null) return;
            _mutedPlayer = player;
            _mutedInput = player.InputSource;
            player.SetInputSource(NullPlayerInput.Instance);
        }

        private void RestorePlayerInput()
        {
            if (_mutedPlayer != null && _mutedInput != null)
            {
                _mutedPlayer.SetInputSource(_mutedInput);
            }
            _mutedPlayer = null;
            _mutedInput = null;
        }

        private static PlayerController FindLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null) return null;
            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal) return players[i];
            }
            return null;
        }

        // ==================== 动态构建 UI ====================

        private void BuildUI()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            Font font = LoadBuiltinFont();

            // 全屏点击阻挡层（仅打开时启用，防止指令输入误触游戏世界）
            _blocker = CreateUIObject("ClickBlocker", transform);
            Image blockerImage = _blocker.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.01f);
            Stretch((RectTransform)_blocker.transform);

            // 顶部控制台面板（占屏高 45%）
            _panel = CreateUIObject("ConsolePanel", transform);
            Image panelImage = _panel.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.09f, 0.12f, 0.92f);
            RectTransform panelRect = (RectTransform)_panel.transform;
            panelRect.anchorMin = new Vector2(0f, 0.55f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // 历史回显区
            GameObject historyObj = CreateUIObject("History", _panel.transform);
            _historyText = historyObj.AddComponent<Text>();
            _historyText.font = font;
            _historyText.fontSize = 20;
            _historyText.color = new Color(0.75f, 0.95f, 0.75f);
            _historyText.alignment = TextAnchor.LowerLeft;
            _historyText.supportRichText = false;
            _historyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _historyText.verticalOverflow = VerticalWrapMode.Truncate;
            RectTransform historyRect = (RectTransform)historyObj.transform;
            historyRect.anchorMin = Vector2.zero;
            historyRect.anchorMax = Vector2.one;
            historyRect.offsetMin = new Vector2(16f, 64f);
            historyRect.offsetMax = new Vector2(-16f, -12f);

            // 指令输入框
            _input = CreateInputField(_panel.transform, font);
            RectTransform inputRect = (RectTransform)_input.transform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0f, 10f);
            inputRect.sizeDelta = new Vector2(-32f, 44f);
            _input.onSubmit.AddListener(OnSubmit);

            // 左上角常驻 GM 按钮（触屏设备入口）
            GameObject buttonObj = CreateUIObject("GmToggleButton", transform);
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.55f);
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => SetVisible(!_visible));
            RectTransform buttonRect = (RectTransform)buttonObj.transform;
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(12f, -12f);
            buttonRect.sizeDelta = new Vector2(84f, 40f);

            Text buttonText = CreateText(buttonObj.transform, "Label", font, 22, FontStyle.Bold, Color.white);
            buttonText.text = "GM";
            Stretch((RectTransform)buttonText.transform);
        }

        private static InputField CreateInputField(Transform parent, Font font)
        {
            GameObject obj = CreateUIObject("CommandInput", parent);
            Image bg = obj.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.17f, 0.2f, 1f);

            InputField input = obj.AddComponent<InputField>();

            Text text = CreateText(obj.transform, "Text", font, 22, FontStyle.Normal, Color.white);
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch((RectTransform)text.transform, 12f, 0f);

            Text placeholder = CreateText(obj.transform, "Placeholder", font, 22, FontStyle.Italic, new Color(0.5f, 0.5f, 0.5f));
            placeholder.text = "输入 GM 指令，回车发送（如 set_phase playing / kill_me）";
            placeholder.alignment = TextAnchor.MiddleLeft;
            Stretch((RectTransform)placeholder.transform, 12f, 0f);

            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
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

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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

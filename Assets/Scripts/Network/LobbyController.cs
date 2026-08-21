using Minigame.Account.V1;
using Minigame.Common.V1;
using Minigame.Gateway.V1;
using SuperQQ.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SuperQQ.Network
{
    /// <summary>
    /// 登录场景控制器（Lobby）：启动自动连接，点击"登录"弹出账号密码弹窗，
    /// 登录成功后进入大厅场景（Hall），建房/加房流程由 HallController 接管。
    /// 依赖场景中的 UI 引用：statusText（可选）。
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        [Header("登录成功后进入的大厅场景（拖入场景资源，需已加入 Build Settings）")]
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset hallSceneAsset;
#endif
        [SerializeField, HideInInspector] private string hallSceneName = "Hall";

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (hallSceneAsset != null) hallSceneName = hallSceneAsset.name;
        }
#endif

        [Header("UI 引用")]
        [SerializeField] private Text statusText;

        [Header("登录按钮（留空则运行时自动创建一个）")]
        [SerializeField] private Button loginButton;

        [Header("登录弹窗 Prefab（留空则运行时代码构建一个简易版）")]
        [SerializeField] private LoginPopup loginPopupPrefab;

        private NetworkManager _net;
        private LoginPopup _activePopup;
        private Text _loginButtonLabel;

        private void Start()
        {
            _net = NetworkManager.Instance;
            if (_net == null)
            {
                SetStatus("错误：场景中缺少 NetworkManager");
                Debug.LogError("[NetWork] Lobby 场景中未找到 NetworkManager");
                return;
            }

            _net.Register<LoginResponse>(OnLogin);
            _net.Register<ErrorResponse>(OnError);
            _net.OnConnectionChanged += OnConnectionChanged;

            SetupLoginButton();

            if (_net.IsConnected)
            {
                OnConnectionChanged(true);
            }
            else
            {
                SetStatus("正在连接服务器...");
                _net.Connect();
            }
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.Unregister<LoginResponse>();
            _net.Unregister<ErrorResponse>();
            _net.OnConnectionChanged -= OnConnectionChanged;
        }

        // ==================== 连接 → 登录按钮 → 账号密码弹窗 ====================

        /// <summary>登录按钮初始化：未配置时在 Canvas 顶部动态创建一个</summary>
        private void SetupLoginButton()
        {
            if (loginButton == null)
            {
                Canvas canvas = FindFirstObjectByType<Canvas>();
                if (canvas == null)
                {
                    Debug.LogError("[NetWork] 场景中未找到 Canvas，无法创建登录按钮");
                    return;
                }
                loginButton = CreateLoginButton(canvas.transform);
            }

            _loginButtonLabel = loginButton.GetComponentInChildren<Text>();
            loginButton.onClick.AddListener(OnLoginClicked);
            loginButton.interactable = false;
        }

        private void OnConnectionChanged(bool connected)
        {
            if (!connected)
            {
                SetStatus("连接失败/已断开");
                if (loginButton != null) loginButton.interactable = false;
                _activePopup?.SetStatus("连接已断开", true);
                _activePopup?.SetConfirmInteractable(false);
                return;
            }

            // 已登录（如从对局返回大厅）：直接刷新状态，不再要求登录
            if (!string.IsNullOrEmpty(_net.LocalPlayerId))
            {
                SetStatus($"已登录：{_net.LocalPlayerId}");
                MarkLoggedIn();
                return;
            }

            SetStatus("已连接，请点击登录");
            if (loginButton != null) loginButton.interactable = true;
        }

        private void OnLoginClicked()
        {
            if (_activePopup != null) return;
            if (!string.IsNullOrEmpty(_net.LocalPlayerId)) return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                SetStatus("错误：场景中缺少 Canvas");
                return;
            }

            if (loginButton != null) loginButton.interactable = false;
            _activePopup = loginPopupPrefab != null
                ? LoginPopup.Show(loginPopupPrefab, canvas.transform, OnLoginConfirm, OnLoginCancel)
                : LoginPopup.Show(canvas.transform, OnLoginConfirm, OnLoginCancel);
        }

        private void OnLoginCancel()
        {
            _activePopup = null;
            if (loginButton != null && string.IsNullOrEmpty(_net.LocalPlayerId))
            {
                loginButton.interactable = _net.IsConnected;
            }
        }

        /// <summary>弹窗确认登录：账号密码 + deviceId 一起上报，服务端优先按账号密码校验</summary>
        private void OnLoginConfirm(string username, string password)
        {
            string deviceId = SystemInfo.deviceUniqueIdentifier
                              + (Application.isEditor ? "-editor" : "-player")
                              + "-" + GetOrCreateDeviceSuffix();

            Debug.Log($"[NetWork] 账号密码登录: username={username} deviceId={deviceId}");
            SetStatus("登录中...");
            _activePopup?.SetStatus("登录中...");
            _activePopup?.SetConfirmInteractable(false);

            _net.Send(new LoginRequest
            {
                DeviceId = deviceId,
                ClientVersion = Application.version,
                Username = username,
                Password = password
            });
        }

        private void OnLogin(LoginResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                string errorMsg = resp.Status?.Message ?? "未知错误";
                SetStatus($"登录失败: {errorMsg}");
                _activePopup?.SetStatus($"登录失败: {errorMsg}", true);
                _activePopup?.SetConfirmInteractable(true);
                return;
            }

            _net.LocalPlayerId = resp.Player.PlayerId;
            _net.Token = resp.Token;
            Debug.Log($"[NetWork] 登录成功: playerId={resp.Player.PlayerId} nickname={resp.Player.Nickname}，进入大厅 {hallSceneName}");
            SetStatus($"登录成功：{resp.Player.Nickname}，进入大厅...");

            // 登录成功：关闭弹窗，进入大厅场景
            _activePopup?.ClosePopup();
            _activePopup = null;
            MarkLoggedIn();
            SceneManager.LoadScene(hallSceneName);
        }

        /// <summary>登录成功后刷新登录按钮为"已登录"状态并禁用</summary>
        private void MarkLoggedIn()
        {
            if (loginButton != null) loginButton.interactable = false;
            if (_loginButtonLabel != null) _loginButtonLabel.text = "已登录";
        }

        /// <summary>运行时创建登录按钮（固定在 Canvas 顶部居中，仅供 Lobby 测试场景使用）</summary>
        private static Button CreateLoginButton(Transform canvasTransform)
        {
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }

            var obj = new GameObject("LoginButton", typeof(RectTransform));
            obj.transform.SetParent(canvasTransform, false);

            Image bg = obj.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.55f, 0.95f);

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;

            RectTransform rect = (RectTransform)obj.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -30f);
            rect.sizeDelta = new Vector2(260f, 70f);

            var textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(obj.transform, false);
            Text text = textObj.AddComponent<Text>();
            text.font = font;
            text.text = "登 录";
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = (RectTransform)textObj.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return button;
        }

        // ==================== 错误处理 ====================

        private void OnError(ErrorResponse err)
        {
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");

            // 网关级登录失败（如账号密码错误走 ErrorResponse 回包）：反馈到弹窗
            if (err.Route == "login" && string.IsNullOrEmpty(_net.LocalPlayerId))
            {
                string errorMsg = err.Status?.Message ?? "登录失败";
                SetStatus($"登录失败: {errorMsg}");
                _activePopup?.SetStatus($"登录失败: {errorMsg}", true);
                _activePopup?.SetConfirmInteractable(true);
            }
        }

        // ==================== 工具 ====================

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        /// <summary>与 NetDebugBootstrap 一致的稳定设备标识后缀</summary>
        private static string GetOrCreateDeviceSuffix()
        {
            const string key = "NetDebug_DeviceIdSuffix";
            string suffix = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(suffix))
            {
                suffix = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                PlayerPrefs.SetString(key, suffix);
                PlayerPrefs.Save();
            }
            return suffix;
        }
    }
}

using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Room.V1;
using SuperQQ.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 大厅控制器（Hall / UI Lobby 场景通用）：登录成功后的落地页。
    /// 提供两个接口：
    ///   1. 创建房间：生成房间码 → CreateRoom → JoinRoom → 进入房间等待场景（Room）
    ///   2. 加入房间：弹窗输入房间码 → JoinRoom → 进入房间等待场景（Room）
    ///
    /// UI 两种接入方式：
    ///   A. Inspector 拖入场景中现成的按钮（如 UI/Lobby 场景的 BtnCreateRoom / BtnScanJoin）；
    ///   B. 字段全部留空 → 运行时动态构建测试 UI（旧 Hall 测试场景用法，需有 Canvas / EventSystem）。
    /// </summary>
    public class HallController : MonoBehaviour
    {
        [Header("房间等待场景（拖入场景资源，需已加入 Build Settings）")]
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset roomSceneAsset;
#endif
        [SerializeField, HideInInspector] private string roomSceneName = "Room";

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (roomSceneAsset != null) roomSceneName = roomSceneAsset.name;
        }
#endif

        [Header("UI 引用（可选：拖入场景现成按钮，全部留空则动态构建测试 UI）")]
        [SerializeField] private Button createButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Text statusText;
        [SerializeField] private TMP_Text statusLabel;

        private NetworkManager _net;
        private Text _statusText;
        private TMP_Text _statusLabel;
        private Text _playerInfoText;
        private Button _createButton;
        private Button _joinButton;
        private JoinRoomPopup _activePopup;

        // 创建房间流程状态：创建成功后需要再进房
        private string _pendingRoomId = "";
        // 本端是否为房主（创建房间成功后置 true，用于后端未实现 owner_player_id 时的兜底）
        private bool _isOwner = false;

        private void Start()
        {
            _net = NetworkManager.Instance;
            if (_net == null)
            {
                // 直接从 Hall 场景启动的兜底：动态补一个 NetworkManager
                var netObj = new GameObject("NetworkManager");
                _net = netObj.AddComponent<NetworkManager>();
            }

            SetupUI();

            _net.Register<JoinRoomResponse>(OnJoinRoom);
            _net.Register<CreateRoomResponse>(OnCreateRoom);
            _net.Register<ErrorResponse>(OnError);

            RefreshState();
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.Unregister<JoinRoomResponse>();
            _net.Unregister<CreateRoomResponse>();
            _net.Unregister<ErrorResponse>();
        }

        // ==================== 状态刷新 ====================

        private void RefreshState()
        {
            bool loggedIn = !string.IsNullOrEmpty(_net.LocalPlayerId);
            if (_createButton != null) _createButton.interactable = loggedIn;
            if (_joinButton != null) _joinButton.interactable = loggedIn;

            if (loggedIn)
            {
                SetStatus("创建新房间，或输入房间码加入好友的房间");
            }
            else
            {
                SetStatus("未登录，请从 Lobby 场景启动游戏");
            }

            if (_playerInfoText != null)
            {
                _playerInfoText.text = loggedIn ? $"玩家：{_net.LocalPlayerId}" : "";
            }
        }

        // ==================== 创建房间 ====================

        private void OnCreateRoomClicked()
        {
            if (string.IsNullOrEmpty(_net.LocalPlayerId)) return;

            // 客户端生成 6 位数字房间码（服务器以请求中的 room_id 建房间）
            _pendingRoomId = Random.Range(0, 1000000).ToString("D6");
            SetStatus("正在创建房间...");
            SetButtonsInteractable(false);

            Debug.Log($"[NetWork] 创建房间: roomId={_pendingRoomId}");
            _net.Send(new CreateRoomRequest
            {
                RoomId = _pendingRoomId,
                Mode = MatchMode.Casual1V1,
                Players =
                {
                    new PlayerRef
                    {
                        PlayerId = _net.LocalPlayerId,
                        GatewayId = _net.GatewayId,
                        SessionId = _net.SessionId
                    }
                },
                CreatedAtMs = NetworkManager.NowMs()
            });
        }

        private void OnCreateRoom(CreateRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                SetStatus($"创建房间失败: {resp.Status?.Message}");
                SetButtonsInteractable(true);
                return;
            }

            // 以服务器返回的房间数据为准（房间码可能被服务端调整）
            string roomId = resp.Room != null ? resp.Room.RoomId : _pendingRoomId;
            // 本端创建者即房主（后端未实现 owner_player_id 时客户端兜底）
            _isOwner = true;
            Debug.Log($"[NetWork] 创建房间成功: roomId={roomId}，加入");
            SetStatus("创建成功，正在进入房间...");
            SendJoin(roomId);
        }

        // ==================== 加入房间 ====================

        private void OnJoinRoomClicked()
        {
            if (string.IsNullOrEmpty(_net.LocalPlayerId)) return;
            if (_activePopup != null) return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            _activePopup = JoinRoomPopup.Show(canvas.transform, OnJoinConfirm, () => _activePopup = null);
        }

        private void OnJoinConfirm(string roomCode)
        {
            SetStatus($"正在加入房间 {roomCode}...");
            _activePopup?.SetStatus("加入中...");
            _activePopup?.SetConfirmInteractable(false);
            SendJoin(roomCode);
        }

        private void SendJoin(string roomId)
        {
            _net.Send(new JoinRoomRequest
            {
                RoomId = roomId,
                PlayerId = _net.LocalPlayerId,
                GatewayId = _net.GatewayId,
                SessionId = _net.SessionId
            });
        }

        private void OnJoinRoom(JoinRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                SetStatus($"进房失败: {resp.Status?.Message}");
                SetButtonsInteractable(true);
                _activePopup?.SetStatus($"进房失败: {resp.Status?.Message}", true);
                _activePopup?.SetConfirmInteractable(true);
                return;
            }

            _net.RoomId = resp.Room.RoomId;
            _net.JoinedRoom = resp.Room;

            // 房主兜底：本端创建的房间，后端未填 owner_player_id 时本地补上
            if (_isOwner && _net.JoinedRoom != null && string.IsNullOrEmpty(_net.JoinedRoom.OwnerPlayerId))
            {
                _net.JoinedRoom.OwnerPlayerId = _net.LocalPlayerId;
            }
            _isOwner = false;

            Debug.Log($"[NetWork] 进房成功: roomId={resp.Room.RoomId} 玩家数={resp.Room.Players.Count}，进入房间场景 {roomSceneName}");

            _activePopup?.ClosePopup();
            _activePopup = null;
            SceneManager.LoadScene(roomSceneName);
        }

        // ==================== 错误处理 ====================

        private void OnError(ErrorResponse err)
        {
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");

            if (err.Route == "join_room")
            {
                string errorMsg = err.Status != null && err.Status.Code == ResultCode.NotFound
                    ? "房间不存在，请检查房间码"
                    : $"进房失败: {err.Status?.Message}";
                SetStatus(errorMsg);
                SetButtonsInteractable(true);
                _activePopup?.SetStatus(errorMsg, true);
                _activePopup?.SetConfirmInteractable(true);
            }
            else if (err.Route == "create_room")
            {
                SetStatus($"创建房间失败: {err.Status?.Message}");
                SetButtonsInteractable(true);
            }
        }

        // ==================== UI 初始化 ====================

        /// <summary>
        /// UI 初始化：已配置场景按钮（UI/Lobby 美术场景）则直接绑定；
        /// 否则动态构建整套测试 UI（旧 Hall 测试场景）。
        /// </summary>
        private void SetupUI()
        {
            _statusText = statusText;
            _statusLabel = statusLabel;

            if (createButton != null || joinButton != null)
            {
                _createButton = createButton;
                _joinButton = joinButton;
                if (_createButton != null) _createButton.onClick.AddListener(OnCreateRoomClicked);
                if (_joinButton != null) _joinButton.onClick.AddListener(OnJoinRoomClicked);
                return;
            }

            BuildUI();
        }

        // ==================== 动态构建 UI ====================

        private void BuildUI()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Hall] 场景中未找到 Canvas");
                return;
            }

            Font font = LoadBuiltinFont();
            Transform root = canvas.transform;

            // 背景
            Image bg = CreatePanel(root, "Background", new Color(0.12f, 0.14f, 0.2f));
            Stretch((RectTransform)bg.transform);

            // 标题
            Text title = CreateText(root, "Title", font, 60, FontStyle.Bold, Color.white);
            title.text = "游戏大厅";
            SetRect(title.transform, 0.5f, 1f, 0f, -100f, 600f, 90f);

            // 玩家信息
            _playerInfoText = CreateText(root, "PlayerInfo", font, 24, FontStyle.Normal, new Color(0.7f, 0.75f, 0.85f));
            SetRect(_playerInfoText.transform, 0.5f, 1f, 0f, -170f, 600f, 40f);

            // 创建房间按钮
            _createButton = CreateButton(root, "CreateRoomButton", font, "创建房间",
                new Color(0.2f, 0.6f, 0.3f), OnCreateRoomClicked);
            SetRect(_createButton.transform, 0.5f, 0.5f, 0f, 60f, 360f, 90f);

            // 加入房间按钮
            _joinButton = CreateButton(root, "JoinRoomButton", font, "加入房间",
                new Color(0.25f, 0.55f, 0.95f), OnJoinRoomClicked);
            SetRect(_joinButton.transform, 0.5f, 0.5f, 0f, -60f, 360f, 90f);

            // 状态文本
            _statusText = CreateText(root, "StatusText", font, 24, FontStyle.Normal, new Color(1f, 0.9f, 0.4f));
            SetRect(_statusText.transform, 0.5f, 0f, 0f, 60f, 900f, 40f);
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null) _statusText.text = msg;
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (_createButton != null) _createButton.interactable = interactable;
            if (_joinButton != null) _joinButton.interactable = interactable;
        }

        private static Image CreatePanel(Transform parent, string name, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            Image image = obj.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, Font font, int fontSize, FontStyle style, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
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

        private static Button CreateButton(Transform parent, string name, Font font, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            Image bg = obj.AddComponent<Image>();
            bg.color = color;

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            Text text = CreateText(obj.transform, "Text", font, 30, FontStyle.Bold, Color.white);
            text.text = label;
            Stretch((RectTransform)text.transform);

            return button;
        }

        private static void SetRect(Transform t, float anchorX, float anchorY, float posX, float posY, float w, float h)
        {
            var rect = (RectTransform)t;
            rect.anchorMin = new Vector2(anchorX, anchorY);
            rect.anchorMax = new Vector2(anchorX, anchorY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(posX, posY);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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

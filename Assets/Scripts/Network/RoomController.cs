using System.Text;
using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Room.V1;
using SuperQQ.Microphone;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 房间等待场景控制器（Room 场景）。
    /// 数据来源：进房时的 JoinedRoom + 服务端 RoomUpdated 推送（成员进出/准备变化/游戏开始）。
    ///
    /// 功能：
    ///   - 显示房间码和玩家列表（房主标记、准备状态）
    ///   - 普通玩家："准备 / 取消准备"按钮（SetReadyRequest）
    ///   - 房主：恒定视为已准备，显示"开始游戏"按钮，全员准备后可点击（StartGameRequest）
    ///   - 收到 game_started 推送后全员切入对局场景（Level1）
    /// UI 全部由代码动态构建，场景里只需挂本脚本（需有 Canvas / EventSystem）。
    /// </summary>
    public class RoomController : MonoBehaviour
    {
        [Header("对局场景名（需已加入 Build Settings）")]
        [SerializeField] private string battleSceneName = "Level1";

        private NetworkManager _net;
        private Room _room;

        private Text _roomCodeText;
        private Text _playerListText;
        private Text _statusText;
        private Text _readyButtonLabel;
        private Button _readyButton;
        private Button _startButton;

        private bool _starting;
        private float _startRequestTime;

        [Header("轮询间隔（秒）：后端未实现 RoomUpdated 推送时，客户端轮询 GetRoom 兜底同步房间状态")]
        [SerializeField] private float pollInterval = 2f;

        [Header("开始游戏请求超时（秒）：超时未收到成功响应则恢复按钮，防止后端未实现该路由时卡死")]
        [SerializeField] private float startGameTimeout = 6f;

        private float _pollTimer;

        private void Start()
        {
            _net = NetworkManager.Instance;
            _room = _net != null ? _net.JoinedRoom : null;

            BuildUI();

            if (_net == null || string.IsNullOrEmpty(_net.RoomId) || _room == null)
            {
                SetStatus("未在房间中，请从大厅进入", true);
                if (_readyButton != null) _readyButton.interactable = false;
                if (_startButton != null) _startButton.interactable = false;
                return;
            }

            _net.Register<RoomUpdated>(OnRoomUpdated);
            _net.Register<SetReadyResponse>(OnSetReady);
            _net.Register<StartGameResponse>(OnStartGame);
            _net.Register<GetRoomResponse>(OnGetRoom);
            _net.Register<ErrorResponse>(OnError);

            Refresh();
        }

        private void Update()
        {
            if (_room == null || string.IsNullOrEmpty(_net.RoomId)) return;

            // 开始游戏请求超时保护：未收到响应/推送时恢复按钮，避免后端未实现该路由时永久卡死
            if (_starting && Time.unscaledTime - _startRequestTime >= startGameTimeout)
            {
                _starting = false;
                SetStatus("开始游戏超时：服务器可能未实现该接口，请联系后端", true);
                Refresh();
            }

            // 等待阶段轮询房间状态（成员进出/准备变化）。后端实现 RoomUpdated 推送后此逻辑可移除。
            if (_room.Phase != RoomPhase.Unspecified && _room.Phase != RoomPhase.Waiting) return;

            _pollTimer += Time.deltaTime;
            if (_pollTimer >= pollInterval)
            {
                _pollTimer = 0f;
                _net.Send(new GetRoomRequest { RoomId = _net.RoomId });
            }
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.Unregister<RoomUpdated>();
            _net.Unregister<SetReadyResponse>();
            _net.Unregister<StartGameResponse>();
            _net.Unregister<GetRoomResponse>();
            _net.Unregister<ErrorResponse>();
        }

        // ==================== 数据刷新 ====================

        private bool IsOwner => _room != null && _room.OwnerPlayerId == _net.LocalPlayerId;

        /// <summary>房主恒定视为已准备；其余玩家读 ready 字段</summary>
        private bool IsReady(RoomPlayerState player)
        {
            if (player.Player != null && player.Player.PlayerId == _room.OwnerPlayerId) return true;
            return player.Ready;
        }

        private RoomPlayerState FindSelf()
        {
            if (_room == null) return null;
            foreach (RoomPlayerState p in _room.Players)
            {
                if (p.Player != null && p.Player.PlayerId == _net.LocalPlayerId) return p;
            }
            return null;
        }

        private void Refresh()
        {
            if (_room == null) return;

            if (_roomCodeText != null)
            {
                _roomCodeText.text = $"房间码：{_room.RoomId}";
            }

            // 玩家列表：房主置顶标记 + 准备状态
            var sb = new StringBuilder();
            foreach (RoomPlayerState p in _room.Players)
            {
                string playerId = p.Player != null ? p.Player.PlayerId : "?";
                string nickname = p.Player != null && !string.IsNullOrEmpty(p.Player.Nickname)
                    ? p.Player.Nickname : playerId;

                bool isOwner = p.Player != null && p.Player.PlayerId == _room.OwnerPlayerId;
                bool isMe = p.Player != null && p.Player.PlayerId == _net.LocalPlayerId;

                sb.Append(isOwner ? "★ " : "　");
                sb.Append(nickname);
                if (isMe) sb.Append("（我）");
                if (isOwner) sb.Append(" [房主]");
                sb.Append(IsReady(p) ? "  ✔已准备" : "  …未准备");
                if (!p.Connected) sb.Append(" [离线]");
                sb.Append('\n');
            }
            if (_playerListText != null) _playerListText.text = sb.ToString();

            // 底部按钮区：房主显示开始游戏，普通玩家显示准备切换
            if (_readyButton != null) _readyButton.gameObject.SetActive(!IsOwner);
            if (_startButton != null) _startButton.gameObject.SetActive(IsOwner);

            if (!IsOwner)
            {
                RoomPlayerState self = FindSelf();
                bool ready = self != null && IsReady(self);
                if (_readyButtonLabel != null) _readyButtonLabel.text = ready ? "取消准备" : "准 备";
            }
            else
            {
                // 全员准备（房主默认已准备）才可开始
                bool allReady = true;
                foreach (RoomPlayerState p in _room.Players)
                {
                    if (!IsReady(p)) { allReady = false; break; }
                }
                if (_startButton != null) _startButton.interactable = allReady && !_starting;
                SetStatus(allReady ? "全员已准备，可以开始游戏" : "等待所有玩家准备...", false);
            }
        }

        // ==================== 网络交互 ====================

        private void OnReadyClicked()
        {
            RoomPlayerState self = FindSelf();
            if (self == null) return;

            bool target = !IsReady(self);
            Debug.Log($"[NetWork] 设置准备状态: {target}");
            _net.Send(new SetReadyRequest
            {
                RoomId = _net.RoomId,
                PlayerId = _net.LocalPlayerId,
                Ready = target
            });
            // 等待 RoomUpdated 推送刷新，不做本地预判，避免与服务端状态不一致
        }

        private void OnStartClicked()
        {
            if (!IsOwner || _starting) return;

            _starting = true;
            _startRequestTime = Time.unscaledTime;
            if (_startButton != null) _startButton.interactable = false;
            SetStatus("正在开始游戏...", false);

            Debug.Log("[NetWork] 房主发起开始游戏");
            _net.Send(new StartGameRequest
            {
                RoomId = _net.RoomId,
                PlayerId = _net.LocalPlayerId
            });
        }

        private void OnSetReady(SetReadyResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                SetStatus($"设置准备状态失败: {resp.Status?.Message}", true);
            }
        }

        private void OnStartGame(StartGameResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                _starting = false;
                SetStatus($"开始游戏失败: {resp.Status?.Message}", true);
                Refresh();
                return;
            }
            // 成功时服务端会广播 RoomUpdated(game_started)，全员统一在推送里切场景
        }

        /// <summary>轮询回包：合并服务器房间状态（保留本端房主标记，防止后端未填 owner 时丢身份）</summary>
        private void OnGetRoom(GetRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok || resp.Room == null) return;
            if (resp.Room.RoomId != _net.RoomId) return;

            MergeRoom(resp.Room);
        }

        /// <summary>合并服务器下发的 Room：owner 为空时保留本端值；phase 推进到 BATTLE 时切场景</summary>
        private void MergeRoom(Room serverRoom)
        {
            // 后端未实现 owner_player_id 时保留本端房主身份
            if (string.IsNullOrEmpty(serverRoom.OwnerPlayerId) && !string.IsNullOrEmpty(_room?.OwnerPlayerId))
            {
                serverRoom.OwnerPlayerId = _room.OwnerPlayerId;
            }

            _room = serverRoom;
            _net.JoinedRoom = serverRoom;

            // 通过轮询也能检测到开局（phase 被服务器推进）
            if (_room.Phase == RoomPhase.Battle || _room.Phase == RoomPhase.Loading)
            {
                SetStatus("游戏开始，进入对局...", false);
                SceneManager.LoadScene(battleSceneName);
                return;
            }

            Refresh();
        }

        private void OnRoomUpdated(RoomUpdated update)
        {
            if (update.Room == null || update.Room.RoomId != _net.RoomId) return;

            Debug.Log($"[NetWork] 房间状态更新: reason={update.Reason} 玩家数={update.Room.Players.Count} phase={update.Room.Phase}");
            _room = update.Room;
            _net.JoinedRoom = update.Room;

            // 游戏开始：全员切对局场景
            if (update.Reason == "game_started" ||
                update.Room.Phase == RoomPhase.Battle || update.Room.Phase == RoomPhase.Loading)
            {
                SetStatus("游戏开始，进入对局...", false);
                SceneManager.LoadScene(battleSceneName);
                return;
            }

            Refresh();
        }

        private void OnError(ErrorResponse err)
        {
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");
            // 后端未识别新路由时统一回 route=unknown：若正处于开局请求中，按开局失败恢复，防止卡死
            if (err.Route == "start_game" || (err.Route == "unknown" && _starting))
            {
                _starting = false;
                SetStatus($"开始游戏失败: {err.Status?.Message}", true);
                Refresh();
            }
            else if (err.Route == "set_ready" || err.Route == "unknown")
            {
                // unknown 且非开局中（多半是轮询 GetRoom 被拒）：不打扰状态栏，仅日志
                if (err.Route == "set_ready")
                {
                    SetStatus($"设置准备状态失败: {err.Status?.Message}", true);
                }
            }
        }

        // ==================== 动态构建 UI ====================

        private void BuildUI()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Room] 场景中未找到 Canvas");
                return;
            }

            Font font = LoadBuiltinFont();
            Transform root = canvas.transform;

            // 背景
            Image bg = CreatePanel(root, "Background", new Color(0.12f, 0.14f, 0.2f));
            Stretch((RectTransform)bg.transform);

            // 标题
            Text title = CreateText(root, "Title", font, 48, FontStyle.Bold, Color.white);
            title.text = "房间等待";
            SetRect(title.transform, 0.5f, 1f, 0f, -70f, 500f, 70f);

            // 房间码（醒目，供分享）
            _roomCodeText = CreateText(root, "RoomCode", font, 36, FontStyle.Bold, new Color(1f, 0.9f, 0.4f));
            SetRect(_roomCodeText.transform, 0.5f, 1f, 0f, -140f, 500f, 50f);

            // 玩家列表面板
            Image listPanel = CreatePanel(root, "PlayerListPanel", new Color(0.18f, 0.2f, 0.28f));
            SetRect(listPanel.transform, 0.5f, 0.5f, 0f, 40f, 560f, 340f);

            _playerListText = CreateText(listPanel.transform, "PlayerList", font, 26, FontStyle.Normal, Color.white);
            _playerListText.alignment = TextAnchor.UpperLeft;
            RectTransform listRect = (RectTransform)_playerListText.transform;
            listRect.anchorMin = Vector2.zero;
            listRect.anchorMax = Vector2.one;
            listRect.offsetMin = new Vector2(30f, 10f);
            listRect.offsetMax = new Vector2(-30f, -10f);

            // 准备按钮（普通玩家）
            _readyButton = CreateButton(root, "ReadyButton", font, "准 备",
                new Color(0.25f, 0.55f, 0.95f), OnReadyClicked);
            SetRect(_readyButton.transform, 0.5f, 0f, 0f, 110f, 320f, 80f);
            _readyButtonLabel = _readyButton.GetComponentInChildren<Text>();

            // 开始游戏按钮（房主）
            _startButton = CreateButton(root, "StartButton", font, "开始游戏",
                new Color(0.2f, 0.6f, 0.3f), OnStartClicked);
            SetRect(_startButton.transform, 0.5f, 0f, 0f, 110f, 320f, 80f);

            // 状态文本
            _statusText = CreateText(root, "StatusText", font, 22, FontStyle.Normal, new Color(0.75f, 0.8f, 0.9f));
            SetRect(_statusText.transform, 0.5f, 0f, 0f, 50f, 800f, 36f);
        }

        private void SetStatus(string msg, bool isError)
        {
            if (_statusText == null) return;
            _statusText.text = msg;
            _statusText.color = isError ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.75f, 0.8f, 0.9f);
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
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

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

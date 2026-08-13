using Minigame.Account.V1;
using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Room.V1;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SuperQQ.Network
{
    /// <summary>
    /// 大厅控制器：启动即连接+登录，玩家输入房间名点击"加入房间"后进房，
    /// 进房成功切到对局场景（Level1）。房间不存在时自动创建再加入。
    /// 依赖场景中的 UI 引用：roomNameInput / joinButton / statusText。
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        [Header("对局场景名（需已加入 Build Settings）")]
        [SerializeField] private string battleSceneName = "Level1";

        [Header("UI 引用")]
        [SerializeField] private InputField roomNameInput;
        [SerializeField] private Button joinButton;
        [SerializeField] private Text statusText;

        private NetworkManager _net;
        private string _pendingRoomId = "";
        private bool _joining;

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
            _net.Register<JoinRoomResponse>(OnJoinRoom);
            _net.Register<CreateRoomResponse>(OnCreateRoom);
            _net.Register<ErrorResponse>(OnError);
            _net.OnConnectionChanged += OnConnectionChanged;

            if (joinButton != null)
            {
                joinButton.onClick.AddListener(OnJoinClicked);
                joinButton.interactable = false;
            }

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
            _net.Unregister<JoinRoomResponse>();
            _net.Unregister<CreateRoomResponse>();
            _net.Unregister<ErrorResponse>();
            _net.OnConnectionChanged -= OnConnectionChanged;
        }

        // ==================== 连接 → 登录 ====================

        private void OnConnectionChanged(bool connected)
        {
            if (!connected)
            {
                SetStatus("连接失败/已断开");
                return;
            }

            string deviceId = SystemInfo.deviceUniqueIdentifier
                              + (Application.isEditor ? "-editor" : "-player")
                              + "-" + GetOrCreateDeviceSuffix();

            Debug.Log($"[NetWork] 大厅自动登录: deviceId={deviceId}");
            SetStatus("已连接，登录中...");
            _net.Send(new LoginRequest { DeviceId = deviceId, ClientVersion = Application.version });
        }

        private void OnLogin(LoginResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                SetStatus($"登录失败: {resp.Status?.Message}");
                return;
            }

            _net.LocalPlayerId = resp.Player.PlayerId;
            _net.Token = resp.Token;
            Debug.Log($"[NetWork] 大厅登录成功: playerId={resp.Player.PlayerId} nickname={resp.Player.Nickname}");
            SetStatus($"登录成功：{resp.Player.Nickname}，输入房间名加入");
            if (joinButton != null) joinButton.interactable = true;
        }

        // ==================== 加入/创建房间 ====================

        private void OnJoinClicked()
        {
            if (_joining || string.IsNullOrEmpty(_net.LocalPlayerId)) return;

            string roomName = roomNameInput != null ? roomNameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(roomName))
            {
                SetStatus("请输入房间名");
                return;
            }

            _joining = true;
            _pendingRoomId = roomName;
            if (joinButton != null) joinButton.interactable = false;
            SetStatus($"正在加入房间 {roomName}...");
            SendJoin(roomName);
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

        private void OnError(ErrorResponse err)
        {
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");

            if (err.Route == "join_room" && err.Status != null && err.Status.Code == ResultCode.NotFound && _joining)
            {
                Debug.Log($"[NetWork] 房间 {_pendingRoomId} 不存在，自动创建");
                SetStatus($"房间不存在，创建 {_pendingRoomId}...");
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
        }

        private void OnCreateRoom(CreateRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                _joining = false;
                if (joinButton != null) joinButton.interactable = true;
                SetStatus($"创建房间失败: {resp.Status?.Message}");
                return;
            }

            Debug.Log($"[NetWork] 创建房间成功: roomId={resp.Room.RoomId}，加入");
            SendJoin(_pendingRoomId);
        }

        // ==================== 进房成功 → 进对局场景 ====================

        private void OnJoinRoom(JoinRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                _joining = false;
                if (joinButton != null) joinButton.interactable = true;
                SetStatus($"进房失败: {resp.Status?.Message}");
                return;
            }

            _net.RoomId = resp.Room.RoomId;
            _net.JoinedRoom = resp.Room;
            Debug.Log($"[NetWork] 进房成功: roomId={resp.Room.RoomId} 玩家数={resp.Room.Players.Count}，进入对局场景 {battleSceneName}");
            SetStatus("进房成功，进入对局...");
            SceneManager.LoadScene(battleSceneName);
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

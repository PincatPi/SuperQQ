using Minigame.Account.V1;
using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Room.V1;
using SuperQQ.Player;
using UnityEngine;
using PlayerProfile = SuperQQ.Player.PlayerProfile;

namespace SuperQQ.Network
{
    /// <summary>
    /// 联机自动引导器：游戏一运行就自动完成联网全流程，无需手动操作。
    /// 挂在启动场景（如 Level1）的空物体上即可。
    ///
    /// 自动执行链路：
    ///   连接网关 → 登录（deviceId 免注册，服务端分配 playerId）
    ///   → 加入固定房间 → 本地玩家接入上报（InputReporter）
    ///   → 注册远程玩家档案并生成化身（RemotePlayerSync 收快照后自动接管）
    ///
    /// 双端验证：
    ///   编辑器 + 打包客户端（或两台手机）同时运行，两端 deviceId 会自动区分，
    ///   填同一个 roomId 即可互见。编辑器本机双开时给其中一端改 deviceIdOverride。
    /// </summary>
    public class NetDebugBootstrap : MonoBehaviour
    {
        [Header("房间ID（两端填一样即可互见，不存在时由服务端自动创建）")]
        [SerializeField] private string roomId = "debug-room";

        [Header("设备ID覆盖（留空自动取设备唯一标识；同机双开测试时必须给一端改一个值）")]
        [SerializeField] private string deviceIdOverride = "";

        private NetworkManager _net;

        private void Start()
        {
            _net = NetworkManager.Instance;
            if (_net == null)
            {
                Debug.LogError("[NetWork] 场景中未找到 NetworkManager，请先放置一个挂 NetworkManager 的物体");
                return;
            }

            // 快照接收器直接挂在本物体上，避免手动配置场景
            if (GetComponent<RoomSnapshotReceiver>() == null)
            {
                gameObject.AddComponent<RoomSnapshotReceiver>();
            }

            _net.Register<LoginResponse>(OnLogin);
            _net.Register<JoinRoomResponse>(OnJoinRoom);
            _net.Register<CreateRoomResponse>(OnCreateRoom);
            _net.Register<ErrorResponse>(OnError);
            _net.OnConnectionChanged += OnConnectionChanged;

            _net.Connect();
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

        // ==================== 错误处理：房间不存在时自动创建 ====================

        private void OnError(ErrorResponse err)
        {
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");

            // 进房时房间不存在 → 自动创建房间，创建成功后会再次进房
            if (err.Route == "join_room" && err.Status != null && err.Status.Code == ResultCode.NotFound)
            {
                Debug.Log($"[NetWork] 房间 {roomId} 不存在，自动创建");
                _net.Send(new CreateRoomRequest
                {
                    RoomId = roomId,
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
                Debug.LogError($"[NetWork] 创建房间失败: {resp.Status?.Message}");
                return;
            }

            Debug.Log($"[NetWork] 创建房间成功: roomId={resp.Room.RoomId}，重新加入");
            _net.Send(new JoinRoomRequest
            {
                RoomId = roomId,
                PlayerId = _net.LocalPlayerId,
                GatewayId = _net.GatewayId,
                SessionId = _net.SessionId
            });
        }

        // ==================== ① 连接成功 → 登录 ====================

        private void OnConnectionChanged(bool connected)
        {
            if (!connected) return;

            string deviceId = string.IsNullOrEmpty(deviceIdOverride)
                ? SystemInfo.deviceUniqueIdentifier + (Application.isEditor ? "-editor" : "-player")
                : deviceIdOverride;

            Debug.Log($"[NetWork] 自动登录: deviceId={deviceId}");
            _net.Send(new LoginRequest
            {
                DeviceId = deviceId,
                ClientVersion = Application.version
            });
        }

        // ==================== ② 登录成功 → 进房 ====================

        private void OnLogin(LoginResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != Minigame.Common.V1.ResultCode.Ok)
            {
                Debug.LogError($"[NetWork] 登录失败: {resp.Status?.Message}");
                return;
            }

            _net.LocalPlayerId = resp.Player.PlayerId;
            _net.Token = resp.Token;
            Debug.Log($"[NetWork] 登录成功: playerId={resp.Player.PlayerId} nickname={resp.Player.Nickname}");

            Debug.Log($"[NetWork] 自动加入房间: roomId={roomId}");
            _net.Send(new JoinRoomRequest
            {
                RoomId = roomId,
                PlayerId = _net.LocalPlayerId,
                GatewayId = _net.GatewayId,
                SessionId = _net.SessionId
            });
        }

        // ==================== ③ 进房成功 → 本地接入上报 + 生成远程化身 ====================

        private void OnJoinRoom(JoinRoomResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != Minigame.Common.V1.ResultCode.Ok)
            {
                Debug.LogError($"[NetWork] 进房失败: {resp.Status?.Message}");
                return;
            }

            _net.RoomId = resp.Room.RoomId;
            Debug.Log($"[NetWork] 进房成功: roomId={resp.Room.RoomId} 房间玩家数={resp.Room.Players.Count}");

            SetupLocalPlayer();
            RegisterRemotePlayers(resp.Room);
        }

        /// <summary>给场景中的本地玩家写入网络 playerId 并挂状态上报器</summary>
        private void SetupLocalPlayer()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (player == null || !player.BIsLocal) continue;

                // 写入网络身份：让 InputReporter 和注册表都用服务端分配的 playerId
                PlayerProfile profile = player.BuildProfile();
                profile.PlayerId = _net.LocalPlayerId;
                profile.IsLocal = true;
                player.ApplyProfile(profile);

                if (player.GetComponent<InputReporter>() == null)
                {
                    player.gameObject.AddComponent<InputReporter>();
                }

                Debug.Log($"[NetWork] 本地玩家已接入联机: {player.PlayerName} -> {_net.LocalPlayerId}");
                return;
            }

            Debug.LogWarning("[NetWork] 场景中未找到本地玩家（isLocal=true 的 PlayerController）");
        }

        /// <summary>把房间里的其他玩家注册为远程档案，并生成化身等待快照驱动</summary>
        private void RegisterRemotePlayers(Room room)
        {
            PlayerSessionManager session = PlayerSessionManager.Instance;
            if (session == null)
            {
                Debug.LogWarning("[NetWork] PlayerSessionManager 不存在，无法注册远程玩家");
                return;
            }

            int index = 1;
            foreach (RoomPlayerState playerState in room.Players)
            {
                string playerId = playerState.Player?.PlayerId;
                if (string.IsNullOrEmpty(playerId) || playerId == _net.LocalPlayerId) continue;

                if (!session.HasPlayerByIdentity(playerId))
                {
                    session.RegisterProfile(new PlayerProfile
                    {
                        PlayerId = playerId,
                        IsLocal = false,
                        PlayerName = string.IsNullOrEmpty(playerState.Player.Nickname)
                            ? $"Remote{index}"
                            : playerState.Player.Nickname,
                        PlayerColor = Color.cyan
                    });
                    Debug.Log($"[NetWork] 已注册远程玩家档案: {playerState.Player.Nickname}({playerId})");
                }
                index++;
            }

            // 档案就绪后补生成远程化身；首个快照到达时 RemotePlayerSync 会自动接管
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.SpawnMissingPlayerAvatars();
            }
        }
    }
}

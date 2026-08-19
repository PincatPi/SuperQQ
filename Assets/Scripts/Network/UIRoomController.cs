using Minigame.Common.V1;
using Minigame.Gateway.V1;
using Minigame.Room.V1;
using SuperQQ.Microphone;
using SuperQQ.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperQQ.Network
{
    /// <summary>
    /// 房间等待控制器（UI/Room 美术场景）：网络与房间状态逻辑。
    /// 视图由 RoomView 承担，本类不含任何 UI 构建/渲染代码，只驱动视图。
    ///
    /// 功能（迁移自旧 RoomController）：
    ///   - 房间码 / 准备进度 / 准备按钮状态 的数据刷新
    ///   - 普通玩家：准备 ↔ 取消准备切换（SetReadyRequest，等推送刷新，不做本地预判）
    ///   - 房主：按钮切为"开始游戏"，全员准备才可点（StartGameRequest，带超时保护）
    ///   - 房间状态同步：RoomUpdated 推送 + GetRoom 轮询兜底
    ///   - phase 推进到 Loading/Battle 或收到 game_started 推送时切对局场景（进入 PropSelection 阶段）
    /// 依赖：场景中有 NetworkManager（大厅进房时已存在）。
    /// </summary>
    public class UIRoomController : MonoBehaviour
    {
        [Header("对局场景（拖入场景资源，需已加入 Build Settings）")]
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset battleSceneAsset;
#endif
        [SerializeField, HideInInspector] private string battleSceneName = "Level1";

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (battleSceneAsset != null) battleSceneName = battleSceneAsset.name;
        }
#endif

        [Header("视图引用")]
        [SerializeField] private RoomView view;

        [Header("轮询间隔（秒）：后端未实现 RoomUpdated 推送时，轮询 GetRoom 兜底同步房间状态")]
        [SerializeField] private float pollInterval = 2f;

        [Header("开始游戏请求超时（秒）：超时未收到成功响应则恢复按钮，防止后端未实现该路由时卡死")]
        [SerializeField] private float startGameTimeout = 6f;

        private NetworkManager _net;
        private Room _room;
        private float _pollTimer;

        private bool _starting;
        private float _startRequestTime;

        private void Start()
        {
            _net = NetworkManager.Instance;
            _room = _net != null ? _net.JoinedRoom : null;

            // 进入房间即开麦（音量检测），失败会自动重试直到成功
            MicVolumeManager.EnsureExists().StartMic();

            if (view == null)
            {
                Debug.LogError("[Room] 未配置 RoomView 引用");
                return;
            }

            view.ReadyClicked += OnReadyClicked;
            view.StartClicked += OnStartClicked;

            if (_net == null || string.IsNullOrEmpty(_net.RoomId) || _room == null)
            {
                Debug.LogWarning("[Room] 未在房间中，请从大厅进入");
                view.SetReadyInteractable(false);
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
            if (_net == null || _room == null || string.IsNullOrEmpty(_net.RoomId)) return;

            // 开始游戏请求超时保护：未收到响应/推送时恢复按钮，避免后端未实现该路由时永久卡死
            if (_starting && Time.unscaledTime - _startRequestTime >= startGameTimeout)
            {
                _starting = false;
                Debug.LogWarning("[NetWork] 开始游戏超时：服务器可能未实现该接口");
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
            if (view != null)
            {
                view.ReadyClicked -= OnReadyClicked;
                view.StartClicked -= OnStartClicked;
            }
            if (_net == null) return;
            _net.Unregister<RoomUpdated>();
            _net.Unregister<SetReadyResponse>();
            _net.Unregister<StartGameResponse>();
            _net.Unregister<GetRoomResponse>();
            _net.Unregister<ErrorResponse>();
        }

        // ==================== 数据刷新 ====================

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

        private bool IsOwner => _room != null && _net != null && _room.OwnerPlayerId == _net.LocalPlayerId;

        /// <summary>把房间数据推给视图：房间码、准备进度（n/m + 进度条）、按身份切换按钮模式</summary>
        private void Refresh()
        {
            if (_room == null || view == null) return;

            view.SetRoomCode(_room.RoomId);
            view.SetPlayerCount(_room.Players.Count);

            int readyCount = 0;
            for (int i = 0; i < _room.Players.Count; i++)
            {
                RoomPlayerState p = _room.Players[i];
                bool ready = IsReady(p);
                if (ready) readyCount++;

                string playerId = p.Player != null ? p.Player.PlayerId : "?";
                string nickname = p.Player != null && !string.IsNullOrEmpty(p.Player.Nickname)
                    ? p.Player.Nickname : playerId;
                view.SetSlotPlayer(i, nickname, ready);
            }
            view.SetReadyProgress(readyCount, _room.Players.Count);

            if (!IsOwner)
            {
                // 普通玩家：准备/取消准备切换
                RoomPlayerState self = FindSelf();
                view.SetReadyMode(self != null && IsReady(self));
            }
            else
            {
                // 房主（恒定视为已准备）：全员准备才可点开始，开局请求中保持置灰
                bool allReady = readyCount == _room.Players.Count;
                view.SetStartMode(allReady && !_starting);
            }
        }

        // ==================== 网络交互 ====================

        private void OnReadyClicked()
        {
            if (_net == null || _room == null) return;

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

        private void OnSetReady(SetReadyResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                Debug.LogWarning($"[NetWork] 设置准备状态失败: {resp.Status?.Message}");
            }
        }

        /// <summary>房主点击开始游戏：发起 StartGameRequest，等待服务端广播 game_started 后全员切场景</summary>
        private void OnStartClicked()
        {
            if (!IsOwner || _starting) return;

            _starting = true;
            _startRequestTime = Time.unscaledTime;
            view.SetStartMode(false);

            Debug.Log("[NetWork] 房主发起开始游戏");
            _net.Send(new StartGameRequest
            {
                RoomId = _net.RoomId,
                PlayerId = _net.LocalPlayerId
            });
        }

        private void OnStartGame(StartGameResponse resp)
        {
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                _starting = false;
                Debug.LogWarning($"[NetWork] 开始游戏失败: {resp.Status?.Message}");
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
                SceneManager.LoadScene(battleSceneName);
                return;
            }

            Refresh();
        }

        /// <summary>合并服务器下发的 Room：owner 为空时保留本端值；phase 推进时切场景</summary>
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
                Refresh();
            }
        }
    }
}

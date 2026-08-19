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
    ///   - 准备 ↔ 取消准备切换（SetReadyRequest，等推送刷新，不做本地预判）
    ///   - 房间状态同步：RoomUpdated 推送 + GetRoom 轮询兜底
    ///   - phase 推进到 Loading/Battle 或收到 game_started 推送时切对局场景
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

        private NetworkManager _net;
        private Room _room;
        private float _pollTimer;

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

            if (_net == null || string.IsNullOrEmpty(_net.RoomId) || _room == null)
            {
                Debug.LogWarning("[Room] 未在房间中，请从大厅进入");
                view.SetReadyInteractable(false);
                return;
            }

            _net.Register<RoomUpdated>(OnRoomUpdated);
            _net.Register<SetReadyResponse>(OnSetReady);
            _net.Register<GetRoomResponse>(OnGetRoom);
            _net.Register<ErrorResponse>(OnError);

            Refresh();
        }

        private void Update()
        {
            if (_net == null || _room == null || string.IsNullOrEmpty(_net.RoomId)) return;

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
            if (view != null) view.ReadyClicked -= OnReadyClicked;
            if (_net == null) return;
            _net.Unregister<RoomUpdated>();
            _net.Unregister<SetReadyResponse>();
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

        /// <summary>把房间数据推给视图：房间码、准备进度（n/m + 进度条）、自身准备按钮文案</summary>
        private void Refresh()
        {
            if (_room == null || view == null) return;

            view.SetRoomCode(_room.RoomId);

            int readyCount = 0;
            foreach (RoomPlayerState p in _room.Players)
            {
                if (IsReady(p)) readyCount++;
            }
            view.SetReadyProgress(readyCount, _room.Players.Count);

            RoomPlayerState self = FindSelf();
            view.SetSelfReady(self != null && IsReady(self));
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
            // 房间阶段无状态栏 UI，统一记日志；状态由推送/轮询纠正
            Debug.LogWarning($"[NetWork] 服务端错误: route={err.Route} code={err.Status?.Code} msg={err.Status?.Message}");
        }
    }
}

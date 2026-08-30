using Minigame.Common.V1;
using Minigame.Room.V1;
using SuperQQ.Player;
using SuperQQ.Score;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Network
{
    /// <summary>
    /// 对局内"退出房间"按钮（挂在 Level1/Level2 的按钮 prefab 上）。
    /// 点击后向服务端发送 LeaveRoomRequest（等待同步应答，超时兜底推进），
    /// 随后清理本地房间态并回到大厅场景，保证可以正常创建/加入新房间开始下一局。
    ///
    /// 本地清理顺序（关键）：
    ///   1. 清空 NetworkManager.RoomId/JoinedRoom —— 断线自动重连会按 RoomId 重新 JoinRoom，
    ///      不清空会把玩家拉回刚退出的旧房间；
    ///   2. RoomSnapshotReceiver.ClearRoomState —— 组件跨场景存活，清掉旧房间快照引用，
    ///      避免污染玩家列表 UI 与道具恢复记录；
    ///   3. PlayerSessionManager.ClearAllProfiles —— 档案跨场景持久，不清会在下一局
    ///      按旧档案错误生成化身；
    ///   4. PlayerScoreManager.ResetForNewGame —— 记分簿跨场景持久，不清会让结算页
    ///      残留上一局分数。
    ///
    /// 使用方式：挂在按钮上自动绑定自身 Button.onClick；也可在 prefab 的
    /// Button.onClick 中直接绑定 OnLeaveRoomClicked（button 字段留空即可）。
    /// </summary>
    public class LeaveRoomButton : MonoBehaviour
    {
        [Header("退房后进入的大厅场景（拖入场景资源，需已加入 Build Settings）")]
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset hallSceneAsset;
#endif
        [SerializeField, HideInInspector] private string hallSceneName = "Hall";

        [Header("等待服务端离房应答的超时（秒），超时后按已退房继续本地清理")]
        [SerializeField] private float responseTimeoutSeconds = 3f;

        [Header("可选：留空则自动取自身 Button 并绑定点击事件")]
        [SerializeField] private Button button;

        // 退房流程进行中（防重复点击）
        private bool _leaving;

        // 等待离房应答的截止时刻（0=未在等待）
        private float _responseDeadline;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (hallSceneAsset != null) hallSceneName = hallSceneAsset.name;
        }
#endif

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }
            if (button != null)
            {
                button.onClick.AddListener(OnLeaveRoomClicked);
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(OnLeaveRoomClicked);
            }
            // 等待应答期间被外部销毁（如场景切换兜底）：归还 handler 占用
            if (_leaving && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Unregister<LeaveRoomResponse>();
            }
        }

        private void Update()
        {
            if (_leaving && _responseDeadline > 0f && Time.realtimeSinceStartup > _responseDeadline)
            {
                Debug.LogWarning("[NetWork] 等待离房应答超时，按已退房继续本地清理");
                FinishLeave();
            }
        }

        /// <summary>按钮点击入口：也可在 prefab 的 Button.onClick 里直接绑定本方法</summary>
        public void OnLeaveRoomClicked()
        {
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            if (button != null)
            {
                button.interactable = false;
            }

            NetworkManager net = NetworkManager.Instance;
            // 离线/未在房：无需通知服务端，直接本地清理回大厅
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId))
            {
                FinishLeave();
                return;
            }

            net.Register<LeaveRoomResponse>(OnLeaveRoomResponse);
            _responseDeadline = Time.realtimeSinceStartup + responseTimeoutSeconds;
            net.Send(new LeaveRoomRequest { RoomId = net.RoomId });
            Debug.Log($"[NetWork] 对局中主动退出房间: room={net.RoomId}");
        }

        private void OnLeaveRoomResponse(LeaveRoomResponse resp)
        {
            if (!_leaving)
            {
                return;
            }
            if (resp.Status == null || resp.Status.Code != ResultCode.Ok)
            {
                // 服务端拒绝也继续本地清理：本端目标是干净离开，服务端残留由断线清理兜底
                Debug.LogWarning($"[NetWork] 服务端离房失败: {resp.Status?.Message}，仍按已退房继续本地清理");
            }
            FinishLeave();
        }

        /// <summary>本地清理 + 回大厅（清理顺序见类注释）</summary>
        private void FinishLeave()
        {
            _responseDeadline = 0f;

            NetworkManager net = NetworkManager.Instance;
            if (net != null)
            {
                net.Unregister<LeaveRoomResponse>();
                net.RoomId = "";
                net.JoinedRoom = null;
            }

            RoomSnapshotReceiver receiver = FindFirstObjectByType<RoomSnapshotReceiver>();
            if (receiver != null)
            {
                receiver.ClearRoomState();
            }

            // 门控缓存重置：旧房间的发牌/阶段缓存消息与服务器分数不带入下一局
            NetGameFlowGate.ResetForRoomLeave();

            PlayerSessionManager.Instance?.ClearAllProfiles();
            PlayerScoreManager.Instance?.ResetForNewGame();

            _leaving = false;

            // 优先走全局 SceneManager（带切换中防重入），缺失时退 Unity 原生加载
            if (SuperQQ.Scene.SceneManager.Instance != null)
            {
                SuperQQ.Scene.SceneManager.Instance.LoadScene(hallSceneName);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(hallSceneName);
            }
        }
    }
}

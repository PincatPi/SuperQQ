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
    /// 点击后先弹出二次确认弹窗（confirmDialogPrefab，弹窗内按钮字段指向 prefab 内部按钮，
    /// 实例化时按相对路径解析到实例上的对应按钮）：
    ///   - 确认退出：执行退房流程——向服务端发送 LeaveRoomRequest（等待同步应答，超时兜底
    ///     推进），随后清理本地房间态并回到大厅场景，保证可以正常创建/加入新房间开始下一局；
    ///   - 回到游戏：关闭弹窗，回到对局。
    /// 未配置 confirmDialogPrefab 时点击直接退房（兼容无弹窗用法）。
    ///
    /// 本地清理顺序（关键）：
    ///   1. 清空 NetworkManager.RoomId/JoinedRoom —— 断线自动重连会按 RoomId 重新 JoinRoom，
    ///      不清空会把玩家拉回刚退出的旧房间；
    ///   2. RoomSnapshotReceiver.ClearRoomState —— 组件跨场景存活，清掉旧房间快照引用，
    ///      避免污染玩家列表 UI 与道具恢复记录；
    ///   3. NetGameFlowGate.ResetForRoomLeave —— 清掉旧房间的发牌/阶段缓存消息与服务器分数；
    ///   4. PlayerSessionManager.ClearAllProfiles —— 档案跨场景持久，不清会在下一局
    ///      按旧档案错误生成化身；
    ///   5. PlayerScoreManager.ResetForNewGame —— 记分簿跨场景持久，不清会让结算页
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

        [Header("二次确认弹窗 prefab（留空则点击直接退房）")]
        [SerializeField] private GameObject confirmDialogPrefab;
        [Header("弹窗内按钮（引用 prefab 内部按钮即可，实例化时自动解析）")]
        [SerializeField] private Button confirmExitButton;
        [SerializeField] private Button backToGameButton;

        // 退房流程进行中（防重复点击）
        private bool _leaving;

        // 等待离房应答的截止时刻（0=未在等待）
        private float _responseDeadline;

        // 确认弹窗实例（懒创建，之后复用）
        private GameObject _dialogInstance;

        // 弹窗是否打开中
        private bool _bDialogOpen;

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
            // 弹窗挂在非本物体下（本场景 Canvas 或自建 Canvas），本物体销毁时一并清掉
            if (_dialogInstance != null)
            {
                Destroy(_dialogInstance);
                _dialogInstance = null;
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
            if (_leaving || _bDialogOpen)
            {
                return;
            }

            // 未配置确认弹窗：保持原有点击即退房行为
            if (confirmDialogPrefab == null)
            {
                ExecuteLeaveRoom();
                return;
            }

            ShowConfirmDialog();
        }

        // ==================== 二次确认弹窗 ====================

        private void ShowConfirmDialog()
        {
            EnsureDialogInstance();
            if (_dialogInstance == null)
            {
                // 弹窗搭建失败：退化为直接退房，避免按钮失效
                Debug.LogWarning("[NetWork] 二次确认弹窗搭建失败，直接执行退房");
                ExecuteLeaveRoom();
                return;
            }

            _bDialogOpen = true;
            _dialogInstance.SetActive(true);
            _dialogInstance.transform.SetAsLastSibling(); // 置顶显示
        }

        private void HideConfirmDialog()
        {
            _bDialogOpen = false;
            if (_dialogInstance != null)
            {
                _dialogInstance.SetActive(false);
            }
        }

        /// <summary>懒创建弹窗实例并绑定两个按钮（字段引用的是 prefab 内部按钮，需解析到实例）</summary>
        private void EnsureDialogInstance()
        {
            if (_dialogInstance != null)
            {
                return;
            }

            // 弹窗 prefab 自带 Canvas 时作为根实例化（进当前场景）；否则挂到本场景的 Canvas 下
            //——不挂跨场景常驻 Canvas（如 SlotIntroVideoPlayer 的），避免退房后弹窗残留
            Transform parent = null;
            if (confirmDialogPrefab.GetComponentInChildren<Canvas>(true) == null)
            {
                Canvas canvas = FindSceneCanvas();
                if (canvas != null)
                {
                    parent = canvas.transform;
                }
            }
            _dialogInstance = Instantiate(confirmDialogPrefab, parent, false);
            _dialogInstance.name = confirmDialogPrefab.name;
            _dialogInstance.SetActive(false);

            Button confirm = ResolveInstanceButton(confirmExitButton);
            if (confirm != null)
            {
                confirm.onClick.AddListener(OnConfirmExitClicked);
            }
            else
            {
                Debug.LogWarning("[NetWork] 二次确认弹窗未配置确认退出按钮，将无法退房", _dialogInstance);
            }

            Button back = ResolveInstanceButton(backToGameButton);
            if (back != null)
            {
                back.onClick.AddListener(OnBackToGameClicked);
            }
            else
            {
                Debug.LogWarning("[NetWork] 二次确认弹窗未配置回到游戏按钮", _dialogInstance);
            }
        }

        /// <summary>按 prefab 内的相对路径，把序列化的 prefab 按钮引用解析为实例上的对应按钮</summary>
        private Button ResolveInstanceButton(Button prefabButton)
        {
            if (prefabButton == null || _dialogInstance == null)
            {
                return null;
            }

            string path = GetRelativePath(prefabButton.transform, confirmDialogPrefab.transform);
            if (path == null)
            {
                Debug.LogWarning($"[NetWork] 按钮 {prefabButton.name} 不在确认弹窗 prefab 内，无法解析", prefabButton);
                return null;
            }

            Transform target = path.Length == 0
                ? _dialogInstance.transform
                : _dialogInstance.transform.Find(path);
            return target != null ? target.GetComponent<Button>() : null;
        }

        /// <summary>child 相对 root 的层级路径；child 不在 root 下时返回 null</summary>
        private static string GetRelativePath(Transform child, Transform root)
        {
            if (child == root)
            {
                return "";
            }

            string path = child.name;
            Transform current = child.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return current == root ? path : null;
        }

        /// <summary>找本场景内的 Canvas（跳过跨场景常驻 Canvas）</summary>
        private Canvas FindSceneCanvas()
        {
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c != null && c.gameObject.scene == gameObject.scene)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>弹窗"确认退出"</summary>
        private void OnConfirmExitClicked()
        {
            if (_leaving)
            {
                return;
            }
            HideConfirmDialog();
            ExecuteLeaveRoom();
        }

        /// <summary>弹窗"回到游戏"</summary>
        private void OnBackToGameClicked()
        {
            HideConfirmDialog();
        }

        // ==================== 退房流程 ====================

        /// <summary>执行退房（原点击逻辑）：发送 LeaveRoomRequest，等应答/超时后本地清理回大厅</summary>
        private void ExecuteLeaveRoom()
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

            NetworkManager.Instance?.Unregister<LeaveRoomResponse>();

            // 统一清理全部房间级本地状态（清理顺序与明细见 NetworkManager.ClearLocalRoomState）
            NetworkManager.ClearLocalRoomState();

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

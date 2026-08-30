using SuperQQ.GameFlow;
using SuperQQ.Network;
using UnityEngine;

namespace SuperQQ.Scene
{
    /// <summary>
    /// 大厅场景清理器：进入大厅（Hall/Lobby）时销毁对局专用的跨场景残留对象、
    /// 清空对局静态注册表，保证"退房/异常回大厅 → 开新局"不被上一局残留污染。
    ///
    /// 销毁清单（对局专用、大厅中不应存在）：
    ///   - GamePhaseManager：DontDestroyOnLoad 的上一局关卡流程管理器，其 GameFlowConfig
    ///     的阶段资产 _scene 指回旧关卡场景，残留会导致新局开局时旧地图被重新加载；
    ///     销毁时 OnDestroy 会执行当前阶段的退出清理（解除保护/停音效等），安全。
    ///
    /// 清空清单（静态对局数据）：
    ///   - ItemLifecycleSync / PickupRegistry：道具生命周期与拾取裁决注册表（已提供
    ///     "退出房间时清空"的 ClearAll 接口）；
    ///   - NetGameFlowGate 缓存：待发牌/待阶段消息、服务器分数、轮次与种子。
    ///
    /// 保留清单（基础设施，设计上跨场景常驻且会自动重建/复用）：
    ///   NetworkManager（socket 会话）、SceneManager、AudioManager、MicVolumeManager、
    ///   PlayerSessionManager / PlayerScoreManager（数据中心，退房时已重置数据，
    ///   销毁后无人重建）、NetGameFlowGate / RoomSnapshotReceiver / NetEventSync /
    ///   SettlementController / ItemPhaseHookDispatcher（AfterSceneLoad 自动重建型，
    ///   无房间时为空操作，已有房间号校验兜底）。
    ///
    /// 安全守卫：仅在"不在任何房间"时执行——联机终局结算后回 Room 场景再来一局
    /// （RoomId 非空）等同房续局路径不受本组件影响。
    ///
    /// 用法：在 Hall 场景（及 Lobby 场景，若使用）任意激活物体上挂一个即可。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class LobbyGameplayCleanup : MonoBehaviour
    {
        private void Awake()
        {
            // 仍在房间中（同房续局/异常路径）：不做任何清理
            NetworkManager net = NetworkManager.Instance;
            if (net != null && !string.IsNullOrEmpty(net.RoomId))
            {
                return;
            }

            // 旧关卡流程管理器（"退房后新局加载旧地图"的主因载体）
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow != null)
            {
                Debug.Log("[LobbyCleanup] 销毁残留的 GamePhaseManager（上一局关卡流程配置）");
                Destroy(flow.gameObject);
            }

            // 对局静态注册表与门控缓存（幂等，空表为空操作）
            ItemLifecycleSync.ClearAll();
            PickupRegistry.ClearAll();
            NetGameFlowGate.ResetForRoomLeave();
        }
    }
}

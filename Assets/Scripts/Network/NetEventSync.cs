using System.Collections.Generic;
using Minigame.Room.V1;
using SuperQQ.Item;
using SuperQQ.Player;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 联机事件同步器（运行时自动创建，跨场景存活）：
    ///   ① 本地一次性事件（跳跃/受击/拾取/死亡）上报 + 远端表现
    ///   ② 收集物拾取裁决广播 → 各端移除对应收集物
    ///   ③ 道具生命周期事件（触发/销毁）透传 → 各端同步道具状态
    ///
    /// 发送侧为静态方法，业务代码在事件点调用 NetEventSync.ReportXXX(...) 即可，
    /// 离线/未进房时自动降级为空操作。
    /// </summary>
    public class NetEventSync : MonoBehaviour
    {
        public static NetEventSync Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            if (NetworkManager.Instance == null) return;

            var go = new GameObject(nameof(NetEventSync));
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetEventSync>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            NetworkManager net = NetworkManager.Instance;
            net.Register<PlayerEventBroadcast>(OnPlayerEvent);
            net.Register<PickupClaimBroadcast>(OnPickupClaim);
            net.Register<ItemStateEventBroadcast>(OnItemStateEvent);
            net.Register<ItemPositionSyncBroadcast>(OnItemPositionSync);
            net.Register<TrapKillBroadcast>(OnTrapKill);
            net.Register<ToastSizeBroadcast>(OnToastSize);

            // 吐司尺寸上传钩子：本地随机完成后上报服务器透传
            SuperQQ.Item.RotatingToastSizeSync.OnUploadSize += ReportToastSize;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SuperQQ.Item.RotatingToastSizeSync.OnUploadSize -= ReportToastSize;
            if (NetworkManager.Instance == null) return;
            NetworkManager.Instance.Unregister<PlayerEventBroadcast>();
            NetworkManager.Instance.Unregister<PickupClaimBroadcast>();
            NetworkManager.Instance.Unregister<ItemStateEventBroadcast>();
            NetworkManager.Instance.Unregister<ItemPositionSyncBroadcast>();
            NetworkManager.Instance.Unregister<TrapKillBroadcast>();
            NetworkManager.Instance.Unregister<ToastSizeBroadcast>();
        }

        // ==================== 旋转吐司尺寸同步 ====================

        /// <summary>本地决定尺寸后上报（RotatingToastSizeSync.OnUploadSize 钩子）</summary>
        private static void ReportToastSize(int size)
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ToastSizeSync
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                Round = NetGameFlowGate.CurrentServerRound,
                Size = size
            });
        }

        /// <summary>
        /// 服务器透传的尺寸广播：应用到本端（含场上已存在实例与后续实例化）。
        /// 注意：联机尺寸主通道已改为服务器轮次种子确定性决定（NetGameFlowGate），
        /// 本广播仅作种子缺失时的兜底——加轮次守卫，迟到的旧轮广播不得覆盖当前轮尺寸
        /// </summary>
        private void OnToastSize(ToastSizeBroadcast msg)
        {
            if (NetworkManager.Instance != null && msg.PlayerId == NetworkManager.Instance.LocalPlayerId) return; // 本端已应用
            if (NetGameFlowGate.CurrentServerRound > 0 && msg.Round != NetGameFlowGate.CurrentServerRound)
            {
                Debug.LogWarning($"[NetWork] 忽略过期吐司尺寸广播: playerId={msg.PlayerId} round={msg.Round}（当前轮 {NetGameFlowGate.CurrentServerRound}）size={msg.Size}");
                return;
            }
            SuperQQ.Item.RotatingToastSizeSync.ApplySyncedSize(msg.Size);
            Debug.Log($"[NetWork] 吐司尺寸同步: playerId={msg.PlayerId} round={msg.Round} size={msg.Size}");
        }

        private static bool BReady =>
            NetworkManager.Instance != null
            && NetworkManager.Instance.IsConnected
            && !string.IsNullOrEmpty(NetworkManager.Instance.RoomId);

        // ==================== 发送侧（业务调用点） ====================

        /// <summary>上报一次性表现事件（跳跃/受击/拾取/死亡等），离线时为空操作</summary>
        public static void ReportEvent(PlayerEventType eventType, Vector2 position)
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new PlayerEvent
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                EventType = eventType,
                Position = new Minigame.Room.V1.Vector2 { X = position.x, Y = position.y }
            });
        }

        /// <summary>上报拾取请求（服务器先到先得裁决后广播）；scoreValue 为拾取物分值（金币等，无分值传 0），服务器算分用</summary>
        public static void ReportPickup(string pickupId, int scoreValue = 0)
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new PickupClaim
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                PickupId = pickupId,
                ScoreValue = scoreValue
            });
        }

        /// <summary>上报道具生命周期事件（触发/销毁），由道具所有者端调用</summary>
        public static void ReportItemState(string itemInstanceId, ItemStateType stateType)
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ItemStateEvent
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                ItemInstanceId = itemInstanceId,
                StateType = stateType
            });
        }

        /// <summary>
        /// 上报对局中道具位置（声控浮桥等随放置者本地输入持续运动的道具），由道具所有者端节流调用。
        /// player_id 由网关自动补全；服务器原样广播 ItemPositionSyncBroadcast；离线为空操作
        /// </summary>
        /// <param name="itemId">道具ID（= prefab 名称）</param>
        public static void ReportItemPosition(string itemId, Vector2 position, int rotation, bool mirrored)
        {
            if (!BReady || string.IsNullOrEmpty(itemId)) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ItemPositionSync
            {
                RoomId = net.RoomId,
                ItemId = itemId,
                Position = new Minigame.Room.V1.Vector2 { X = position.x, Y = position.y },
                Rotation = rotation,
                Mirrored = mirrored
            });
        }

        /// <summary>上报陷阱击杀（受害者本地端调用；ownerPlayerId 为陷阱放置者），离线为空操作</summary>
        public static void ReportTrapKill(string ownerPlayerId)
        {
            if (!BReady || string.IsNullOrEmpty(ownerPlayerId)) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new TrapKillEvent
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                OwnerPlayerId = ownerPlayerId
            });
        }

        // ==================== 事件3（言出法随）上行 ====================

        /// <summary>上报本地玩家选择的咒语子类型（1/2/3），无同步应答，服务端状态经 RoomSnapshot.event3_states 下发；离线为空操作</summary>
        public static void ReportEvent3Subtype(int subtype)
        {
            if (!BReady || subtype <= 0) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ReportEvent3Subtype
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                Subtype = subtype
            });
        }

        /// <summary>上报本机音量超标（player_id 由网关自动补 = 音量超标玩家），无同步应答；离线为空操作</summary>
        public static void ReportEvent3LoudPlayer()
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new ReportEvent3LoudPlayer
            {
                RoomId = net.RoomId
                // PlayerId 留空，网关自动补
            });
        }

        // ==================== 接收侧（远端表现） ====================

        private void OnPlayerEvent(PlayerEventBroadcast msg)
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || msg.PlayerId == net.LocalPlayerId) return; // 自己的事件本地已表现

            Vector2 pos = msg.Position != null ? new Vector2(msg.Position.X, msg.Position.Y) : Vector2.zero;

            // 找到远端化身挂表现组件播音效/特效
            PlayerController remote = FindRemotePlayer(msg.PlayerId);
            RemotePlayerEffects fx = null;
            if (remote != null)
            {
                fx = remote.GetComponent<RemotePlayerEffects>();
                if (fx == null)
                {
                    fx = remote.gameObject.AddComponent<RemotePlayerEffects>();
                }
            }
            fx?.Play(msg.EventType, pos);
        }

        private void OnPickupClaim(PickupClaimBroadcast msg)
        {
            NetworkManager net = NetworkManager.Instance;
            bool isMine = net != null && msg.PlayerId == net.LocalPlayerId;

            // 各端按 pickup_id 移除对应收集物；自己拾取的端在此之前已本地处理过（跟随/计分），此处兜底销毁
            PickupRegistry.MarkClaimed(msg.PickupId, msg.PlayerId, isMine);
        }

        private void OnItemStateEvent(ItemStateEventBroadcast msg)
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || msg.PlayerId == net.LocalPlayerId) return; // 所有者端本地已表现

            ItemLifecycleSync.ApplyRemote(msg.ItemInstanceId, msg.StateType);
        }

        /// <summary>远端对局中道具位置广播到达：按 player_id + item_id 寻址应用到本端实例（当前仅声控浮桥响应）</summary>
        private void OnItemPositionSync(ItemPositionSyncBroadcast msg)
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || msg.PlayerId == net.LocalPlayerId) return; // 所有者端本地已驱动
            if (msg.Position == null) return;

            if (ItemLifecycleSync.FindByOwnerAndPrefab(msg.PlayerId, msg.ItemId) is VoicePath voicePath)
            {
                voicePath.ApplyRemotePosition(new Vector2(msg.Position.X, msg.Position.Y));
            }
        }

        private void OnTrapKill(TrapKillBroadcast msg)
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || msg.PlayerId == net.LocalPlayerId) return; // 受害者端本地已记账

            // 其他端（含陷阱主所在端）为放置者记一次陷阱有效击杀，
            // 陷阱主端据此把 TrapKill 分计入自己的 RoundScoreReport
            TrapKillReporter.RecordLocal(msg.OwnerPlayerId);
        }

        private static PlayerController FindRemotePlayer(string playerId)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null) return null;

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].IdentityKey == playerId)
                {
                    return players[i];
                }
            }
            return null;
        }
    }
}

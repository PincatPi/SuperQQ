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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager.Instance == null) return;
            NetworkManager.Instance.Unregister<PlayerEventBroadcast>();
            NetworkManager.Instance.Unregister<PickupClaimBroadcast>();
            NetworkManager.Instance.Unregister<ItemStateEventBroadcast>();
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

        /// <summary>上报拾取请求（服务器先到先得裁决后广播）</summary>
        public static void ReportPickup(string pickupId)
        {
            if (!BReady) return;
            NetworkManager net = NetworkManager.Instance;
            net.Send(new PickupClaim
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                PickupId = pickupId
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

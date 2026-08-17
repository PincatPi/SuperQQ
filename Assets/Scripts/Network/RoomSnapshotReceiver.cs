using System.Collections.Generic;
using Minigame.Room.V1;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 房间快照接收器（关卡场景中放置一个即可）。
    /// 订阅网关推送的 RoomSnapshot，把每个远程玩家的 TransformState
    /// 分发给对应的 RemotePlayerSync 做插值表现。
    ///
    /// 与 InputReporter 配合构成"纯转发 + 客户端权威"闭环：
    ///   本地：InputReporter 上报自身状态 → 服务器转发
    ///   远程：本组件收快照 → RemotePlayerSync 插值显示
    /// </summary>
    public class RoomSnapshotReceiver : MonoBehaviour
    {
        // playerId -> 远程玩家同步器
        private readonly Dictionary<string, RemotePlayerSync> _remotePlayers = new();

        /// <summary>最近一次收到的房间快照（供玩家列表 UI 等读取），无快照时为 null</summary>
        public RoomSnapshot LatestSnapshot { get; private set; }

        /// <summary>
        /// 自动创建 + 跨场景存活：大厅流程（Hall→Room→Level1）不经过 NetDebugBootstrap，
        /// 移动同步组件需自行存在。进房后由 NetworkManager 驱动；未进房时注册着但不产生行为。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindFirstObjectByType<RoomSnapshotReceiver>() != null) return;
            if (NetworkManager.Instance == null) return;

            var go = new GameObject(nameof(RoomSnapshotReceiver));
            DontDestroyOnLoad(go);
            go.AddComponent<RoomSnapshotReceiver>();
            go.AddComponent<LocalPlayerNetSetup>();
        }

        private void OnEnable()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Register<RoomSnapshot>(OnRoomSnapshot);
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Unregister<RoomSnapshot>();
            }
            _remotePlayers.Clear();
        }

        private bool _firstSnapshotLogged;

        private void OnRoomSnapshot(RoomSnapshot snapshot)
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(net.LocalPlayerId)) return;

            LatestSnapshot = snapshot;

            if (!_firstSnapshotLogged)
            {
                _firstSnapshotLogged = true;
                Debug.Log($"[NetWork] 首个房间快照到达: room={snapshot.RoomId} tick={snapshot.Tick} 玩家数={snapshot.Players.Count}");
            }

            foreach (RoomPlayerState playerState in snapshot.Players)
            {
                string playerId = playerState.Player?.PlayerId;
                if (string.IsNullOrEmpty(playerId)) continue;

                // 跳过自己：本地玩家由本地状态机驱动，不受快照影响
                if (playerId == net.LocalPlayerId) continue;

                RemotePlayerSync sync = GetOrCreateRemoteSync(playerId, playerState);
                sync?.ApplySnapshot(playerState.Transform);

                // 同步远端玩家的存活/幽灵/通关状态到 Registry
                // 驱动相机目标组过滤、全员出局检测等依赖状态的逻辑
                if (sync != null)
                {
                    SyncRemotePlayerState(sync, playerState.Transform);
                }
            }
        }

        /// <summary>
        /// 把快照中的远端玩家状态（0=存活 1=幽灵 2=已通关）同步到 LevelPlayerRegistry
        /// 仅状态变化时更新，避免每次快照都触发状态变更事件
        /// </summary>
        private void SyncRemotePlayerState(RemotePlayerSync sync, TransformState transform)
        {
            if (transform == null || LevelPlayerRegistry.Instance == null) return;

            PlayerController player = sync.GetComponent<PlayerController>();
            if (player == null) return;

            PlayerStateType stateType = transform.PlayerState switch
            {
                1 => PlayerStateType.Ghost,
                2 => PlayerStateType.Finished,
                _ => PlayerStateType.Alive
            };

            if (LevelPlayerRegistry.Instance.GetPlayerState(player) != stateType)
            {
                LevelPlayerRegistry.Instance.UpdatePlayerState(player, stateType);
            }
        }

        /// <summary>
        /// 按 playerId 查找场景中的远程玩家并挂载同步组件；
        /// 玩家尚未生成（快照先于场景加载到达）时返回 null，等下一帧快照再试
        /// </summary>
        private RemotePlayerSync GetOrCreateRemoteSync(string playerId, RoomPlayerState state)
        {
            if (_remotePlayers.TryGetValue(playerId, out RemotePlayerSync cached) && cached != null)
            {
                return cached;
            }

            PlayerController player = FindPlayerById(playerId);
            if (player == null) return null;

            RemotePlayerSync sync = player.GetComponent<RemotePlayerSync>();
            if (sync == null)
            {
                sync = player.gameObject.AddComponent<RemotePlayerSync>();
            }

            _remotePlayers[playerId] = sync;
            Debug.Log($"[NetWork] 远程玩家 {state.Player?.Nickname}({playerId}) 已接入同步");
            return sync;
        }

        private PlayerController FindPlayerById(string playerId)
        {
            if (LevelPlayerRegistry.Instance == null) return null;

            IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].PlayerId == playerId)
                {
                    return players[i];
                }
            }

            // 晚进房的玩家：场景中还没有化身，注册档案并补生成
            SpawnLateJoiner(playerId, FindPlayerState(playerId));
            return null;
        }

        /// <summary>从最近快照中查某玩家的完整状态（取昵称/色号用）</summary>
        private RoomPlayerState FindPlayerState(string playerId)
        {
            RoomSnapshot snapshot = LatestSnapshot;
            if (snapshot == null) return null;
            foreach (RoomPlayerState p in snapshot.Players)
            {
                if (p.Player?.PlayerId == playerId) return p;
            }
            return null;
        }

        private void SpawnLateJoiner(string playerId, RoomPlayerState playerState)
        {
            PlayerSessionManager session = PlayerSessionManager.Instance;
            if (session == null || session.HasPlayerByIdentity(playerId)) return;

            string nickname = playerState?.Player?.Nickname;
            session.RegisterProfile(new PlayerProfile
            {
                PlayerId = playerId,
                IsLocal = false,
                PlayerName = string.IsNullOrEmpty(nickname) ? $"Remote_{playerId}" : nickname,
                PlayerColor = PlayerColorPalette.Get(playerState?.ColorIndex ?? -1)
            });

            LevelPlayerRegistry.Instance?.SpawnMissingPlayerAvatars();
            Debug.Log($"[NetWork] 晚进房玩家 {playerId} 已生成化身");
        }
    }
}

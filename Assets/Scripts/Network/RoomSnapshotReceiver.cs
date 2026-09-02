using System.Collections.Generic;
using Minigame.Room.V1;
using SuperQQ.Grid;
using SuperQQ.Item;
using SuperQQ.Player;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

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

            // 房间号不匹配（退房瞬间旧房间的迟到快照）：直接丢弃，避免污染 LatestSnapshot
            // 及玩家列表 UI；未在房（RoomId 为空）时同样不接收任何快照
            if (string.IsNullOrEmpty(net.RoomId) || snapshot.RoomId != net.RoomId) return;

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

            // 恢复已摆放道具：断连/迟到时 ItemPlaceResult 已错过，靠快照里的 placed_items 补齐
            RestorePlacedItems(snapshot);

            // 随机事件触发翻牌：快照全量重复下发，Announcer 内部按轮去重；
            // 触发时刻以服务器时钟为锚点，两端对齐引爆，迟到/断线重连立即补爆
            if (snapshot.EventTriggered)
            {
                SuperQQ.Event.LevelEventAnnouncer.Instance?.OnServerEventTriggered(snapshot.EventTriggeredAtMs);

                // 联调诊断：事件已触发但服务端始终未携带参数包（一次性告警，随触发复位重置）。
                // 仅在触发后从未收到过参数包时告警——波次间隙服务端不携带参数属于正常行为
                if (snapshot.EventParams1 == null && !_bLoggedEventParamsReceived && !_bLoggedMissingEventParams)
                {
                    _bLoggedMissingEventParams = true;
                    Debug.LogWarning("[RoomSnapshotReceiver] 事件已触发但快照未携带 event_params1——陨石波次等待服务端下发参数包（请后端确认触发后是否在快照中填充 event_params1）");
                }
            }
            else
            {
                _bLoggedMissingEventParams = false;
                _bLoggedEventParamsReceived = false;
            }

            // 随机事件参数（陨石波次等）：事件触发后随快照持续下发，
            // 首包驱动客户端生成、后续包做位置校验；Announcer 路由给对应事件 Modifier
            if (snapshot.EventParams1 != null)
            {
                if (!_bLoggedEventParamsReceived)
                {
                    _bLoggedEventParamsReceived = true;
                    Debug.Log($"[RoomSnapshotReceiver] 收到事件参数包: count={snapshot.EventParams1.Count} initial={snapshot.EventParams1.InitialPositions.Count} current={snapshot.EventParams1.CurrentPositions.Count} angles={snapshot.EventParams1.Angles.Count} speed={snapshot.EventParams1.Speed:F1}");
                }
                SuperQQ.Event.LevelEventAnnouncer.Instance?.OnServerEventParams(snapshot.EventParams1);
            }

            // 随机事件2参数（冰冻事件）：事件触发后随快照下发，服务端决定冰冻持续时间
            if (snapshot.EventParams2 != null)
            {
                if (!_bLoggedEventParams2Received)
                {
                    _bLoggedEventParams2Received = true;
                    Debug.Log($"[RoomSnapshotReceiver] 收到事件2参数包: unfreeze={snapshot.EventParams2.Unfreeze}");
                }
                SuperQQ.Event.LevelEventAnnouncer.Instance?.OnServerEventParams2(snapshot.EventParams2);
            }

            // 随机事件3玩家状态（言出法随：子类型/检测声音/劈/音量超标玩家列表）：
            // 事件期间随快照全量重复下发，Announcer 路由给事件3 Modifier（内部边沿触发去重）；
            // map 由多变空时补发一次空 map，供 Modifier 清理服务端驱动的表现
            if (snapshot.Event3States.Count > 0 || _bHadEvent3States)
            {
                if (!_bLoggedEvent3StatesReceived && snapshot.Event3States.Count > 0)
                {
                    _bLoggedEvent3StatesReceived = true;
                    Debug.Log($"[RoomSnapshotReceiver] 收到事件3状态包: players={snapshot.Event3States.Count}");
                }
                _bHadEvent3States = snapshot.Event3States.Count > 0;
                if (!_bHadEvent3States)
                {
                    _bLoggedEvent3StatesReceived = false;
                }
                SuperQQ.Event.LevelEventAnnouncer.Instance?.OnServerEvent3States(snapshot.Event3States);
            }
        }

        // 联调诊断用日志去重标志（随事件触发状态复位）
        private bool _bLoggedMissingEventParams;
        private bool _bLoggedEventParamsReceived;
        private bool _bLoggedEventParams2Received;
        private bool _bLoggedEvent3StatesReceived;

        // 上一帧快照是否携带过事件3状态（由多变空时补发一次空 map 用）
        private bool _bHadEvent3States;

        // 已恢复的远端道具：anchorCell key -> 实例，避免每次快照重复生成
        private readonly HashSet<string> _restoredItems = new();

        // 本地兜底拆除但服务器未裁定（placed_items 中仍存在）的道具 key：
        // 阻止 RestorePlacedItems 把已拆道具重新生成（"复活"）。
        // 跨轮次保留——服务器的陈旧记录不会自行消失；退房时随 ClearRoomState 清空
        private readonly HashSet<string> _demolishedItems = new();

        /// <summary>
        /// 标记某锚点的道具已被本地兜底拆除（PropPlacementDirector 收到拆除结果时调用）：
        /// 之后快照恢复跳过该条目，防止已拆道具被快照重新生成
        /// </summary>
        public void MarkItemDemolished(string itemId, Vector2Int anchor)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            _demolishedItems.Add($"{itemId}_{anchor.x}_{anchor.y}");
        }

        /// <summary>
        /// 新一轮开始（进入道具选择阶段）时清空已恢复记录：
        /// 本组件跨场景存活，不清空会导致新一轮同 itemId 同锚点的道具被误判"已恢复"而永远补不出来
        /// </summary>
        public void ClearRestoredItems() => _restoredItems.Clear();

        /// <summary>
        /// 退房/离房时清空全部房间态：快照引用、远端同步器缓存、道具恢复记录及诊断标志。
        /// 本组件跨场景存活，不清空会让旧房间数据残留到下一个房间
        /// （旧快照驱动玩家列表 UI、同 itemId 同锚点道具被误判"已恢复"而永远补不出来）。
        /// 由 LeaveRoomButton 在退房流程中调用。
        /// </summary>
        public void ClearRoomState()
        {
            LatestSnapshot = null;
            _remotePlayers.Clear();
            _restoredItems.Clear();
            _demolishedItems.Clear();
            _firstSnapshotLogged = false;
            _bHadEvent3States = false;
            _bLoggedMissingEventParams = false;
            _bLoggedEventParamsReceived = false;
            _bLoggedEventParams2Received = false;
            _bLoggedEvent3StatesReceived = false;

            // 同物体的本地玩家接入组件一并复位：下一局重新轮询写入身份/挂载上报组件
            LocalPlayerNetSetup setup = GetComponent<LocalPlayerNetSetup>();
            if (setup != null)
            {
                setup.ResetSetup();
            }
        }

        /// <summary>
        /// 按快照恢复房间内已摆放的道具：本地还没有的（错过实时广播 / 阶段边界迟到被丢弃 /
        /// 本地确认后实体异常丢失）就实例化并登记占用。本地玩家自己的道具同样补放——
        /// 判重逻辑（占据物归属+类型 / 附着物类型）已能识别"本地已有实体"的正常情况。
        /// </summary>
        private void RestorePlacedItems(RoomSnapshot snapshot)
        {
            if (snapshot.PlacedItems == null || snapshot.PlacedItems.Count == 0) return;
            if (GridManager.Instance == null) return;

            foreach (PlacedItemState placed in snapshot.PlacedItems)
            {
                if (placed.AnchorCell == null) continue;

                string key = $"{placed.ItemId}_{placed.AnchorCell.X}_{placed.AnchorCell.Y}";
                if (_restoredItems.Contains(key)) continue;
                if (_demolishedItems.Contains(key)) continue; // 本地已兜底拆除：服务器记录陈旧，跳过防止复活

                ItemBase prefab = FindItemPrefab(placed.ItemId);
                if (prefab == null)
                {
                    Debug.LogWarning($"[NetWork] 快照恢复道具失败：itemId={placed.ItemId} 无对应 prefab");
                    continue;
                }

                // 判重：锚点格被占用时，只有确认占据物就是这条快照道具（同摆放者+同类型，
                // 即实时 ItemPlaceResult 已生成过）才标记已恢复；被其他道具占用时不标记，
                // 后续快照继续重试，防止把迟到广播里的真实道具永久误杀。
                // 附着类道具不占格子，改查附着物注册表判重（同锚点可能合法存在承载物占据）
                var anchorCell = new Vector2Int(placed.AnchorCell.X, placed.AnchorCell.Y);
                if (prefab.RegistersOccupancy)
                {
                    PlacedItem occupant = GridManager.Instance.GetItemAt(anchorCell);
                    if (occupant != null)
                    {
                        ItemBase occupantItem = occupant.GetComponent<ItemBase>();
                        bool bSameItem = occupant.OwnerKey == placed.PlayerId
                            && occupantItem != null
                            && occupantItem.GetType() == prefab.GetType();
                        if (bSameItem)
                        {
                            _restoredItems.Add(key);
                        }
                        continue;
                    }
                }
                else
                {
                    bool bAlreadyAttached = false;
                    foreach (ItemBase attachment in GridManager.Instance.GetAttachments(anchorCell))
                    {
                        if (attachment != null && attachment.GetType() == prefab.GetType())
                        {
                            bAlreadyAttached = true;
                            break;
                        }
                    }
                    if (bAlreadyAttached)
                    {
                        _restoredItems.Add(key);
                        continue;
                    }
                }

                GridManager grid = GridManager.Instance;
                var anchor = new Vector2Int(placed.AnchorCell.X, placed.AnchorCell.Y);
                FootprintBoxView prefabBox = prefab.GetComponent<FootprintBoxView>();
                Vector2Int footprint = prefabBox != null ? prefabBox.Footprint : Vector2Int.one;

                Vector2 worldPos = grid.GetPlacementWorldPos(anchor, footprint, placed.Rotation);
                GameObject item = Instantiate(prefab.gameObject, worldPos,
                    GridManager.GetRotationQuaternion(placed.Rotation));
                item.name = $"Restored_{placed.PlayerId}_{prefab.name}";

                var placedItem = item.AddComponent<PlacedItem>();
                placedItem.Init(null, anchor, placed.Rotation, -1);
                placedItem.SetOwnerKey(placed.PlayerId); // 陷阱击杀计分归属
                // 附着类道具（RegistersOccupancy=false，如黄油块）不登记占据——与各端实时口径一致
                if (prefab.RegistersOccupancy)
                {
                    grid.Occupy(anchor, footprint, placedItem, placed.Rotation);
                }

                PlacementController pc = item.GetComponent<PlacementController>();
                if (pc != null)
                {
                    pc.DebugHotkeys = false;
                    pc.enabled = false;
                }
                ItemBase itemBase = item.GetComponent<ItemBase>();
                if (itemBase != null)
                {
                    itemBase.NetItemId = placed.ItemId; // ItemLifecycleSync 实例键与所有者端一致
                    itemBase.InitPlaced(placedItem, placed.Rotation);
                    itemBase.SetMirrored(placed.Mirrored); // 镜像朝向同步（樱桃发射器/流星锤等）
                    itemBase.OnPlaced();
                }

                // 传送门诊断：打印生成时的配对状态（两端各自出现在 placed_items 时会自动配对）
                if (itemBase is SuperQQ.Item.Portal p)
                {
                    Debug.Log($"[Portal] 快照恢复传送门: owner={placed.PlayerId} anchor=({placed.AnchorCell.X},{placed.AnchorCell.Y}) linked={p.IsLinked} entrance={p.IsEntrance}");
                }

                _restoredItems.Add(key);
                Debug.Log($"[NetWork] 快照恢复道具: {prefab.name}(itemId={placed.ItemId}) @ ({anchor.x},{anchor.y}) 摆放者={placed.PlayerId}");
            }
        }

        /// <summary>按 itemId 查道具 prefab：目录数字代号优先，名字兜底，最后走选择阶段发牌解析映射</summary>
        private static ItemBase FindItemPrefab(string itemId)
        {
            if (ItemCatalog.Instance != null)
            {
                ItemBase byId = ItemCatalog.Instance.Find(itemId);
                if (byId != null) return byId;
                ItemBase byName = ItemCatalog.Instance.FindByPrefabName(itemId);
                if (byName != null) return byName;
            }
            // 与实时摆放路径（PropPlacementDirector.FindPoolItem）同级兜底：
            // 传送门等未登记 ItemCatalog 的道具，选择阶段已按 offer.ItemId 解析出 prefab
            return SuperQQ.Selection.Runtime.PropSelectionDirector.ResolveOfferPrefab(itemId);
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
                3 => PlayerStateType.Frozen,   // 冻结（液氮事件）：仍在场，远端挂载冰封视觉
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

        /// <summary>按房间快照列表下标取座位/颜色索引（进房顺序，两端一致）；找不到返回 -1</summary>
        private int GetSeatIndex(string playerId)
        {
            RoomSnapshot snapshot = LatestSnapshot;
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Players.Count; i++)
                {
                    if (snapshot.Players[i].Player?.PlayerId == playerId)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private void SpawnLateJoiner(string playerId, RoomPlayerState playerState)
        {
            PlayerSessionManager session = PlayerSessionManager.Instance;
            if (session == null || session.HasPlayerByIdentity(playerId)) return;

            string nickname = playerState?.Player?.Nickname;
            // 颜色/角色按房间列表下标（进房顺序，两端一致），与 NetGameFlowGate 的分配规则统一
            int seatIndex = GetSeatIndex(playerId);
            session.RegisterProfile(new PlayerProfile
            {
                PlayerId = playerId,
                IsLocal = false,
                PlayerName = string.IsNullOrEmpty(nickname) ? $"Remote_{playerId}" : nickname,
                PlayerColor = PlayerColorPalette.Get(seatIndex),
                CharacterIndex = seatIndex
            });

            LevelPlayerRegistry.Instance?.SpawnMissingPlayerAvatars();
            Debug.Log($"[NetWork] 晚进房玩家 {playerId} 已生成化身");
        }
    }
}

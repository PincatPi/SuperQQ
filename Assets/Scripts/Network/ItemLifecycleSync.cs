using System.Collections.Generic;
using Minigame.Room.V1;
using SuperQQ.Grid;
using SuperQQ.Item;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 道具生命周期同步：按 item_instance_id（"{itemId}_{anchorX}_{anchorY}"）登记场上已摆放道具，
    /// 所有者端在触发/销毁瞬间经 NetEventSync.ReportItemState 上报，各端收到广播后同步状态。
    ///
    /// 计时由各端本地推演（摆放确认即开始计时，各端起始时刻差在百毫秒级，可接受）；
    /// 只有"触发瞬间"和"销毁"走网络事件，保证爆炸/销毁表现一致。
    /// </summary>
    public static class ItemLifecycleSync
    {
        private static readonly Dictionary<string, ItemBase> _items = new();

        /// <summary>由锚点格子生成道具实例ID（与摆放仲裁结果对应，各端一致）</summary>
        public static string MakeInstanceId(string itemId, Vector2Int anchorCell) => $"{itemId}_{anchorCell.x}_{anchorCell.y}";

        /// <summary>
        /// 解析道具的网络 itemId：优先读摆放路径写入的 NetItemId（各端一致）；
        /// 未写入时回退 GameObject 名去 "(Clone)" 后缀后再经目录反查数字 ID，
        /// 保证所有者端上报的实例键与远端登记键一致（远端实体名带 "RemotePlaced_" 等前缀）
        /// </summary>
        public static string ResolveItemId(ItemBase item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrEmpty(item.NetItemId)) return item.NetItemId;

            string n = item.name;
            int cloneIdx = n.IndexOf("(Clone)", System.StringComparison.Ordinal);
            if (cloneIdx > 0)
            {
                n = n.Substring(0, cloneIdx).TrimEnd();
            }
            if (ItemCatalog.Instance != null)
            {
                ItemBase prefab = ItemCatalog.Instance.FindByPrefabName(n);
                if (prefab != null)
                {
                    return ItemCatalog.Instance.GetItemId(prefab) ?? n;
                }
            }
            return n;
        }

        /// <summary>
        /// 求道具的 prefab 名（ItemPositionSync 协议的 item_id）：优先取 Placed.Def.Prefab 名（各端一致）。
        /// 远端生成/快照恢复的实例 Def 为 null（两条路径均 Init(null, ...)），回退 GameObject 名解析：
        /// 去 "(Clone)" 后缀，并按 OwnerKey 剥掉 "RemotePlaced_{owner}_" / "Restored_{owner}_" 前缀
        /// </summary>
        public static string ResolvePrefabName(ItemBase item)
        {
            if (item == null) return string.Empty;
            if (item.Placed?.Def != null && item.Placed.Def.Prefab != null)
            {
                return item.Placed.Def.Prefab.name;
            }

            string n = item.name;
            int cloneIdx = n.IndexOf("(Clone)", System.StringComparison.Ordinal);
            if (cloneIdx > 0)
            {
                n = n.Substring(0, cloneIdx).TrimEnd();
            }

            string ownerKey = item.Placed != null ? item.Placed.OwnerKey : null;
            if (!string.IsNullOrEmpty(ownerKey))
            {
                // PropPlacementDirector: RemotePlaced_{playerId}_{itemId}；RoomSnapshotReceiver: Restored_{playerId}_{prefabName}
                foreach (string head in new[] { "RemotePlaced_", "Restored_" })
                {
                    string prefix = head + ownerKey + "_";
                    if (n.StartsWith(prefix, System.StringComparison.Ordinal) && n.Length > prefix.Length)
                    {
                        return n.Substring(prefix.Length);
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// 按放置者 + prefab 名查找场上已登记道具（ItemPositionSync 广播按 player_id + item_id 寻址）。
        /// 匹配键：prefab 名（ResolvePrefabName）或 NetItemId——远端实例的 NetItemId 可能是目录数字ID
        /// （PlacementSession 优先用数字ID上报），故同时用 ItemCatalog 反查数字ID 做等价比较。
        /// 未命中或已销毁返回 null
        /// </summary>
        public static ItemBase FindByOwnerAndPrefab(string ownerKey, string prefabName)
        {
            if (string.IsNullOrEmpty(ownerKey) || string.IsNullOrEmpty(prefabName)) return null;

            // 反查 prefab 名的目录数字ID（目录未配置该道具时为 null）
            string catalogId = null;
            if (ItemCatalog.Instance != null)
            {
                ItemBase prefab = ItemCatalog.Instance.FindByPrefabName(prefabName);
                if (prefab != null)
                {
                    catalogId = ItemCatalog.Instance.GetItemId(prefab);
                }
            }

            foreach (ItemBase item in _items.Values)
            {
                if (item == null || item.Placed == null) continue;
                if (item.Placed.OwnerKey != ownerKey) continue;
                if (ResolvePrefabName(item) == prefabName)
                {
                    return item;
                }
                if (!string.IsNullOrEmpty(item.NetItemId)
                    && (item.NetItemId == prefabName || (catalogId != null && item.NetItemId == catalogId)))
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>登记一个已确认摆放的道具（本地确认与远端摆放两条路径都调用）</summary>
        public static void Register(ItemBase item)
        {
            if (item?.Placed == null) return;
            _items[MakeInstanceId(ResolveItemId(item), item.Placed.AnchorCell)] = item;
        }

        /// <summary>注销（道具自身销毁路径调用，幂等）</summary>
        public static void Unregister(ItemBase item)
        {
            if (item?.Placed == null) return;
            string id = MakeInstanceId(ResolveItemId(item), item.Placed.AnchorCell);
            if (_items.TryGetValue(id, out ItemBase existing) && existing == item)
            {
                _items.Remove(id);
            }
        }

        /// <summary>所有者端：道具触发/销毁瞬间上报（内部先本地登记移除，再发网络事件）</summary>
        public static void ReportTriggered(ItemBase item)
        {
            if (item?.Placed == null) return;
            NetEventSync.ReportItemState(MakeInstanceId(ResolveItemId(item), item.Placed.AnchorCell), ItemStateType.Triggered);
        }

        public static void ReportDestroyed(ItemBase item)
        {
            if (item?.Placed == null) return;
            NetEventSync.ReportItemState(MakeInstanceId(ResolveItemId(item), item.Placed.AnchorCell), ItemStateType.Destroyed);
        }

        /// <summary>远端广播到达：对本地对应实例应用状态（非所有者端的表现同步）</summary>
        public static void ApplyRemote(string itemInstanceId, ItemStateType stateType)
        {
            if (!_items.TryGetValue(itemInstanceId, out ItemBase item) || item == null)
            {
                return;
            }

            switch (stateType)
            {
                case ItemStateType.Triggered:
                    // 触发表现：道具自身的触发动画/爆炸由本地计时器已推进，这里只兜底补播
                    // （具体道具在 OnRunPhaseStart 驱动，远端差异在百毫秒级）
                    break;
                case ItemStateType.Destroyed:
                    Unregister(item);
                    if (item.Placed != null && GridManager.Instance != null)
                    {
                        GridManager.Instance.RemoveAt(item.Placed.AnchorCell);
                    }
                    else
                    {
                        Object.Destroy(item.gameObject);
                    }
                    break;
            }
        }

        /// <summary>新一轮/退出房间时清空</summary>
        public static void ClearAll() => _items.Clear();
    }
}

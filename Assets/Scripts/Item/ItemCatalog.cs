using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 道具目录：维护"网络 itemId → 道具 prefab"的映射，供联机发牌/摆放按 ID 查找。
    /// 资产放在 Resources/Items/ItemCatalog.asset，运行时自动加载。
    ///
    /// 查找顺序：目录精确匹配 → prefab 名称匹配（兼容旧约定）。
    /// 服务器下发的 item_id 既可以是目录里配置的 ID（如 "11"），也可以是 prefab 名（如 "BlackBomb"）。
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "SuperQQ/ItemCatalog")]
    public class ItemCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("网络传输的道具ID（与服务器道具表一致，如 11 / black_bomb）")]
            public string itemId;
            [Tooltip("对应的道具 prefab（挂 ItemBase）")]
            public ItemBase prefab;
        }

        [SerializeField] private List<Entry> entries = new();

        private static ItemCatalog _instance;
        private static bool _loadTried;

        /// <summary>目录实例（Resources/Items/ItemCatalog 自动加载；未配置时为 null）</summary>
        public static ItemCatalog Instance
        {
            get
            {
                if (!_loadTried)
                {
                    _loadTried = true;
                    _instance = Resources.Load<ItemCatalog>("Items/ItemCatalog");
                }
                return _instance;
            }
        }

        /// <summary>按网络 itemId 查 prefab；未命中返回 null</summary>
        public ItemBase Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e != null && e.prefab != null && e.itemId == itemId)
                {
                    return e.prefab;
                }
            }
            return null;
        }

        /// <summary>反查：由 prefab 求数字 itemId（摆放上报时用，与服务器发牌的代号一致）；未命中返回 null</summary>
        public string GetItemId(ItemBase prefab)
        {
            if (prefab == null) return null;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e != null && e.prefab == prefab)
                {
                    return e.itemId;
                }
            }
            return null;
        }

        /// <summary>按 prefab 名称查找（服务器下发名字而非 itemId 时的兜底）；未命中返回 null</summary>
        public ItemBase FindByPrefabName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e != null && e.prefab != null && e.prefab.name == prefabName)
                {
                    return e.prefab;
                }
            }
            return null;
        }

        /// <summary>列出目录中全部 itemId（排查映射失败时打日志用）</summary>
        public string DumpIds()
        {
            var names = new List<string>(entries.Count);
            foreach (Entry e in entries)
            {
                if (e?.prefab != null) names.Add($"{e.itemId}({e.prefab.name})");
            }
            return string.Join(", ", names);
        }
    }
}

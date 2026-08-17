using System;
using System.Collections.Generic;
using SuperQQ.Item;
using UnityEngine;

namespace SuperQQ.Selection.Core
{
    /// <summary>
    /// 道具选择会话（纯 C#，由场景层驱动）。
    /// 进入阶段时由外部 <see cref="RollOffers"/> 从候选池随机抽取一批不重复道具作为本轮候选；
    /// 每名玩家通过 <see cref="TrySelect"/> 认领其中一件：已被认领的槽位对其他玩家不可再选，
    /// 每名玩家每轮最多认领一件，认领即确认、不可更改。
    /// 不读取任何输入、不持有 Unity 生命周期，点击操作由外部喂入；
    /// 多玩家互斥认领在会话层保证，联机接入时本地与远端玩家的选中走同一入口。
    /// </summary>
    public class SelectionSession
    {
        private readonly List<ItemBase> offerItems = new List<ItemBase>();
        private readonly List<string> offerClaims = new List<string>(); // 与 offerItems 对齐，null = 未被认领
        private readonly Dictionary<string, int> playerSelections = new Dictionary<string, int>();

        /// <summary>任一玩家成功认领候选道具时触发</summary>
        public event Action<SelectionResult> OnOfferClaimed;

        /// <summary>本轮候选道具（按槽位下标排列）</summary>
        public IReadOnlyList<ItemBase> OfferItems => offerItems;

        /// <summary>候选槽位数量</summary>
        public int OfferCount => offerItems.Count;

        /// <summary>指定槽位是否已被认领</summary>
        public bool BIsClaimed(int slotIndex)
        {
            return IsValidSlot(slotIndex) && offerClaims[slotIndex] != null;
        }

        /// <summary>指定槽位的认领者标识；未认领或下标非法时为 null</summary>
        public string GetClaimer(int slotIndex)
        {
            return IsValidSlot(slotIndex) ? offerClaims[slotIndex] : null;
        }

        /// <summary>指定玩家本轮是否已认领道具</summary>
        public bool BHasSelection(string playerKey)
        {
            return !string.IsNullOrEmpty(playerKey) && playerSelections.ContainsKey(playerKey);
        }

        /// <summary>指定玩家认领的道具；未认领时为 null</summary>
        public ItemBase GetSelectedItem(string playerKey)
        {
            if (!string.IsNullOrEmpty(playerKey)
                && playerSelections.TryGetValue(playerKey, out int slotIndex)
                && IsValidSlot(slotIndex))
            {
                return offerItems[slotIndex];
            }
            return null;
        }

        /// <summary>
        /// 从候选池随机抽取一批不重复道具作为本轮候选（不放回抽样）。
        /// 池内重复条目会去重；池内有效道具不足时按实际数量发牌。
        /// </summary>
        /// <param name="pool">候选道具池（挂有 ItemBase 的 prefab）</param>
        /// <param name="desiredCount">期望的候选数量，小于等于 0 时清空候选</param>
        /// <returns>实际抽取的候选数量</returns>
        public int RollOffers(IReadOnlyList<ItemBase> pool, int desiredCount)
        {
            offerItems.Clear();
            offerClaims.Clear();
            playerSelections.Clear();

            if (pool == null || desiredCount <= 0)
            {
                return 0;
            }

            // 收集有效且互不重复的候选
            List<ItemBase> valid = new List<ItemBase>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !valid.Contains(pool[i]))
                {
                    valid.Add(pool[i]);
                }
            }

            // Fisher-Yates 洗牌后取前 desiredCount 件
            for (int i = valid.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                ItemBase temp = valid[i];
                valid[i] = valid[j];
                valid[j] = temp;
            }

            int count = Mathf.Min(desiredCount, valid.Count);
            for (int i = 0; i < count; i++)
            {
                offerItems.Add(valid[i]);
                offerClaims.Add(null);
            }
            return count;
        }

        /// <summary>
        /// 联机模式：由服务器下发的道具列表直接设置本轮候选（替代本地 RollOffers）。
        /// 列表顺序即槽位下标（slot_index 从 0 开始连续）。
        /// </summary>
        public void SetOffers(IReadOnlyList<ItemBase> items)
        {
            offerItems.Clear();
            offerClaims.Clear();
            playerSelections.Clear();

            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                offerItems.Add(items[i]);
                offerClaims.Add(null);
            }
        }

        /// <summary>
        /// 认领指定槽位的道具：槽位未被认领且该玩家本轮尚未认领时成功。
        /// 认领即确认、不可更改；失败（已认领/已选过/下标非法）时不产生任何副作用。
        /// </summary>
        /// <param name="playerKey">认领者标识（PlayerController.IdentityKey）</param>
        /// <param name="slotIndex">候选槽位下标</param>
        /// <returns>认领成功返回 true</returns>
        public bool TrySelect(string playerKey, int slotIndex)
        {
            if (string.IsNullOrEmpty(playerKey) || !IsValidSlot(slotIndex))
            {
                return false;
            }
            if (BHasSelection(playerKey) || BIsClaimed(slotIndex))
            {
                return false;
            }

            offerClaims[slotIndex] = playerKey;
            playerSelections[playerKey] = slotIndex;
            OnOfferClaimed?.Invoke(new SelectionResult(playerKey, offerItems[slotIndex].name, slotIndex));
            return true;
        }

        private bool IsValidSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < offerItems.Count;
        }
    }
}

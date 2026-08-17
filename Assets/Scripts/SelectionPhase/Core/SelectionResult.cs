namespace SuperQQ.Selection.Core
{
    /// <summary>
    /// 一次已确认选择的结果。
    /// 作为选择流程对外发布的事件载荷；后续联机时可直接映射为网络包体。
    /// </summary>
    public readonly struct SelectionResult
    {
        /// <summary>选择者标识（PlayerController.IdentityKey）</summary>
        public readonly string PlayerKey;

        /// <summary>道具标识（本期为 prefab 名，后续可切换为 PlacableItemDef.ItemId）</summary>
        public readonly string ItemId;

        /// <summary>被选中的候选槽位下标</summary>
        public readonly int SlotIndex;

        public SelectionResult(string playerKey, string itemId, int slotIndex)
        {
            PlayerKey = playerKey;
            ItemId = itemId;
            SlotIndex = slotIndex;
        }

        public override string ToString()
        {
            return $"{PlayerKey} 选中 {ItemId}（槽位 {SlotIndex}）";
        }
    }
}

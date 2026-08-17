using UnityEngine;

namespace SuperQQ.Placement.Core
{
    /// <summary>
    /// 一次已确认放置的结果。
    /// 作为放置流程对外发布的事件载荷；后续联机时可直接映射为网络包体。
    /// </summary>
    public readonly struct PlacementResult
    {
        /// <summary>放置者标识（PlayerController.IdentityKey）</summary>
        public readonly string PlayerKey;

        /// <summary>道具标识（本期为 prefab 名，后续可切换为 PlacableItemDef.ItemId）</summary>
        public readonly string ItemId;

        /// <summary>占位矩形左下角锚点格子</summary>
        public readonly Vector2Int AnchorCell;

        /// <summary>是否处于旋转 90° 状态</summary>
        public readonly bool BRotated;

        public PlacementResult(string playerKey, string itemId, Vector2Int anchorCell, bool bRotated)
        {
            PlayerKey = playerKey;
            ItemId = itemId;
            AnchorCell = anchorCell;
            BRotated = bRotated;
        }

        public override string ToString()
        {
            return $"{PlayerKey} 放置 {ItemId} @ {AnchorCell}{(BRotated ? "（旋转）" : string.Empty)}";
        }
    }
}

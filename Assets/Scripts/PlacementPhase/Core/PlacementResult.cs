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

        /// <summary>旋转档：0=0° 1=顺时针90° 2=180° 3=270°</summary>
        public readonly int Rotation;

        /// <summary>是否处于旋转状态（非 0° 档）；兼容旧读法</summary>
        public bool BRotated => Rotation != 0;

        /// <summary>左右镜像（樱桃发射器/流星锤等朝向类道具）</summary>
        public readonly bool Mirrored;

        public PlacementResult(string playerKey, string itemId, Vector2Int anchorCell, bool bRotated)
            : this(playerKey, itemId, anchorCell, bRotated ? 1 : 0)
        {
        }

        public PlacementResult(string playerKey, string itemId, Vector2Int anchorCell, int rotation, bool mirrored = false)
        {
            PlayerKey = playerKey;
            ItemId = itemId;
            AnchorCell = anchorCell;
            Rotation = ((rotation % 4) + 4) % 4;
            Mirrored = mirrored;
        }

        public override string ToString()
        {
            return $"{PlayerKey} 放置 {ItemId} @ {AnchorCell}{(Rotation != 0 ? $"（旋转{Rotation * 90}°）" : string.Empty)}";
        }
    }
}

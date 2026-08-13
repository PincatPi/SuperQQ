using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 摔炮 — 拆除类消耗品（最小范围档）
    /// 2x2 格占位，放置后立即引爆，清除 footprint 覆盖格子内的道具；
    /// 范围极小，通常仅清除落点处 1 件道具，是精准点杀工具
    /// 爆破逻辑由 DemolitionItemBase 实现，本类仅声明占位档位
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = (2,2)，canRotate = false
    /// - PlacableItemDef：category = Demolition，facingSteps = 0
    /// </summary>
    public class SnapPop : DemolitionItemBase
    {
        /// <summary>摔炮固定 2x2 占位</summary>
        protected override Vector2Int DefaultFootprint => new Vector2Int(2, 2);
    }
}

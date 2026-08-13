using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 黑炸弹 — 拆除类消耗品（中范围档）
    /// 3x3 格占位，放置后立即引爆；主力中范围拆除
    /// 爆破逻辑由 DemolitionItemBase 实现，本类仅声明占位档位
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = (3,3)，canRotate = false
    /// - PlacableItemDef：category = Demolition，facingSteps = 0
    /// </summary>
    public class BlackBomb : DemolitionItemBase
    {
        /// <summary>黑炸弹固定 3x3 占位</summary>
        protected override Vector2Int DefaultFootprint => new Vector2Int(3, 3);
    }
}

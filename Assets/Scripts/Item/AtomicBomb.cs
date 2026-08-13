using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 原子弹 — 拆除类消耗品（最大范围档）
    /// 5x5 格占位，放置后立即引爆；全场最大范围拆除，低权重稀有件
    /// （识别场上复杂度或按关卡进度增加权重，由投放/生成系统负责，本类不含权重逻辑）
    /// 爆破逻辑由 DemolitionItemBase 实现，本类仅声明占位档位
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = (5,5)，canRotate = false
    /// - PlacableItemDef：category = Demolition，facingSteps = 0
    /// - 建议在 Inspector 中将 blastExpandCells 调大（如 2~3），匹配"最大范围"定位
    /// </summary>
    public class AtomicBomb : DemolitionItemBase
    {
        /// <summary>原子弹固定 5x5 占位</summary>
        protected override Vector2Int DefaultFootprint => new Vector2Int(5, 5);
    }
}

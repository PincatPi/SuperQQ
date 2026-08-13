using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 肥皂 — 2x1，踩上后完全无摩擦，滑行不可控
    /// 表面效果由 prefab 的 HitZones/StandZone 上的 SoapSurface 组件实现（无乘算倍率）
    /// 不旋转：FootprintBoxView 的 canRotate 保持关闭
    /// </summary>
    public class SoapItem : ItemBase
    {
        public override ItemCategory Category => ItemCategory.Path;
    }
}

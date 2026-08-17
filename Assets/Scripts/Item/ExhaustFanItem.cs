using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 排气扇 — 2x2，持续吹风，推动玩家向一个方向位移
    /// 吹风区域与风力由 prefab 的 HitZones/WindZone 上的 WindZone 组件实现
    /// 可旋转（围绕中心点）：FootprintBoxView 开启 canRotate，pivotCell 自动中心格子；
    /// 吹风方向 = transform.right，随放置旋转自动联动
    /// </summary>
    public class ExhaustFanItem : ItemBase
    {
        public override ItemCategory Category => ItemCategory.Control;
    }
}

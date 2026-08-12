namespace SuperQQ.Item
{
    /// <summary>
    /// 黄油块 — 搭路类基础平台
    /// 3x0.3 格薄板，可站立通行；表面减速由 HitZones/StandZone 上的 SurfaceModifier 实现
    /// 不可旋转
    /// </summary>
    public class ButterBlock : ItemBase
    {
        public override ItemCategory Category => ItemCategory.Path;
    }
}

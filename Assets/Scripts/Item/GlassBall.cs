namespace SuperQQ.Item
{
    /// <summary>
    /// 玻璃球 — 伤害类陷阱
    /// 1x1 格，危险判定为圆形 Ø32px（0.32单位）；体积小隐蔽性强
    /// 玩家接触即死：alive → dying(0.35s) → ghost（由 KillZone 触发）
    /// 不可旋转，无实体碰撞（玩家可直接穿过触发判定）
    /// </summary>
    public class GlassBall : ItemBase
    {
        public override ItemCategory Category => ItemCategory.Hazard;
    }
}

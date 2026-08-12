namespace SuperQQ.Item
{
    /// <summary>
    /// 道具类别（与策划配置表一致，用于道具栏分组与统计）
    /// </summary>
    public enum ItemCategory
    {
        /// <summary>搭路：平台类，提供站立/通行（黄油块、披萨盒、磁带、手风琴等）</summary>
        Path = 0,
        /// <summary>伤害：对玩家造成击杀（玻璃球、大剪刀、流星锤、电击枪等）</summary>
        Hazard = 1,
        /// <summary>控制：改变玩家移动/状态（吹风机、大炮、肥皂、磁铁、传送门等）</summary>
        Control = 2,
        /// <summary>拆除：即放即爆的消耗品，摧毁范围内道具（摔炮、黑炸弹、原子弹）</summary>
        Demolition = 3,
    }
}

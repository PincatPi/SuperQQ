namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件类型枚举
    /// 定义关卡中可能触发的特殊事件种类
    /// 每关开始前由 LevelEventAnnouncer 从中选定本关事件
    /// </summary>
    public enum LevelEventType
    {
        /// <summary>
        /// 老板巡视：声音大的玩家会被攻击，安静玩家获得加分
        /// </summary>
        BossPatrol,

        /// <summary>
        /// 小蛋糕陨石：周期性从关卡顶部落下陨石，命中玩家即死并击飞
        /// </summary>
        CakeMeteor = 1
    }
}

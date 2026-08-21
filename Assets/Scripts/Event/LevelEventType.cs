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
        /// 小蛋糕陨石：周期性从关卡顶部落下陨石，命中玩家即死并击飞
        /// </summary>
        CakeMeteor = 1,

        /// <summary>
        /// 液氮泄露：随机触发一次，预警后冻结所有存活玩家，摇晃手机累积进度解冻
        /// </summary>
        LiquidNitrogenLeak = 2,

        /// <summary>
        /// 言出法随：场景中创建固定位置的法阵，存活玩家走入范围后头顶弹出吟唱提示
        /// </summary>
        MagicCircle = 3
    }
}

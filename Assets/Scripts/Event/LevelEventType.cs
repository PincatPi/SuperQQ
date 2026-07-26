namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件类型枚举
    /// 定义关卡中可能触发的随机事件种类
    /// 对应策划文档 4.6 节的两个必触发特殊事件
    /// </summary>
    public enum LevelEventType
    {
        /// <summary>
        /// 老板巡视：声音大的玩家会被攻击，安静玩家获得加分
        /// </summary>
        BossPatrol,

        /// <summary>
        /// 空调变冷：冷气命中后冻结玩家，需摇晃/连按解除
        /// </summary>
        ColdSnap
    }
}

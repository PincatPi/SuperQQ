namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家状态类型枚举
    /// 用于在管理器中记录和查询玩家的当前状态
    /// 与 IPlayerState 的具体实现类（PlayerAliveState/PlayerGhostState/PlayerFinishedState）一一对应
    /// </summary>
    public enum PlayerStateType
    {
        /// <summary>
        /// 存活状态：可移动、跳跃、被攻击
        /// </summary>
        Alive,

        /// <summary>
        /// 幽灵状态：死亡后四向飞行，无碰撞
        /// </summary>
        Ghost,

        /// <summary>
        /// 通关状态：到达终点，停止所有行为
        /// </summary>
        Finished,

        /// <summary>
        /// 冻结状态：被冰封无法操作；仍视为在场（未出局），可被击杀，解冻后恢复存活
        /// </summary>
        Frozen
    }
}

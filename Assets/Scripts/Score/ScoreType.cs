namespace SuperQQ.Score
{
    /// <summary>
    /// 得分类型枚举
    /// 对应结算系统五层纵向颁奖台的五个得分项
    /// 后出现的分数更难预测，排名牌随每层落账更新
    /// </summary>
    public enum ScoreType
    {
        /// <summary>
        /// 本次得分：通关+20
        /// </summary>
        Completion,

        /// <summary>
        /// 第一名加分：第一名+10
        /// </summary>
        FirstPlace,

        /// <summary>
        /// 独行积分：仅一人通关时该玩家+15
        /// </summary>
        SoloClear,

        /// <summary>
        /// 陷阱得分：每次有效击杀+5，最多计2次
        /// </summary>
        TrapKill,

        /// <summary>
        /// 特殊效果加分：老板巡视安静达标+10
        /// </summary>
        SpecialEffect,

        /// <summary>
        /// 得分道具得分：金币等得分道具在跟随角色通关时提供的额外加分
        /// </summary>
        ScoreItem
    }
}

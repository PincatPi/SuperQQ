using System.Collections.Generic;

namespace SuperQQ.Score
{
    /// <summary>
    /// 单玩家得分累计记录
    /// 保存跨轮累计总分、每轮明细和用于最终排序的统计量
    /// 纯数据结构，不依赖 Unity
    /// </summary>
    public class PlayerScoreRecord
    {
        /// <summary>
        /// 玩家名称（如 "P1"）
        /// </summary>
        public string PlayerName;

        /// <summary>
        /// 跨轮累计总分
        /// </summary>
        public int TotalScore;

        /// <summary>
        /// 每轮得分明细历史
        /// </summary>
        public List<RoundScoreData> RoundHistory = new();

        /// <summary>
        /// 累计通关次数（用于最终排序第二键）
        /// </summary>
        public int TotalFinishCount;

        /// <summary>
        /// 累计陷阱有效击杀次数（用于最终排序第三键）
        /// </summary>
        public int TotalTrapKillCount;
    }
}

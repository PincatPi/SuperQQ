using System.Collections.Generic;

namespace SuperQQ.Score
{
    /// <summary>
    /// 单轮得分明细数据
    /// 记录一个玩家在某一轮的五项得分、本轮合计与截至本轮的累计总分
    /// 纯数据结构，不依赖 Unity
    /// </summary>
    public class RoundScoreData
    {
        /// <summary>
        /// 轮次索引（从1开始）
        /// </summary>
        public int RoundIndex;

        /// <summary>
        /// 五项得分明细，键为 ScoreType，值为该项得分
        /// </summary>
        public Dictionary<ScoreType, int> ScoreBreakdown = new();

        /// <summary>
        /// 本轮总得分（五项之和）
        /// </summary>
        public int RoundTotal;

        /// <summary>
        /// 截至本轮的累计总分
        /// </summary>
        public int CumulativeTotal;
    }
}

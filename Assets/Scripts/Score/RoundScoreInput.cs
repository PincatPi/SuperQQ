using System.Collections.Generic;

namespace SuperQQ.Score
{
    /// <summary>
    /// 单轮结算的输入数据
    /// 由 PlayerScoreManager 在轮次结束时汇总，传给 ScoreCalculator
    /// 将"谁做了什么"与"怎么算分"完全分离
    /// 纯数据结构，不依赖 Unity
    /// </summary>
    public class RoundScoreInput
    {
        /// <summary>
        /// 通关玩家名称列表，按通关先后顺序排列
        /// 索引0为第一名，用于判定 FirstPlace 加分
        /// </summary>
        public List<string> FinishedPlayerNames = new();

        /// <summary>
        /// 每个玩家本轮陷阱有效击杀次数
        /// 键为玩家名称，值为击杀次数
        /// </summary>
        public Dictionary<string, int> TrapKillCounts = new();

        /// <summary>
        /// 每个玩家本轮的额外加分（金币等得分道具在通关时提交）
        /// 键为玩家名称，值为加分点数
        /// </summary>
        public Dictionary<string, int> BonusScores = new();
    }
}

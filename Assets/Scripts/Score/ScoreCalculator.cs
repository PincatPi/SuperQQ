using System.Collections.Generic;

namespace SuperQQ.Score
{
    /// <summary>
    /// 得分计算器
    /// 纯逻辑类，不依赖 Unity，不继承 MonoBehaviour
    /// 接收 RoundScoreInput 和此前累计分，输出每个玩家的 RoundScoreData
    /// 可独立进行单元测试
    /// </summary>
    public static class ScoreCalculator
    {
        // ==================== 得分常量 ====================

        /// <summary>
        /// 本次得分：通关+20
        /// </summary>
        private const int COMPLETION_SCORE = 20;

        /// <summary>
        /// 第一名加分：+10
        /// </summary>
        private const int FIRST_PLACE_SCORE = 10;

        /// <summary>
        /// 独行积分：仅一人通关+15
        /// </summary>
        private const int SOLO_CLEAR_SCORE = 15;

        /// <summary>
        /// 每次陷阱有效击杀+5
        /// </summary>
        private const int TRAP_KILL_SCORE_PER = 5;

        /// <summary>
        /// 陷阱得分最多计2次
        /// </summary>
        private const int MAX_TRAP_KILL_COUNT = 2;

        /// <summary>
        /// 胜利线分数：100分
        /// </summary>
        public const int VICTORY_LINE = 100;

        // ==================== 核心计算 ====================

        /// <summary>
        /// 计算一轮中所有参与玩家的得分明细
        /// 规则参考策划案的结算系统（三条全局覆盖规则）：
        ///   1. 无人通关：所有玩家六类得分全部为 0；
        ///   2. 全员通关：只结算成功带回的金币，其余五类全部为 0；
        ///   3. 部分玩家通关：执行完整计分公式，陷阱拥有者本人未通关也可获得有效击杀分。
        /// </summary>
        /// <param name="roundIndex">当前轮次索引（从1开始）</param>
        /// <param name="allPlayerNames">所有参与玩家名称列表</param>
        /// <param name="input">本轮结算输入数据</param>
        /// <param name="previousCumulativeScores">此前累计总分，键为玩家名称</param>
        /// <returns>每个玩家的本轮得分明细，键为玩家名称</returns>
        public static Dictionary<string, RoundScoreData> Calculate(
            int roundIndex,
            List<string> allPlayerNames,
            RoundScoreInput input,
            Dictionary<string, int> previousCumulativeScores)
        {
            Dictionary<string, RoundScoreData> results = new();

            bool bHasAnyFinish = input.FinishedPlayerNames.Count > 0;
            bool bAllFinished = bHasAnyFinish
                && input.FinishedPlayerNames.Count == allPlayerNames.Count;
            bool bIsSoloClear = input.FinishedPlayerNames.Count == 1;
            string firstPlayerName = bHasAnyFinish ? input.FinishedPlayerNames[0] : null;

            for (int i = 0; i < allPlayerNames.Count; i++)
            {
                string playerName = allPlayerNames[i];
                RoundScoreData data = new RoundScoreData
                {
                    RoundIndex = roundIndex,
                    ScoreBreakdown = new Dictionary<ScoreType, int>()
                };

                if (bAllFinished)
                {
                    // 规则2 全员通关：只结算金币，其余五类全部为0
                    data.ScoreBreakdown[ScoreType.Completion] = 0;
                    data.ScoreBreakdown[ScoreType.FirstPlace] = 0;
                    data.ScoreBreakdown[ScoreType.SoloClear] = 0;
                    data.ScoreBreakdown[ScoreType.TrapKill] = 0;
                    data.ScoreBreakdown[ScoreType.ScoreItem] = GetBonusScore(input, playerName);
                }
                else if (bHasAnyFinish)
                {
                    // 规则3 部分玩家通关：完整计分公式
                    // 1. 本次得分：通关+20，未通关0
                    bool bIsFinished = IsPlayerFinished(input, playerName);
                    data.ScoreBreakdown[ScoreType.Completion] = bIsFinished ? COMPLETION_SCORE : 0;

                    // 2. 第一名加分：第一个通关者+10
                    data.ScoreBreakdown[ScoreType.FirstPlace] =
                        (firstPlayerName == playerName) ? FIRST_PLACE_SCORE : 0;

                    // 3. 独行积分：仅一人通关且该玩家通关时+15
                    data.ScoreBreakdown[ScoreType.SoloClear] =
                        (bIsSoloClear && bIsFinished) ? SOLO_CLEAR_SCORE : 0;

                    // 4. 陷阱得分：每次有效击杀+5，最多计2次；
                    //    陷阱拥有者本人未通关也可获得有效击杀分
                    int trapKills = GetTrapKillCount(input, playerName);
                    int cappedKills = System.Math.Min(trapKills, MAX_TRAP_KILL_COUNT);
                    data.ScoreBreakdown[ScoreType.TrapKill] = cappedKills * TRAP_KILL_SCORE_PER;

                    // 5. 得分道具得分：金币等得分道具的额外加分，单独成项；
                    //    金币仅在跟随角色通关时提交，天然满足"通关才加分"
                    data.ScoreBreakdown[ScoreType.ScoreItem] = GetBonusScore(input, playerName);
                }
                else
                {
                    // 规则1 无人通关：六类全部为0
                    data.ScoreBreakdown[ScoreType.Completion] = 0;
                    data.ScoreBreakdown[ScoreType.FirstPlace] = 0;
                    data.ScoreBreakdown[ScoreType.SoloClear] = 0;
                    data.ScoreBreakdown[ScoreType.TrapKill] = 0;
                    data.ScoreBreakdown[ScoreType.ScoreItem] = 0;
                }

                // 汇总本轮总得分
                data.RoundTotal = SumBreakdown(data.ScoreBreakdown);

                // 截至本轮的累计总分 = 此前累计 + 本轮得分
                int previousTotal = 0;
                if (previousCumulativeScores != null && previousCumulativeScores.ContainsKey(playerName))
                {
                    previousTotal = previousCumulativeScores[playerName];
                }
                data.CumulativeTotal = previousTotal + data.RoundTotal;

                results[playerName] = data;
            }

            return results;
        }

        // ==================== 辅助方法 ====================

        /// <summary>
        /// 判断玩家是否在本轮通关
        /// </summary>
        private static bool IsPlayerFinished(RoundScoreInput input, string playerName)
        {
            for (int i = 0; i < input.FinishedPlayerNames.Count; i++)
            {
                if (input.FinishedPlayerNames[i] == playerName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取玩家本轮陷阱有效击杀次数
        /// </summary>
        private static int GetTrapKillCount(RoundScoreInput input, string playerName)
        {
            if (input.TrapKillCounts != null && input.TrapKillCounts.TryGetValue(playerName, out int count))
            {
                return count;
            }
            return 0;
        }

        /// <summary>
        /// 获取玩家本轮的额外加分（金币等得分道具，无记录返回 0）
        /// </summary>
        private static int GetBonusScore(RoundScoreInput input, string playerName)
        {
            if (input.BonusScores != null && input.BonusScores.TryGetValue(playerName, out int bonus))
            {
                return bonus;
            }
            return 0;
        }

        /// <summary>
        /// 汇总五项得分
        /// </summary>
        private static int SumBreakdown(Dictionary<ScoreType, int> breakdown)
        {
            int total = 0;
            foreach (var pair in breakdown)
            {
                total += pair.Value;
            }
            return total;
        }
    }
}

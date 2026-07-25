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
        /// 特殊效果加分：老板巡视安静达标+10
        /// </summary>
        private const int SPECIAL_EFFECT_SCORE = 10;

        /// <summary>
        /// 胜利线分数：100分
        /// </summary>
        public const int VICTORY_LINE = 100;

        // ==================== 核心计算 ====================

        /// <summary>
        /// 计算一轮中所有参与玩家的得分明细
        /// 规则参考策划案的结算系统
        /// 无人通关时本轮五项全部为0；有人通关时按顺序追加五类得分
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

                if (bHasAnyFinish)
                {
                    // 1. 本次得分：通关+20，未通关0
                    bool bIsFinished = IsPlayerFinished(input, playerName);
                    data.ScoreBreakdown[ScoreType.Completion] = bIsFinished ? COMPLETION_SCORE : 0;

                    // 2. 第一名加分：第一个通关者+10
                    data.ScoreBreakdown[ScoreType.FirstPlace] =
                        (firstPlayerName == playerName) ? FIRST_PLACE_SCORE : 0;

                    // 3. 独行积分：仅一人通关且该玩家通关时+15
                    data.ScoreBreakdown[ScoreType.SoloClear] =
                        (bIsSoloClear && bIsFinished) ? SOLO_CLEAR_SCORE : 0;
                }
                else
                {
                    // 无人通关时五项全部为0
                    data.ScoreBreakdown[ScoreType.Completion] = 0;
                    data.ScoreBreakdown[ScoreType.FirstPlace] = 0;
                    data.ScoreBreakdown[ScoreType.SoloClear] = 0;
                }

                // 4. 陷阱得分：每次有效击杀+5，最多计2次；无人通关时为0
                int trapKills = GetTrapKillCount(input, playerName);
                int cappedKills = System.Math.Min(trapKills, MAX_TRAP_KILL_COUNT);
                data.ScoreBreakdown[ScoreType.TrapKill] =
                    bHasAnyFinish ? cappedKills * TRAP_KILL_SCORE_PER : 0;

                // 5. 特殊效果加分：老板巡视安静达标+10；无人通关时为0
                data.ScoreBreakdown[ScoreType.SpecialEffect] =
                    (bHasAnyFinish && IsQuietPlayer(input, playerName)) ? SPECIAL_EFFECT_SCORE : 0;

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
        /// 判断玩家是否在本轮老板巡视安静达标
        /// </summary>
        private static bool IsQuietPlayer(RoundScoreInput input, string playerName)
        {
            if (input.QuietPlayerNames == null)
            {
                return false;
            }

            for (int i = 0; i < input.QuietPlayerNames.Count; i++)
            {
                if (input.QuietPlayerNames[i] == playerName)
                {
                    return true;
                }
            }
            return false;
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

using System.Collections.Generic;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 最终结算控制器。
    /// 进入最终结算场景后，从 PlayerScoreManager 读取累计排名并展示胜者。
    /// </summary>
    public class FinalSettlementController : MonoBehaviour
    {
        [Header("文本显示")]
        [SerializeField] private TextMesh _titleText;
        [SerializeField] private TextMesh _winnerText;
        [SerializeField] private TextMesh _rankingText;

        private void Start()
        {
            RefreshFinalSettlement();
        }

        /// <summary>
        /// 刷新最终结算展示。
        /// </summary>
        public void RefreshFinalSettlement()
        {
            if (PlayerScoreManager.Instance == null)
            {
                Debug.LogError("[FinalSettlementController] PlayerScoreManager 不存在，无法刷新最终结算。");
                return;
            }

            List<string> rankedPlayerNames = PlayerScoreManager.Instance.GetRankedPlayerNames();
            if (rankedPlayerNames.Count == 0)
            {
                SetText(_titleText, "最终结算");
                SetText(_winnerText, "暂无玩家数据");
                SetText(_rankingText, "");
                return;
            }

            string winnerName = rankedPlayerNames[0];
            int winnerScore = PlayerScoreManager.Instance.GetPlayerTotalScore(winnerName);

            SetText(_titleText, "最终结算");
            SetText(_winnerText, $"胜者：{winnerName}  {winnerScore}分");
            SetText(_rankingText, BuildRankingText(rankedPlayerNames));
        }

        /// <summary>
        /// 构建排名文本。
        /// </summary>
        /// <param name="rankedPlayerNames">已排序的玩家名称列表。</param>
        private string BuildRankingText(List<string> rankedPlayerNames)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < rankedPlayerNames.Count; i++)
            {
                string playerName = rankedPlayerNames[i];
                int totalScore = PlayerScoreManager.Instance.GetPlayerTotalScore(playerName);
                PlayerScoreRecord record = PlayerScoreManager.Instance.GetPlayerScoreRecord(playerName);

                int finishCount = record != null ? record.TotalFinishCount : 0;
                int trapKillCount = record != null ? record.TotalTrapKillCount : 0;

                builder.AppendLine($"第{i + 1}名  {playerName}  {totalScore}分  通关:{finishCount}  陷阱:{trapKillCount}");
            }
            return builder.ToString();
        }

        /// <summary>
        /// 设置 TextMesh 文本，允许未绑定字段为空。
        /// </summary>
        /// <param name="target">目标文本组件。</param>
        /// <param name="content">文本内容。</param>
        private void SetText(TextMesh target, string content)
        {
            if (target != null)
            {
                target.text = content;
            }
        }
    }
}

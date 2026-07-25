using System.Collections.Generic;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 玩家轨道 — 单条轨道，管理该玩家的所有得分柱体
    /// 每个玩家拥有一条轨道，轨道内按蓝→绿→黄→红→紫顺序排列柱体
    /// 柱体从底部向上堆叠，后一种颜色的柱体位于前一种颜色柱体的上方
    /// </summary>
    public class PlayerTrack : MonoBehaviour
    {
        // 玩家名称
        private string _playerName;

        // 玩家颜色
        private Color _playerColor;

        // 本轮得分数据
        private RoundScoreData _roundScoreData;

        // 配置引用
        private ScorePillarConfig _config;

        // 该轨道中的所有柱体，按 ScoreType 索引
        private readonly Dictionary<ScoreType, ScorePillar> _pillars = new();

        // 玩家名称文本
        private TextMesh _nameText;

        // 累计总分文本
        private TextMesh _totalScoreText;

        // 当前柱体堆叠的累计高度
        private float _accumulatedHeight;

        // 轨道宽度（由外部动态设置）
        private float _trackWidth;

        // 柱体宽度（由外部动态计算）
        private float _pillarWidth;

        /// <summary>
        /// 玩家名称
        /// </summary>
        public string PlayerName => _playerName;

        /// <summary>
        /// 当前柱体堆叠的累计高度
        /// </summary>
        public float AccumulatedHeight => _accumulatedHeight;

        /// <summary>
        /// 初始化轨道：设置玩家信息和配置，创建名称文本和柱体
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <param name="playerColor">玩家颜色</param>
        /// <param name="roundScoreData">本轮得分数据</param>
        /// <param name="config">柱体配置</param>
        /// <param name="trackWidth">轨道宽度（世界单位）</param>
        public void Initialize(string playerName, Color playerColor, RoundScoreData roundScoreData, ScorePillarConfig config, float trackWidth)
        {
            _playerName = playerName;
            _playerColor = playerColor;
            _roundScoreData = roundScoreData;
            _config = config;
            _trackWidth = trackWidth;
            _pillarWidth = config.CalculatePillarWidth(trackWidth);
            _accumulatedHeight = 0f;

            ClearPillars();
            CreateNameText(playerName, playerColor);
            CreateTotalScoreText();
            CreatePillars();
        }

        /// <summary>
        /// 获取指定得分类型的柱体，不存在时返回 null
        /// </summary>
        /// <param name="scoreType">得分类型</param>
        public ScorePillar GetPillar(ScoreType scoreType)
        {
            if (_pillars.TryGetValue(scoreType, out ScorePillar pillar))
            {
                return pillar;
            }
            return null;
        }

        /// <summary>
        /// 更新累计总分文本显示
        /// </summary>
        /// <param name="totalScore">累计总分</param>
        public void UpdateTotalScoreText(int totalScore)
        {
            if (_totalScoreText != null)
            {
                _totalScoreText.text = totalScore.ToString();
            }
        }

        /// <summary>
        /// 清除所有柱体和文本，用于复用轨道时重置
        /// </summary>
        public void ClearPillars()
        {
            // 销毁所有柱体子对象
            List<GameObject> childrenToDestroy = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                childrenToDestroy.Add(transform.GetChild(i).gameObject);
            }

            for (int i = 0; i < childrenToDestroy.Count; i++)
            {
                Destroy(childrenToDestroy[i]);
            }

            _pillars.Clear();
            _accumulatedHeight = 0f;
            _nameText = null;
            _totalScoreText = null;
        }

        /// <summary>
        /// 创建玩家名称文本，显示在轨道底部
        /// </summary>
        private void CreateNameText(string playerName, Color playerColor)
        {
            GameObject textObj = new GameObject("PlayerName");
            textObj.transform.SetParent(transform, false);

            _nameText = textObj.AddComponent<TextMesh>();
            _nameText.text = playerName;
            _nameText.fontSize = _config.FontSize;
            _nameText.color = playerColor;
            _nameText.anchor = TextAnchor.LowerCenter;
            _nameText.alignment = TextAlignment.Center;
            _nameText.characterSize = 0.1f;
            _nameText.transform.localPosition = new Vector3(0f, -0.4f, -0.1f);

            Renderer textRenderer = textObj.GetComponent<Renderer>();
            if (textRenderer != null)
            {
                textRenderer.sortingOrder = 3;
            }
        }

        /// <summary>
        /// 创建累计总分文本，位于轨道顶部（动画中动态更新位置）
        /// </summary>
        private void CreateTotalScoreText()
        {
            GameObject textObj = new GameObject("TotalScore");
            textObj.transform.SetParent(transform, false);

            _totalScoreText = textObj.AddComponent<TextMesh>();
            _totalScoreText.text = "0";
            _totalScoreText.fontSize = _config.FontSize + 4;
            _totalScoreText.color = _playerColor;
            _totalScoreText.anchor = TextAnchor.LowerCenter;
            _totalScoreText.alignment = TextAlignment.Center;
            _totalScoreText.characterSize = 0.1f;
            _totalScoreText.transform.localPosition = new Vector3(0f, 0f, -0.1f);

            Renderer textRenderer = textObj.GetComponent<Renderer>();
            if (textRenderer != null)
            {
                textRenderer.sortingOrder = 3;
            }
        }

        /// <summary>
        /// 根据得分数据创建所有柱体
        /// 按蓝→绿→黄→红→紫的顺序，只有得分大于0的项才创建柱体
        /// 柱体从底部向上堆叠
        /// </summary>
        private void CreatePillars()
        {
            if (_roundScoreData == null || _config == null)
            {
                return;
            }

            List<ScoreType> order = _config.GetScoreTypeOrder();

            for (int i = 0; i < order.Count; i++)
            {
                ScoreType scoreType = order[i];
                int score = GetScoreValue(scoreType);

                // 得分为0时不创建柱体
                if (score <= 0)
                {
                    continue;
                }

                float height = _config.CalculatePillarHeight(score);
                Color color = _config.GetScoreTypeColor(scoreType);

                // 创建柱体 GameObject
                GameObject pillarObj = new GameObject($"Pillar_{scoreType}");
                pillarObj.transform.SetParent(transform, false);
                pillarObj.transform.localPosition = new Vector3(0f, _accumulatedHeight, 0f);

                // 初始化柱体
                ScorePillar pillar = pillarObj.AddComponent<ScorePillar>();
                pillar.Initialize(color, score, height, _pillarWidth, _config.FontSize, _config.TextColor, _config.TextOffset);

                _pillars[scoreType] = pillar;
                _accumulatedHeight += height;
            }

            // 设置总分文本初始位置（位于所有柱体顶部）
            if (_totalScoreText != null)
            {
                _totalScoreText.transform.localPosition = new Vector3(0f, _accumulatedHeight + 0.3f, -0.1f);
                _totalScoreText.text = _roundScoreData.CumulativeTotal.ToString();
            }
        }

        /// <summary>
        /// 从本轮得分数据中获取指定类型的得分值
        /// </summary>
        /// <param name="scoreType">得分类型</param>
        private int GetScoreValue(ScoreType scoreType)
        {
            if (_roundScoreData != null && _roundScoreData.ScoreBreakdown != null
                && _roundScoreData.ScoreBreakdown.TryGetValue(scoreType, out int value))
            {
                return value;
            }
            return 0;
        }
    }
}

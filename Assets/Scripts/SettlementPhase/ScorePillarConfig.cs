using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 结算柱体配置 — ScriptableObject 资产
    /// 集中管理颜色映射、弹出动画参数和高度缩放系数
    /// 柱体宽度和轨道间距由 SettlementController 根据玩家数量动态计算
    /// 在 Project 窗口右键 Create → SuperQQ → ScorePillarConfig 创建实例
    /// </summary>
    [CreateAssetMenu(fileName = "ScorePillarConfig", menuName = "SuperQQ/ScorePillarConfig")]
    public class ScorePillarConfig : ScriptableObject
    {
        [Header("柱体尺寸")]
        [Tooltip("每1分对应的高度（世界单位），柱体高度 = 分数 × 此值")]
        public float HeightPerPoint = 0.15f;

        [Tooltip("柱体宽度占轨道宽度的比例（0~1），轨道宽度由玩家数量动态决定")]
        [Range(0.1f, 1f)]
        public float PillarWidthRatio = 0.6f;

        [Header("颜色映射 — 对应五层颁奖台")]
        public Color CompletionColor = new Color(0.2f, 0.5f, 1f);       // 蓝
        public Color FirstPlaceColor = new Color(0.2f, 0.8f, 0.3f);     // 绿
        public Color SoloClearColor = new Color(1f, 0.85f, 0.1f);       // 黄
        public Color TrapKillColor = new Color(0.9f, 0.2f, 0.15f);      // 红
        public Color SpecialEffectColor = new Color(0.6f, 0.3f, 0.85f); // 紫

        [Header("文本")]
        [Tooltip("分数文本字体大小")]
        public int FontSize = 24;

        [Tooltip("分数文本颜色")]
        public Color TextColor = Color.white;

        [Tooltip("分数文本偏移（相对于柱体垂直中心）")]
        public Vector2 TextOffset = new Vector2(0f, 0f);

        [Header("弹出动画")]
        [Tooltip("每种得分类型的弹出动画时长（秒）")]
        public float PopDuration = 0.6f;

        [Tooltip("同批次柱体之间的弹出间隔（秒）")]
        public float PopStaggerDelay = 0.1f;

        [Tooltip("不同颜色批次之间的间隔（秒）")]
        public float BatchInterval = 1.2f;

        [Tooltip("弹出动画曲线，默认为缓入缓出")]
        public AnimationCurve PopCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1f),
            new Keyframe(1f, 1f, 1f, 0f));

        [Header("轨道")]
        [Tooltip("轨道底部留白（世界单位），柱体从此高度开始堆叠")]
        public float TrackBottomPadding = 1f;

        [Tooltip("轨道顶部留白（世界单位），预留给总分文本")]
        public float TrackTopPadding = 1f;

        [Tooltip("单条轨道最大宽度（世界单位），玩家数量少时轨道不会超过此值以保持美观")]
        public float MaxTrackWidth = 6f;

        [Header("胜利线")]
        [Tooltip("胜利线颜色")]
        public Color VictoryLineColor = new Color(1f, 0.85f, 0f);

        [Tooltip("胜利线文本")]
        public string VictoryLineText = "100";

        // ==================== 动态布局计算 ====================

        /// <summary>
        /// 根据相机正交大小和玩家数量计算单条轨道宽度
        /// 所有轨道平分整个屏幕宽度，但不超过 MaxTrackWidth
        /// 玩家数量少时轨道宽度被限制，保持美观的比例
        /// </summary>
        /// <param name="cameraOrthographicSize">相机正交大小</param>
        /// <param name="cameraAspect">相机宽高比</param>
        /// <param name="playerCount">玩家数量</param>
        /// <returns>单条轨道宽度（世界单位），不超过 MaxTrackWidth</returns>
        public float CalculateTrackWidth(float cameraOrthographicSize, float cameraAspect, int playerCount)
        {
            if (playerCount <= 0)
            {
                return 0f;
            }

            float cameraWidth = cameraOrthographicSize * 2f * cameraAspect;
            float rawTrackWidth = cameraWidth / playerCount;

            // 限制轨道宽度不超过最大值，玩家少时保持美观比例
            return Mathf.Min(rawTrackWidth, MaxTrackWidth);
        }

        /// <summary>
        /// 根据轨道宽度计算柱体宽度
        /// 柱体宽度 = 轨道宽度 × 比例系数
        /// </summary>
        /// <param name="trackWidth">轨道宽度</param>
        /// <returns>柱体宽度（世界单位）</returns>
        public float CalculatePillarWidth(float trackWidth)
        {
            return trackWidth * PillarWidthRatio;
        }

        /// <summary>
        /// 根据得分计算柱体高度
        /// 得分为0时返回0，表示不创建柱体
        /// </summary>
        /// <param name="score">得分值</param>
        /// <returns>柱体高度（世界单位），得分为0时返回0</returns>
        public float CalculatePillarHeight(int score)
        {
            if (score <= 0)
            {
                return 0f;
            }
            return score * HeightPerPoint;
        }

        // ==================== 颜色与顺序 ====================

        /// <summary>
        /// 获取指定得分类型对应的颜色
        /// </summary>
        /// <param name="scoreType">得分类型</param>
        /// <returns>对应的颜色</returns>
        public Color GetScoreTypeColor(Score.ScoreType scoreType)
        {
            switch (scoreType)
            {
                case Score.ScoreType.Completion:
                    return CompletionColor;
                case Score.ScoreType.FirstPlace:
                    return FirstPlaceColor;
                case Score.ScoreType.SoloClear:
                    return SoloClearColor;
                case Score.ScoreType.TrapKill:
                    return TrapKillColor;
                case Score.ScoreType.SpecialEffect:
                    return SpecialEffectColor;
                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// 获取得分类型的弹出顺序列表
        /// 按蓝→绿→黄→红→紫的顺序
        /// </summary>
        public List<Score.ScoreType> GetScoreTypeOrder()
        {
            return new List<Score.ScoreType>
            {
                Score.ScoreType.Completion,
                Score.ScoreType.FirstPlace,
                Score.ScoreType.SoloClear,
                Score.ScoreType.TrapKill,
                Score.ScoreType.SpecialEffect
            };
        }
    }
}

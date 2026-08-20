using UnityEngine;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 胜利线 — 横向标记100分胜利线位置的视觉元素
    /// 由 SettlementController 在结算时创建/更新
    /// 包含一条横贯屏幕的细线和"100"标签，作为整场结束的视觉目标线
    /// </summary>
    public class VictoryLine : MonoBehaviour
    {
        // 线条厚度（世界单位）
        private const float LINE_THICKNESS = 0.08f;

        // 标签距线条右端的水平间距（向内收缩，避免超出屏幕）
        private const float LABEL_RIGHT_MARGIN = 0.3f;

        // 标签相对线条中心的垂直偏移（位于线条上方）
        private const float LABEL_VERTICAL_OFFSET = 0.15f;

        private SpriteRenderer _lineRenderer;
        private TextMesh _labelText;

        /// <summary>
        /// 初始化胜利线：创建/更新线条和标签，定位到指定高度
        /// </summary>
        /// <param name="config">柱体配置（提供颜色、文本等）</param>
        /// <param name="lineY">胜利线在父级本地坐标系的Y位置</param>
        /// <param name="cameraWidth">相机宽度（世界单位），用于确定线条横向跨度</param>
        public void Initialize(ScorePillarConfig config, float lineY, float cameraWidth)
        {
            CreateLine(config, cameraWidth);
            CreateLabel(config, cameraWidth);

            transform.localPosition = new Vector3(0f, lineY, 0f);
        }

        /// <summary>
        /// 创建/更新横线 SpriteRenderer
        /// 使用中心锚点的1x1白色Sprite，通过缩放拉伸为横贯屏幕的细线
        /// </summary>
        private void CreateLine(ScorePillarConfig config, float cameraWidth)
        {
            if (_lineRenderer == null)
            {
                GameObject lineObj = new GameObject("Line");
                lineObj.transform.SetParent(transform, false);

                _lineRenderer = lineObj.AddComponent<SpriteRenderer>();
                _lineRenderer.sprite = CreateWhiteSprite();
                _lineRenderer.sortingOrder = 4;
            }

            _lineRenderer.color = config.VictoryLineColor;
            _lineRenderer.transform.localPosition = Vector3.zero;
            _lineRenderer.transform.localScale = new Vector3(cameraWidth, LINE_THICKNESS, 1f);
        }

        /// <summary>
        /// 创建/更新"100"标签 TextMesh，位于线条右端上方
        /// </summary>
        private void CreateLabel(ScorePillarConfig config, float cameraWidth)
        {
            if (_labelText == null)
            {
                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(transform, false);

                _labelText = labelObj.AddComponent<TextMesh>();
                _labelText.anchor = TextAnchor.LowerRight;
                _labelText.alignment = TextAlignment.Right;
                _labelText.characterSize = 0.1f;

                Renderer textRenderer = labelObj.GetComponent<Renderer>();
                if (textRenderer != null)
                {
                    textRenderer.sortingOrder = 5;
                }
            }

            _labelText.text = config.VictoryLineText;
            _labelText.fontSize = config.FontSize + 4;
            _labelText.color = config.VictoryLineColor;
            _labelText.transform.localPosition = new Vector3(
                cameraWidth * 0.5f - LABEL_RIGHT_MARGIN,
                LINE_THICKNESS * 0.5f + LABEL_VERTICAL_OFFSET,
                -0.1f);
        }

        /// <summary>
        /// 创建 1x1 白色 Sprite（中心锚点）用于线条渲染
        /// </summary>
        private Sprite CreateWhiteSprite()
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

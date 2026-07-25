using System.Collections;
using UnityEngine;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 得分柱体 — 单个柱体的视觉表现和弹出动画
    /// 由 PlayerTrack 在运行时动态创建，不需要 Prefab
    /// 自行构建 SpriteRenderer 和 TextMesh，管理弹出动画
    /// </summary>
    public class ScorePillar : MonoBehaviour
    {
        // 柱体的 SpriteRenderer，运行时创建
        private SpriteRenderer _spriteRenderer;

        // 柱体中央的分数文本
        private TextMesh _scoreText;

        // 当前柱体的目标高度
        private float _targetHeight;

        // 弹出动画是否已完成
        private bool _bIsPopComplete;

        // 弹出动画协程引用
        private Coroutine _popCoroutine;

        // 柱体子级 Sprite Transform（独立缩放，避免影响文本）
        private Transform _spriteTransform;

        // 文本相对柱体中心的偏移
        private Vector2 _textOffset;

        /// <summary>
        /// 弹出动画是否已完成
        /// </summary>
        public bool BIsPopComplete => _bIsPopComplete;

        /// <summary>
        /// 初始化柱体：创建视觉组件、设置颜色和文本
        /// </summary>
        /// <param name="color">柱体颜色</param>
        /// <param name="score">该部分得分值</param>
        /// <param name="height">柱体目标高度</param>
        /// <param name="width">柱体宽度</param>
        /// <param name="fontSize">文本字体大小</param>
        /// <param name="textColor">文本颜色</param>
        /// <param name="textOffset">文本偏移</param>
        public void Initialize(Color color, int score, float height, float width, int fontSize, Color textColor, Vector2 textOffset)
        {
            _targetHeight = height;
            _bIsPopComplete = false;
            _textOffset = textOffset;

            CreateSpriteRenderer(color, width);
            CreateScoreText(score, fontSize, textColor);

            // 初始高度为0，等待弹出动画
            SetVisualHeight(0f);
        }

        /// <summary>
        /// 开始弹出动画
        /// </summary>
        /// <param name="duration">动画时长（秒）</param>
        /// <param name="curve">动画曲线</param>
        public void StartPopAnimation(float duration, AnimationCurve curve)
        {
            if (_popCoroutine != null)
            {
                StopCoroutine(_popCoroutine);
            }
            _popCoroutine = StartCoroutine(PopAnimationCoroutine(duration, curve));
        }

        /// <summary>
        /// 立即跳到最终状态，不播放动画
        /// </summary>
        public void SkipAnimation()
        {
            if (_popCoroutine != null)
            {
                StopCoroutine(_popCoroutine);
                _popCoroutine = null;
            }

            SetVisualHeight(_targetHeight);
            _bIsPopComplete = true;
        }

        /// <summary>
        /// 创建 SpriteRenderer 组件（作为子级 Sprite GameObject，独立缩放，避免影响文本）
        /// </summary>
        private void CreateSpriteRenderer(Color color, float width)
        {
            GameObject spriteObj = new GameObject("Sprite");
            spriteObj.transform.SetParent(transform, false);

            _spriteTransform = spriteObj.transform;
            _spriteTransform.localScale = new Vector3(width, 0.001f, 1f);

            _spriteRenderer = spriteObj.AddComponent<SpriteRenderer>();
            _spriteRenderer.sprite = CreateWhiteSprite();
            _spriteRenderer.color = color;
            _spriteRenderer.sortingOrder = 1;
        }

        /// <summary>
        /// 创建分数文本（TextMesh），位于柱体垂直中央
        /// </summary>
        private void CreateScoreText(int score, int fontSize, Color textColor)
        {
            GameObject textObj = new GameObject("ScoreText");
            textObj.transform.SetParent(transform, false);

            _scoreText = textObj.AddComponent<TextMesh>();
            _scoreText.text = score.ToString();
            _scoreText.fontSize = fontSize;
            _scoreText.color = textColor;
            _scoreText.anchor = TextAnchor.MiddleCenter;
            _scoreText.alignment = TextAlignment.Center;
            _scoreText.characterSize = 0.1f;

            // 文本排序在柱体之上
            Renderer textRenderer = textObj.GetComponent<Renderer>();
            if (textRenderer != null)
            {
                textRenderer.sortingOrder = 2;
            }
        }

        /// <summary>
        /// 创建 1x1 白色 Sprite 用于柱体渲染
        /// </summary>
        private Sprite CreateWhiteSprite()
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0f), 1f);
        }

        /// <summary>
        /// 设置柱体的视觉高度（通过缩放子级 Sprite 的 Y 轴和更新文本位置）
        /// 文本定位到柱体垂直中央，使用柱体本地坐标（柱体 Transform 未缩放，文本不会被拉伸）
        /// </summary>
        /// <param name="height">目标视觉高度</param>
        private void SetVisualHeight(float height)
        {
            if (_spriteTransform != null)
            {
                Vector3 scale = _spriteTransform.localScale;
                scale.y = Mathf.Max(height, 0.001f);
                _spriteTransform.localScale = scale;
            }

            if (_scoreText != null)
            {
                // 柱体高度过小时隐藏文本，避免文本悬浮或超出柱体
                bool bShouldShowText = height > 0.05f;
                if (_scoreText.gameObject.activeSelf != bShouldShowText)
                {
                    _scoreText.gameObject.SetActive(bShouldShowText);
                }

                if (bShouldShowText)
                {
                    _scoreText.transform.localPosition = new Vector3(
                        _textOffset.x,
                        height * 0.5f + _textOffset.y,
                        -0.1f);
                }
            }
        }

        /// <summary>
        /// 弹出动画协程：从高度0缓动到目标高度
        /// </summary>
        /// <param name="duration">动画时长（秒）</param>
        /// <param name="curve">动画曲线</param>
        private IEnumerator PopAnimationCoroutine(float duration, AnimationCurve curve)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float evaluatedTime = curve != null ? curve.Evaluate(normalizedTime) : normalizedTime;
                float currentHeight = Mathf.Lerp(0f, _targetHeight, evaluatedTime);
                SetVisualHeight(currentHeight);
                yield return null;
            }

            SetVisualHeight(_targetHeight);
            _bIsPopComplete = true;
            _popCoroutine = null;
        }
    }
}

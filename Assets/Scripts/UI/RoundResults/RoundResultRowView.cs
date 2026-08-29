using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI.RoundResults
{
    public sealed class RoundResultRowView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _rowRect;
        [SerializeField] private TMP_Text _rankText;
        [SerializeField] private Image _avatarBackground;
        [SerializeField] private TMP_Text _avatarInitial;
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _deltaText;
        [SerializeField] private RectTransform _fillContent;
        [SerializeField] private Image _previousFill;
        [SerializeField] private Image _previousHatch;
        [SerializeField] private RectTransform _segmentRoot;
        [SerializeField] private GameObject _winnerBadge;
        [SerializeField] private TMP_Text _winnerText;

        [Header("分数条比例")]
        [Tooltip("得分栏分段条的满分刻度：各细分得分按此值计算宽度占比（只影响条的比例，胜利线判定与分数文本仍用 Populate 传入的 victoryScore）")]
        [SerializeField, Min(1)] private int _barMaxScore = 125;

        private readonly List<RectTransform> _segmentRects = new();
        private readonly List<int> _segmentPoints = new();
        private Vector2 _restPosition;
        private int _previousTotal;
        private float _previousRatio;
        private int _finalTotal;
        private int _victoryScore = 100;
        private float _revealedScore;
        private float _revealEased;
        private Coroutine _rankMoveAnimation;

        public float RevealedScore => _revealedScore;
        public Vector2 LayoutPosition => _restPosition;
        public int DisplayedRank { get; private set; }
        public string PlayerName => _playerNameText != null ? _playerNameText.text : string.Empty;

        public int RevealStageCount => Mathf.Max(1, _segmentRects.Count + 1);

        public void Configure(
            CanvasGroup canvasGroup,
            RectTransform rowRect,
            TMP_Text rankText,
            Image avatarBackground,
            TMP_Text avatarInitial,
            TMP_Text playerNameText,
            TMP_Text scoreText,
            TMP_Text deltaText,
            RectTransform fillContent,
            Image previousFill,
            Image previousHatch,
            RectTransform segmentRoot,
            GameObject winnerBadge,
            TMP_Text winnerText)
        {
            _canvasGroup = canvasGroup;
            _rowRect = rowRect;
            _rankText = rankText;
            _avatarBackground = avatarBackground;
            _avatarInitial = avatarInitial;
            _playerNameText = playerNameText;
            _scoreText = scoreText;
            _deltaText = deltaText;
            _fillContent = fillContent;
            _previousFill = previousFill;
            _previousHatch = previousHatch;
            _segmentRoot = segmentRoot;
            _winnerBadge = winnerBadge;
            _winnerText = winnerText;
        }

        public void Populate(RoundResultPlayerData data, int rank, int victoryScore)
        {
            int safeVictoryScore = Mathf.Max(1, victoryScore);
            _previousTotal = Mathf.Max(0, data.PreviousTotal);
            _finalTotal = Mathf.Max(_previousTotal, data.CumulativeTotal);
            _victoryScore = safeVictoryScore;
            _revealedScore = _previousTotal;
            _revealEased = 0f;

            if (_rankMoveAnimation != null)
            {
                StopCoroutine(_rankMoveAnimation);
                _rankMoveAnimation = null;
            }

            _rowRect.localScale = Vector3.one;
            _restPosition = _rowRect.anchoredPosition;
            SetRank(rank);
            _playerNameText.text = string.IsNullOrWhiteSpace(data.PlayerName) ? "Player" : data.PlayerName;
            // 有 icon 时显示头像 sprite，否则回退首字母 + 玩家颜色块
            ApplyAvatar(data.PlayerIcon, data.PlayerName);
            if (data.PlayerIcon == null && _avatarBackground != null)
            {
                _avatarBackground.color = data.PlayerColor;
            }
            _scoreText.text = $"{_previousTotal} / {safeVictoryScore}";
            _deltaText.text = data.RoundTotal > 0 ? $"+{data.RoundTotal}" : "+0";
            _deltaText.color = data.RoundTotal > 0
                ? new Color32(35, 153, 112, 255)
                : new Color32(112, 112, 126, 255);
            _winnerBadge.SetActive(false);
            _winnerText.text = "WINNER";

            ClearDynamicSegments();

            // 分段条宽度占比按 _barMaxScore 刻度计算（默认 120 > 胜利线 100，细分占比相应缩小）
            float barMax = Mathf.Max(1, _barMaxScore);
            float cursor = Mathf.Clamp01(_previousTotal / barMax);
            float finalRatio = Mathf.Clamp01(data.CumulativeTotal / barMax);
            _previousRatio = cursor;
            _previousFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            SetAnchors(_previousFill.rectTransform, 0f, cursor);

            // 最前面的默认斜线条：表示之前回合累计总分（0 → previousTotal 区间）
            if (_previousHatch != null)
            {
                _previousHatch.rectTransform.pivot = new Vector2(0f, 0.5f);
                SetAnchors(_previousHatch.rectTransform, 0f, cursor);
                _previousHatch.gameObject.SetActive(cursor > 0f);
            }

            for (int i = 0; i < data.Segments.Count; i++)
            {
                RoundResultScoreSegment segment = data.Segments[i];
                if (segment == null || segment.Points <= 0)
                {
                    continue;
                }

                float next = Mathf.Min(finalRatio, cursor + segment.Points / barMax);
                RectTransform rect = null;
                if (next > cursor)
                {
                    rect = CreateColorSegment(
                        $"Segment_{segment.ScoreType}",
                        cursor,
                        next,
                        segment.Color);
                    cursor = next;
                }

                _segmentRects.Add(rect);
                _segmentPoints.Add(segment.Points);
            }

            SetReveal(0f);
        }

        /// <summary>
        /// 轻量填充：只展示玩家名、玩家 icon 与当前总分数（供记分行列表使用）。
        /// icon 为空时回退为玩家名首字母；排名角标、本轮增减与 Winner 徽标不展示。
        /// </summary>
        public void PopulateSummary(string playerName, Sprite playerIcon, int totalScore, int rank = 0)
        {
            // SetReveal 每帧按 _previousTotal/_finalTotal 重写分数文本，
            // 轻量行不设置会定格显示 "0 / victory"——补上：无分段概念，全程显示目标总分
            _previousTotal = Mathf.Max(0, totalScore);
            _finalTotal = _previousTotal;

            if (_rankText != null)
            {
                _rankText.text = rank > 0 ? rank.ToString() : string.Empty;
            }
            if (_playerNameText != null)
            {
                _playerNameText.text = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
            }
            ApplyAvatar(playerIcon, playerName);
            if (_scoreText != null)
            {
                _scoreText.text = Mathf.Max(0, totalScore).ToString();
            }
            if (_deltaText != null)
            {
                _deltaText.gameObject.SetActive(false);
            }
            if (_winnerBadge != null)
            {
                _winnerBadge.SetActive(false);
            }

            CaptureLayoutPosition();
            SetImmediateVisible();
        }

        /// <summary>
        /// 应用头像：有 icon 时显示 icon sprite（隐藏首字母），否则回退为首字母 + 背景色块。
        /// </summary>
        private void ApplyAvatar(Sprite playerIcon, string playerName)
        {
            if (_avatarBackground != null && playerIcon != null)
            {
                _avatarBackground.sprite = playerIcon;
                _avatarBackground.color = Color.white;
                _avatarBackground.preserveAspect = true;
            }
            if (_avatarInitial != null)
            {
                bool showInitial = playerIcon == null;
                _avatarInitial.gameObject.SetActive(showInitial);
                if (showInitial)
                {
                    _avatarInitial.text = GetInitial(playerName);
                }
            }
        }

        public void SetReveal(float t)
        {
            float clamped = Mathf.Clamp01(t);
            _revealEased = EaseOutCubic(clamped);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _revealEased;
            }

            ApplyVisualPosition();

            if (_fillContent != null)
            {
                _fillContent.localScale = Vector3.one;
            }

            float stagePosition = clamped * RevealStageCount;
            if (_previousFill != null)
            {
                float previousProgress = EaseOutCubic(Mathf.Clamp01(stagePosition));
                _previousFill.rectTransform.localScale = new Vector3(previousProgress, 1f, 1f);

                // 默认斜线条同步揭示：用锚点推进而非缩放，避免 Tiled 斜线纹理被压缩
                if (_previousHatch != null)
                {
                    SetAnchors(_previousHatch.rectTransform, 0f, _previousRatio * previousProgress);
                }
            }

            float visibleScore = _previousTotal;
            for (int i = 0; i < _segmentRects.Count; i++)
            {
                float segmentProgress = EaseOutCubic(Mathf.Clamp01(stagePosition - (i + 1f)));
                if (_segmentRects[i] != null)
                {
                    _segmentRects[i].localScale = new Vector3(segmentProgress, 1f, 1f);
                }

                visibleScore += _segmentPoints[i] * segmentProgress;
            }

            _revealedScore = clamped >= 1f ? _finalTotal : visibleScore;
            if (_winnerBadge != null)
            {
                _winnerBadge.SetActive(_revealedScore >= _victoryScore);
            }

            if (_scoreText != null)
            {
                _scoreText.text = $"{Mathf.RoundToInt(_revealedScore)} / {_victoryScore}";
            }
        }

        public void SetImmediateVisible()
        {
            SetReveal(1f);
        }

        private void ClearDynamicSegments()
        {
            _segmentRects.Clear();
            _segmentPoints.Clear();

            if (_segmentRoot == null)
            {
                return;
            }

            for (int i = _segmentRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = _segmentRoot.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private static void SetAnchors(RectTransform rect, float minX, float maxX)
        {
            rect.anchorMin = new Vector2(Mathf.Clamp01(minX), 0f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(maxX), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string GetInitial(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                return "?";
            }

            string trimmed = playerName.Trim();
            return trimmed.Substring(0, 1).ToUpperInvariant();
        }
        public void CaptureLayoutPosition()
        {
            if (_rankMoveAnimation != null)
            {
                StopCoroutine(_rankMoveAnimation);
                _rankMoveAnimation = null;
            }

            if (_rowRect != null)
            {
                _rowRect.localScale = Vector3.one;
                _restPosition = _rowRect.anchoredPosition;
                ApplyVisualPosition();
            }
        }

        public void SetRank(int rank)
        {
            DisplayedRank = Mathf.Max(1, rank);
            if (_rankText != null)
            {
                _rankText.text = DisplayedRank.ToString();
            }
        }

        public void MoveToRankSlot(Vector2 targetPosition, float duration, bool promoted)
        {
            if (_rankMoveAnimation != null)
            {
                StopCoroutine(_rankMoveAnimation);
            }

            _rankMoveAnimation = StartCoroutine(
                PlayRankMove(targetPosition, Mathf.Max(0.01f, duration), promoted));
        }

        public void SnapToRankSlot(Vector2 targetPosition)
        {
            if (_rankMoveAnimation != null)
            {
                StopCoroutine(_rankMoveAnimation);
                _rankMoveAnimation = null;
            }

            _restPosition = targetPosition;
            if (_rowRect != null)
            {
                _rowRect.localScale = Vector3.one;
            }

            ApplyVisualPosition();
        }

        private IEnumerator PlayRankMove(Vector2 targetPosition, float duration, bool promoted)
        {
            Vector2 startPosition = _restPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                _restPosition = Vector2.LerpUnclamped(startPosition, targetPosition, eased);

                if (_rowRect != null)
                {
                    float pulse = Mathf.Sin(t * Mathf.PI) * (promoted ? 0.055f : 0.025f);
                    _rowRect.localScale = Vector3.one * (1f + pulse);
                }

                ApplyVisualPosition();
                yield return null;
            }

            _restPosition = targetPosition;
            if (_rowRect != null)
            {
                _rowRect.localScale = Vector3.one;
            }

            ApplyVisualPosition();
            _rankMoveAnimation = null;
        }

        private void ApplyVisualPosition()
        {
            if (_rowRect == null)
            {
                return;
            }

            // 行进场只走 alpha 淡入，不动位置：行是板的子级，板 reveal 期间正在缩放
            // （0.88→1，pivot 居中），此时再叠加位置偏移会与板缩放"下沿位置更近"的观感叠加，
            // 表现为"分数条先在下方然后上移"。位置保持 rest 静止即可，透明度由 SetReveal 控制。
            _rowRect.anchoredPosition = _restPosition;
        }

        private static float EaseOutCubic(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return 1f - Mathf.Pow(1f - clamped, 3f);
        }
        /// <summary>
        /// 创建手绘斜线纹理分段条：浅色纸底 + HandDrawnHatch 斜线纹理按得分类型换色，
        /// 每种得分一段颜色（对应底部 Legend 颜色）。
        /// </summary>
        private RectTransform CreateColorSegment(
            string objectName,
            float minX,
            float maxX,
            Color segmentColor)
        {
            GameObject segmentObject = new(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline),
                typeof(Mask));

            RectTransform rect = segmentObject.GetComponent<RectTransform>();
            rect.SetParent(_segmentRoot, false);
            rect.pivot = new Vector2(0f, 0.5f);
            SetAnchors(rect, minX, maxX);

            // 纸色底：向分段颜色轻微靠拢，保持整体手绘纸面质感
            Image paper = segmentObject.GetComponent<Image>();
            paper.sprite = _previousFill.sprite;
            paper.type = _previousFill.type;
            paper.pixelsPerUnitMultiplier = _previousFill.pixelsPerUnitMultiplier;
            paper.color = Color.Lerp(new Color32(245, 240, 214, 255), segmentColor, 0.22f);
            paper.raycastTarget = false;

            Outline outline = segmentObject.GetComponent<Outline>();
            outline.effectColor = new Color(segmentColor.r, segmentColor.g, segmentColor.b, 0.94f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;

            Mask mask = segmentObject.GetComponent<Mask>();
            mask.showMaskGraphic = true;

            GameObject hatchObject = new(
                "HandDrawnHatch",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform hatchRect = hatchObject.GetComponent<RectTransform>();
            hatchRect.SetParent(rect, false);
            SetAnchors(hatchRect, 0f, 1f);

            Image hatch = hatchObject.GetComponent<Image>();
            hatch.sprite = _previousHatch.sprite;
            hatch.type = Image.Type.Tiled;
            hatch.pixelsPerUnitMultiplier = _previousHatch.pixelsPerUnitMultiplier;
            hatch.color = segmentColor;
            hatch.raycastTarget = false;

            return rect;
        }
}
}

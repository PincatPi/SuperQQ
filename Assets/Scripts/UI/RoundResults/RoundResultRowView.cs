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

        private readonly List<RectTransform> _segmentRects = new();
        private readonly List<int> _segmentPoints = new();
        private Vector2 _restPosition;
        private int _previousTotal;
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
            _avatarBackground.color = data.PlayerColor;
            _avatarInitial.text = GetInitial(data.PlayerName);
            _scoreText.text = $"{_previousTotal} / {safeVictoryScore}";
            _deltaText.text = data.RoundTotal > 0 ? $"+{data.RoundTotal}" : "+0";
            _deltaText.color = data.RoundTotal > 0
                ? new Color32(35, 153, 112, 255)
                : new Color32(112, 112, 126, 255);
            _winnerBadge.SetActive(false);
            _winnerText.text = "WINNER";

            ClearDynamicSegments();

            float cursor = Mathf.Clamp01(_previousTotal / (float)safeVictoryScore);
            float finalRatio = Mathf.Clamp01(data.CumulativeTotal / (float)safeVictoryScore);
            _previousFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            SetAnchors(_previousFill.rectTransform, 0f, cursor);

            for (int i = 0; i < data.Segments.Count; i++)
            {
                RoundResultScoreSegment segment = data.Segments[i];
                if (segment == null || segment.Points <= 0)
                {
                    continue;
                }

                float next = Mathf.Min(finalRatio, cursor + segment.Points / (float)safeVictoryScore);
                RectTransform rect = null;
                if (next > cursor)
                {
                    rect = CreateHatchedSegment(
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

            _rowRect.anchoredPosition =
                _restPosition + Vector2.left * (34f * (1f - _revealEased));
        }

        private static float EaseOutCubic(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return 1f - Mathf.Pow(1f - clamped, 3f);
        }
        private RectTransform CreateHatchedSegment(
            string objectName,
            float minX,
            float maxX,
            Color inkColor)
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

            Image paper = segmentObject.GetComponent<Image>();
            paper.sprite = _previousFill.sprite;
            paper.type = _previousFill.type;
            paper.pixelsPerUnitMultiplier = _previousFill.pixelsPerUnitMultiplier;
            paper.color = Color.Lerp(new Color32(245, 240, 214, 255), inkColor, 0.16f);
            paper.raycastTarget = false;

            Outline outline = segmentObject.GetComponent<Outline>();
            outline.effectColor = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
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
            hatch.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.88f);
            hatch.raycastTarget = false;

            return rect;
        }
}
}

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
        [SerializeField] private RectTransform _segmentRoot;
        [SerializeField] private GameObject _winnerBadge;
        [SerializeField] private TMP_Text _winnerText;

        private Vector2 _restPosition;

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
            _segmentRoot = segmentRoot;
            _winnerBadge = winnerBadge;
            _winnerText = winnerText;
        }

        public void Populate(RoundResultPlayerData data, int rank, int victoryScore)
        {
            int safeVictoryScore = Mathf.Max(1, victoryScore);
            _restPosition = _rowRect.anchoredPosition;
            _rankText.text = rank.ToString();
            _playerNameText.text = string.IsNullOrWhiteSpace(data.PlayerName) ? "Player" : data.PlayerName;
            _avatarBackground.color = data.PlayerColor;
            _avatarInitial.text = GetInitial(data.PlayerName);
            _scoreText.text = $"{data.CumulativeTotal} / {safeVictoryScore}";
            _deltaText.text = data.RoundTotal > 0 ? $"+{data.RoundTotal}" : "+0";
            _deltaText.color = data.RoundTotal > 0 ? new Color32(35, 153, 112, 255) : new Color32(112, 112, 126, 255);
            _winnerBadge.SetActive(data.IsRoundWinner);
            _winnerText.text = "WINNER";

            ClearDynamicSegments();

            float cursor = Mathf.Clamp01(data.PreviousTotal / (float)safeVictoryScore);
            float finalRatio = Mathf.Clamp01(data.CumulativeTotal / (float)safeVictoryScore);
            SetAnchors(_previousFill.rectTransform, 0f, cursor);

            for (int i = 0; i < data.Segments.Count; i++)
            {
                RoundResultScoreSegment segment = data.Segments[i];
                if (segment == null || segment.Points <= 0)
                {
                    continue;
                }

                float next = Mathf.Min(finalRatio, cursor + segment.Points / (float)safeVictoryScore);
                GameObject segmentObject = new($"Segment_{segment.ScoreType}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                RectTransform rect = segmentObject.GetComponent<RectTransform>();
                rect.SetParent(_segmentRoot, false);
                SetAnchors(rect, cursor, next);
                Image image = segmentObject.GetComponent<Image>();
                image.sprite = _previousFill.sprite;
                image.type = _previousFill.type;
                image.pixelsPerUnitMultiplier = _previousFill.pixelsPerUnitMultiplier;
                image.color = segment.Color;
                image.raycastTarget = false;
                cursor = next;
            }

            SetReveal(0f);
        }

        public void SetReveal(float t)
        {
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = eased;
            }

            if (_rowRect != null)
            {
                _rowRect.anchoredPosition = _restPosition + Vector2.left * (34f * (1f - eased));
            }

            if (_fillContent != null)
            {
                _fillContent.localScale = new Vector3(eased, 1f, 1f);
            }
        }

        public void SetImmediateVisible()
        {
            SetReveal(1f);
        }

        private void ClearDynamicSegments()
        {
            if (_segmentRoot == null)
            {
                return;
            }

            for (int i = _segmentRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = _segmentRoot.GetChild(i).gameObject;
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
            if (_rowRect != null)
            {
                _restPosition = _rowRect.anchoredPosition;
            }
        }
}
}

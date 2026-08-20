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

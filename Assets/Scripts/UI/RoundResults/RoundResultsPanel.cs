using System;
using System.Collections;
using System.Collections.Generic;
using SuperQQ.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI.RoundResults
{
    public sealed class RoundResultsPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _board;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _subtitleText;
        [SerializeField] private TMP_Text _victoryLineText;
        [SerializeField] private RectTransform _rowsRoot;
        [SerializeField] private RoundResultRowView _rowPrefab;
        [SerializeField] private Button _continueButton;
        [SerializeField] private TMP_Text _continueButtonText;

        [Header("Behaviour")]
        [SerializeField, Min(1)] private int _victoryScore = 100;
        [SerializeField] private bool _notifyGameFlowOnContinue;
        [SerializeField, Min(0.05f)] private float _panelRevealDuration = 0.28f;
        [SerializeField, Min(0.05f)] private float _rowRevealDuration = 0.42f;
        [SerializeField, Min(0f)] private float _rowStagger = 0.08f;

        private readonly List<RoundResultRowView> _rows = new();
        private Coroutine _animation;
        private Action _onContinue;
        private Vector3 _boardRestScale = Vector3.one;

        public int VictoryScore
        {
            get => _victoryScore;
            set => _victoryScore = Mathf.Max(1, value);
        }

        public void Configure(
            CanvasGroup canvasGroup,
            RectTransform board,
            TMP_Text titleText,
            TMP_Text subtitleText,
            TMP_Text victoryLineText,
            RectTransform rowsRoot,
            RoundResultRowView rowPrefab,
            Button continueButton,
            TMP_Text continueButtonText)
        {
            _canvasGroup = canvasGroup;
            _board = board;
            _titleText = titleText;
            _subtitleText = subtitleText;
            _victoryLineText = victoryLineText;
            _rowsRoot = rowsRoot;
            _rowPrefab = rowPrefab;
            _continueButton = continueButton;
            _continueButtonText = continueButtonText;
        }

        private void Awake()
        {
            if (_board != null)
            {
                _boardRestScale = _board.localScale;
            }

            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(HandleContinuePressed);
                _continueButton.onClick.AddListener(HandleContinuePressed);
            }
        }

        private void OnDestroy()
        {
            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(HandleContinuePressed);
            }
        }

        public bool ShowCurrentRound(Action onContinue = null)
        {
            List<RoundResultPlayerData> entries = RoundResultsDataAdapter.BuildCurrentRound(out int roundIndex);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[RoundResultsPanel] 当前轮没有可显示的结算数据。");
                return false;
            }

            Show(entries, roundIndex, _victoryScore, onContinue);
            return true;
        }

public void Show(
            IReadOnlyList<RoundResultPlayerData> entries,
            int roundIndex,
            int victoryScore = 100,
            Action onContinue = null)
        {
            int usedRows = PrepareView(entries, roundIndex, victoryScore, onContinue);

            if (_animation != null)
            {
                StopCoroutine(_animation);
            }
            _animation = StartCoroutine(PlayRevealAnimation(usedRows));
        }

public void ShowImmediate(
            IReadOnlyList<RoundResultPlayerData> entries,
            int roundIndex,
            int victoryScore = 100,
            Action onContinue = null)
        {
            int usedRows = PrepareView(entries, roundIndex, victoryScore, onContinue);

            if (_animation != null)
            {
                StopCoroutine(_animation);
                _animation = null;
            }

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _board.localScale = _boardRestScale;

            for (int i = 0; i < usedRows; i++)
            {
                _rows[i].SetImmediateVisible();
            }

            _continueButton.interactable = true;
        }

private int PrepareView(
            IReadOnlyList<RoundResultPlayerData> entries,
            int roundIndex,
            int victoryScore,
            Action onContinue)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            _victoryScore = Mathf.Max(1, victoryScore);
            _onContinue = onContinue;
            gameObject.SetActive(true);

            _titleText.text = "ROUND RESULTS";
            _subtitleText.text = roundIndex > 0 ? $"ROUND {roundIndex}" : "ROUND COMPLETE";
            _victoryLineText.text = $"GOAL  {_victoryScore}";
            _continueButtonText.text = "CONTINUE";
            _continueButton.interactable = false;

            EnsureRowCount(entries.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool used = i < entries.Count;
                _rows[i].gameObject.SetActive(used);
                if (used)
                {
                    _rows[i].Populate(entries[i], i + 1, _victoryScore);
                }
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rowsRoot);

            for (int i = 0; i < entries.Count; i++)
            {
                _rows[i].CaptureLayoutPosition();
                _rows[i].SetReveal(0f);
            }

            return entries.Count;
        }



        public void HideImmediate()
        {
            if (_animation != null)
            {
                StopCoroutine(_animation);
                _animation = null;
            }

            _onContinue = null;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }

            gameObject.SetActive(false);
        }

        public void SetNotifyGameFlowOnContinue(bool enabled)
        {
            _notifyGameFlowOnContinue = enabled;
        }

        private void EnsureRowCount(int count)
        {
            if (_rowPrefab == null || _rowsRoot == null)
            {
                throw new InvalidOperationException("[RoundResultsPanel] Row Prefab 或 Rows Root 未配置。");
            }

            if (_rows.Count == 0)
            {
                RoundResultRowView[] existing = _rowsRoot.GetComponentsInChildren<RoundResultRowView>(true);
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != _rowPrefab)
                    {
                        _rows.Add(existing[i]);
                    }
                }
            }

            while (_rows.Count < count)
            {
                RoundResultRowView row = Instantiate(_rowPrefab, _rowsRoot);
                row.name = $"RoundResultRow_{_rows.Count + 1:00}";
                row.gameObject.SetActive(true);
                _rows.Add(row);
            }

            _rowPrefab.gameObject.SetActive(false);
        }

        private IEnumerator PlayRevealAnimation(int usedRows)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _board.localScale = _boardRestScale * 0.88f;

            float elapsed = 0f;
            while (elapsed < _panelRevealDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _panelRevealDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _canvasGroup.alpha = eased;
                _board.localScale = Vector3.LerpUnclamped(_boardRestScale * 0.88f, _boardRestScale, eased);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            _board.localScale = _boardRestScale;

            for (int i = 0; i < usedRows; i++)
            {
                RoundResultRowView row = _rows[i];
                float rowElapsed = 0f;
                while (rowElapsed < _rowRevealDuration)
                {
                    rowElapsed += Time.unscaledDeltaTime;
                    row.SetReveal(rowElapsed / _rowRevealDuration);
                    yield return null;
                }

                row.SetImmediateVisible();
                if (_rowStagger > 0f)
                {
                    yield return new WaitForSecondsRealtime(_rowStagger);
                }
            }

            _continueButton.interactable = true;
            _animation = null;
        }

        private void HandleContinuePressed()
        {
            if (_continueButton != null)
            {
                _continueButton.interactable = false;
            }

            Action callback = _onContinue;
            _onContinue = null;

            if (_notifyGameFlowOnContinue && GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.NotifyCurrentPhaseEvent();
            }

            HideImmediate();
            callback?.Invoke();
        }
    }
}

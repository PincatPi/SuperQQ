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

        // 动态记分行（由外部配置的 prefab + container 实例化生成，与 _rows 的内建行相互独立）
        private RoundResultRowView _dynamicRowPrefab;
        private RectTransform _dynamicRowsContainer;
        private readonly List<RoundResultRowView> _dynamicRows = new();

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
            _animation = StartCoroutine(PlayRevealAnimation(CollectUsedRows(usedRows)));
        }

        /// <summary>
        /// 收集内建行中实际使用的前 usedRows 行，供揭示动画驱动。
        /// </summary>
        private List<RoundResultRowView> CollectUsedRows(int usedRows)
        {
            var used = new List<RoundResultRowView>(usedRows);
            for (int i = 0; i < usedRows; i++)
            {
                used.Add(_rows[i]);
            }
            return used;
        }

        /// <summary>
        /// 一体展示：面板框架（标题/目标线/继续按钮）+ 动态记分行。
        /// 记分行统一由传入的 prefab + container 实例化生成（玩家名、icon、当前总分），
        /// 不走内建行路径，避免两套行同时出现。
        /// </summary>
        public bool ShowCurrentRoundPlayerRows(RoundResultRowView rowPrefab, RectTransform rowsContainer, Action onContinue = null)
        {
            List<RoundResultPlayerData> entries = RoundResultsDataAdapter.BuildCurrentRound(out int roundIndex);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[RoundResultsPanel] 当前轮没有可显示的结算数据。");
                return false;
            }

            ConfigureRowFactory(rowPrefab, rowsContainer);
            ClearPlayerRows();

            // 复用框架展示流程（标题/目标线/按钮），传空数据跳过内建行生成
            PrepareView(System.Array.Empty<RoundResultPlayerData>(), roundIndex, _victoryScore, onContinue);

            for (int i = 0; i < entries.Count; i++)
            {
                RoundResultPlayerData entry = entries[i];
                RoundResultRowView row = AddPlayerRow(entry.PlayerName, entry.PlayerIcon, entry.CumulativeTotal);
                if (row != null)
                {
                    row.CaptureLayoutPosition();
                    row.SetReveal(0f);
                }
            }

            Canvas.ForceUpdateCanvases();
            if (rowsContainer != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rowsContainer);
            }
            for (int i = 0; i < _dynamicRows.Count; i++)
            {
                _dynamicRows[i].CaptureLayoutPosition();
                _dynamicRows[i].SetReveal(0f);
            }

            if (_animation != null)
            {
                StopCoroutine(_animation);
            }
            _animation = StartCoroutine(PlayRevealAnimation(_dynamicRows));
            return true;
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
            if (_rowsRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_rowsRoot);
            }

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

            ClearPlayerRows();
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

        // ==================== 玩家记分行（动态实例化） ====================

        /// <summary>
        /// 配置记分行的 prefab 与挂载容器（VerticalLayoutGroup），供 <see cref="AddPlayerRow"/> 使用。
        /// 若 prefab 引用的是场景内实例（摆在 container 下作模板），将其隐藏，只作为实例化模板使用。
        /// </summary>
        public void ConfigureRowFactory(RoundResultRowView rowPrefab, RectTransform rowsContainer)
        {
            _dynamicRowPrefab = rowPrefab;
            _dynamicRowsContainer = rowsContainer;

            if (_dynamicRowPrefab != null && _dynamicRowPrefab.gameObject.scene.IsValid())
            {
                _dynamicRowPrefab.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 实例化一行玩家记分 prefab 并挂载到 container 下，填充玩家名、icon 与当前总分数。
        /// 返回生成的行视图；未配置工厂时返回 null。
        /// </summary>
        public RoundResultRowView AddPlayerRow(string playerName, Sprite playerIcon, int totalScore)
        {
            if (_dynamicRowPrefab == null || _dynamicRowsContainer == null)
            {
                Debug.LogError("[RoundResultsPanel] 记分行 prefab 或 container 未配置，请先调用 ConfigureRowFactory。", this);
                return null;
            }

            RoundResultRowView row = Instantiate(_dynamicRowPrefab, _dynamicRowsContainer);
            row.name = $"PlayerScoreRow_{_dynamicRows.Count + 1:00}";
            row.gameObject.SetActive(true);
            row.PopulateSummary(playerName, playerIcon, totalScore, _dynamicRows.Count + 1);
            _dynamicRows.Add(row);
            return row;
        }

        /// <summary>
        /// 销毁全部动态生成的记分行。
        /// </summary>
        public void ClearPlayerRows()
        {
            for (int i = 0; i < _dynamicRows.Count; i++)
            {
                if (_dynamicRows[i] != null)
                {
                    Destroy(_dynamicRows[i].gameObject);
                }
            }
            _dynamicRows.Clear();
        }

        private void EnsureRowCount(int count)
        {
            // 不使用内建行（动态记分行模式传 0）：隐藏已收集的行与模板后直接返回，
            // 此时允许内建 prefab/容器未配置
            if (count <= 0)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i] != null)
                    {
                        _rows[i].gameObject.SetActive(false);
                    }
                }
                if (_rowPrefab != null)
                {
                    _rowPrefab.gameObject.SetActive(false);
                }
                return;
            }

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

        private IEnumerator PlayRevealAnimation(IReadOnlyList<RoundResultRowView> rows)
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

            for (int i = 0; i < rows.Count; i++)
            {
                RoundResultRowView row = rows[i];
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

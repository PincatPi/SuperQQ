using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.UI.RoundResults
{
    public sealed class RoundResultsPanelHost : MonoBehaviour
    {
        [SerializeField] private RoundResultsPanel _panelPrefab;
        [SerializeField] private Canvas _targetCanvas;
        [SerializeField] private bool _notifyGameFlowOnContinue;

        private RoundResultsPanel _panelInstance;

        public RoundResultsPanel CurrentInstance => _panelInstance;

        public bool ShowCurrentRound(Action onContinue = null)
        {
            RoundResultsPanel panel = GetOrCreatePanel();
            if (panel == null)
            {
                return false;
            }

            panel.SetNotifyGameFlowOnContinue(_notifyGameFlowOnContinue);
            return panel.ShowCurrentRound(onContinue);
        }

        public void Show(
            IReadOnlyList<RoundResultPlayerData> entries,
            int roundIndex,
            int victoryScore = 100,
            Action onContinue = null)
        {
            RoundResultsPanel panel = GetOrCreatePanel();
            if (panel == null)
            {
                return;
            }

            panel.SetNotifyGameFlowOnContinue(_notifyGameFlowOnContinue);
            panel.Show(entries, roundIndex, victoryScore, onContinue);
        }

        public void Hide()
        {
            if (_panelInstance != null)
            {
                _panelInstance.HideImmediate();
            }
        }

        private RoundResultsPanel GetOrCreatePanel()
        {
            if (_panelInstance != null)
            {
                return _panelInstance;
            }

            if (_panelPrefab == null)
            {
                Debug.LogError("[RoundResultsPanelHost] Panel Prefab is not assigned.", this);
                return null;
            }

            Transform parent = _targetCanvas != null ? _targetCanvas.transform : transform;
            _panelInstance = Instantiate(_panelPrefab, parent, false);
            _panelInstance.name = _panelPrefab.name;
            return _panelInstance;
        }
    }
}

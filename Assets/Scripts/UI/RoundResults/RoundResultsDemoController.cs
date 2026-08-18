using System.Collections.Generic;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.UI.RoundResults
{
    /// <summary>
    /// Standalone sample driver for Assets/Scenes/RoundResultsDemo.unity.
    /// Press R in Play Mode to replay the settlement reveal.
    /// </summary>
    public sealed class RoundResultsDemoController : MonoBehaviour
    {
        [SerializeField] private RoundResultsPanel _panel;
        [SerializeField, Min(1)] private int _roundIndex = 3;
        [SerializeField, Min(1)] private int _victoryScore = 100;

        public void Configure(RoundResultsPanel panel)
        {
            _panel = panel;
        }

        private void Start()
        {
            ShowDemo();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ShowDemo();
            }
        }

        public void ShowDemo()
        {
            if (_panel == null)
            {
                Debug.LogError("[RoundResultsDemo] RoundResultsPanel reference is missing.", this);
                return;
            }

            _panel.SetNotifyGameFlowOnContinue(false);
            _panel.Show(BuildSampleResults(), _roundIndex, _victoryScore, HandleContinue);
        }

        public List<RoundResultPlayerData> BuildSampleResults()
        {
            List<RoundResultPlayerData> results = new List<RoundResultPlayerData>();

            RoundResultPlayerData fox = CreatePlayer("PAPER FOX", new Color32(244, 107, 78, 255), 55, 32, true);
            AddSegment(fox, ScoreType.Completion, 10);
            AddSegment(fox, ScoreType.FirstPlace, 12);
            AddSegment(fox, ScoreType.TrapKill, 10);
            results.Add(fox);

            RoundResultPlayerData turtle = CreatePlayer("TURBO TURTLE", new Color32(65, 184, 198, 255), 48, 21, false);
            AddSegment(turtle, ScoreType.Completion, 10);
            AddSegment(turtle, ScoreType.SpecialEffect, 6);
            AddSegment(turtle, ScoreType.ScoreItem, 5);
            results.Add(turtle);

            RoundResultPlayerData sheep = CreatePlayer("WOOLLY", new Color32(155, 112, 211, 255), 43, 16, false);
            AddSegment(sheep, ScoreType.Completion, 10);
            AddSegment(sheep, ScoreType.TrapKill, 6);
            results.Add(sheep);

            RoundResultPlayerData raccoon = CreatePlayer("RASCAL", new Color32(245, 183, 62, 255), 31, 8, false);
            AddSegment(raccoon, ScoreType.ScoreItem, 8);
            results.Add(raccoon);

            return results;
        }

        private static RoundResultPlayerData CreatePlayer(
            string playerName,
            Color playerColor,
            int previousTotal,
            int roundTotal,
            bool isRoundWinner)
        {
            return new RoundResultPlayerData
            {
                PlayerName = playerName,
                PlayerColor = playerColor,
                PreviousTotal = previousTotal,
                RoundTotal = roundTotal,
                CumulativeTotal = previousTotal + roundTotal,
                IsRoundWinner = isRoundWinner
            };
        }

        private static void AddSegment(RoundResultPlayerData player, ScoreType scoreType, int points)
        {
            player.Segments.Add(new RoundResultScoreSegment
            {
                ScoreType = scoreType,
                Label = RoundResultsDataAdapter.GetSegmentLabel(scoreType),
                Points = points,
                Color = RoundResultsDataAdapter.GetSegmentColor(scoreType)
            });
        }

        private static void HandleContinue()
        {
            Debug.Log("[RoundResultsDemo] Continue pressed. Press R to replay the demo.");
        }
    }
}

using System;
using System.Collections.Generic;
using SuperQQ.Player;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.UI.RoundResults
{
    [Serializable]
    public sealed class RoundResultScoreSegment
    {
        public ScoreType ScoreType;
        public string Label;
        public int Points;
        public Color Color;
    }

    [Serializable]
    public sealed class RoundResultPlayerData
    {
        public string PlayerName;
        public Color PlayerColor = Color.white;
        public Sprite PlayerIcon;
        public int PreviousTotal;
        public int RoundTotal;
        public int CumulativeTotal;
        public bool IsRoundWinner;
        public List<RoundResultScoreSegment> Segments = new();
    }

    public static class RoundResultsDataAdapter
    {
        // 分段顺序与面板底部 Legend 一致：通关/第一名/独行/陷阱击败/翻盘/金币
        private static readonly ScoreType[] SegmentOrder =
        {
            ScoreType.Completion,
            ScoreType.FirstPlace,
            ScoreType.SoloClear,
            ScoreType.TrapKill,
            ScoreType.SpecialEffect,
            ScoreType.ScoreItem
        };

        /// <param name="bNoPlayerFinished">本轮是否无人通关（全员不加分，供结算面板提示）</param>
        public static List<RoundResultPlayerData> BuildCurrentRound(out int roundIndex, out bool bNoPlayerFinished)
        {
            PlayerScoreManager scoreManager = PlayerScoreManager.Instance;
            roundIndex = scoreManager != null ? scoreManager.CurrentRoundIndex : 0;
            List<RoundResultPlayerData> result = new();

            if (scoreManager == null)
            {
                Debug.LogWarning("[RoundResultsDataAdapter] PlayerScoreManager 不存在。");
                bNoPlayerFinished = false;
                return result;
            }

            List<string> names = GetOrderedNames(scoreManager);
            int bestRoundScore = int.MinValue;

            for (int i = 0; i < names.Count; i++)
            {
                string playerName = names[i];
                RoundScoreData round = scoreManager.GetPlayerRoundScore(playerName, roundIndex);
                if (round == null)
                {
                    continue;
                }

                RoundResultPlayerData player = new()
                {
                    PlayerName = playerName,
                    PlayerColor = GetPlayerColor(playerName),
                    PlayerIcon = GetPlayerIcon(playerName),
                    PreviousTotal = Mathf.Max(0, round.CumulativeTotal - round.RoundTotal),
                    RoundTotal = round.RoundTotal,
                    CumulativeTotal = round.CumulativeTotal
                };

                // 联机模式：总分与六个明细分均以服务器结算为准（服务器统一算分）；
                // 服务器为旧版本（明细全 0）时回退本地算分明细
                bool bServerBreakdown = false;
                if (TryGetServerScore(playerName, out Network.NetGameFlowGate.ServerPlayerScore serverScore))
                {
                    player.RoundTotal = serverScore.RoundScore;
                    player.CumulativeTotal = serverScore.TotalScore;
                    player.PreviousTotal = Mathf.Max(0, serverScore.TotalScore - serverScore.RoundScore);
                    bServerBreakdown = serverScore.BHasBreakdown;
                }

                if (bServerBreakdown)
                {
                    AddServerSegments(player, serverScore);
                }
                else
                {
                    AddSegments(player, round);
                }
                bestRoundScore = Mathf.Max(bestRoundScore, player.RoundTotal);
                result.Add(player);
            }

            result.Sort((a, b) =>
            {
                int cumulative = b.CumulativeTotal.CompareTo(a.CumulativeTotal);
                if (cumulative != 0)
                {
                    return cumulative;
                }

                int round = b.RoundTotal.CompareTo(a.RoundTotal);
                return round != 0 ? round : string.CompareOrdinal(a.PlayerName, b.PlayerName);
            });

            for (int i = 0; i < result.Count; i++)
            {
                result[i].IsRoundWinner = result[i].RoundTotal > 0 &&
                                          result[i].RoundTotal == bestRoundScore;
            }

            bNoPlayerFinished = ResolveNoPlayerFinished(result);
            return result;
        }

        /// <summary>
        /// 判定本轮是否为"不加分局"（无人通关 或 全员通关，统一提示不加分）：
        /// 以玩家注册表中的 Finished 状态为准（结算阶段玩家尚未复活，状态仍有效；
        /// 联机端远端状态由快照/出局广播同步到本地化身）——通关人数为 0（规则1：无人通关）
        /// 或等于在场人数（规则2：全员通关，仅金币计分）时提示不加分。
        /// 注册表不可用时回退为"全员本轮 0 分"推断（此时无法区分两种局，统一文案下无影响）。
        /// </summary>
        private static bool ResolveNoPlayerFinished(List<RoundResultPlayerData> entries)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null && registry.Players.Count > 0)
            {
                int finished = registry.GetPlayersByState(PlayerStateType.Finished).Count;
                return finished == 0 || finished >= registry.Players.Count;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].RoundTotal > 0)
                {
                    return false;
                }
            }
            return entries.Count > 0;
        }

        public static Color GetSegmentColor(ScoreType scoreType)
        {
            return scoreType switch
            {
                ScoreType.Completion => new Color32(41, 185, 172, 255),
                ScoreType.FirstPlace => new Color32(88, 190, 105, 255),
                ScoreType.SoloClear => new Color32(38, 167, 231, 255),
                ScoreType.TrapKill => new Color32(244, 126, 178, 255),
                ScoreType.SpecialEffect => new Color32(166, 138, 229, 255),
                ScoreType.ScoreItem => new Color32(254, 210, 108, 255),
                _ => Color.white
            };
        }

        public static string GetSegmentLabel(ScoreType scoreType)
        {
            return scoreType switch
            {
                ScoreType.Completion => "通关",
                ScoreType.FirstPlace => "第一名",
                ScoreType.SoloClear => "独行",
                ScoreType.TrapKill => "陷阱击败",
                ScoreType.SpecialEffect => "翻盘",
                ScoreType.ScoreItem => "金币",
                _ => scoreType.ToString()
            };
        }

        private static void AddSegments(RoundResultPlayerData player, RoundScoreData round)
        {
            if (round.ScoreBreakdown == null)
            {
                return;
            }

            for (int i = 0; i < SegmentOrder.Length; i++)
            {
                ScoreType scoreType = SegmentOrder[i];
                if (!round.ScoreBreakdown.TryGetValue(scoreType, out int points) || points <= 0)
                {
                    continue;
                }

                player.Segments.Add(new RoundResultScoreSegment
                {
                    ScoreType = scoreType,
                    Label = GetSegmentLabel(scoreType),
                    Points = points,
                    Color = GetSegmentColor(scoreType)
                });
            }
        }

        /// <summary>按服务器下发的六个明细分构建分段（映射到 ScoreType 六类）</summary>
        private static void AddServerSegments(
            RoundResultPlayerData player,
            Network.NetGameFlowGate.ServerPlayerScore serverScore)
        {
            for (int i = 0; i < SegmentOrder.Length; i++)
            {
                ScoreType scoreType = SegmentOrder[i];
                int points = scoreType switch
                {
                    ScoreType.Completion => serverScore.FinishScore,
                    ScoreType.FirstPlace => serverScore.FirstFinishScore,
                    ScoreType.SoloClear => serverScore.SoloFinishScore,
                    ScoreType.TrapKill => serverScore.TrapKillScore,
                    ScoreType.SpecialEffect => serverScore.OvertakeScore,
                    ScoreType.ScoreItem => serverScore.CoinScore,
                    _ => 0
                };
                if (points <= 0)
                {
                    continue;
                }

                player.Segments.Add(new RoundResultScoreSegment
                {
                    ScoreType = scoreType,
                    Label = GetSegmentLabel(scoreType),
                    Points = points,
                    Color = GetSegmentColor(scoreType)
                });
            }
        }

        /// <summary>取服务器下发的该玩家分数（玩家名 → playerId 映射后查询）；无则返回 false</summary>
        private static bool TryGetServerScore(string playerName, out Network.NetGameFlowGate.ServerPlayerScore score)
        {
            score = default;
            if (!Network.NetGameFlowGate.BHasServerScores)
            {
                return false;
            }

            string playerId = null;
            PlayerProfile profile = PlayerSessionManager.Instance != null
                ? PlayerSessionManager.Instance.GetProfile(playerName)
                : null;
            if (profile != null && !string.IsNullOrEmpty(profile.PlayerId))
            {
                playerId = profile.PlayerId;
            }

            // 档案未写入 PlayerId 时回退用化身的网络身份（LocalPlayerNetSetup 已写入化身），
            // 避免档案身份缺失导致服务器分数永远查不到
            if (playerId == null && LevelPlayerRegistry.Instance != null)
            {
                IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].PlayerName == playerName &&
                        !string.IsNullOrEmpty(players[i].PlayerId))
                    {
                        playerId = players[i].PlayerId;
                        break;
                    }
                }
            }

            return playerId != null && Network.NetGameFlowGate.TryGetServerScore(playerId, out score);
        }

        private static List<string> GetOrderedNames(PlayerScoreManager scoreManager)
        {
            if (PlayerSessionManager.Instance != null)
            {
                List<string> sessionNames = PlayerSessionManager.Instance.GetOrderedPlayerNames();
                if (sessionNames != null && sessionNames.Count > 0)
                {
                    return sessionNames;
                }
            }

            return scoreManager.GetRankedPlayerNames();
        }

        /// <summary>
        /// 从当前关卡玩家注册表解析玩家图标（选择阶段配置的标识图）；化身不在场时返回 null。
        /// </summary>
        private static Sprite GetPlayerIcon(string playerName)
        {
            if (LevelPlayerRegistry.Instance != null)
            {
                IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].PlayerName == playerName)
                    {
                        return players[i].SelectionIconSprite;
                    }
                }
            }

            return null;
        }

        private static Color GetPlayerColor(string playerName)
        {
            if (PlayerSessionManager.Instance != null)
            {
                PlayerProfile profile = PlayerSessionManager.Instance.GetProfile(playerName);
                if (profile != null)
                {
                    return profile.PlayerColor;
                }
            }

            return Color.white;
        }
    }
}

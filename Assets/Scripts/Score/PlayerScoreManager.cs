using System;
using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperQQ.Score
{
    /// <summary>
    /// 玩家得分管理器 — 跨场景持久化的得分数据中心
    /// 由 GamePhaseManager 在正式游玩结束时主动触发本轮结算
    /// 暴露查询接口供结算页、HUD 等读取
    /// 陷阱系统和老板事件系统通过本类的记录接口提交数据
    /// 持久化层：与 PlayerSessionManager 同层，随 DontDestroyOnLoad 跨场景保留
    /// </summary>
    public class PlayerScoreManager : MonoBehaviour
    {
        // ==================== 事件 ====================

        /// <summary>
        /// 轮次结算完成事件
        /// 参数为本轮所有玩家的得分明细，键为玩家名称
        /// </summary>
        public event Action<Dictionary<string, RoundScoreData>> OnRoundScored;

        /// <summary>
        /// 整场结束事件：至少一人达到100分胜利线时触发
        /// 参数为最终排名的玩家名称列表（已排序）
        /// </summary>
        public event Action<List<string>> OnGameFinished;

        // 单例实例
        private static PlayerScoreManager _instance;

        // 每个玩家的得分记录，键为玩家名称
        private readonly Dictionary<string, PlayerScoreRecord> _scoreRecords = new();

        // 本轮中间数据：陷阱击杀次数
        private readonly Dictionary<string, int> _roundTrapKillCounts = new();

        // 本轮中间数据：额外加分（金币等得分道具在玩家通关时提交）
        private readonly Dictionary<string, int> _roundBonusScores = new();

        // 当前轮次索引（从1开始，0表示尚未开始）
        private int _currentRoundIndex;

        // 是否已完成本轮结算
        private bool _bIsRoundScored;

        // 当前订阅的关卡注册表引用（场景级，随场景切换更换）
        private LevelPlayerRegistry _currentRegistry;

        // ==================== 公开常量 ====================

        /// <summary>
        /// 胜利线分数
        /// </summary>
        public const int VICTORY_LINE = 100;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static PlayerScoreManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlayerScoreManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 当前轮次索引
        /// </summary>
        public int CurrentRoundIndex => _currentRoundIndex;

        /// <summary>
        /// 是否已完成本轮结算
        /// </summary>
        public bool BIsRoundScored => _bIsRoundScored;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            UnsubscribeFromRegistry();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// 场景加载完成回调
        /// 关卡场景加载完成时订阅本关 LevelPlayerRegistry 的事件
        /// 结算场景加载完成时清理对本关 Registry 的订阅
        /// </summary>
        /// <param name="scene">已加载的场景</param>
        /// <param name="mode">加载模式</param>
        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            // 总是先清理旧订阅，再尝试订阅新场景的 Registry
            UnsubscribeFromRegistry();
            SubscribeToCurrentRegistry();
        }

        // ==================== 事件订阅 ====================

        /// <summary>
        /// 订阅当前场景中的 LevelPlayerRegistry 事件
        /// 若当前场景无 Registry（如结算场景），则不做任何事
        /// 首次进入关卡时初始化第一轮得分记录
        /// </summary>
        private void SubscribeToCurrentRegistry()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            _currentRegistry = registry;

            // 首次进入关卡时初始化第一轮
            if (_currentRoundIndex == 0)
            {
                InitializeFirstRound();
            }
        }

        /// <summary>
        /// 取消当前 LevelPlayerRegistry 引用
        /// 场景切换前调用，避免引用已销毁的 Registry
        /// </summary>
        private void UnsubscribeFromRegistry()
        {
            _currentRegistry = null;
        }

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化第一轮：根据 PlayerSessionManager 的档案列表注册所有玩家得分记录
        /// 后续新玩家通过 PlayerSessionManager.OnProfileRegistered 事件被动注册
        /// </summary>
        private void InitializeFirstRound()
        {
            _currentRoundIndex = 1;
            _bIsRoundScored = false;

            if (PlayerSessionManager.Instance == null)
            {
                Debug.LogWarning("[PlayerScoreManager] PlayerSessionManager 不存在，无法初始化第一轮。");
                return;
            }

            IReadOnlyList<PlayerProfile> profiles = PlayerSessionManager.Instance.Profiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] != null)
                {
                    RegisterScoreRecord(profiles[i].PlayerName);
                }
            }

            // 订阅后续新档案注册事件
            PlayerSessionManager.Instance.OnProfileRegistered += HandleProfileRegistered;
        }

        /// <summary>
        /// 注册一个玩家的得分记录
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        private void RegisterScoreRecord(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return;
            }

            if (_scoreRecords.ContainsKey(playerName))
            {
                return;
            }

            _scoreRecords[playerName] = new PlayerScoreRecord
            {
                PlayerName = playerName,
                TotalScore = 0,
                TotalFinishCount = 0,
                TotalTrapKillCount = 0
            };
        }

        /// <summary>
        /// 新玩家 Profile 注册时的处理：初始化其得分记录
        /// 由 PlayerSessionManager.OnProfileRegistered 事件触发
        /// </summary>
        /// <param name="profile">新注册的玩家档案</param>
        private void HandleProfileRegistered(PlayerProfile profile)
        {
            if (profile != null)
            {
                RegisterScoreRecord(profile.PlayerName);
            }
        }

        // ==================== 轮次结算触发 ====================

        /// <summary>
        /// 主动结算当前轮次。
        /// 由 GamePhaseManager 在正式游玩阶段结束时调用，避免计分系统直接驱动游戏流程。
        /// </summary>
        public void SettleCurrentRound()
        {
            if (_bIsRoundScored)
            {
                return;
            }

            CalculateCurrentRoundScores();
        }

        // ==================== 核心计算 ====================

        /// <summary>
        /// 计算当前轮次的得分
        /// 汇总本轮中间数据，构建 RoundScoreInput，调用 ScoreCalculator
        /// 更新每个玩家的记录，发出轮次结算事件，检测胜利线
        /// </summary>
        private void CalculateCurrentRoundScores()
        {
            RoundScoreInput input = BuildRoundScoreInput();
            Dictionary<string, int> previousCumulative = BuildPreviousCumulativeScores();
            List<string> allPlayerNames = GetAllPlayerNames();

            Dictionary<string, RoundScoreData> results = ScoreCalculator.Calculate(
                _currentRoundIndex, allPlayerNames, input, previousCumulative);

            // 更新每个玩家的记录
            UpdateScoreRecords(results, input);

            _bIsRoundScored = true;

            // 发出轮次结算事件
            OnRoundScored?.Invoke(results);

            // 检测是否有人达到胜利线
            CheckVictoryLine();
        }

        /// <summary>
        /// 构建本轮结算输入数据：通关顺序、陷阱击杀、安静达标
        /// 通关玩家列表按当前 Registry 中的注册顺序筛选 Finished 状态玩家
        /// </summary>
        private RoundScoreInput BuildRoundScoreInput()
        {
            RoundScoreInput input = new RoundScoreInput();

            // 通关玩家列表（按 Registry 注册顺序）
            if (_currentRegistry != null)
            {
                List<PlayerController> finishedPlayers =
                    _currentRegistry.GetPlayersByState(PlayerStateType.Finished);

                for (int i = 0; i < finishedPlayers.Count; i++)
                {
                    if (finishedPlayers[i] != null)
                    {
                        input.FinishedPlayerNames.Add(finishedPlayers[i].PlayerName);
                    }
                }
            }

            // 陷阱击杀次数
            foreach (var pair in _roundTrapKillCounts)
            {
                input.TrapKillCounts[pair.Key] = pair.Value;
            }

            // 额外加分（金币等得分道具，仅通关玩家提交）
            foreach (var pair in _roundBonusScores)
            {
                input.BonusScores[pair.Key] = pair.Value;
            }

            return input;
        }

        /// <summary>
        /// 构建此前累计总分字典
        /// </summary>
        private Dictionary<string, int> BuildPreviousCumulativeScores()
        {
            Dictionary<string, int> previous = new();
            foreach (var pair in _scoreRecords)
            {
                previous[pair.Key] = pair.Value.TotalScore;
            }
            return previous;
        }

        /// <summary>
        /// 获取所有已注册玩家名称列表
        /// </summary>
        private List<string> GetAllPlayerNames()
        {
            List<string> names = new();
            foreach (var pair in _scoreRecords)
            {
                names.Add(pair.Key);
            }
            return names;
        }

        /// <summary>
        /// 更新每个玩家的得分记录：追加本轮明细、更新累计值和统计量
        /// </summary>
        /// <param name="results">ScoreCalculator 计算出的本轮得分结果</param>
        /// <param name="input">本轮结算输入数据，用于更新通关和击杀统计</param>
        private void UpdateScoreRecords(
            Dictionary<string, RoundScoreData> results,
            RoundScoreInput input)
        {
            foreach (var pair in results)
            {
                string playerName = pair.Key;
                RoundScoreData data = pair.Value;

                if (!_scoreRecords.TryGetValue(playerName, out PlayerScoreRecord record))
                {
                    continue;
                }

                // 追加本轮明细
                record.RoundHistory.Add(data);

                // 更新累计总分
                record.TotalScore = data.CumulativeTotal;

                // 更新通关次数
                if (IsPlayerFinished(input, playerName))
                {
                    record.TotalFinishCount++;
                }

                // 更新陷阱击杀次数
                int trapKills = GetTrapKillCount(input, playerName);
                record.TotalTrapKillCount += trapKills;
            }
        }

        // ==================== 胜利线检测 ====================

        /// <summary>
        /// 检测是否有人达到100分胜利线
        /// 若有，按累计总分→通关次数→陷阱命中次数排序，发出 OnGameFinished 事件
        /// 同轮多人过线时按累计总分、通关次数和陷阱命中次数排序
        /// </summary>
        private void CheckVictoryLine()
        {
            List<string> winners = new();

            foreach (var pair in _scoreRecords)
            {
                if (pair.Value.TotalScore >= VICTORY_LINE)
                {
                    winners.Add(pair.Key);
                }
            }

            if (winners.Count == 0)
            {
                return;
            }

            // 按累计总分降序 → 通关次数降序 → 陷阱命中次数降序排序
            winners.Sort((a, b) =>
            {
                PlayerScoreRecord ra = _scoreRecords[a];
                PlayerScoreRecord rb = _scoreRecords[b];

                if (rb.TotalScore != ra.TotalScore)
                {
                    return rb.TotalScore.CompareTo(ra.TotalScore);
                }
                if (rb.TotalFinishCount != ra.TotalFinishCount)
                {
                    return rb.TotalFinishCount.CompareTo(ra.TotalFinishCount);
                }
                return rb.TotalTrapKillCount.CompareTo(ra.TotalTrapKillCount);
            });

            OnGameFinished?.Invoke(winners);
        }

        // ==================== 中间数据记录（供其他系统调用） ====================

        /// <summary>
        /// 记录一次陷阱有效击杀
        /// 由陷阱系统在击杀发生时调用，同一玩家可多次调用
        /// </summary>
        /// <param name="ownerPlayerName">放置陷阱的玩家名称</param>
        public void RecordTrapKill(string ownerPlayerName)
        {
            if (string.IsNullOrEmpty(ownerPlayerName))
            {
                return;
            }

            if (!_roundTrapKillCounts.ContainsKey(ownerPlayerName))
            {
                _roundTrapKillCounts[ownerPlayerName] = 0;
            }
            _roundTrapKillCounts[ownerPlayerName]++;
        }

        /// <summary>
        /// 记录一次额外加分（金币等得分道具在跟随角色通关时提交，同一玩家可多次调用累加）
        /// 计入本轮结算的 BonusScores（叠加在通关分之上；未通关玩家不会提交，不产生分数）
        /// </summary>
        /// <param name="playerName">获得加分的玩家名称</param>
        /// <param name="points">加分值</param>
        public void RecordBonusScore(string playerName, int points)
        {
            if (string.IsNullOrEmpty(playerName) || points <= 0)
            {
                return;
            }

            if (!_roundBonusScores.ContainsKey(playerName))
            {
                _roundBonusScores[playerName] = 0;
            }
            _roundBonusScores[playerName] += points;
        }

        // ==================== 轮次管理 ====================

        /// <summary>
        /// 联机模式：按服务器轮次对齐本地记分簿。
        /// 联机时本地阶段转移被屏蔽，AdvanceToNextRound 的唯一调用点（OnTransitionSelected）
        /// 不会触发，记分簿会永远停在第 1 轮——这里改为跟随服务器 round 翻页：
        /// 服务器 round 更大 → 逐轮推进（清理中间数据、复位已结算标记）；
        /// 服务器 round 回退（全新一局） → 清空重开。
        /// 由 NetGameFlowGate 在收到 GamePhaseSync{PROP_SELECTION} 时调用。
        /// </summary>
        /// <param name="serverRound">服务器 GamePhaseSync 携带的轮次（从 1 起）</param>
        public void SyncToServerRound(int serverRound)
        {
            if (serverRound <= 0)
            {
                return;
            }

            if (serverRound < _currentRoundIndex)
            {
                // 服务器轮次回退说明是全新一局（本管理器跨场景存活，会残留上局数据）
                _scoreRecords.Clear();
                _roundTrapKillCounts.Clear();
                _roundBonusScores.Clear();
                // InitializeFirstRound 内部会重新订阅档案事件，先退订防止重复订阅
                if (PlayerSessionManager.Instance != null)
                {
                    PlayerSessionManager.Instance.OnProfileRegistered -= HandleProfileRegistered;
                }
                InitializeFirstRound();
                Debug.Log($"[Score] 检测到新一局（服务器 round={serverRound}），本地记分簿已重置");
                return;
            }

            while (_currentRoundIndex < serverRound)
            {
                AdvanceToNextRound();
            }
        }

        /// <summary>
        /// 进入下一轮：递增轮次索引、清空本轮中间数据
        /// 由 GamePhaseManager 在确认继续下一轮时调用
        /// </summary>
        public void AdvanceToNextRound()
        {
            _currentRoundIndex++;
            _bIsRoundScored = false;
            _roundTrapKillCounts.Clear();
            _roundBonusScores.Clear();
        }

        // ==================== 查询接口 ====================

        /// <summary>
        /// 获取指定玩家的得分记录
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <returns>得分记录，未找到时返回 null</returns>
        public PlayerScoreRecord GetPlayerScoreRecord(string playerName)
        {
            if (playerName != null && _scoreRecords.TryGetValue(playerName, out PlayerScoreRecord record))
            {
                return record;
            }
            return null;
        }

        /// <summary>
        /// 获取指定玩家的累计总分
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        public int GetPlayerTotalScore(string playerName)
        {
            PlayerScoreRecord record = GetPlayerScoreRecord(playerName);
            if (record != null)
            {
                return record.TotalScore;
            }
            return 0;
        }

        /// <summary>
        /// 获取指定玩家指定轮次的得分明细
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <param name="roundIndex">轮次索引（从1开始）</param>
        /// <returns>轮次得分明细，未找到时返回 null</returns>
        public RoundScoreData GetPlayerRoundScore(string playerName, int roundIndex)
        {
            PlayerScoreRecord record = GetPlayerScoreRecord(playerName);
            if (record == null)
            {
                return null;
            }

            for (int i = 0; i < record.RoundHistory.Count; i++)
            {
                if (record.RoundHistory[i].RoundIndex == roundIndex)
                {
                    return record.RoundHistory[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 获取所有玩家的累计总分排名（降序）
        /// 排序键：累计总分 → 通关次数 → 陷阱命中次数
        /// </summary>
        public List<string> GetRankedPlayerNames()
        {
            List<string> ranked = new();

            // 收集所有玩家名称
            foreach (var pair in _scoreRecords)
            {
                ranked.Add(pair.Key);
            }

            // 降序排序
            ranked.Sort((a, b) =>
            {
                PlayerScoreRecord ra = _scoreRecords[a];
                PlayerScoreRecord rb = _scoreRecords[b];

                if (rb.TotalScore != ra.TotalScore)
                {
                    return rb.TotalScore.CompareTo(ra.TotalScore);
                }
                if (rb.TotalFinishCount != ra.TotalFinishCount)
                {
                    return rb.TotalFinishCount.CompareTo(ra.TotalFinishCount);
                }
                return rb.TotalTrapKillCount.CompareTo(ra.TotalTrapKillCount);
            });

            return ranked;
        }

        /// <summary>
        /// 判断是否有人已达到胜利线
        /// </summary>
        public bool BHasPlayerReachedVictoryLine()
        {
            foreach (var pair in _scoreRecords)
            {
                if (pair.Value.TotalScore >= VICTORY_LINE)
                {
                    return true;
                }
            }
            return false;
        }

        // ==================== 辅助方法 ====================

        /// <summary>
        /// 判断玩家是否在本轮通关（从输入数据中查询）
        /// </summary>
        private bool IsPlayerFinished(RoundScoreInput input, string playerName)
        {
            for (int i = 0; i < input.FinishedPlayerNames.Count; i++)
            {
                if (input.FinishedPlayerNames[i] == playerName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取玩家本轮陷阱有效击杀次数（从输入数据中查询）
        /// </summary>
        private int GetTrapKillCount(RoundScoreInput input, string playerName)
        {
            if (input.TrapKillCounts != null && input.TrapKillCounts.TryGetValue(playerName, out int count))
            {
                return count;
            }
            return 0;
        }
    }
}
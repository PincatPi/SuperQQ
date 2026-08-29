using System.Collections;
using System.Collections.Generic;
using SuperQQ.GameFlow;
using SuperQQ.Player;
using SuperQQ.Score;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 结算控制器 — 持久化单例，跨场景保留结算状态
    /// 监听场景加载事件，进入 Settlement 场景时刷新结算显示
    /// 退出 Settlement 场景时隐藏轨道根节点，保留所有对象不销毁
    /// 下次进入时直接复现上次的状态，避免重复创建对象
    /// 结算动画完成后通知当前阶段结算展示已完成
    /// 放置在 Settlement 场景中，首次加载后通过 DontDestroyOnLoad 持久化
    /// </summary>
    public class SettlementController : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private ScorePillarConfig _config;

        [Header("相机")]
        [SerializeField] private float _cameraOrthographicSize = 6f;

        [Header("结算后延迟")]
        [SerializeField] private float _settlementEndDelay = 1.5f;

        // 单例实例
        private static SettlementController _instance;

        // 轨道根节点：所有玩家轨道的父级，持久化不销毁
        private Transform _tracksRoot;

        // 所有玩家轨道
        private readonly List<PlayerTrack> _tracks = new();

        // 胜利线（100分标记线）
        private VictoryLine _victoryLine;

        // 当前动画协程引用
        private Coroutine _animationCoroutine;

        // 结算流程协程引用
        private Coroutine _settlementFlowCoroutine;

        // OnGUI调试文本
        private string _debugFlowText = "";

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static SettlementController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SettlementController>();
                }
                return _instance;
            }
        }

        // ==================== 生命周期 ====================

        /// <summary>
        /// 自动创建兜底：结算阶段已改为场景内覆盖层（RoundSettlementPhase 不切场景），
        /// Settlement 场景不再每轮加载，控制器需在任何场景下都能自动存在。
        /// 配置从 Resources 加载；场景中预置的实例（带 Inspector 配置）优先，不重复创建。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (_instance != null || FindFirstObjectByType<SettlementController>() != null)
            {
                return;
            }

            var go = new GameObject(nameof(SettlementController));
            var controller = go.AddComponent<SettlementController>();
            controller._config = Resources.Load<ScorePillarConfig>("Settlement/ScorePillarConfig");
            if (controller._config == null)
            {
                Debug.LogWarning("[SettlementController] 未找到 Resources/Settlement/ScorePillarConfig，结算展示将不可用。");
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 创建持久化轨道根节点
            CreateTracksRoot();
        }

        // 已订阅阶段事件的 GamePhaseManager（其单例随关卡场景更换，需按实例跟踪订阅）
        private GamePhaseManager _subscribedPhaseManager;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TrySubscribePhaseManager();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (_subscribedPhaseManager != null)
            {
                _subscribedPhaseManager.OnPhaseChanged -= HandlePhaseChanged;
                _subscribedPhaseManager = null;
            }
        }

        /// <summary>
        /// 订阅当前关卡 GamePhaseManager 的阶段事件（幂等）。
        /// 本控制器在启动场景就被 AutoSpawn 创建（此时 GamePhaseManager 尚不存在，OnEnable
        /// 订阅会落空且 DontDestroyOnLoad 后不再重试），因此每次场景加载都要补订阅，
        /// 否则联机流程（Lobby→Room→Level1）下阶段切换事件永远收不到、记分柱动画不触发。
        /// </summary>
        private void TrySubscribePhaseManager()
        {
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == null || ReferenceEquals(flow, _subscribedPhaseManager))
            {
                return;
            }

            if (_subscribedPhaseManager != null)
            {
                _subscribedPhaseManager.OnPhaseChanged -= HandlePhaseChanged;
            }
            flow.OnPhaseChanged += HandlePhaseChanged;
            _subscribedPhaseManager = flow;
        }

        /// <summary>
        /// 阶段切换回调：进入单轮结算阶段时显示结算柱体并刷新（场景内覆盖层，不切场景）；
        /// 离开结算阶段时隐藏。这是结算展示的主触发路径（RoundSettlementPhase 场景名为空）。
        /// </summary>
        private void HandlePhaseChanged(GamePhaseBase prev, GamePhaseBase next)
        {
            if (next is RoundSettlementPhase)
            {
                _tracksRoot.gameObject.SetActive(true);
                _debugFlowText = "";
                RefreshSettlement();
            }
            else if (prev is RoundSettlementPhase)
            {
                // 联机下阶段可能被服务器提前切走：停掉未跑完的动画/流程协程，
                // 避免隐藏期间协程跑完后 NotifyCurrentPhaseEvent 误推进新阶段
                StopSettlementCoroutines();
                _tracksRoot.gameObject.SetActive(false);
                _debugFlowText = "";
            }
        }

        /// <summary>停止批次动画与结算流程协程（中断旧动画/离开结算阶段时调用）</summary>
        private void StopSettlementCoroutines()
        {
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
            }

            if (_settlementFlowCoroutine != null)
            {
                StopCoroutine(_settlementFlowCoroutine);
                _settlementFlowCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
            }

            if (_settlementFlowCoroutine != null)
            {
                StopCoroutine(_settlementFlowCoroutine);
                _settlementFlowCoroutine = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// OnGUI调试信息：在屏幕左上角显示结算流程状态
        /// </summary>
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_debugFlowText))
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = Color.yellow;

            GUI.Label(new Rect(10, 10, 600, 30), _debugFlowText, style);
        }

        // ==================== 轨道根节点 ====================

        /// <summary>
        /// 创建持久化轨道根节点
        /// 根节点随 SettlementController 一起 DontDestroyOnLoad
        /// </summary>
        private void CreateTracksRoot()
        {
            _tracksRoot = new GameObject("SettlementTracksRoot").transform;
            _tracksRoot.SetParent(transform, false);
            _tracksRoot.gameObject.SetActive(false);
        }

        // ==================== 场景加载处理 ====================

        /// <summary>
        /// 场景加载完成回调
        /// 进入 Settlement 场景时显示轨道根节点并刷新结算
        /// 进入其他场景时隐藏轨道根节点并清空调试文本
        /// </summary>
        /// <param name="scene">已加载的场景</param>
        /// <param name="mode">加载模式</param>
        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            // 关卡场景加载完成后 GamePhaseManager 才存在，补订阅阶段事件（启动场景创建时订阅会落空）
            TrySubscribePhaseManager();

            bool bIsRoundSettlementPhase = GamePhaseManager.Instance != null &&
                                           GamePhaseManager.Instance.CurrentPhaseAsset is RoundSettlementPhase;

            if (bIsRoundSettlementPhase || scene.name == "Settlement")
            {
                _tracksRoot.gameObject.SetActive(true);
                _debugFlowText = "";
                RefreshSettlement();
            }
            else
            {
                _tracksRoot.gameObject.SetActive(false);
                _debugFlowText = "";
            }
        }

        // ==================== 结算刷新 ====================

        /// <summary>
        /// 联机：服务器 Settlement 晚于结算阶段进入到达时调用。
        /// 仅结算展示期间（轨道根已激活）按最新记分簿重建柱体；未进入结算阶段时为空操作
        /// （分数已写入记分簿，进入阶段时 HandlePhaseChanged 自会按最新数据刷新）。
        /// </summary>
        public void RefreshSettlementIfShowing()
        {
            if (_tracksRoot == null || !_tracksRoot.gameObject.activeSelf)
            {
                return;
            }

            RefreshSettlement();
        }

        /// <summary>
        /// 刷新结算显示
        /// 清除旧轨道的柱体，根据当前得分数据重建柱体并播放动画
        /// 轨道 GameObject 本身被复用，只重建柱体内容
        /// </summary>
        private void RefreshSettlement()
        {
            if (_config == null)
            {
                Debug.LogError("[SettlementController] 未配置 ScorePillarConfig，请在 Inspector 中设置。");
                return;
            }

            if (PlayerScoreManager.Instance == null)
            {
                Debug.LogError("[SettlementController] PlayerScoreManager 不存在，无法刷新结算。");
                return;
            }

            // 停止正在进行的动画和流程协程
            StopSettlementCoroutines();

            // 场景内覆盖层：轨道根对齐当前相机中心（Level1 相机跟随玩家，需归位才能看到结算）
            if (Camera.main != null)
            {
                Vector3 camPos = Camera.main.transform.position;
                _tracksRoot.position = new Vector3(camPos.x, camPos.y, 0f);
            }

            // 清除旧轨道内容并重建
            ClearAllTracks();
            CreateOrUpdateTracks();
            CreateOrUpdateVictoryLine();
            StartBatchAnimation();
        }

        // ==================== 轨道创建与更新 ====================

        /// <summary>
        /// 根据当前玩家数量创建或更新轨道
        /// 轨道按 PlayerSessionManager 中 Profile 列表的注册顺序从左到右排列（Player1 → Player2 → ...）
        /// 轨道平分整个屏幕宽度，柱体宽度根据轨道宽度动态计算
        /// </summary>
        private void CreateOrUpdateTracks()
        {
            List<string> orderedNames = GetOrderedPlayerNames();

            if (orderedNames.Count == 0)
            {
                Debug.LogWarning("[SettlementController] 没有注册的玩家，无法创建轨道。");
                return;
            }

            int playerCount = orderedNames.Count;
            float cameraAspect = Camera.main != null ? Camera.main.aspect : 1f;
            float trackWidth = _config.CalculateTrackWidth(_cameraOrthographicSize, cameraAspect, playerCount);
            float cameraWidth = _cameraOrthographicSize * 2f * cameraAspect;
            // 当轨道总宽度不满屏时居中显示，保持视觉平衡
            float totalTracksWidth = playerCount * trackWidth;
            float startX = -totalTracksWidth / 2f + trackWidth / 2f;
            // 轨道Y定位到相机视口底部 + 底部留白，确保柱体从屏幕最下方开始堆叠
            float trackBottomY = -_cameraOrthographicSize + _config.TrackBottomPadding;

            int roundIndex = PlayerScoreManager.Instance.CurrentRoundIndex;

            for (int i = 0; i < playerCount; i++)
            {
                string playerName = orderedNames[i];
                RoundScoreData roundScore = PlayerScoreManager.Instance.GetPlayerRoundScore(playerName, roundIndex);

                if (roundScore == null)
                {
                    Debug.LogWarning($"[SettlementController] 玩家 {playerName} 无第 {roundIndex} 轮得分数据，跳过。");
                    continue;
                }

                Color playerColor = GetPlayerColor(playerName);
                Vector3 trackPosition = new Vector3(startX + i * trackWidth, trackBottomY, 0f);

                // 获取过去轮次得分数据
                List<RoundScoreData> pastRoundScores = GetPastRoundScores(playerName, roundIndex);

                // 创建或复用轨道
                PlayerTrack track;
                if (i < _tracks.Count)
                {
                    // 复用已有轨道
                    track = _tracks[i];
                    track.transform.localPosition = trackPosition;
                    track.Initialize(playerName, playerColor, roundScore, _config, trackWidth, pastRoundScores);
                }
                else
                {
                    // 创建新轨道
                    GameObject trackObj = new GameObject($"Track_{playerName}");
                    trackObj.transform.SetParent(_tracksRoot, false);
                    trackObj.transform.localPosition = trackPosition;

                    track = trackObj.AddComponent<PlayerTrack>();
                    track.Initialize(playerName, playerColor, roundScore, _config, trackWidth, pastRoundScores);
                    _tracks.Add(track);
                }
            }

            // 移除多余的轨道（玩家数量减少时）
            while (_tracks.Count > playerCount)
            {
                int lastIndex = _tracks.Count - 1;
                if (_tracks[lastIndex] != null)
                {
                    Destroy(_tracks[lastIndex].gameObject);
                }
                _tracks.RemoveAt(lastIndex);
            }

        }

        /// <summary>
        /// 清除所有轨道的柱体内容（保留轨道 GameObject 本身）
        /// </summary>
        private void ClearAllTracks()
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i] != null)
                {
                    _tracks[i].ClearPillars();
                }
            }
        }

        // ==================== 胜利线 ====================

        /// <summary>
        /// 创建或更新胜利线
        /// 胜利线横贯屏幕，位于100分对应的高度（trackBottomY + VICTORY_LINE × HeightPerPoint）
        /// 作为整场结束的视觉目标线，结算开始时即显示
        /// </summary>
        private void CreateOrUpdateVictoryLine()
        {
            float cameraAspect = Camera.main != null ? Camera.main.aspect : 1f;
            float cameraWidth = _cameraOrthographicSize * 2f * cameraAspect;
            float trackBottomY = -_cameraOrthographicSize + _config.TrackBottomPadding;
            float victoryLineY = trackBottomY + ScoreCalculator.VICTORY_LINE * _config.HeightPerPoint;

            if (_victoryLine == null)
            {
                GameObject victoryObj = new GameObject("VictoryLine");
                victoryObj.transform.SetParent(_tracksRoot, false);
                _victoryLine = victoryObj.AddComponent<VictoryLine>();
            }

            _victoryLine.gameObject.SetActive(true);
            _victoryLine.Initialize(_config, victoryLineY, cameraWidth);

            // 检查胜利线是否在相机视口内，超出时提示用户调整配置
            float cameraTopY = _cameraOrthographicSize;
            if (victoryLineY > cameraTopY)
            {
                Debug.LogWarning($"[SettlementController] 胜利线Y({victoryLineY:F2})超出相机视口顶部({cameraTopY:F2})，" +
                                 $"请减小 HeightPerPoint({_config.HeightPerPoint}) 或增大相机正交大小({_cameraOrthographicSize})。");
            }
        }

        /// <summary>
        /// 按 PlayerSessionManager 中 Profile 列表的注册顺序获取玩家名称
        /// 用于结算轨道的固定展示顺序（Player1 → Player2 → Player3 ...），与得分排名无关
        /// 从持久化层读取，即使本关 PlayerController 已随场景销毁也能正常获取
        /// </summary>
        private List<string> GetOrderedPlayerNames()
        {
            List<string> names = new List<string>();

            if (PlayerSessionManager.Instance == null)
            {
                Debug.LogWarning("[SettlementController] PlayerSessionManager 不存在，无法获取玩家名称列表。");
                return names;
            }

            return PlayerSessionManager.Instance.GetOrderedPlayerNames();
        }

        /// <summary>
        /// 获取玩家颜色
        /// 优先从当前场景的 LevelPlayerRegistry 读取（化身仍在场时）
        /// 失败时回退到 PlayerSessionManager 的 Profile（化身已销毁时）
        /// 都失败时使用默认白色
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        private Color GetPlayerColor(string playerName)
        {
            // 优先从当前场景的 Registry 取（玩家化身可能还在）
            if (LevelPlayerRegistry.Instance != null)
            {
                Color color = LevelPlayerRegistry.Instance.GetPlayerColor(playerName);
                if (color != Color.white)
                {
                    return color;
                }
            }

            // 回退到 SessionManager 的 Profile
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

        /// <summary>
        /// 获取指定玩家在当前轮次之前的所有轮次得分数据
        /// 结果按轮次索引升序排列，用于结算时显示过去轮次的柱状得分底座
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <param name="currentRoundIndex">当前轮次索引</param>
        private List<RoundScoreData> GetPastRoundScores(string playerName, int currentRoundIndex)
        {
            List<RoundScoreData> pastScores = new List<RoundScoreData>();

            PlayerScoreRecord record = PlayerScoreManager.Instance.GetPlayerScoreRecord(playerName);
            if (record == null)
            {
                return pastScores;
            }

            for (int i = 0; i < record.RoundHistory.Count; i++)
            {
                if (record.RoundHistory[i].RoundIndex < currentRoundIndex)
                {
                    pastScores.Add(record.RoundHistory[i]);
                }
            }

            // 按轮次索引升序排列
            pastScores.Sort((a, b) => a.RoundIndex.CompareTo(b.RoundIndex));
            return pastScores;
        }

        // ==================== 批次动画 ====================

        /// <summary>
        /// 启动批次弹出动画
        /// 按蓝→绿→黄→红→紫的顺序，同种颜色的所有玩家柱体同时弹出
        /// </summary>
        private void StartBatchAnimation()
        {
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
            }
            _animationCoroutine = StartCoroutine(BatchAnimationCoroutine());
        }

        /// <summary>
        /// 批次动画协程
        /// 每种得分类型作为一个批次，同批次内所有玩家的柱体同时开始弹出
        /// 批次之间有间隔时间
        /// 每个批次结束后逐步更新累计总分文本
        /// </summary>
        private IEnumerator BatchAnimationCoroutine()
        {
            List<ScoreType> order = _config.GetScoreTypeOrder();
            int currentRoundIndex = PlayerScoreManager.Instance.CurrentRoundIndex;

            // 计算每名玩家的此前累计底座分（前几轮的累计分）
            // 第一轮没有底座，第二轮起此前总分作为底座
            Dictionary<string, int> displayedScores = new Dictionary<string, int>();
            for (int i = 0; i < _tracks.Count; i++)
            {
                string playerName = _tracks[i].PlayerName;
                int totalScore = PlayerScoreManager.Instance.GetPlayerTotalScore(playerName);
                RoundScoreData roundScore = PlayerScoreManager.Instance.GetPlayerRoundScore(playerName, currentRoundIndex);
                int previousTotal = roundScore != null ? totalScore - roundScore.RoundTotal : totalScore;
                displayedScores[playerName] = previousTotal;
                _tracks[i].UpdateTotalScoreText(previousTotal);
            }

            for (int batchIndex = 0; batchIndex < order.Count; batchIndex++)
            {
                ScoreType scoreType = order[batchIndex];

                // 等待批次间隔（第一批不等待）
                if (batchIndex > 0)
                {
                    yield return new WaitForSeconds(_config.BatchInterval);
                }

                // 启动同批次所有玩家的柱体弹出动画
                for (int trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
                {
                    PlayerTrack track = _tracks[trackIndex];
                    ScorePillar pillar = track.GetPillar(scoreType);

                    if (pillar != null)
                    {
                        // 同批次内加一点交错延迟，让效果更有节奏感
                        float delay = trackIndex * _config.PopStaggerDelay;
                        StartCoroutine(DelayedPopPillar(pillar, delay));
                    }
                }

                // 等待本批次动画完成
                float batchDuration = _config.PopDuration + (_tracks.Count - 1) * _config.PopStaggerDelay;
                yield return new WaitForSeconds(batchDuration);

                // 更新累计总分文本（逐步累加每层的分数）
                for (int trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
                {
                    PlayerTrack track = _tracks[trackIndex];
                    RoundScoreData roundScore = PlayerScoreManager.Instance.GetPlayerRoundScore(
                        track.PlayerName, currentRoundIndex);

                    if (roundScore != null && roundScore.ScoreBreakdown.TryGetValue(scoreType, out int score))
                    {
                        displayedScores[track.PlayerName] += score;
                        track.UpdateTotalScoreText(displayedScores[track.PlayerName]);
                    }
                }
            }

            _animationCoroutine = null;

            // 动画完成后，启动结算流程判断
            StartSettlementFlow();
        }

        /// <summary>
        /// 延迟弹出柱体的协程
        /// </summary>
        /// <param name="pillar">待弹出的柱体</param>
        /// <param name="delay">延迟时间（秒）</param>
        private IEnumerator DelayedPopPillar(ScorePillar pillar, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            // 动画中断（服务器结算到达触发重建 / 阶段被服务器提前切走）后，
            // 本协程不受 StopCoroutine(_animationCoroutine) 管控仍在跑：
            // 旧柱体已被 Destroy 或随轨道根隐藏，启动协程会报 "game object is inactive"
            if (pillar == null || !pillar.gameObject.activeInHierarchy)
            {
                yield break;
            }
            pillar.StartPopAnimation(_config.PopDuration, _config.PopCurve);
        }

        // ==================== 结算流程控制 ====================

        /// <summary>
        /// 启动结算流程：动画完成后延迟指定秒数，通知阶段管理器推进后续流程。
        /// </summary>
        private void StartSettlementFlow()
        {
            if (_settlementFlowCoroutine != null)
            {
                StopCoroutine(_settlementFlowCoroutine);
            }
            _settlementFlowCoroutine = StartCoroutine(SettlementFlowCoroutine());
        }

        /// <summary>
        /// 结算流程协程：延迟后通知当前单轮结算阶段展示已完成。
        /// </summary>
        private IEnumerator SettlementFlowCoroutine()
        {
            if (PlayerScoreManager.Instance == null)
            {
                Debug.LogError("[SettlementController] PlayerScoreManager 不存在，无法完成结算流程。");
                yield break;
            }

            int currentRound = PlayerScoreManager.Instance.CurrentRoundIndex;
            bool bHasWinner = PlayerScoreManager.Instance.BHasPlayerReachedVictoryLine();

            if (bHasWinner)
            {
                _debugFlowText = $"[整场结束] 第{currentRound}轮结算完毕，有人达到胜利线，{_settlementEndDelay}秒后通知阶段状态机...";
            }
            else
            {
                _debugFlowText = $"[继续闯关] 第{currentRound}轮无人达线，{_settlementEndDelay}秒后通知阶段状态机...";
            }

            Debug.Log($"[SettlementController] {_debugFlowText}");
            yield return new WaitForSeconds(_settlementEndDelay);

            _debugFlowText = "";

            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.NotifyCurrentPhaseEvent();
            }
            else
            {
                Debug.LogError("[SettlementController] GamePhaseManager 不存在，无法通知结算阶段完成。");
            }

            _settlementFlowCoroutine = null;
        }
    }
}
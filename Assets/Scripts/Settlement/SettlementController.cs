using System.Collections;
using System.Collections.Generic;
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
    /// 放置在 Settlement 场景中，首次加载后通过 DontDestroyOnLoad 持久化
    /// </summary>
    public class SettlementController : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private ScorePillarConfig _config;

        [Header("相机")]
        [SerializeField] private float _cameraOrthographicSize = 6f;

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

        // 是否已初始化轨道（用于区分首次创建和复用）
        private bool _bIsTracksInitialized;

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

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
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
        /// 进入其他场景时隐藏轨道根节点
        /// </summary>
        /// <param name="scene">已加载的场景</param>
        /// <param name="mode">加载模式</param>
        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Settlement")
            {
                _tracksRoot.gameObject.SetActive(true);
                RefreshSettlement();
            }
            else
            {
                _tracksRoot.gameObject.SetActive(false);
            }
        }

        // ==================== 结算刷新 ====================

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

            // 停止正在进行的动画
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
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
            float startX = -cameraWidth / 2f + trackWidth / 2f;
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

                // 创建或复用轨道
                PlayerTrack track;
                if (i < _tracks.Count)
                {
                    // 复用已有轨道
                    track = _tracks[i];
                    track.transform.localPosition = trackPosition;
                    track.Initialize(playerName, playerColor, roundScore, _config, trackWidth);
                }
                else
                {
                    // 创建新轨道
                    GameObject trackObj = new GameObject($"Track_{playerName}");
                    trackObj.transform.SetParent(_tracksRoot, false);
                    trackObj.transform.localPosition = trackPosition;

                    track = trackObj.AddComponent<PlayerTrack>();
                    track.Initialize(playerName, playerColor, roundScore, _config, trackWidth);
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

            _bIsTracksInitialized = true;
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
            pillar.StartPopAnimation(_config.PopDuration, _config.PopCurve);
        }
    }
}

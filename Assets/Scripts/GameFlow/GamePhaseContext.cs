using SuperQQ.Placement.Runtime;
using SuperQQ.Player;
using SuperQQ.Score;
using SuperQQ.Selection.Runtime;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 游戏阶段运行时上下文。
    /// 阶段类通过该对象访问外部服务，避免直接散落查找全局对象。
    /// </summary>
    public class GamePhaseContext
    {
        /// <summary>
        /// 阶段管理器。
        /// </summary>
        public GamePhaseManager PhaseManager { get; }

        /// <summary>
        /// 游戏流程配置。
        /// </summary>
        public GameFlowConfig Config { get; }

        /// <summary>
        /// 计分管理器。
        /// </summary>
        public PlayerScoreManager ScoreManager => PlayerScoreManager.Instance;

        /// <summary>
        /// 当前关卡玩家注册表。
        /// </summary>
        public LevelPlayerRegistry LevelRegistry => LevelPlayerRegistry.Instance;

        /// <summary>
        /// 当前场景的道具放置阶段门面。放置阶段不在场时为 null。
        /// </summary>
        public PropPlacementDirector PlacementDirector => PropPlacementDirector.Instance;

        /// <summary>
        /// 当前场景的道具选择阶段门面。选择阶段不在场时为 null。
        /// </summary>
        public PropSelectionDirector SelectionDirector => PropSelectionDirector.Instance;

        /// <summary>
        /// 构造阶段上下文。
        /// </summary>
        /// <param name="phaseManager">阶段管理器。</param>
        /// <param name="config">流程配置。</param>
        public GamePhaseContext(GamePhaseManager phaseManager, GameFlowConfig config)
        {
            PhaseManager = phaseManager;
            Config = config;
        }

        /// <summary>
        /// 加载场景。
        /// </summary>
        /// <param name="sceneName">场景名称。</param>
        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[GamePhaseContext] 阶段目标场景名称为空，跳过场景加载。");
                return;
            }

            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.Equals(currentSceneName, sceneName, System.StringComparison.Ordinal))
            {
                Debug.Log($"[GamePhaseContext] 目标场景与当前场景相同：{sceneName}，跳过重复加载。");
                return;
            }

            if (global::SuperQQ.Scene.SceneManager.Instance != null)
            {
                global::SuperQQ.Scene.SceneManager.Instance.LoadScene(sceneName);
                return;
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }

        /// <summary>
        /// 请求当前阶段重新尝试订阅与场景相关的运行时对象。
        /// 场景加载完成后由阶段管理器调用。
        /// </summary>
        public void RefreshSceneRuntimeBindings()
        {
            PhaseManager?.RefreshCurrentPhaseSceneBindings();
        }
    }
}

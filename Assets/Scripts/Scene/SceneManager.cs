using System.Collections;
using UnityEngine;
using SuperQQ.Player;
using UnityEngine.SceneManagement;

namespace SuperQQ.Scene
{
    /// <summary>
    /// 场景管理器 — 负责场景切换逻辑
    /// 监听 LevelPlayerRegistry 的所有玩家出局事件，自动切换到指定场景
    /// 也可通过 LoadScene 主动切换到任意场景
    /// 挂载到场景中跨场景不销毁的 GameObject 上（如 GameManager）
    /// </summary>
    public class SceneManager : MonoBehaviour
    {
        // 单例实例
        private static SceneManager _instance;

        [Header("单关卡结算场景")]
        [SerializeField] private string _settlementSceneName = "";           // 结算场景名称

        [Header("切换延迟")]
        [SerializeField] private float _transitionDelay = 2f;           // 所有玩家出局后延迟切换的秒数

        // 是否正在切换场景中（防止重复触发）
        private bool _bIsTransitioning;

        // 延迟切换的协程引用
        private Coroutine _transitionCoroutine;

        // 当前订阅的关卡注册表引用（场景级，随场景切换更换）
        private LevelPlayerRegistry _currentRegistry;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static SceneManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SceneManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 是否正在切换场景中
        /// </summary>
        public bool BIsTransitioning => _bIsTransitioning;

        /// <summary>
        /// 当前激活的场景名称
        /// </summary>
        public string CurrentSceneName => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

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

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            // 取消订阅当前 Registry 事件
            UnsubscribeFromLevelRegistry();
        }

        private void Start()
        {
            // 启动时尝试订阅当前场景的 LevelPlayerRegistry 事件
            SubscribeToLevelRegistry();
        }

        // ==================== 事件订阅 ====================

        /// <summary>
        /// 场景加载完成回调
        /// 场景切换完成后订阅新场景的 LevelPlayerRegistry 事件
        /// </summary>
        /// <param name="scene">已加载的场景</param>
        /// <param name="mode">加载模式</param>
        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            // 先清理旧订阅，再尝试订阅新场景的 Registry
            UnsubscribeFromLevelRegistry();
            SubscribeToLevelRegistry();
        }

        /// <summary>
        /// 订阅当前场景中的 LevelPlayerRegistry 的所有玩家出局事件
        /// 若当前场景无 Registry（如结算场景），则不做任何事
        /// </summary>
        private void SubscribeToLevelRegistry()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            _currentRegistry = registry;
            _currentRegistry.OnAllPlayersOut += HandleSettlement;
        }

        /// <summary>
        /// 取消订阅当前 LevelPlayerRegistry 的所有玩家出局事件
        /// 场景切换前调用，避免引用已销毁的 Registry
        /// </summary>
        private void UnsubscribeFromLevelRegistry()
        {
            if (_currentRegistry != null)
            {
                _currentRegistry.OnAllPlayersOut -= HandleSettlement;
                _currentRegistry = null;
            }
        }

        // ==================== 场景切换 ====================

        /// <summary>
        /// 加载指定名称的场景
        /// </summary>
        /// <param name="sceneName">目标场景名称</param>
        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[SceneManager] 场景名称为空，无法加载场景。");
                return;
            }

            if (_bIsTransitioning)
            {
                return;
            }

            Debug.Log($"[SceneManager] 开始切换到场景：{sceneName}");
            StartCoroutine(LoadSceneAsync(sceneName));
        }

        /// <summary>
        /// 重新加载当前场景
        /// </summary>
        public void ReloadCurrentScene()
        {
            if (_bIsTransitioning)
            {
                return;
            }

            string sceneName = CurrentSceneName;
            Debug.Log($"[SceneManager] 重新加载当前场景：{sceneName}");
            StartCoroutine(LoadSceneAsync(sceneName));
        }

        // ==================== 内部方法 ====================

        /// <summary>
        /// 单关卡结束时的处理函数：延迟后加载结算场景
        /// </summary>
        private void HandleSettlement()
        {
            if (_bIsTransitioning)
            {
                return;
            }

            if (string.IsNullOrEmpty(_settlementSceneName))
            {
                Debug.LogWarning("[SceneManager] 未配置所有玩家出局后的目标场景名称（_settlementSceneName）。");
                return;
            }

            Debug.Log($"[SceneManager] 所有玩家已出局，{_transitionDelay} 秒后切换到场景：{_settlementSceneName}");

            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
            }
            _transitionCoroutine = StartCoroutine(DelayedLoadScene(_settlementSceneName));
        }

        /// <summary>
        /// 延迟加载指定场景的协程
        /// </summary>
        /// <param name="sceneName">目标场景名称</param>
        private IEnumerator DelayedLoadScene(string sceneName)
        {
            yield return new WaitForSeconds(_transitionDelay);
            LoadScene(sceneName);
            _transitionCoroutine = null;
        }

        /// <summary>
        /// 异步加载场景的协程
        /// </summary>
        /// <param name="sceneName">目标场景名称</param>
        private IEnumerator LoadSceneAsync(string sceneName)
        {
            _bIsTransitioning = true;

            // 取消订阅旧场景的 Registry 事件
            UnsubscribeFromLevelRegistry();

            AsyncOperation asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
            if (asyncLoad == null)
            {
                Debug.LogError($"[SceneManager] 无法加载场景：{sceneName}。请确认场景名称正确且已添加到 Build Settings。");
                _bIsTransitioning = false;
                yield break;
            }

            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            _bIsTransitioning = false;

            // 订阅新场景的 Registry 事件（若存在）
            // 注意：此处也可依赖 HandleSceneLoaded 回调自动订阅，这里主动调用保证时序
            SubscribeToLevelRegistry();

            Debug.Log($"[SceneManager] 场景加载完成：{sceneName}");
        }
    }
}

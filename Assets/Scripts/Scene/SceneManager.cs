using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperQQ.Scene
{
    /// <summary>
    /// 场景管理器 — 负责场景切换逻辑。
    /// 只处理场景加载、重载和切换中状态，不参与游戏阶段判断。
    /// 挂载到场景中跨场景不销毁的 GameObject 上（如 GameManager）。
    /// </summary>
    public class SceneManager : MonoBehaviour
    {
        // 单例实例
        private static SceneManager _instance;

        // 是否正在切换场景中（防止重复触发）
        private bool _bIsTransitioning;

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

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
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
        /// 异步加载场景的协程
        /// </summary>
        /// <param name="sceneName">目标场景名称</param>
        private IEnumerator LoadSceneAsync(string sceneName)
        {
            _bIsTransitioning = true;

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

            Debug.Log($"[SceneManager] 场景加载完成：{sceneName}");
        }
    }
}

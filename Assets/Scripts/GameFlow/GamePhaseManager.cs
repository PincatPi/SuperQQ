using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 游戏阶段管理器。
    /// 作为通用状态机执行器，负责阶段资产的进入、退出、更新和转移。
    /// </summary>
    public class GamePhaseManager : MonoBehaviour
    {
        private static GamePhaseManager _instance;

        [Header("流程配置")]
        [SerializeField] private GameFlowConfig _config;

        [Header("启动设置")]
        [SerializeField] private bool _bStartFlowOnStart = true;

        [Header("调试")]
        [Tooltip("在屏幕右上角实时显示当前阶段与即将切换的下一阶段名称")]
        [SerializeField] private bool _bShowPhaseDebugInfo = true;

        private GamePhaseContext _context;
        private GamePhaseBase _currentPhase;
        private bool _bFlowStarted;
        private string _debugNextPhaseName = string.Empty;

        /// <summary>
        /// 阶段变化事件。
        /// 参数依次为上一个阶段和新阶段。
        /// </summary>
        public event Action<GamePhaseBase, GamePhaseBase> OnPhaseChanged;

        /// <summary>
        /// 全局唯一实例。
        /// </summary>
        public static GamePhaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GamePhaseManager>();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 当前阶段名称。
        /// </summary>
        public string CurrentPhaseName => _currentPhase != null ? _currentPhase.LogName : string.Empty;

        /// <summary>
        /// 当前阶段资产。
        /// </summary>
        public GamePhaseBase CurrentPhaseAsset => _currentPhase;

        /// <summary>游戏流程是否已启动</summary>
        public bool BFlowStarted => _bFlowStarted;

        /// <summary>设置是否自动启动流程（联机模式下由服务器消息触发，需关闭自动启动）</summary>
        public void SetStartFlowOnStart(bool value) => _bStartFlowOnStart = value;

        /// <summary>
        /// 屏蔽本地阶段转移（联机模式下为 true）：阶段切换只响应服务器 GamePhaseSync，
        /// 本地倒计时/条件评估不再触发切换。
        /// </summary>
        public bool SuppressLocalTransitions { get; set; }

        /// <summary>
        /// 按阶段类型进入对应阶段（联机模式下由服务器 GamePhaseSync 驱动）。
        /// 在流程配置的阶段列表中按类型查找首个匹配的阶段资产。
        /// </summary>
        public bool EnterPhaseByType<T>(string reason) where T : GamePhaseBase
        {
            if (_config == null) return false;

            IReadOnlyList<GamePhaseBase> phases = _config.Phases;
            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] is T)
                {
                    EnterPhase(phases[i], reason);
                    return true;
                }
            }

            Debug.LogError($"[GamePhaseManager] 阶段列表中未找到类型 {typeof(T).Name} 的阶段资产。");
            return false;
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
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
            _currentPhase?.OnExit(_context);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _currentPhase?.OnExit(_context);
        }

        private void Start()
        {
            if (_bStartFlowOnStart)
            {
                StartGameFlow();
            }
        }

        private void Update()
        {
            if (!_bFlowStarted || _currentPhase == null || _context == null)
            {
                return;
            }

            _currentPhase.OnUpdate(_context, Time.deltaTime);

            UpdateDebugNextPhaseName();

            // 联机模式下阶段切换由服务器 GamePhaseSync 统一驱动，本地条件不再触发转移
            if (!SuppressLocalTransitions
                && _currentPhase.TryGetNextPhase(_context, out GamePhaseBase nextPhase, out string reason))
            {
                EnterPhase(nextPhase, reason);
            }
        }

        /// <summary>
        /// 刷新调试用的下一阶段名称。
        /// 仅评估条件，不触发 TryGetNextPhase 中的转移选中副作用。
        /// </summary>
        private void UpdateDebugNextPhaseName()
        {
            _debugNextPhaseName = string.Empty;

            if (!_bShowPhaseDebugInfo || _currentPhase == null || _context == null)
            {
                return;
            }

            IReadOnlyList<GamePhaseTransition> transitions = _currentPhase.Transitions;
            if (transitions == null)
            {
                return;
            }

            for (int i = 0; i < transitions.Count; i++)
            {
                GamePhaseTransition transition = transitions[i];
                if (transition == null || transition.TargetPhase == null)
                {
                    continue;
                }

                if (transition.Evaluate(_context))
                {
                    _debugNextPhaseName = transition.TargetPhase.LogName;
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (!_bShowPhaseDebugInfo || !_bFlowStarted || _currentPhase == null)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = Color.black }
            };

            string nextPhaseText = string.IsNullOrEmpty(_debugNextPhaseName) ? "无（条件未满足）" : _debugNextPhaseName;
            string text = $"当前阶段：{_currentPhase.LogName}\n下一阶段：{nextPhaseText}";

            GUIContent content = new GUIContent(text);
            Vector2 size = style.CalcSize(content);
            Rect rect = new Rect(Screen.width - size.x - 12f, 10f, size.x, size.y);
            GUI.Label(rect, content, style);
        }

        /// <summary>
        /// 从配置的初始阶段开始整局流程。
        /// </summary>
        public void StartGameFlow()
        {
            if (_config == null)
            {
                Debug.LogError("[GamePhaseManager] 未配置 GameFlowConfig，无法启动游戏流程。");
                return;
            }

            if (!_config.ValidateConfig(out string errorMessage))
            {
                Debug.LogError($"[GamePhaseManager] GameFlowConfig 校验失败：{errorMessage}");
                return;
            }

            _context = new GamePhaseContext(this, _config);
            _bFlowStarted = true;
            EnterPhase(_config.InitialPhase, "启动游戏流程");
        }

        /// <summary>
        /// 外部请求进入指定阶段。
        /// </summary>
        /// <param name="nextPhase">目标阶段。</param>
        /// <param name="reason">切换原因。</param>
        public void EnterPhase(GamePhaseBase nextPhase, string reason = "外部请求切换阶段")
        {
            if (_config == null)
            {
                Debug.LogError("[GamePhaseManager] 未配置 GameFlowConfig，无法切换阶段。");
                return;
            }

            if (nextPhase == null)
            {
                Debug.LogError("[GamePhaseManager] 目标阶段为空，无法切换阶段。");
                return;
            }

            if (_currentPhase == nextPhase)
            {
                return;
            }

            if (_context == null)
            {
                _context = new GamePhaseContext(this, _config);
            }

            GamePhaseBase previousPhase = _currentPhase;
            string previousPhaseName = previousPhase != null ? previousPhase.LogName : string.Empty;
            string nextPhaseName = nextPhase.LogName;

            previousPhase?.OnExit(_context);

            _currentPhase = nextPhase;

            OnPhaseChanged?.Invoke(previousPhase, nextPhase);

            Debug.Log($"[GamePhaseManager] 阶段切换：{previousPhaseName} -> {nextPhaseName}，原因：{reason}");
            _currentPhase.OnEnter(_context);
        }

        /// <summary>
        /// 通知当前阶段的阶段事件已完成。
        /// 由当前阶段自行决定如何响应该事件。
        /// </summary>
        public void NotifyCurrentPhaseEvent()
        {
            _currentPhase?.NotifyPhaseEvent();
        }

        /// <summary>
        /// 场景加载完成后刷新当前阶段依赖的场景对象绑定。
        /// </summary>
        public void RefreshCurrentPhaseSceneBindings()
        {
            _currentPhase?.RefreshSceneRuntimeBindings(_context);
        }

        private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            _currentPhase?.RefreshSceneRuntimeBindings(_context);
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using SuperQQ.GameFlow;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件播报器 — 场景级单例
    /// 进入关卡时经 LevelEventSelector 选定本关事件（固定事件全部执行，非固定事件按权重抽取一个）
    /// 事件选定后：
    ///   - 通过 PopupManager 依次播放每个事件的说明弹窗（3秒自动销毁）
    ///   - 待进入 Playing 游玩阶段时，调用每个事件对应 LevelEventModifier 的 Activate 方法启动事件逻辑
    ///     （事件计时统一从游玩阶段起算，道具选择/放置阶段不推进计时）
    /// 场景销毁时调用所有 Modifier 的 Deactivate 方法进行清理
    /// 选取决策已抽离到 LevelEventSelector（纯 C#，可单元测试），本类只负责调度与播报
    /// </summary>
    public class LevelEventAnnouncer : MonoBehaviour
    {
        // 场景级单例实例
        private static LevelEventAnnouncer _instance;

        // 事件配置表引用，在 Inspector 中指定
        [Header("事件配置")]
        [SerializeField] private LevelEventConfig _eventConfig;

        // 弹窗自动关闭时长（秒），对应策划文档：3秒后自动销毁
        private const float POPUP_AUTO_CLOSE_DURATION = 3f;

        // 多个弹窗之间的播放间隔（秒），前一个弹窗关闭后等待此时长再播下一个
        // 避免多个弹窗同时弹出造成视觉叠加
        private const float POPUP_INTERVAL = 0.2f;

        // 本关选中的所有事件条目（固定事件 + 随机事件）
        private readonly List<LevelEventEntry> _selectedEntries = new();

        // 是否已完成事件选取（播报协程可能仍在进行中）
        private bool _bHasAnnounced;

        // 是否已激活事件 Modifier（游玩阶段闸门保证只激活一次）
        private bool _bModifiersActivated;

        // 弹窗依次播放的协程引用
        private Coroutine _popupPlaybackCoroutine;

        // 运行时上下文，在事件激活时创建，传递给各 LevelEventModifier
        private LevelEventContext _eventContext;

        // ==================== 公开事件 ====================

        /// <summary>
        /// 事件选中事件：本关事件选取完成后触发
        /// 参数为本关所有选中的事件条目列表（固定事件 + 随机事件）
        /// 外部系统可订阅此事件做额外处理（如联机同步、UI 展示）
        /// </summary>
        public event Action<IReadOnlyList<LevelEventEntry>> OnEventsSelected;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 当前场景中的全局唯一实例
        /// </summary>
        public static LevelEventAnnouncer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<LevelEventAnnouncer>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 本关选中的所有事件条目（只读视图，按固定事件在前、随机事件在后的顺序排列）
        /// </summary>
        public IReadOnlyList<LevelEventEntry> SelectedEvents => _selectedEntries;

        /// <summary>
        /// 本关选中事件的数量
        /// </summary>
        public int SelectedEventCount => _selectedEntries.Count;

        /// <summary>
        /// 是否已完成事件选取
        /// </summary>
        public bool BHasAnnounced => _bHasAnnounced;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 场景级单例：不 DontDestroyOnLoad，场景卸载时本对象随之销毁
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            // 场景销毁时退订阶段切换（若事件尚未等到游玩阶段激活）
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChangedForActivation;
            }

            // 场景销毁时停止弹窗播放协程
            if (_popupPlaybackCoroutine != null)
            {
                StopCoroutine(_popupPlaybackCoroutine);
                _popupPlaybackCoroutine = null;
            }

            // 场景销毁时停用所有事件 Modifier，进行清理
            DeactivateAllModifiers();
        }

        private void Start()
        {
            SelectAndAnnounceEvents();
        }

        // ==================== 核心流程 ====================

        /// <summary>
        /// 选定本关事件并依次播报弹窗、激活事件逻辑
        /// 选取决策委托给 LevelEventSelector：固定事件全部执行，非固定事件按权重抽取一个
        /// 在 Start 中自动调用，确保每次进入关卡时都执行
        /// </summary>
        /// <param name="random">
        /// 随机源，用于非固定事件的权重抽取；为 null 时使用时间种子
        /// 联机模式下应由主机传入固定种子的实例，保证各端选取结果一致
        /// </param>
        public void SelectAndAnnounceEvents(System.Random random = null)
        {
            if (_bHasAnnounced)
            {
                return;
            }

            _selectedEntries.Clear();

            // 选取决策委托给纯 C# 选取器（本类不感知选取规则细节）
            if (_eventConfig != null)
            {
                _selectedEntries.AddRange(LevelEventSelector.SelectEvents(_eventConfig.Events, random));
            }

            _bHasAnnounced = true;

            if (_selectedEntries.Count == 0)
            {
                Debug.LogWarning("[LevelEventAnnouncer] 未选中任何事件，请检查配置表。");
                return;
            }

            // 创建运行时上下文，供 Modifier 启动协程和访问场景
            _eventContext = new LevelEventContext
            {
                CoroutineRunner = this,
                SceneRoot = transform
            };

            // 通知外部：本关事件已选定
            OnEventsSelected?.Invoke(_selectedEntries);

            // 激活所有选中事件的 Modifier（进入游玩阶段后才真正启动，事件计时从游玩阶段起算）
            ActivateModifiersWhenPlaying();

            // 依次播放每个事件的说明弹窗
            if (_popupPlaybackCoroutine != null)
            {
                StopCoroutine(_popupPlaybackCoroutine);
            }
            _popupPlaybackCoroutine = StartCoroutine(ShowEventPopupsSequentially());
        }

        // ==================== 内部方法：Modifier 激活/停用 ====================

        /// <summary>
        /// 游玩阶段闸门：事件 Modifier 的计时（首次落石延迟、随机触发时机等）从
        /// Playing 游玩阶段开始才起算，道具选择/放置等其它阶段不推进事件计时
        /// 当前已在游玩阶段或场景中无 GamePhaseManager（纯测试场景）时立即激活；
        /// 否则订阅阶段切换事件，待进入游玩阶段时激活
        /// </summary>
        private void ActivateModifiersWhenPlaying()
        {
            GamePhaseManager phaseManager = GamePhaseManager.Instance;
            if (phaseManager == null || phaseManager.CurrentPhaseAsset is PlayingPhase)
            {
                ActivateSelectedModifiers();
                return;
            }

            phaseManager.OnPhaseChanged += HandlePhaseChangedForActivation;
        }

        /// <summary>
        /// 阶段切换回调：进入游玩阶段时激活事件 Modifier，并退订（只激活一次）
        /// 单机走本地条件转移、联机走服务器 GamePhaseSync，二者均经 EnterPhase 触发本事件
        /// </summary>
        private void HandlePhaseChangedForActivation(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            if (!(nextPhase is PlayingPhase))
            {
                return;
            }

            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChangedForActivation;
            }

            ActivateSelectedModifiers();
        }

        /// <summary>
        /// 激活本关所有选中事件对应的 Modifier
        /// 直接遍历条目引用调用 Modifier.Activate，无需按枚举回查配置表
        /// 幂等：游玩阶段闸门保证只激活一次，重复调用为空操作
        /// </summary>
        private void ActivateSelectedModifiers()
        {
            if (_bModifiersActivated || _eventContext == null)
            {
                return;
            }
            _bModifiersActivated = true;

            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier != null)
                {
                    _selectedEntries[i].Modifier.Activate(_eventContext);
                }
            }
        }

        /// <summary>
        /// 停用本关所有已激活的 Modifier
        /// 在场景销毁或强制中断时调用，确保各事件逻辑正确清理协程和资源
        /// </summary>
        private void DeactivateAllModifiers()
        {
            if (_eventContext == null)
            {
                return;
            }

            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                if (_selectedEntries[i].Modifier != null)
                {
                    _selectedEntries[i].Modifier.Deactivate(_eventContext);
                }
            }
        }

        // ==================== 内部方法：弹窗播放 ====================

        /// <summary>
        /// 依次播放本关所有选中事件的说明弹窗
        /// 每个弹窗持续 POPUP_AUTO_CLOSE_DURATION 秒后自动关闭，
        /// 再等待 POPUP_INTERVAL 秒后播放下一个，避免视觉叠加
        /// </summary>
        private IEnumerator ShowEventPopupsSequentially()
        {
            for (int i = 0; i < _selectedEntries.Count; i++)
            {
                ShowEventPopup(_selectedEntries[i]);

                // 最后一个事件无需等待
                if (i < _selectedEntries.Count - 1)
                {
                    yield return new WaitForSeconds(POPUP_AUTO_CLOSE_DURATION + POPUP_INTERVAL);
                }
            }

            _popupPlaybackCoroutine = null;
        }

        /// <summary>
        /// 通过 PopupManager 播放单个事件说明弹窗
        /// 弹窗持续3秒后自动关闭
        /// </summary>
        /// <param name="entry">要播报的事件条目</param>
        private void ShowEventPopup(LevelEventEntry entry)
        {
            if (entry.PopupPrefab == null)
            {
                Debug.LogWarning($"[LevelEventAnnouncer] 事件 {entry.EventType} 的弹窗 Prefab 未配置，无法播放事件弹窗。");
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LevelEventAnnouncer] PopupManager 不存在，无法播放事件弹窗。");
                return;
            }

            PopupManager.Instance.ShowPopup(entry.PopupPrefab, POPUP_AUTO_CLOSE_DURATION);
            Debug.Log($"[LevelEventAnnouncer] 本关事件：{entry.DisplayName}（{entry.EventType}），弹窗已播放。");
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件播报器 — 场景级单例
    /// 进入关卡时按以下规则选定本关事件：
    ///   1. 配置表中所有 BIsFixed 为 true 的事件全部执行（固定事件）
    ///   2. 从所有 BIsFixed 为 false 的事件中随机抽取一个执行（随机事件）
    /// 事件选定后：
    ///   - 通过 PopupManager 依次播放每个事件的说明弹窗（3秒自动销毁）
    ///   - 调用每个事件对应 LevelEventModifier 的 Activate 方法启动事件逻辑
    /// 场景销毁时调用所有 Modifier 的 Deactivate 方法进行清理
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

        // 本关选中的所有事件类型（固定事件 + 随机事件）
        private readonly List<LevelEventType> _selectedEvents = new();

        // 是否已完成事件选取（播报协程可能仍在进行中）
        private bool _bHasAnnounced;

        // 弹窗依次播放的协程引用
        private Coroutine _popupPlaybackCoroutine;

        // 运行时上下文，在事件激活时创建，传递给各 LevelEventModifier
        private LevelEventContext _eventContext;

        // ==================== 公开事件 ====================

        /// <summary>
        /// 事件选中事件：本关事件选取完成后触发
        /// 参数为本关所有选中的事件类型列表（固定事件 + 随机事件）
        /// 外部系统可订阅此事件做额外处理
        /// </summary>
        public event Action<IReadOnlyList<LevelEventType>> OnEventsSelected;

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
        /// 本关选中的所有事件类型（只读视图，按固定事件在前、随机事件在后的顺序排列）
        /// </summary>
        public IReadOnlyList<LevelEventType> SelectedEvents => _selectedEvents;

        /// <summary>
        /// 本关选中事件的数量
        /// </summary>
        public int SelectedEventCount => _selectedEvents.Count;

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
        /// 固定事件全部执行，非固定事件随机抽取一个执行
        /// 在 Start 中自动调用，确保每次进入关卡时都执行
        /// </summary>
        public void SelectAndAnnounceEvents()
        {
            if (_bHasAnnounced)
            {
                return;
            }

            _selectedEvents.Clear();

            // 步骤1：收集所有固定事件
            CollectFixedEvents();

            // 步骤2：从非固定事件中随机抽取一个
            SelectRandomFlexibleEvent();

            _bHasAnnounced = true;

            if (_selectedEvents.Count == 0)
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
            OnEventsSelected?.Invoke(_selectedEvents);

            // 激活所有选中事件的 Modifier，启动事件逻辑
            ActivateSelectedModifiers();

            // 依次播放每个事件的说明弹窗
            if (_popupPlaybackCoroutine != null)
            {
                StopCoroutine(_popupPlaybackCoroutine);
            }
            _popupPlaybackCoroutine = StartCoroutine(ShowEventPopupsSequentially());
        }

        // ==================== 内部方法：事件选取 ====================

        /// <summary>
        /// 收集配置表中所有 BIsFixed 为 true 的固定事件
        /// 这些事件每次进入关卡都会执行，不参与随机抽取
        /// </summary>
        private void CollectFixedEvents()
        {
            if (_eventConfig == null)
            {
                return;
            }

            IReadOnlyList<LevelEventEntry> events = _eventConfig.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].BIsFixed)
                {
                    _selectedEvents.Add(events[i].EventType);
                }
            }
        }

        /// <summary>
        /// 从配置表中所有 BIsFixed 为 false 的非固定事件中随机抽取一个
        /// 若无非固定事件则跳过
        /// 使用 UnityEngine.Random 保证分布均匀
        /// </summary>
        private void SelectRandomFlexibleEvent()
        {
            if (_eventConfig == null)
            {
                return;
            }

            // 收集所有非固定事件类型
            List<LevelEventType> flexibleEvents = new List<LevelEventType>();
            IReadOnlyList<LevelEventEntry> events = _eventConfig.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (!events[i].BIsFixed)
                {
                    flexibleEvents.Add(events[i].EventType);
                }
            }

            if (flexibleEvents.Count == 0)
            {
                return;
            }

            int index = UnityEngine.Random.Range(0, flexibleEvents.Count);
            _selectedEvents.Add(flexibleEvents[index]);
        }

        // ==================== 内部方法：Modifier 激活/停用 ====================

        /// <summary>
        /// 激活本关所有选中事件对应的 Modifier
        /// 通过配置表查找每个事件条目，调用其 Modifier.Activate 启动事件逻辑
        /// </summary>
        private void ActivateSelectedModifiers()
        {
            if (_eventConfig == null || _eventContext == null)
            {
                return;
            }

            for (int i = 0; i < _selectedEvents.Count; i++)
            {
                LevelEventEntry entry = _eventConfig.FindEntry(_selectedEvents[i]);
                if (entry.Modifier != null)
                {
                    entry.Modifier.Activate(_eventContext);
                }
            }
        }

        /// <summary>
        /// 停用本关所有已激活的 Modifier
        /// 在场景销毁或强制中断时调用，确保各事件逻辑正确清理协程和资源
        /// </summary>
        private void DeactivateAllModifiers()
        {
            if (_eventConfig == null || _eventContext == null)
            {
                return;
            }

            for (int i = 0; i < _selectedEvents.Count; i++)
            {
                LevelEventEntry entry = _eventConfig.FindEntry(_selectedEvents[i]);
                if (entry.Modifier != null)
                {
                    entry.Modifier.Deactivate(_eventContext);
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
            for (int i = 0; i < _selectedEvents.Count; i++)
            {
                ShowEventPopup(_selectedEvents[i]);

                // 最后一个事件无需等待
                if (i < _selectedEvents.Count - 1)
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
        /// <param name="eventType">要播报的事件类型</param>
        private void ShowEventPopup(LevelEventType eventType)
        {
            if (_eventConfig == null)
            {
                Debug.LogWarning("[LevelEventAnnouncer] 事件配置表为空，无法播放事件弹窗。");
                return;
            }

            LevelEventEntry entry = _eventConfig.FindEntry(eventType);

            if (entry.PopupPrefab == null)
            {
                Debug.LogWarning($"[LevelEventAnnouncer] 事件 {eventType} 的弹窗 Prefab 未配置，无法播放事件弹窗。");
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LevelEventAnnouncer] PopupManager 不存在，无法播放事件弹窗。");
                return;
            }

            PopupManager.Instance.ShowPopup(entry.PopupPrefab, POPUP_AUTO_CLOSE_DURATION);
            Debug.Log($"[LevelEventAnnouncer] 本关事件：{entry.DisplayName}（{eventType}），弹窗已播放。");
        }
    }
}

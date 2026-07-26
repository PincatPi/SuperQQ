using System;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件播报器 — 场景级单例
    /// 进入关卡时从 LevelEventConfig 中随机抽取一个事件
    /// 通过 PopupManager 播放持续3秒自动销毁的弹窗说明本关事件内容
    /// 暴露当前事件类型和 OnEventSelected 事件，供未来事件逻辑处理器订阅
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

        // 本关随机选中并播报的事件类型
        private LevelEventType _currentEventType;

        // 是否已完成事件播报
        private bool _bHasAnnounced;

        // ==================== 公开事件 ====================

        /// <summary>
        /// 事件选中事件：随机抽取事件后触发
        /// 参数为选中的 LevelEventType
        /// 未来事件逻辑处理器（如 BossPatrolHandler、ColdSnapHandler）订阅此事件启动对应逻辑
        /// </summary>
        public event Action<LevelEventType> OnEventSelected;

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
        /// 本关随机选中的事件类型
        /// 未来事件逻辑处理器可查询此属性获取当前事件
        /// </summary>
        public LevelEventType CurrentEvent => _currentEventType;

        /// <summary>
        /// 是否已完成事件播报
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
        }

        private void Start()
        {
            SelectAndAnnounceEvent();
        }

        // ==================== 核心流程 ====================

        /// <summary>
        /// 从配置表中随机抽取一个事件并播报弹窗
        /// 在 Start 中自动调用，确保每次进入关卡时都执行
        /// </summary>
        public void SelectAndAnnounceEvent()
        {
            if (_bHasAnnounced)
            {
                return;
            }

            LevelEventType selectedType = SelectRandomEvent();
            _currentEventType = selectedType;
            _bHasAnnounced = true;

            // 通知外部：事件已选中，未来事件逻辑处理器可订阅此事件启动对应逻辑
            OnEventSelected?.Invoke(selectedType);

            // 播放事件说明弹窗
            ShowEventPopup(selectedType);
        }

        // ==================== 内部方法 ====================

        /// <summary>
        /// 从配置表的事件列表中随机抽取一个事件类型
        /// 使用 UnityEngine.Random 保证分布均匀
        /// </summary>
        /// <returns>随机选中的事件类型</returns>
        private LevelEventType SelectRandomEvent()
        {
            if (_eventConfig == null || _eventConfig.EventCount == 0)
            {
                Debug.LogWarning("[LevelEventAnnouncer] 事件配置表为空或无事件条目，默认选择 BossPatrol。");
                return LevelEventType.BossPatrol;
            }

            int index = UnityEngine.Random.Range(0, _eventConfig.EventCount);
            return _eventConfig.Events[index].EventType;
        }

        /// <summary>
        /// 通过 PopupManager 播放事件说明弹窗
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

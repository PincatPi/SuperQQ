using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 弹窗管理器 — 局内弹窗与提示（Tips）的统一播放入口（场景级单例）
    /// 职责：
    ///   - 维护「类型枚举 → Prefab + 默认时长」注册表（Inspector 配置），游戏逻辑只依赖枚举与 PopupArgs 参数
    ///   - 播放/关闭弹窗与 Tips，统一管理自动关闭计时，实例关闭后即销毁
    /// 生命周期规则：
    ///   - 弹窗：时长 > 0 自动关闭（销毁实例）；时长 = 0 不自动关闭，
    ///     由 Prefab 上的关闭按钮（PopupView.RequestClose）或外部 ClosePopup 关闭
    ///   - Tips：仅承载一段提示文本，必须自动关闭，未提供有效时长时回退到注册表默认时长
    ///   - 浮动文本：在指定位置展示一段临时文本（伤害数字、落点提示等），固定时长后自动销毁；
    ///     类型化的文本/位置偏移/时长在浮动文本注册表（FloatingTextType）集中配置，
    ///     也可由调用方直接传入文本与时长参数
    /// 本类不感知任何具体弹窗的业务逻辑。
    /// 新增弹窗/Tips 只需：枚举加值 → 制作挂有 PopupView/TipsView 的 Prefab → 注册表登记
    /// </summary>
    public class PopupManager : MonoBehaviour
    {
        /// <summary>弹窗注册表条目：类型 → Prefab + 默认时长</summary>
        [Serializable]
        private sealed class PopupConfig
        {
            public PopupType Type;
            public PopupView Prefab;

            [Tooltip("默认自动关闭时长（秒）。0 = 不自动关闭，需关闭按钮或外部 ClosePopup；PopupArgs.Duration 非负时覆盖本值")]
            [Min(0f)]
            public float DefaultDuration = 3f;
        }

        /// <summary>Tips 注册表条目：类型 → Prefab + 默认时长</summary>
        [Serializable]
        private sealed class TipsConfig
        {
            public TipsType Type;
            public TipsView Prefab;

            [Tooltip("默认自动关闭时长（秒）。Tips 必须自动关闭，需大于 0；PopupArgs.Duration 为正时覆盖本值")]
            [Min(0.1f)]
            public float DefaultDuration = 2f;
        }

        /// <summary>浮动文本注册表条目：类型 → 文本内容 + 位置偏移 + 展示时长（调用方只需提供类型与世界锚点）</summary>
        [Serializable]
        private sealed class FloatingTextConfig
        {
            public FloatingTextType Type;

            [Tooltip("提示文本内容")]
            public string Text = string.Empty;

            [Tooltip("相对调用方传入的世界锚点的偏移（世界单位，x 向右、y 向上）")]
            public Vector2 Offset = new Vector2(0f, 0.5f);

            [Tooltip("展示时长（秒）。非正时使用上方浮动文本默认时长")]
            public float Duration = -1f;
        }

        /// <summary>活跃实例类别：弹窗 / Tips / 浮动文本</summary>
        private enum EntryCategory
        {
            Popup,
            Tips,
            FloatingText
        }

        /// <summary>展示中实例的运行时记录</summary>
        private sealed class ActiveEntry
        {
            public Component View;      // PopupView、TipsView 或 FloatingTextView
            public float RemainingTime; // 负数表示不自动关闭（仅弹窗）
            public EntryCategory Category;
            public Action OnClosed;     // 仅弹窗使用，Tips 与浮动文本无关闭回调
        }

        // 单例实例（场景级，不跨场景保留）
        private static PopupManager _instance;

        [Header("容器")]
        [Tooltip("弹窗容器：所有弹窗实例作为其子级，留空时使用自身 Transform")]
        [SerializeField] private Transform _popupContainer;

        [Tooltip("Tips 容器：所有 Tips 实例作为其子级，留空时使用自身 Transform")]
        [SerializeField] private Transform _tipsContainer;

        [Header("弹窗注册表")]
        [SerializeField] private List<PopupConfig> _popups = new();

        [Header("Tips 注册表")]
        [SerializeField] private List<TipsConfig> _tips = new();

        [Header("浮动文本")]
        [Tooltip("浮动文本容器：所有浮动文本实例作为其子级，留空时回退使用 Tips 容器")]
        [SerializeField] private Transform _floatingTextContainer;

        [Tooltip("预先配置的浮动文本 Prefab（根节点挂 FloatingTextView，内含 TextMeshPro 文本）")]
        [SerializeField] private FloatingTextView _floatingTextPrefab;

        [Tooltip("浮动文本默认展示时长（秒）；ShowFloatingText 传入有效时长时以参数为准")]
        [Min(0.1f)]
        [SerializeField] private float _defaultFloatingTextDuration = 3f;

        [Header("浮动文本注册表")]
        [Tooltip("类型化的浮动文本配置：文本、位置偏移与时长统一在此维护，调用方只传 FloatingTextType 与世界锚点")]
        [SerializeField] private List<FloatingTextConfig> _floatingTexts = new();

        // Tips 兜底时长：注册表与参数均未提供有效时长时使用
        private const float FALLBACK_TIPS_DURATION = 2f;

        // 浮动文本兜底时长：默认时长配置无效时使用
        private const float FALLBACK_FLOATING_TEXT_DURATION = 3f;

        // 当前展示中的弹窗/Tips（按播放顺序，后播放的在列表末尾）
        private readonly List<ActiveEntry> _activeEntries = new();

        // ==================== 单例访问 ====================

        /// <summary>
        /// 当前场景中的全局唯一实例
        /// </summary>
        public static PopupManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PopupManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 当前展示中的弹窗与 Tips 总数
        /// </summary>
        public int ActiveCount => _activeEntries.Count;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            if (_popupContainer == null)
            {
                _popupContainer = transform;
            }
            if (_tipsContainer == null)
            {
                _tipsContainer = transform;
            }
            if (_floatingTextContainer == null)
            {
                _floatingTextContainer = _tipsContainer;
            }

            // 弹窗/Tips 必须渲染在其它 UI 之上：容器置顶为同级最后一个子级，
            // 避免被场景中后摆放的全屏面板（道具选择、结算等）遮挡
            _popupContainer.SetAsLastSibling();
            _tipsContainer.SetAsLastSibling();
            _floatingTextContainer.SetAsLastSibling();
        }

        private void Update()
        {
            // 统一推进自动关闭计时，避免每个实例各自挂 Update
            float deltaTime = Time.deltaTime;
            for (int i = _activeEntries.Count - 1; i >= 0; i--)
            {
                ActiveEntry entry = _activeEntries[i];
                if (entry.RemainingTime < 0f)
                {
                    continue;
                }

                entry.RemainingTime -= deltaTime;
                if (entry.RemainingTime <= 0f)
                {
                    CloseInternal(entry);
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _activeEntries.Clear();
        }

        // ==================== 核心接口：播放弹窗 ====================

        /// <summary>
        /// 播放弹窗：从注册表取 Prefab，实例化后立即展示
        /// </summary>
        /// <param name="type">弹窗类型（注册表索引键）</param>
        /// <param name="args">播放参数，可为 null；时长为负数时使用注册表默认时长，0 表示不自动关闭</param>
        /// <returns>弹窗视图引用，可用于手动关闭或查询状态；播放失败返回 null</returns>
        public PopupView ShowPopup(PopupType type, PopupArgs args = null)
        {
            PopupConfig config = FindPopupConfig(type);
            if (config == null)
            {
                return null;
            }

            float duration = args != null && args.Duration >= 0f ? args.Duration : config.DefaultDuration;
            return ShowInternal(config.Prefab, duration, args);
        }

        /// <summary>
        /// 播放弹窗（泛型）：返回具体视图类型，便于调用方继续驱动弹窗内容（如进度条刷新）
        /// </summary>
        /// <typeparam name="T">Prefab 根节点上挂载的 PopupView 子类</typeparam>
        public T ShowPopup<T>(PopupType type, PopupArgs args = null) where T : PopupView
        {
            PopupView view = ShowPopup(type, args);
            if (view != null && !(view is T))
            {
                Debug.LogWarning($"[PopupManager] 弹窗 {type} 的 Prefab 视图类型与调用方期望的 {typeof(T).Name} 不一致。");
            }
            return view as T;
        }

        // ==================== 核心接口：播放 Tips ====================

        /// <summary>
        /// 播放 Tips：展示一段提示文本，固定时长后自动关闭销毁（Tips 不支持手动关闭）
        /// </summary>
        /// <param name="type">Tips 类型（注册表索引键）</param>
        /// <param name="content">提示文本内容</param>
        /// <param name="duration">展示时长（秒），非正时使用注册表默认时长</param>
        /// <returns>Tips 视图引用；播放失败返回 null</returns>
        public TipsView ShowTips(TipsType type, string content, float duration = -1f)
        {
            TipsConfig config = FindTipsConfig(type);
            if (config == null)
            {
                return null;
            }

            float actualDuration = duration > 0f ? duration : config.DefaultDuration;
            if (actualDuration <= 0f)
            {
                actualDuration = FALLBACK_TIPS_DURATION;
            }

            TipsView view = Instantiate(config.Prefab, _tipsContainer);
            _activeEntries.Add(new ActiveEntry
            {
                View = view,
                RemainingTime = actualDuration,
                Category = EntryCategory.Tips
            });

            view.SetContent(content);
            return view;
        }

        // ==================== 核心接口：播放浮动文本 ====================

        /// <summary>
        /// 播放注册表配置的浮动文本：文本内容、位置偏移与展示时长统一取自浮动文本注册表，
        /// 调用方只需提供类型与世界锚点（如道具包围盒顶部中点）
        /// </summary>
        /// <param name="type">浮动文本类型（注册表索引键）</param>
        /// <param name="worldAnchor">世界坐标锚点，注册表配置的位置偏移将叠加在其上</param>
        /// <param name="camera">观察世界的相机；为 null 时回退 Camera.main</param>
        /// <returns>浮动文本视图引用；播放失败返回 null</returns>
        public FloatingTextView ShowFloatingText(FloatingTextType type, Vector3 worldAnchor, Camera camera = null)
        {
            FloatingTextConfig config = FindFloatingTextConfig(type);
            if (config == null)
            {
                return null;
            }
            return ShowFloatingText(config.Text, worldAnchor + (Vector3)config.Offset, config.Duration, camera);
        }

        /// <summary>
        /// 播放浮动文本：在指定位置展示一段临时文本，固定时长后自动关闭销毁（不支持手动关闭）
        /// </summary>
        /// <param name="content">文本内容</param>
        /// <param name="anchoredPosition">展示位置（浮动文本容器局部坐标）</param>
        /// <param name="duration">展示时长（秒），非正时使用默认时长（默认 3 秒）</param>
        /// <returns>浮动文本视图引用；播放失败返回 null</returns>
        public FloatingTextView ShowFloatingText(string content, Vector2 anchoredPosition, float duration = -1f)
        {
            FloatingTextView view = CreateFloatingText(content, duration);
            if (view != null)
            {
                view.SetPosition(anchoredPosition);
            }
            return view;
        }

        /// <summary>
        /// 播放浮动文本（世界坐标版）：自动将世界坐标转换为容器局部坐标后展示
        /// </summary>
        /// <param name="content">文本内容</param>
        /// <param name="worldPosition">世界坐标（如角色头顶位置）</param>
        /// <param name="duration">展示时长（秒），非正时使用默认时长（默认 3 秒）</param>
        /// <param name="camera">观察世界的相机；为 null 时回退 Camera.main</param>
        /// <returns>浮动文本视图引用；播放失败返回 null</returns>
        public FloatingTextView ShowFloatingText(string content, Vector3 worldPosition, float duration = -1f, Camera camera = null)
        {
            FloatingTextView view = CreateFloatingText(content, duration);
            if (view != null)
            {
                view.SetWorldPosition(worldPosition, camera);
            }
            return view;
        }

        /// <summary>
        /// 实例化浮动文本并登记活跃记录：实例创建、计时登记、内容设置的统一步骤
        /// </summary>
        private FloatingTextView CreateFloatingText(string content, float duration)
        {
            if (_floatingTextPrefab == null)
            {
                Debug.LogWarning("[PopupManager] 浮动文本 Prefab 未配置，拒绝播放。");
                return null;
            }

            float actualDuration = duration > 0f ? duration : _defaultFloatingTextDuration;
            if (actualDuration <= 0f)
            {
                actualDuration = FALLBACK_FLOATING_TEXT_DURATION;
            }

            FloatingTextView view = Instantiate(_floatingTextPrefab, _floatingTextContainer);
            _activeEntries.Add(new ActiveEntry
            {
                View = view,
                RemainingTime = actualDuration,
                Category = EntryCategory.FloatingText
            });

            view.SetContent(content);
            view.Play(actualDuration);
            return view;
        }

        // ==================== 关闭 ====================

        /// <summary>
        /// 手动关闭指定弹窗/Tips（对自动关闭中的实例同样生效）
        /// </summary>
        /// <param name="view">待关闭的弹窗视图（ShowPopup 的返回值；Tips 只能自动关闭，不在此列）</param>
        public void ClosePopup(PopupView view)
        {
            if (view == null)
            {
                return;
            }

            ActiveEntry entry = FindActiveEntry(view);
            if (entry == null)
            {
                return;
            }

            CloseInternal(entry);
        }

        /// <summary>
        /// 关闭所有展示中的弹窗（不含 Tips）
        /// </summary>
        public void CloseAllPopups()
        {
            CloseWhere(entry => entry.Category == EntryCategory.Popup);
        }

        /// <summary>
        /// 关闭所有展示中的 Tips（不含弹窗与浮动文本）
        /// </summary>
        public void CloseAllTips()
        {
            CloseWhere(entry => entry.Category == EntryCategory.Tips);
        }

        /// <summary>
        /// 关闭所有展示中的浮动文本（不含弹窗与 Tips）
        /// </summary>
        public void CloseAllFloatingTexts()
        {
            CloseWhere(entry => entry.Category == EntryCategory.FloatingText);
        }

        /// <summary>
        /// 关闭所有展示中的弹窗与 Tips
        /// </summary>
        public void CloseAll()
        {
            CloseWhere(_ => true);
        }

        // ==================== 内部：播放与关闭 ====================

        /// <summary>
        /// 播放弹窗内部实现：实例化到弹窗容器下、订阅关闭请求、登记活跃记录并应用参数
        /// 实例创建时即订阅关闭请求，实例随关闭销毁，订阅关系随之失效
        /// </summary>
        private PopupView ShowInternal(PopupView prefab, float duration, PopupArgs args)
        {
            PopupView view = Instantiate(prefab, _popupContainer);
            view.CloseRequested += HandleCloseRequested;

            _activeEntries.Add(new ActiveEntry
            {
                View = view,
                RemainingTime = duration > 0f ? duration : -1f,
                Category = EntryCategory.Popup,
                OnClosed = args != null ? args.OnClosed : null
            });

            view.BIsShowing = true;
            view.OnShow(args);
            return view;
        }

        /// <summary>
        /// 关闭内部实现：移出活跃列表、通知视图（弹窗）、销毁实例、触发关闭回调
        /// 自动关闭倒计时、关闭按钮请求、外部 ClosePopup 均汇聚到本方法，保证生命周期单一出口
        /// </summary>
        private void CloseInternal(ActiveEntry entry)
        {
            _activeEntries.Remove(entry);

            Component view = entry.View;
            if (view != null)
            {
                if (view is PopupView popupView)
                {
                    popupView.BIsShowing = false;
                    popupView.OnHide();
                }
                Destroy(view.gameObject);
            }

            entry.OnClosed?.Invoke();
        }

        /// <summary>
        /// 视图关闭按钮请求关闭时的回调
        /// </summary>
        private void HandleCloseRequested(PopupView view)
        {
            ClosePopup(view);
        }

        /// <summary>
        /// 按条件批量关闭（倒序遍历，避免遍历时修改列表）
        /// </summary>
        private void CloseWhere(Predicate<ActiveEntry> predicate)
        {
            for (int i = _activeEntries.Count - 1; i >= 0; i--)
            {
                if (predicate(_activeEntries[i]))
                {
                    CloseInternal(_activeEntries[i]);
                }
            }
        }

        // ==================== 内部：注册表与活跃记录查找 ====================

        private PopupConfig FindPopupConfig(PopupType type)
        {
            if (type == PopupType.None)
            {
                Debug.LogWarning("[PopupManager] 传入 PopupType.None，拒绝播放。");
                return null;
            }

            for (int i = 0; i < _popups.Count; i++)
            {
                if (_popups[i] != null && _popups[i].Type == type)
                {
                    if (_popups[i].Prefab == null)
                    {
                        Debug.LogWarning($"[PopupManager] 弹窗 {type} 已注册但 Prefab 未配置。");
                        return null;
                    }
                    return _popups[i];
                }
            }

            Debug.LogWarning($"[PopupManager] 弹窗 {type} 未在注册表中配置。");
            return null;
        }

        private TipsConfig FindTipsConfig(TipsType type)
        {
            if (type == TipsType.None)
            {
                Debug.LogWarning("[PopupManager] 传入 TipsType.None，拒绝播放。");
                return null;
            }

            for (int i = 0; i < _tips.Count; i++)
            {
                if (_tips[i] != null && _tips[i].Type == type)
                {
                    if (_tips[i].Prefab == null)
                    {
                        Debug.LogWarning($"[PopupManager] Tips {type} 已注册但 Prefab 未配置。");
                        return null;
                    }
                    return _tips[i];
                }
            }

            Debug.LogWarning($"[PopupManager] Tips {type} 未在注册表中配置。");
            return null;
        }

        private FloatingTextConfig FindFloatingTextConfig(FloatingTextType type)
        {
            if (type == FloatingTextType.None)
            {
                Debug.LogWarning("[PopupManager] 传入 FloatingTextType.None，拒绝播放。");
                return null;
            }

            for (int i = 0; i < _floatingTexts.Count; i++)
            {
                if (_floatingTexts[i] != null && _floatingTexts[i].Type == type)
                {
                    return _floatingTexts[i];
                }
            }

            Debug.LogWarning($"[PopupManager] 浮动文本 {type} 未在注册表中配置。");
            return null;
        }

        private ActiveEntry FindActiveEntry(Component view)
        {
            for (int i = 0; i < _activeEntries.Count; i++)
            {
                if (_activeEntries[i].View == view)
                {
                    return _activeEntries[i];
                }
            }
            return null;
        }

    }
}

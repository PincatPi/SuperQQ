using System.Collections;
using System.Collections.Generic;
using SuperQQ.Audio;
using SuperQQ.Sensors;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 液氮泄露事件修饰符 — ScriptableObject 资产
    /// 整局随机触发一次，流程：随机延迟 → Tips 文字预警（经 PopupManager 播放，自动关闭）
    /// → 冻结所有存活玩家（刚体全约束，冰块视觉挂为玩家子节点）
    /// → 玩家摇晃手机累积解冻进度（摇晃强度越大进度涨得越快，配进度条 UI）
    /// → 进度满（或超时兜底）后全员解冻，伴随冰块碎裂音效
    /// 摇晃检测由独立的 ShakeDetector 模块提供，本事件只轮询其强度输出
    /// 所有策划参数均在本资产上配置；运行时状态（随机源、协程、冻结记录、UI）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "LiquidNitrogenLeakModifier", menuName = "SuperQQ/Event/Liquid Nitrogen Leak Modifier")]
    public class LiquidNitrogenLeakModifier : LevelEventModifier
    {
        [Header("触发时机")]
        [Tooltip("关卡开始后触发事件的最小延迟（秒），实际触发时机在 最小~最大 之间随机")]
        [Min(0f)]
        [SerializeField] private float _minTriggerDelay = 8f;

        [Tooltip("关卡开始后触发事件的最大延迟（秒），实际触发时机在 最小~最大 之间随机")]
        [Min(0f)]
        [SerializeField] private float _maxTriggerDelay = 20f;

        [Header("预警")]
        [Tooltip("预警 Tips 文本内容（经 PopupManager 以通用 Tips 播放，自动关闭）；留空则跳过预警直接冻结")]
        [SerializeField] private string _warningTipText = "液氮即将泄露，注意躲避！";

        [Tooltip("预警时长（秒）：预警 Tips 弹出到正式冻结之间的缓冲时间")]
        [Min(0f)]
        [SerializeField] private float _warningDuration = 3f;

        [Header("冻结表现")]
        [Tooltip("冰封特效 Prefab（根节点需挂 FrozenIceEffect 脚本）：冻结时实例化为玩家子节点随其移动（播放后自动定格为冰封画面），解冻时调用其 Dissipate 接口让粒子自然消散后销毁；留空则无冰封视觉")]
        [SerializeField] private FrozenIceEffect _iceBlockPrefab;

        [Tooltip("解冻时调用 Dissipate 后到销毁的等待时长（秒），需不短于特效的 fadeTime 消散时长")]
        [Min(0.1f)]
        [SerializeField] private float _iceEffectDestroyDelay = 0.6f;

        [Tooltip("冻结发生时经 PopupManager 播放的 Tips 文本内容（留空则不播放）")]
        [SerializeField] private string _freezeTipText = "全员被冻结！快摇晃手机解冻！";

        [Tooltip("冻结期间显示的 UI Prefab（屏幕空间 UI，冻结时实例化到本地主 Canvas 下，解冻时销毁）；留空则无冻结 UI")]
        [SerializeField] private GameObject _frozenUiPrefab;

        [Header("解冻进度")]
        [Tooltip("以满强度持续摇晃解冻所需的秒数（强度减半则耗时约翻倍）")]
        [Min(0.1f)]
        [SerializeField] private float _fullShakeThawSeconds = 3f;

        [Tooltip("进度自然衰减速度（进度/秒）：不摇晃时进度缓慢回退，鼓励持续摇晃")]
        [Min(0f)]
        [SerializeField] private float _progressDecayPerSecond = 0.1f;

        [Tooltip("自动解冻兜底时间（秒）：从冻结开始计时，超时强制解冻，防止传感器不可用时玩家被永久冻结")]
        [Min(1f)]
        [SerializeField] private float _autoThawTimeout = 15f;

        [Header("震动反馈")]
        [Tooltip("触发震动的有效摇晃强度（0~1）：解冻阶段摇晃强度达到该值时产生震动反馈")]
        [Range(0f, 1f)]
        [SerializeField] private float _vibrateShakeThreshold = 0.5f;

        [Tooltip("相邻两次震动的最小间隔（秒），防止震动过于频繁")]
        [Min(0.05f)]
        [SerializeField] private float _vibrateMinInterval = 0.3f;

        [Header("音效")]
        [Tooltip("冰块碎裂音效（经 AudioManager 播放，玩家结束冻结状态时统一播放一次）；None 则静默")]
        [SerializeField] private SfxId _crackSfx = SfxId.IceCrack;

        [Tooltip("冻结循环音效：冻结期间持续循环播放（与冻结 UI 同帧开始），解冻后音量渐弱至停止；None 则静默")]
        [SerializeField] private SfxId _freezeStartSfx = SfxId.FreezeStart;

        [Tooltip("解冻后冻结循环音效的淡出时长（秒）")]
        [Min(0.05f)]
        [SerializeField] private float _freezeSfxFadeOut = 0.5f;

        [Header("随机源")]
        [Tooltip("固定随机种子；为 0 时使用时间种子。联机模式下主机广播该种子即可各端确定性模拟")]
        [SerializeField] private int _fixedSeed = 0;

        // 被本事件冻结的玩家记录（玩家 + 其冰封特效实例）
        private sealed class FrozenEntry
        {
            public readonly PlayerController Player;
            public readonly FrozenIceEffect IceEffect;

            public FrozenEntry(PlayerController player, FrozenIceEffect iceEffect)
            {
                Player = player;
                IceEffect = iceEffect;
            }
        }

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 事件内统一的随机源：触发时机走它，不用 UnityEngine.Random
        private System.Random _random;

        // 事件流程协程引用，Deactivate 时停止
        private Coroutine _eventCoroutine;

        // 本事件冻结的玩家及其冰块实例，解冻/清理时遍历
        private readonly List<FrozenEntry> _frozenEntries = new();

        // 解冻进度条弹窗引用（由 PopupManager 管理，结束时手动关闭并销毁）
        private ThawProgressBar _progressBarPopup;

        // 解冻阶段启用的摇晃检测器引用，结束时禁用
        private ShakeDetector _shakeDetector;

        // 冻结期间显示的 UI 实例，解冻/清理时销毁
        private GameObject _frozenUiInstance;

        // 上次震动反馈时刻（Time.time，用于最小间隔节流）
        private float _lastVibrateTime = float.NegativeInfinity;

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：创建随机源，启动事件流程协程（随机延迟 → 预警 → 冻结 → 摇晃解冻）
        /// </summary>
        public override void Activate(LevelEventContext context)
        {
            if (context == null || context.CoroutineRunner == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] 上下文或协程宿主为空，事件不生效。");
                return;
            }

            _random = _fixedSeed != 0 ? new System.Random(_fixedSeed) : new System.Random();
            _eventCoroutine = context.CoroutineRunner.StartCoroutine(EventRoutine(context));
        }

        /// <summary>
        /// 停用事件：停止流程协程，强制解冻本事件冻结的玩家并清理冰块/UI/检测器
        /// </summary>
        public override void Deactivate(LevelEventContext context)
        {
            if (_eventCoroutine != null && context != null && context.CoroutineRunner != null)
            {
                context.CoroutineRunner.StopCoroutine(_eventCoroutine);
                _eventCoroutine = null;
            }

            // 无论事件进行到哪个阶段，统一走解冻清理（内部各项均有空值守卫）。
            // 强制中断属于清理路径（场景销毁/阶段切换），静默解冻不播放碎裂音效，
            // 避免场景关闭阶段经 OnDestroy 链路调用 AudioManager 重建已销毁的单例
            EndThaw(playCrackSfx: false);
            _random = null;
        }

        // ==================== 事件流程协程 ====================

        /// <summary>
        /// 事件主流程：随机延迟一次 → Tips 文字预警 → 冻结全员 → 摇晃解冻循环 → 解冻收尾
        /// 整局只触发一次，流程结束后协程自然退出
        /// </summary>
        private IEnumerator EventRoutine(LevelEventContext context)
        {
            float triggerDelay = Mathf.Lerp(_minTriggerDelay, _maxTriggerDelay, (float)_random.NextDouble());
            yield return new WaitForSeconds(triggerDelay);

            ShowWarning();
            yield return new WaitForSeconds(_warningDuration);

            FreezeAllAlivePlayers();
            if (_frozenEntries.Count == 0)
            {
                // 无可冻结玩家（全员已出局/通关），事件直接结束
                yield break;
            }

            PlayFreezeTip();
            ShowFrozenUi();

            ThawProgressBar progressBar = ShowProgressBar();
            _shakeDetector = ShakeDetector.GetOrCreate();
            _shakeDetector.enabled = true;

            // 解冻循环：进度按摇晃强度累积、无摇晃时缓慢衰减；满进度或超时兜底退出
            float progress = 0f;
            float elapsed = 0f;
            _lastVibrateTime = float.NegativeInfinity;

            while (progress < 1f && elapsed < _autoThawTimeout)
            {
                float deltaTime = Time.deltaTime;
                elapsed += deltaTime;
                progress = Mathf.Clamp01(progress +
                    (_shakeDetector.CurrentIntensity / _fullShakeThawSeconds - _progressDecayPerSecond) * deltaTime);

                // 有效摇晃幅度触发震动反馈（按最小间隔节流，防止震动过于频繁）
                if (_shakeDetector.CurrentIntensity >= _vibrateShakeThreshold
                    && Time.time - _lastVibrateTime >= _vibrateMinInterval)
                {
                    _lastVibrateTime = Time.time;
                    Handheld.Vibrate();
                }

                if (progressBar != null)
                {
                    progressBar.SetProgress(progress);
                }

                PruneInvalidFrozenEntries();
                if (_frozenEntries.Count == 0)
                {
                    // 冻结期间全部被击杀/通关，无需再解冻
                    break;
                }

                yield return null;
            }

            EndThaw();
            _eventCoroutine = null;
        }

        // ==================== 预警 ====================

        /// <summary>
        /// 播放预警 Tips（通用 Tips 类型，按预警时长展示后自动关闭）
        /// 文本未配置或 PopupManager 缺失时跳过（不阻断后续冻结流程）
        /// </summary>
        private void ShowWarning()
        {
            if (string.IsNullOrEmpty(_warningTipText))
            {
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] PopupManager 不存在，跳过预警 Tips 播放。");
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _warningTipText, _warningDuration);
        }

        // ==================== 冻结 ====================

        /// <summary>
        /// 冻结所有当前存活玩家：切入冻结状态并挂接冰块视觉（快照取自 Registry）
        /// 已死亡/幽灵/通关玩家不受影响（Freeze 内部有状态守卫）
        /// </summary>
        private void FreezeAllAlivePlayers()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] LevelPlayerRegistry 不存在，事件不生效。");
                return;
            }

            List<PlayerController> targets = registry.GetPlayersByState(PlayerStateType.Alive);
            for (int i = 0; i < targets.Count; i++)
            {
                PlayerController player = targets[i];
                if (player == null)
                {
                    continue;
                }

                player.Freeze();
                if (!player.BIsFrozen)
                {
                    continue;
                }

                FrozenIceEffect iceEffect = null;
                if (_iceBlockPrefab != null)
                {
                    iceEffect = Instantiate(_iceBlockPrefab, player.transform);
                }
                _frozenEntries.Add(new FrozenEntry(player, iceEffect));
            }
        }

        /// <summary>
        /// 播放冻结 Tips（通用 Tips 类型，自动关闭）；文本未配置或 PopupManager 缺失时静默跳过
        /// </summary>
        private void PlayFreezeTip()
        {
            if (string.IsNullOrEmpty(_freezeTipText))
            {
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] PopupManager 不存在，跳过冻结 Tips 播放。");
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _freezeTipText);
        }

        /// <summary>
        /// 弹出冻结期间 UI：实例化到本地主 Canvas 下（各客户端本地各自执行，即所有玩家的本地 UI 都会弹出）
        /// 未配置 Prefab 或未找到主 Canvas 时跳过，解冻逻辑不受影响
        /// </summary>
        private void ShowFrozenUi()
        {
            if (_frozenUiPrefab == null)
            {
                return;
            }

            RectTransform canvasRect = ResolveMainCanvasRect();
            if (canvasRect == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] 未找到主 Canvas，跳过冻结 UI。");
                return;
            }

            _frozenUiInstance = Instantiate(_frozenUiPrefab, canvasRect, false);

            // 冻结循环音效：与全屏冻结 UI 同帧开始循环，解冻收尾（EndThaw）时淡出
            if (_freezeStartSfx != SfxId.None)
            {
                AudioManager.StartLoopSfx(_freezeStartSfx);
            }
        }

        /// <summary>
        /// 解析本地主 Canvas：优先玩家名称标签管理器所在的 Canvas（玩家头顶 UI 的既有宿主），
        /// 其次 PopupManager 所在的 Canvas；均未找到时返回 null
        /// </summary>
        private static RectTransform ResolveMainCanvasRect()
        {
            Canvas canvas = null;

            if (PlayerNameLabelManager.Instance != null)
            {
                canvas = PlayerNameLabelManager.Instance.GetComponentInParent<Canvas>();
            }

            if (canvas == null && PopupManager.Instance != null)
            {
                canvas = PopupManager.Instance.GetComponentInParent<Canvas>();
            }

            return canvas != null ? canvas.GetComponent<RectTransform>() : null;
        }

        // ==================== 解冻 ====================

        /// <summary>
        /// 弹出解冻进度条弹窗（显式指定时长 0 = 不自动关闭，由 EndThaw 手动关闭）
        /// PopupManager 缺失或未注册时返回 null，进度逻辑不受影响
        /// </summary>
        private ThawProgressBar ShowProgressBar()
        {
            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] PopupManager 不存在，无法弹出解冻进度条。");
                return null;
            }

            _progressBarPopup = PopupManager.Instance.ShowPopup<ThawProgressBar>(PopupType.ThawProgress, PopupArgs.WithDuration(0f));
            return _progressBarPopup;
        }

        /// <summary>
        /// 清理已失效的冻结记录：玩家被销毁、被击杀（转幽灵）或被外部解冻时，移除记录并销毁其冰块
        /// </summary>
        private void PruneInvalidFrozenEntries()
        {
            for (int i = _frozenEntries.Count - 1; i >= 0; i--)
            {
                FrozenEntry entry = _frozenEntries[i];
                if (entry.Player == null || !entry.Player.BIsFrozen)
                {
                    DestroyIceBlock(entry);
                    _frozenEntries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 解冻收尾：解冻所有仍被本事件冻结的玩家，销毁冰块，关闭进度条，禁用摇晃检测器
        /// 正常结束与 Deactivate 强制中断共用本方法，各项均有空值守卫
        /// </summary>
        /// <param name="playCrackSfx">解冻时是否播放碎裂音效（Deactivate 清理路径传 false）</param>
        private void EndThaw(bool playCrackSfx = true)
        {
            bool bAnyPlayerThawed = false;
            for (int i = 0; i < _frozenEntries.Count; i++)
            {
                FrozenEntry entry = _frozenEntries[i];
                if (entry.Player != null && entry.Player.BIsFrozen)
                {
                    entry.Player.Unfreeze();
                    bAnyPlayerThawed = true;
                }
                DestroyIceBlock(entry);
            }
            _frozenEntries.Clear();

            // 有玩家结束冻结状态时统一播放一次碎裂音效（正常解冻与超时兜底共用）
            if (bAnyPlayerThawed && playCrackSfx)
            {
                PlayCrackSfx();
            }

            // 冻结循环音效淡出停止（正常解冻、超时兜底、Deactivate 强制中断共用本出口）
            if (_freezeStartSfx != SfxId.None)
            {
                AudioManager.StopLoopSfx(_freezeStartSfx, _freezeSfxFadeOut);
            }

            if (_frozenUiInstance != null)
            {
                // 场景正常销毁时冻结 UI 可能已随之销毁，此处判空后兜底销毁
                Destroy(_frozenUiInstance);
                _frozenUiInstance = null;
            }

            if (_progressBarPopup != null)
            {
                if (PopupManager.Instance != null)
                {
                    PopupManager.Instance.ClosePopup(_progressBarPopup);
                }
                _progressBarPopup = null;
            }

            if (_shakeDetector != null)
            {
                _shakeDetector.enabled = false;
                _shakeDetector = null;
            }
        }

        /// <summary>
        /// 移除单条冻结记录的冰封特效：调用 FrozenIceEffect.Dissipate 让所有粒子
        /// 在 fadeTime 内自然消散，延迟足够时长后再销毁；
        /// 场景销毁时特效可能已随之销毁，判空后兜底
        /// </summary>
        private void DestroyIceBlock(FrozenEntry entry)
        {
            if (entry.IceEffect == null)
            {
                return;
            }

            entry.IceEffect.Dissipate();
            Destroy(entry.IceEffect.gameObject, _iceEffectDestroyDelay);
        }

        // ==================== 音效 ====================

        /// <summary>
        /// 播放冰块碎裂音效：统一经 AudioManager 播放（未注册/未配置 Clip 时内部静默降级并告警）
        /// </summary>
        private void PlayCrackSfx()
        {
            Debug.Log($"[LiquidNitrogenLeakModifier] 冰块碎裂音效：请求播放 SfxId={_crackSfx}" +
                (_crackSfx == SfxId.None ? "（未配置，静默跳过）" : ""));
            AudioManager.PlaySfx(_crackSfx);
        }
    }
}

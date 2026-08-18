using System.Collections;
using System.Collections.Generic;
using SuperQQ.Sensors;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 液氮泄露事件修饰符 — ScriptableObject 资产
    /// 整局随机触发一次，流程：随机延迟 → 文字预警（屏幕上方正中央，自动关闭）
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
        [Tooltip("预警弹窗 Prefab（文字提醒，经 PopupManager 弹出并自动关闭），留空则跳过预警直接冻结")]
        [SerializeField] private GameObject _warningPopupPrefab;

        [Tooltip("预警时长（秒）：预警弹出到正式冻结之间的缓冲时间")]
        [Min(0f)]
        [SerializeField] private float _warningDuration = 3f;

        [Header("冻结表现")]
        [Tooltip("冰块视觉 Prefab（无物理组件），冻结时实例化为玩家子节点，解冻时销毁；留空则无冰块视觉")]
        [SerializeField] private GameObject _iceBlockPrefab;

        [Header("解冻进度")]
        [Tooltip("解冻进度条弹窗 Prefab（根节点需挂 ThawProgressBar，经 PopupManager 弹出）；留空则无进度条 UI")]
        [SerializeField] private ThawProgressBar _thawProgressBarPrefab;

        [Tooltip("以满强度持续摇晃解冻所需的秒数（强度减半则耗时约翻倍）")]
        [Min(0.1f)]
        [SerializeField] private float _fullShakeThawSeconds = 3f;

        [Tooltip("进度自然衰减速度（进度/秒）：不摇晃时进度缓慢回退，鼓励持续摇晃")]
        [Min(0f)]
        [SerializeField] private float _progressDecayPerSecond = 0.1f;

        [Tooltip("自动解冻兜底时间（秒）：从冻结开始计时，超时强制解冻，防止传感器不可用时玩家被永久冻结")]
        [Min(1f)]
        [SerializeField] private float _autoThawTimeout = 15f;

        [Header("音效")]
        [Tooltip("冰块碎裂音效：在解冻进度里程碑（1/3、2/3）与完全解冻时播放；留空则静默")]
        [SerializeField] private AudioClip _crackSfx;

        [Header("随机源")]
        [Tooltip("固定随机种子；为 0 时使用时间种子。联机模式下主机广播该种子即可各端确定性模拟")]
        [SerializeField] private int _fixedSeed = 0;

        // 解冻进度的碎裂音效里程碑（1/3、2/3）
        private const float FIRST_CRACK_MILESTONE = 1f / 3f;
        private const float SECOND_CRACK_MILESTONE = 2f / 3f;

        // 被本事件冻结的玩家记录（玩家 + 其冰块实例）
        private sealed class FrozenEntry
        {
            public readonly PlayerController Player;
            public readonly GameObject IceBlock;

            public FrozenEntry(PlayerController player, GameObject iceBlock)
            {
                Player = player;
                IceBlock = iceBlock;
            }
        }

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 事件内统一的随机源：触发时机走它，不用 UnityEngine.Random
        private System.Random _random;

        // 事件流程协程引用，Deactivate 时停止
        private Coroutine _eventCoroutine;

        // 本事件冻结的玩家及其冰块实例，解冻/清理时遍历
        private readonly List<FrozenEntry> _frozenEntries = new();

        // 解冻进度条弹窗引用（PopupManager 池化管理，结束时手动关闭）
        private PopupController _progressBarPopup;

        // 解冻阶段启用的摇晃检测器引用，结束时禁用
        private ShakeDetector _shakeDetector;

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

            // 无论事件进行到哪个阶段，统一走解冻清理（内部各项均有空值守卫）
            EndThaw();
            _random = null;
        }

        // ==================== 事件流程协程 ====================

        /// <summary>
        /// 事件主流程：随机延迟一次 → 文字预警 → 冻结全员 → 摇晃解冻循环 → 解冻收尾
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

            ThawProgressBar progressBar = ShowProgressBar();
            _shakeDetector = ShakeDetector.GetOrCreate();
            _shakeDetector.enabled = true;

            // 解冻循环：进度按摇晃强度累积、无摇晃时缓慢衰减；满进度或超时兜底退出
            float progress = 0f;
            float elapsed = 0f;
            bool bFirstCrackPlayed = false;
            bool bSecondCrackPlayed = false;

            while (progress < 1f && elapsed < _autoThawTimeout)
            {
                float deltaTime = Time.deltaTime;
                elapsed += deltaTime;
                progress = Mathf.Clamp01(progress +
                    (_shakeDetector.CurrentIntensity / _fullShakeThawSeconds - _progressDecayPerSecond) * deltaTime);

                if (progressBar != null)
                {
                    progressBar.SetProgress(progress);
                }

                if (!bFirstCrackPlayed && progress >= FIRST_CRACK_MILESTONE)
                {
                    bFirstCrackPlayed = true;
                    PlayCrackSfx();
                }
                else if (!bSecondCrackPlayed && progress >= SECOND_CRACK_MILESTONE)
                {
                    bSecondCrackPlayed = true;
                    PlayCrackSfx();
                }

                PruneInvalidFrozenEntries();
                if (_frozenEntries.Count == 0)
                {
                    // 冻结期间全部被击杀/通关，无需再解冻
                    break;
                }

                yield return null;
            }

            if (progress >= 1f)
            {
                PlayCrackSfx();
            }

            EndThaw();
            _eventCoroutine = null;
        }

        // ==================== 预警 ====================

        /// <summary>
        /// 弹出文字预警弹窗，经 PopupManager 自动关闭
        /// 未配置 Prefab 或 PopupManager 缺失时打 Warning 并跳过（不阻断后续冻结流程）
        /// </summary>
        private void ShowWarning()
        {
            if (_warningPopupPrefab == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] 预警弹窗 Prefab 未配置，跳过预警。");
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] PopupManager 不存在，跳过预警。");
                return;
            }

            PopupManager.Instance.ShowPopup(_warningPopupPrefab, _warningDuration);
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

                GameObject iceBlock = null;
                if (_iceBlockPrefab != null)
                {
                    iceBlock = Instantiate(_iceBlockPrefab, player.transform);
                }
                _frozenEntries.Add(new FrozenEntry(player, iceBlock));
            }
        }

        // ==================== 解冻 ====================

        /// <summary>
        /// 弹出解冻进度条弹窗（不自动关闭，由 EndThaw 手动关闭）
        /// 未配置 Prefab 或 PopupManager 缺失时返回 null，进度逻辑不受影响
        /// </summary>
        private ThawProgressBar ShowProgressBar()
        {
            if (_thawProgressBarPrefab == null)
            {
                return null;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LiquidNitrogenLeakModifier] PopupManager 不存在，无法弹出解冻进度条。");
                return null;
            }

            _progressBarPopup = PopupManager.Instance.ShowPopup(_thawProgressBarPrefab.gameObject, 0f);
            return _progressBarPopup != null ? _progressBarPopup.GetComponent<ThawProgressBar>() : null;
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
        private void EndThaw()
        {
            for (int i = 0; i < _frozenEntries.Count; i++)
            {
                FrozenEntry entry = _frozenEntries[i];
                if (entry.Player != null && entry.Player.BIsFrozen)
                {
                    entry.Player.Unfreeze();
                }
                DestroyIceBlock(entry);
            }
            _frozenEntries.Clear();

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
        /// 销毁单条冻结记录的冰块实例（场景销毁时可能已随之销毁，判空后兜底）
        /// </summary>
        private static void DestroyIceBlock(FrozenEntry entry)
        {
            if (entry.IceBlock != null)
            {
                Destroy(entry.IceBlock);
            }
        }

        // ==================== 音效 ====================

        /// <summary>
        /// 播放冰块碎裂音效：经 AudioSource.PlayClipAtPoint 自管理临时音源，Clip 为空时静默
        /// </summary>
        private void PlayCrackSfx()
        {
            if (_crackSfx == null)
            {
                return;
            }

            Vector3 position = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            AudioSource.PlayClipAtPoint(_crackSfx, position);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Text;
using SuperQQ.Audio;
using SuperQQ.Microphone;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 言出法随事件修饰符 — ScriptableObject 资产
    /// 事件被选中后：在场景固定位置创建一个法阵，持续整场游玩阶段
    /// 法阵触发范围由法阵 Prefab 上的 Trigger 碰撞体定义：玩家（Player 标签）进入范围时，
    /// 吟唱提示 Text 框显示在法阵旁的固定位置；范围内无存活玩家时提示隐藏
    /// 本地玩家进阵开启语音识别，识别结果与咒语列表（SpellDefinition）逐条做覆盖率匹配；
    /// 命中咒语且其配置了效果时，对触发玩家执行该咒语效果（如"无敌金身"护盾）
    /// 提示框为屏幕空间 UI，实例化到主 Canvas 下，位置 = 法阵位置 + 可配置偏移（随相机实时换算）
    /// 在 Project 窗口选中本资产时，Scene 视图会标注法阵位置与提示框偏移（均可拖拽调节）
    /// 所有策划参数均在本资产上配置；运行时状态（法阵实例、提示框实例）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "MagicCircleModifier", menuName = "SuperQQ/Event/Magic Circle Modifier")]
    public class MagicCircleModifier : LevelEventModifier, IServerDrivenRandomEvent3
    {
        [Header("法阵")]
        [Tooltip("法阵 Prefab（根节点需挂 MagicCircle 脚本与 Trigger 碰撞体，碰撞体形状即触发范围），激活时在固定位置实例化")]
        [SerializeField] private MagicCircle _circlePrefab;

        [Tooltip("法阵在场景中的固定位置（世界坐标）")]
        [SerializeField] private Vector2 _circlePosition = Vector2.zero;

        [Header("吟唱提示")]
        [Tooltip("吟唱提示 Text 框 Prefab（屏幕空间 UI，根节点需挂 ChantPrompt，运行时实例化到主 Canvas 下）；留空则无提示 UI")]
        [SerializeField] private ChantPrompt _chantPromptPrefab;

        [Tooltip("提示文字内容（未配置咒语时的兜底显示）")]
        [SerializeField] private string _promptText = "请吟唱";

        [Tooltip("提示框相对法阵的世界坐标偏移（Scene 视图中可拖拽调节）")]
        [SerializeField] private Vector2 _promptOffset = new Vector2(0f, 1.5f);

        [Header("语音吟唱")]
        [Tooltip("本地玩家进入法阵时开启语音识别（吟唱内容经远端 ASR 识别为文本，显示在调试 HUD）")]
        [SerializeField] private bool _bEnableVoiceChant = true;

        [Tooltip("单次吟唱的录音识别时长（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _chantDurationSeconds = 5f;

        [Tooltip("咒语列表：每次语音识别的最终结果会与其中每一条咒语依次做匹配判定，命中后执行咒语效果")]
        [SerializeField] private SpellDefinition[] _spells;

        [Tooltip("咒语匹配阈值（0~1）：咒语中占比超过该值的字符能在识别结果中找到即视为匹配，如 0.75 表示 75%")]
        [Range(0f, 1f)]
        [SerializeField] private float _spellMatchThreshold = 0.75f;

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 法阵实例，Deactivate 时销毁
        private MagicCircle _circleInstance;

        // 当前处于法阵触发范围内的玩家（由法阵进出事件维护）
        private readonly HashSet<PlayerController> _playersInside = new();

        // 吟唱提示实例（整场一个，Deactivate 时销毁）
        private ChantPrompt _promptInstance;

        // 提示框的宿主 Canvas（主 Canvas，Activate 时解析）
        private RectTransform _promptCanvasRect;

        // 世界坐标转屏幕坐标所用相机（Activate 时缓存）
        private Camera _camera;

        // 事件上下文（Activate 时缓存，执行咒语效果时构造 SpellEffectContext 用）
        private LevelEventContext _eventContext;

        // 触发本次语音识别的本地玩家（识别完成时作为咒语效果目标）
        private PlayerController _chantingPlayer;

        // 最新一次语音识别的最终结果（变量单存，不分成数组）
        private string _lastRecognizedText = "";

        // 最新一次识别匹配到的咒语定义（无匹配时为 null）
        private SpellDefinition _lastMatchedSpell;

        // 当前生效中的咒语效果实例（Deactivate 时统一结束清理）
        private readonly List<SpellEffectInstance> _activeEffects = new();

        // GUI 匹配结果的展示截止时刻（Time.unscaledTime）
        private float _matchHudExpireTime;

        // GUI 匹配结果驻留时长（秒）
        private const float MATCH_HUD_DURATION = 8f;

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：在固定位置创建法阵与吟唱提示（初始隐藏），订阅法阵进出事件与玩家状态事件
        /// </summary>
        public override void Activate(LevelEventContext context)
        {
            if (_circlePrefab == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 法阵 Prefab 未配置，事件不生效。");
                return;
            }

            if (_chantPromptPrefab == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 吟唱提示 Prefab 未配置，法阵将无提示 UI。");
            }

            _eventContext = context;

            Transform parent = context != null ? context.SceneRoot : null;
            _circleInstance = Instantiate(_circlePrefab, _circlePosition, Quaternion.identity, parent);
            _circleInstance.OnPlayerEntered += HandlePlayerEntered;
            _circleInstance.OnPlayerExited += HandlePlayerExited;

            CreatePrompt();

            if (_bEnableVoiceChant)
            {
                VoiceChantRecognizer.EnsureExists().OnChantRecognized += HandleChantRecognized;
            }

            if (LevelPlayerRegistry.Instance != null)
            {
                // 状态变化驱动提示的显示/隐藏（如范围内玩家全部死亡时隐藏）
                LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                // 玩家化身销毁时清理其残留记录
                LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
            }

            // 咒语效果的每帧驱动（如飞行音效）已由效果实例内置驱动器自治，无需事件协程驱动
        }

        /// <summary>
        /// 停用事件：退订全部事件，销毁法阵与吟唱提示，清空运行时状态
        /// </summary>
        public override void Deactivate(LevelEventContext context)
        {
            if (_circleInstance != null)
            {
                _circleInstance.OnPlayerEntered -= HandlePlayerEntered;
                _circleInstance.OnPlayerExited -= HandlePlayerExited;
                // 场景正常销毁时法阵可能已随之销毁，此处判空后兜底销毁
                Destroy(_circleInstance.gameObject);
                _circleInstance = null;
            }

            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
                LevelPlayerRegistry.Instance.OnPlayersChanged -= HandlePlayersChanged;
            }

            if (_promptInstance != null)
            {
                Destroy(_promptInstance.gameObject);
                _promptInstance = null;
            }

            if (_bEnableVoiceChant && VoiceChantRecognizer.Instance != null)
            {
                VoiceChantRecognizer.Instance.OnChantRecognized -= HandleChantRecognized;
            }

            // 统一结束所有生效中的咒语效果（计时/订阅/特效/驱动器由各实例自行清理）
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                _activeEffects[i]?.End();
            }
            _activeEffects.Clear();

            // 清理服务端同步驱动的咒语表现（如雷公助我的施法者雷光）
            if (_spells != null)
            {
                for (int i = 0; i < _spells.Length; i++)
                {
                    SpellDefinition spell = _spells[i];
                    if (spell != null && spell.Subtype > 0 && spell.Effect != null)
                    {
                        spell.Effect.EndServerDrivenEffects();
                    }
                }
            }

            _playersInside.Clear();
            _promptCanvasRect = null;
            _camera = null;
            _eventContext = null;
            _chantingPlayer = null;
            _lastRecognizedText = "";
            _lastMatchedSpell = null;
            _matchHudExpireTime = 0f;
        }

        // ==================== 事件响应 ====================

        /// <summary>
        /// 玩家进入法阵范围：记录到场并刷新提示显隐；本地玩家进阵时开启语音吟唱识别
        /// </summary>
        private void HandlePlayerEntered(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Add(player);
            RefreshPromptVisibility();

            // 语音吟唱：仅本地玩家触发；成功开启识别时记录触发玩家（识别进行中重复进入会被忽略且不覆盖记录）
            if (_bEnableVoiceChant && player.BIsLocal
                && VoiceChantRecognizer.EnsureExists().StartChantCapture(_chantDurationSeconds))
            {
                _chantingPlayer = player;
            }
        }

        /// <summary>
        /// 吟唱识别完成：存储最新识别结果，与咒语列表逐条做覆盖率匹配，
        /// 命中且配置了效果的咒语对触发玩家执行效果，打印匹配结果
        /// </summary>
        private void HandleChantRecognized(string recognizedText)
        {
            _lastRecognizedText = recognizedText ?? "";

            _lastMatchedSpell = FindBestMatchedSpell(_lastRecognizedText, out float bestCoverage);
            _matchHudExpireTime = Time.unscaledTime + MATCH_HUD_DURATION;

            if (_lastMatchedSpell != null)
            {
                Debug.Log($"[MagicCircleModifier] 吟唱匹配：识别\"{_lastRecognizedText}\" 命中咒语\"{_lastMatchedSpell.DisplayName}\"（覆盖率 {bestCoverage:P0}，阈值 {_spellMatchThreshold:P0}）");
                TryActivateSpellEffect(_lastMatchedSpell);
            }
            else
            {
                Debug.Log($"[MagicCircleModifier] 吟唱匹配：识别\"{_lastRecognizedText}\" 无匹配咒语（最高覆盖率 {bestCoverage:P0}，阈值 {_spellMatchThreshold:P0}）");
            }

            _chantingPlayer = null;
        }

        /// <summary>
        /// 执行命中咒语的效果：目标为触发本次识别的本地玩家
        /// 未配置效果（纯匹配咒语）或触发玩家已不在场（死亡/通关/销毁）时跳过执行，仅保留匹配结果
        /// </summary>
        private void TryActivateSpellEffect(SpellDefinition spell)
        {
            if (spell.Effect == null)
            {
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            PlayerController target = _chantingPlayer;
            if (registry == null || target == null || !IsRegistered(registry, target))
            {
                Debug.LogWarning($"[MagicCircleModifier] 咒语\"{spell.DisplayName}\"命中但触发玩家已离场，效果不执行。");
                return;
            }

            PlayerStateType state = registry.GetPlayerState(target);
            if (state != PlayerStateType.Alive && state != PlayerStateType.Frozen)
            {
                Debug.LogWarning($"[MagicCircleModifier] 咒语\"{spell.DisplayName}\"命中但触发玩家已出局（{state}），效果不执行。");
                return;
            }

            // 联机：上报本地玩家选择的咒语子类型（服务端据此经快照 event3_states 下发事件状态；离线为空操作）
            if (spell.Subtype > 0)
            {
                SuperQQ.Network.NetEventSync.ReportEvent3Subtype(spell.Subtype);
            }

            SpellEffectContext context = new SpellEffectContext(
                target,
                _eventContext != null ? _eventContext.CoroutineRunner : null,
                _eventContext != null ? _eventContext.SceneRoot : null);

            SpellEffectInstance instance = spell.Effect.Activate(context);
            if (instance != null)
            {
                _activeEffects.Add(instance);
            }
        }

        // ==================== 联机：服务端事件3状态同步 ====================

        /// <summary>
        /// 服务端事件3玩家状态下发（LevelEventAnnouncer 路由，快照全量重复下发）：
        /// 转发给配置了联机子类型的咒语效果（如雷公助我），各效果内部自行幂等（边沿触发/去重）
        /// </summary>
        public void ApplyServerEvent3States(System.Collections.Generic.IDictionary<string, Minigame.Room.V1.Event3PlayerState> states)
        {
            if (_spells == null || states == null)
            {
                return;
            }

            for (int i = 0; i < _spells.Length; i++)
            {
                SpellDefinition spell = _spells[i];
                if (spell != null && spell.Subtype > 0 && spell.Effect != null)
                {
                    spell.Effect.ApplyServerEvent3States(states, _eventContext);
                }
            }
        }

        /// <summary>
        /// 在咒语列表中查找与识别结果覆盖率最高的咒语：
        /// 逐条计算咒语文本字符在识别结果中的覆盖占比，超过阈值的最高者返回；
        /// 无任何超过阈值的咒语时返回 null（bestCoverage 输出所有咒语中的最高覆盖率）
        /// </summary>
        private SpellDefinition FindBestMatchedSpell(string recognizedText, out float bestCoverage)
        {
            bestCoverage = 0f;
            SpellDefinition bestSpell = null;

            string normalizedRecognized = NormalizeChantText(recognizedText);
            if (_spells == null || normalizedRecognized.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < _spells.Length; i++)
            {
                SpellDefinition spell = _spells[i];
                if (spell == null)
                {
                    continue;
                }

                string normalizedSpell = NormalizeChantText(spell.SpellText);
                if (normalizedSpell.Length == 0)
                {
                    continue;
                }

                float coverage = ComputeSpellCoverage(normalizedSpell, normalizedRecognized);
                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    if (coverage >= _spellMatchThreshold)
                    {
                        bestSpell = spell;
                    }
                }
            }
            return bestSpell;
        }

        /// <summary>
        /// 计算咒语字符覆盖率：咒语中能在识别结果里找到的字符数占咒语总长度的比例
        /// 字符消耗式匹配——识别结果中的同一字符不会重复计数（如咒语含两个"糕"需识别结果也有两个"糕"）
        /// 双方均已归一化（忽略大小写/空白/标点）
        /// </summary>
        private static float ComputeSpellCoverage(string normalizedSpell, string normalizedRecognized)
        {
            // 消耗式匹配：复制识别结果为字符列表，命中一个删一个
            var pool = new List<char>(normalizedRecognized);
            int hitCount = 0;
            foreach (char c in normalizedSpell)
            {
                if (pool.Remove(c))
                {
                    hitCount++;
                }
            }
            return (float)hitCount / normalizedSpell.Length;
        }

        /// <summary>
        /// 吟唱文本归一化：去除空白与标点、统一小写，消除 ASR 标点差异对比对的影响
        /// </summary>
        private static string NormalizeChantText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    continue;
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 玩家离开法阵范围：移除在场记录并刷新提示显隐
        /// </summary>
        private void HandlePlayerExited(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Remove(player);
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 玩家状态变化：刷新提示显隐（范围内玩家全部出局时隐藏，恢复存活时重新显示）
        /// </summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
        {
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 玩家集合变化（注册/注销）：清理已离场玩家的残留记录并刷新提示显隐
        /// </summary>
        private void HandlePlayersChanged()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            _playersInside.RemoveWhere(player => !IsRegistered(registry, player));
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 判断玩家是否仍注册在 Registry 中（未注销、未销毁）
        /// 房间制玩家数量少，线性查找开销可忽略
        /// </summary>
        private static bool IsRegistered(LevelPlayerRegistry registry, PlayerController player)
        {
            if (registry == null || player == null)
            {
                return false;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == player)
                {
                    return true;
                }
            }
            return false;
        }

        // ==================== 吟唱提示管理 ====================

        /// <summary>
        /// 创建吟唱提示实例：实例化到主 Canvas 下，跟随法阵固定位置，初始隐藏
        /// 未配置 Prefab 或未找到主 Canvas 时跳过（无提示 UI 降级）
        /// </summary>
        private void CreatePrompt()
        {
            if (_chantPromptPrefab == null)
            {
                return;
            }

            _promptCanvasRect = ResolvePromptCanvasRect();
            _camera = Camera.main;
            if (_promptCanvasRect == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 未找到主 Canvas，法阵将无提示 UI。");
                return;
            }

            _promptInstance = Instantiate(_chantPromptPrefab, _promptCanvasRect, false);
            _promptInstance.Initialize(_camera, _promptCanvasRect, _circleInstance.transform, _promptOffset);
            _promptInstance.SetText(_promptText);
            _promptInstance.gameObject.SetActive(false);
        }

        /// <summary>
        /// 刷新提示显隐：法阵范围内存在存活（Alive）玩家时显示，否则隐藏
        /// </summary>
        private void RefreshPromptVisibility()
        {
            if (_promptInstance == null)
            {
                return;
            }

            _promptInstance.gameObject.SetActive(HasAlivePlayerInside());
        }

        /// <summary>
        /// 法阵范围内是否存在存活玩家
        /// </summary>
        private bool HasAlivePlayerInside()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return false;
            }

            foreach (PlayerController player in _playersInside)
            {
                if (player != null && registry.GetPlayerState(player) == PlayerStateType.Alive)
                {
                    return true;
                }
            }
            return false;
        }

        // ==================== 匹配结果 GUI 显示 ====================

        private GUIStyle _matchHudStyle;

        /// <summary>
        /// 匹配结果 HUD：识别完成后驻留显示——命中咒语时以醒目绿色显示咒语，否则显示"无匹配咒语"
        /// </summary>
        private void OnGUI()
        {
            if (Time.unscaledTime >= _matchHudExpireTime)
            {
                return;
            }

            if (_matchHudStyle == null)
            {
                _matchHudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 56,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            bool bMatched = _lastMatchedSpell != null;
            _matchHudStyle.normal.textColor = bMatched ? Color.green : Color.red;

            string display = bMatched ? $"匹配咒语：{_lastMatchedSpell.DisplayName}" : "无匹配咒语";
            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 80f), display, _matchHudStyle);
        }

        /// <summary>
        /// 解析提示框的宿主 Canvas：优先玩家名称标签管理器所在的 Canvas（玩家头顶 UI 的既有宿主），
        /// 其次 PopupManager 所在的 Canvas；均未找到时返回 null
        /// </summary>
        private static RectTransform ResolvePromptCanvasRect()
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
    }
}

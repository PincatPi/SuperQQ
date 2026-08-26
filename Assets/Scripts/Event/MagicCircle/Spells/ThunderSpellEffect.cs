using System.Collections;
using System.Collections.Generic;
using System.Text;
using Minigame.Room.V1;
using SuperQQ.Microphone;
using SuperQQ.Network;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Event
{
    /// <summary>
    /// 雷公助我（雷电）咒语效果 — ScriptableObject 资产
    /// 触发时在目标玩家身上挂载雷电特效（作为玩家子节点随其移动），效果持续配置时长
    /// 攻击循环（分贝检测 → 落雷预警 → 雷击伤害）：
    ///   效果开始经过首轮延迟后执行第一轮检测——当前分贝超过阈值的玩家位置被记录，
    ///   在该位置生成落雷预警 Prefab；预警结束后在该位置播放雷电 Prefab 并伤害范围内的所有玩家；
    ///   之后每隔配置间隔重复一轮，直至持续时间结束
    /// 施法者本人大声说话同样会被记录位置，走进自己招来的雷击范围也会受伤
    /// 效果期间玩家死亡/通关/化身销毁时提前结束（冻结不移除）；退场时已发出的预警与雷电照常收尾
    ///
    /// 联机（服务端驱动）模式：吟唱命中后由 MagicCircleModifier 上报子类型（report_event3_subtype），
    /// 之后本地不跑攻击循环，全部由服务端快照 RoomSnapshot.event3_states 驱动——
    ///   subtype==3 的玩家各端挂载施法者雷光；detect_voice=true 时本机检测音量并上报超标
    ///   （report_event3_loud_player）；strike 边沿按 loud_players 列表执行 预警→落雷→伤害。
    ///   时间线（检测/劈/轮次）由服务端算好随快照下发，客户端只读值做表现
    /// </summary>
    [CreateAssetMenu(fileName = "ThunderSpellEffect", menuName = "SuperQQ/Event/Spells/Thunder Spell Effect")]
    public class ThunderSpellEffect : SpellEffect
    {
        [Header("施法者特效")]
        [Tooltip("雷电特效 Prefab（挂载为玩家子节点）；留空则只计时无视觉")]
        [SerializeField] private GameObject _thunderPrefab;

        [Tooltip("雷电特效在玩家本地坐标系下的位置偏移")]
        [SerializeField] private Vector2 _thunderOffset = Vector2.zero;

        [Header("持续与时序")]
        [Tooltip("雷电效果总持续时长（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _duration = 6f;

        [Tooltip("效果开始后到第一轮分贝检测的延迟（秒）")]
        [Min(0f)]
        [SerializeField] private float _firstDetectDelay = 1f;

        [Tooltip("相邻两轮分贝检测的间隔（秒）")]
        [Min(0.1f)]
        [SerializeField] private float _roundInterval = 2f;

        [Tooltip("落雷预警时长（秒）：预警生成到雷击落下之间的时间，供玩家躲避")]
        [Min(0.1f)]
        [SerializeField] private float _warningDuration = 1.5f;

        [Header("分贝检测")]
        [Tooltip("触发落雷的分贝阈值（0~1 归一化音量）：超过该值的玩家位置会被记录")]
        [Range(0f, 1f)]
        [SerializeField] private float _volumeThreshold = 0.5f;

        [Header("雷击表现与判定")]
        [Tooltip("落雷预警 Prefab（如地面红光/阴影）：生成于被记录位置，雷击落下时销毁；留空则无预警视觉")]
        [SerializeField] private GameObject _warningPrefab;

        [Tooltip("雷电 Prefab（雷击视觉/音效）：雷击时刻生成于被记录位置，短暂存在后自动销毁；留空则无雷电视觉")]
        [SerializeField] private GameObject _lightningPrefab;

        [Tooltip("雷电视觉的自动销毁时长（秒）")]
        [Min(0.1f)]
        [SerializeField] private float _lightningLifetime = 1f;

        [Tooltip("雷电伤害半径（世界单位）：雷击时刻以落点为中心的范围判定，命中即死（无敌金身可免疫）")]
        [Min(0.1f)]
        [SerializeField] private float _strikeRadius = 1.5f;

        [Header("Tips")]
        [Tooltip("效果生效时播放的 Tips 文本内容（经 PopupManager 播放，留空则不播放）")]
        [SerializeField] private string _activateTipText = "雷公助我！";

        [Tooltip("服务端驱动模式：检测声音窗口开启时播放的 Tips 文本（提示玩家发声会引来落雷，留空则不播放）")]
        [SerializeField] private string _detectVoiceTipText = "雷电正在聆听——大声说话会引来落雷！";

        [Tooltip("服务端驱动模式：施法者雷光在最后一次检测/劈活动后保留的宽限秒数（需大于轮回间隙，超过后视为效果结束并移除雷光）")]
        [Min(0.5f)]
        [SerializeField] private float _casterFxIdleTimeout = 2.5f;

        [Header("临时测试（服务端联调完成后删除）")]
        [Tooltip("【临时测试】勾选后走纯客户端本地测试：每轮检测不判断分贝，直接以本地玩家在检测时刻所在位置为落点执行 预警→雷击；联机下也不会走服务端驱动逻辑")]
        [SerializeField] private bool _bTestStrikeLocalPlayer = false;

        /// <summary>
        /// 激活雷电效果：在目标玩家身上挂载特效并启动攻击循环
        /// </summary>
        protected override SpellEffectInstance OnActivate(SpellEffectContext context)
        {
            if (context == null || context.Target == null)
            {
                Debug.LogWarning("[ThunderSpellEffect] 上下文或目标玩家为空，效果不生效。");
                return null;
            }

            if (_thunderPrefab == null)
            {
                Debug.LogWarning("[ThunderSpellEffect] 雷电特效 Prefab 未配置，本次仅有计时无视觉表现。");
            }

            ShowActivateTip();

            // 联机（服务端正式流程）：子类型已由 MagicCircleModifier 上报，
            // 施法者雷光/声音检测/预警/落雷全部由服务端快照 event3_states 驱动（ApplyServerEvent3States），
            // 本地不创建攻击循环实例，各端读值做表现即可
            // 【临时测试】勾选测试开关时跳过服务端驱动，强制走本地攻击循环
            if (BServerDriven && !_bTestStrikeLocalPlayer)
            {
                PlayCastSfx(context.Target.transform.position);
                return null;
            }

            return new ThunderInstance(context, this);
        }

        /// <summary>
        /// 服务端驱动模式判定：已连接且在房间内，且未开启"客户端本地触发"临时测试开关
        /// （测试开关开启时事件由客户端本地掷签触发，咒语走纯本地逻辑）
        /// </summary>
        private static bool BServerDriven
        {
            get
            {
                LevelEventAnnouncer announcer = LevelEventAnnouncer.Instance;
                if (announcer != null && announcer.BTempClientLocalTrigger)
                {
                    return false;
                }

                NetworkManager net = NetworkManager.Instance;
                return net != null && net.IsConnected && !string.IsNullOrEmpty(net.RoomId);
            }
        }

        /// <summary>
        /// 播放效果生效 Tips（通用 Tips 类型，自动关闭时长用注册表默认）；
        /// 文本未配置或 PopupManager 缺失时静默跳过
        /// </summary>
        private void ShowActivateTip()
        {
            if (string.IsNullOrEmpty(_activateTipText))
            {
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[ThunderSpellEffect] PopupManager 不存在，跳过生效 Tips 播放。");
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _activateTipText);
        }

        /// <summary>
        /// 播放"检测声音"提示 Tips（服务端驱动模式：detect_voice 窗口开启时提示玩家发声会引来落雷）
        /// </summary>
        private void ShowDetectVoiceTip()
        {
            if (string.IsNullOrEmpty(_detectVoiceTipText) || PopupManager.Instance == null)
            {
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _detectVoiceTipText);
        }

        // ==================== 单机/联机共用的落雷流程 ====================

        /// <summary>
        /// 单个落点的预警→雷击流程：生成预警，等待预警时长，销毁预警并落雷+范围伤害
        /// </summary>
        /// <param name="position">落点（世界坐标）</param>
        /// <param name="sceneRoot">预警/雷电视觉的父节点（可为 null）</param>
        /// <param name="bLocalOnly">true（联机服务端驱动）时只杀伤本机玩家：各端本地权威自己的生死</param>
        private IEnumerator StrikeRoutine(Vector2 position, Transform sceneRoot, bool bLocalOnly)
        {
            GameObject warning = null;
            if (_warningPrefab != null)
            {
                warning = Instantiate(_warningPrefab, position, Quaternion.identity, sceneRoot);
            }

            yield return new WaitForSeconds(_warningDuration);

            if (warning != null)
            {
                Destroy(warning);
            }

            if (_lightningPrefab != null)
            {
                GameObject lightning = Instantiate(_lightningPrefab, position, Quaternion.identity, sceneRoot);
                Destroy(lightning, _lightningLifetime);
            }

            DamagePlayersInRange(position, bLocalOnly);
        }

        /// <summary>
        /// 雷击伤害：以落点为中心做范围判定，命中仍在场（存活/冻结）的玩家即死
        /// 走 PlayerDie——无敌金身可免疫，掉落出界等强制死亡不受影响
        /// bLocalOnly=true（联机服务端驱动）时跳过远端化身：远端玩家的生死由其所属端本地判定，
        /// 各端跑同一套服务端时间线，落点一致，各自结算自己的本地玩家即可
        /// </summary>
        private void DamagePlayersInRange(Vector2 position, bool bLocalOnly)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            float sqrRadius = _strikeRadius * _strikeRadius;
            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                if (bLocalOnly && !player.BIsLocal)
                {
                    continue;
                }

                PlayerStateType state = registry.GetPlayerState(player);
                if (state != PlayerStateType.Alive && state != PlayerStateType.Frozen)
                {
                    continue;
                }

                if (((Vector2)player.transform.position - position).sqrMagnitude <= sqrRadius)
                {
                    player.PlayerDie();
                }
            }
        }

        /// <summary>按网络 playerId 在场景中查找玩家化身（本地/远端均可）</summary>
        private static PlayerController FindPlayerById(string playerId)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null || string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].PlayerId == playerId)
                {
                    return players[i];
                }
            }
            return null;
        }

        /// <summary>是否存在仍在场（存活/冻结）的本机玩家</summary>
        private static bool HasAliveLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return false;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player != null && player.BIsLocal)
                {
                    PlayerStateType state = registry.GetPlayerState(player);
                    if (state == PlayerStateType.Alive || state == PlayerStateType.Frozen)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ==================== 服务端驱动模式（联机） ====================

        // 与服务端 Event3 子类型约定：雷公助我 = 3（SpellDefinition_Thunder 资产同步配置 _subtype=3）
        private const int THUNDER_SUBTYPE = 3;

        // 服务端驱动运行时（非序列化）：事件期间由快照驱动，事件结束/状态清空时清理
        private ServerDrivenRuntime _serverRuntime;

        /// <summary>
        /// 联机：应用服务端下发的事件3玩家状态（每次快照到达都可能调用，全量重复下发，内部边沿触发幂等）。
        /// 客户端只读值做表现，不算时间：
        ///   subtype==3 的玩家：各端为其化身挂载施法者雷光，状态消失时移除；
        ///   detect_voice=true：提示检测声音，本机音量超标时上报 report_event3_loud_player（每检测窗口至多一次）；
        ///   strike 边沿（false→true）：按 loud_players 列表在对应玩家当前位置执行 预警→落雷→范围伤害
        /// </summary>
        public override void ApplyServerEvent3States(IDictionary<string, Event3PlayerState> states, LevelEventContext eventContext)
        {
            // 【临时测试】测试开关开启时忽略服务端状态下发（纯本地测试，联调完成后随开关一并删除）
            if (states == null || _bTestStrikeLocalPlayer)
            {
                return;
            }

            if (_serverRuntime == null)
            {
                Debug.Log("[ThunderSpellEffect] 服务端驱动运行时启动（首个 event3_states 包到达本效果）");
                _serverRuntime = new ServerDrivenRuntime(this);
            }
            _serverRuntime.Apply(states, eventContext);
        }

        /// <summary>
        /// 联机：事件结束，清理服务端驱动表现（施法者雷光等）；已发出的预警/落雷协程照常收尾
        /// </summary>
        public override void EndServerDrivenEffects()
        {
            _serverRuntime?.Clear();
            _serverRuntime = null;
        }

        /// <summary>
        /// 服务端驱动运行时：按快照 event3_states 驱动施法者雷光挂载、声音检测上报与落雷表现。
        /// 快照全量重复下发，所有触发均为边沿/去重语义，重复 Apply 幂等
        /// </summary>
        private sealed class ServerDrivenRuntime
        {
            private readonly ThunderSpellEffect _config;

            // 施法者 playerId -> 已挂载的雷光特效
            private readonly Dictionary<string, GameObject> _casterFx = new();

            // 施法者 playerId -> 自己条目最后一次活动（detect_voice/strike 任一为 true）的本地时刻：
            // 服务端按协议把 detect/strike 挂在 subtype=3 条目上时，按此精确判定到个人（多施法者互不影响）。
            // 雷光移除时保留本表记录：避免效果已结束的施法者在他人施法期间被重新挂载
            private readonly Dictionary<string, float> _casterLastActivity = new();

            // 任一条目最后一次活动的本地时刻（兜底：服务端把活动信号挂在其他玩家条目上时，
            // 施法者自己条目永远没有活动，退化为按全局活动判定）
            private float _lastAnyActivity;
            private bool _bSeenAnyActivity;

            // 施法者 playerId -> 上一帧快照的 strike 值（边沿触发用）
            private readonly Dictionary<string, bool> _prevStrike = new();

            // 移除用的临时缓存（避免遍历时修改集合）
            private readonly List<string> _removeCache = new();

            // 本检测窗口是否已上报过音量超标（detect_voice 变 false 时复位）
            private bool _bLoudReported;

            // 本检测窗口是否已播放过提示 Tips（detect_voice 变 false 时复位）
            private bool _bDetectTipShown;

            // ===== 联调埋点（分析用，联调完成后删除）=====
            private float _lastStatesDumpTime = -999f;  // 状态 dump 节流（1s）
            private float _lastVolumeLogTime = -999f;   // 音量状态日志节流（0.5s）
            private bool _bPrevDetectVoice;             // 检测窗口跳变日志用
            private bool _bLoggedCasterSkip;            // "施法者跳过检测"每窗口只打一次

            public ServerDrivenRuntime(ThunderSpellEffect config)
            {
                _config = config;
            }

            public void Apply(IDictionary<string, Event3PlayerState> states, LevelEventContext eventContext)
            {
                // 【联调埋点】每秒 dump 一次服务端下发的完整状态（分析 detect_voice/strike/loud_players 挂在哪些条目上）
                if (Time.time - _lastStatesDumpTime >= 1f)
                {
                    _lastStatesDumpTime = Time.time;
                    Debug.Log(BuildStatesDump(states));
                }

                // ① 施法者雷光：subtype==3 的玩家各端统一挂载，状态消失时移除
                SyncCasterFx(states);

                NetworkManager net = NetworkManager.Instance;
                string localPlayerId = net != null ? net.LocalPlayerId : null;

                // 本地玩家是否为施法者：施法者不参与声音检测、不会被标记为攻击目标
                // （需求：触发雷公助我的人不会被雷电击打，只有其余玩家音量过大才会被标记）
                bool bLocalIsCaster = IsCaster(states, localPlayerId);

                // ② 检测声音：任一条目处于检测窗口即开始本机检测（宽松判定——
                // 服务端可能把 detect_voice 挂在施法者条目或每个玩家自己的条目上，两种语义都兼容）
                bool bDetectVoice = false;
                foreach (KeyValuePair<string, Event3PlayerState> pair in states)
                {
                    Event3PlayerState state = pair.Value;
                    if (state != null && state.DetectVoice)
                    {
                        bDetectVoice = true;
                        break;
                    }
                }

                // 【联调埋点】检测窗口跳变（含本地是否施法者）
                if (bDetectVoice != _bPrevDetectVoice)
                {
                    _bPrevDetectVoice = bDetectVoice;
                    _bLoggedCasterSkip = false;
                    Debug.Log($"[ThunderSpellEffect] 检测窗口{(bDetectVoice ? "开启" : "关闭")}（bLocalIsCaster={bLocalIsCaster}）");
                }

                if (bDetectVoice && !bLocalIsCaster)
                {
                    if (!_bDetectTipShown)
                    {
                        _bDetectTipShown = true;
                        _config.ShowDetectVoiceTip();
                    }
                    TryReportLoud();
                }
                else if (!bDetectVoice)
                {
                    _bDetectTipShown = false;
                    _bLoudReported = false;
                }
                else if (!_bLoggedCasterSkip)
                {
                    // 【联调埋点】bDetectVoice && bLocalIsCaster 分支：确认施法者跳过检测
                    _bLoggedCasterSkip = true;
                    Debug.Log("[ThunderSpellEffect] 本地玩家是施法者，本窗口跳过音量检测与上报");
                }

                // ③ 劈：strike 边沿（false→true）的条目收集 loud_players 并集（宽松判定，不限条目 subtype），
                // 剔除施法者后逐人落雷——施法者免疫，即使服务端误标也不会被劈
                HashSet<string> strikeTargets = null;
                bool bStrikeEdge = false; // 【联调埋点】本帧是否存在 strike 边沿
                foreach (KeyValuePair<string, Event3PlayerState> pair in states)
                {
                    Event3PlayerState state = pair.Value;
                    if (state == null)
                    {
                        continue;
                    }

                    bool bPrev = _prevStrike.TryGetValue(pair.Key, out bool prev) && prev;

                    // 【联调埋点】strike 跳变（含该条目 subtype 与 loud 列表原文）
                    if (state.Strike != bPrev)
                    {
                        Debug.Log($"[ThunderSpellEffect] 条目 {pair.Key}（subtype={state.Subtype}）strike: {bPrev}→{state.Strike}，loud=[{string.Join(",", state.LoudPlayers)}]");
                    }

                    if (state.Strike && !bPrev)
                    {
                        bStrikeEdge = true;
                    }

                    if (state.Strike && !bPrev && state.LoudPlayers != null && state.LoudPlayers.Count > 0)
                    {
                        for (int i = 0; i < state.LoudPlayers.Count; i++)
                        {
                            string loudId = state.LoudPlayers[i];
                            if (IsCaster(states, loudId))
                            {
                                Debug.Log($"[ThunderSpellEffect] loud 目标 {loudId} 是施法者，已剔除");
                                continue;
                            }
                            strikeTargets ??= new HashSet<string>();
                            strikeTargets.Add(loudId);
                        }
                    }
                    _prevStrike[pair.Key] = state.Strike;
                }

                // 【联调埋点】strike 边沿的最终落雷目标
                if (bStrikeEdge)
                {
                    Debug.Log(strikeTargets != null
                        ? $"[ThunderSpellEffect] strike 边沿触发，落雷目标（已剔除施法者）: {string.Join(",", strikeTargets)}"
                        : "[ThunderSpellEffect] strike 边沿触发，但 loud_players 为空或全部为施法者，无落雷目标");
                }

                // 清理已消失条目的 strike 边沿记录
                if (_prevStrike.Count > 0)
                {
                    _removeCache.Clear();
                    foreach (KeyValuePair<string, bool> pair in _prevStrike)
                    {
                        if (!states.ContainsKey(pair.Key))
                        {
                            _removeCache.Add(pair.Key);
                        }
                    }
                    for (int i = 0; i < _removeCache.Count; i++)
                    {
                        _prevStrike.Remove(_removeCache[i]);
                    }
                }

                if (strikeTargets != null)
                {
                    StrikeLoudPlayers(strikeTargets, eventContext);
                }
            }

            /// <summary>该玩家是否为雷电施法者（subtype==3）</summary>
            private static bool IsCaster(IDictionary<string, Event3PlayerState> states, string playerId)
            {
                return !string.IsNullOrEmpty(playerId)
                    && states.TryGetValue(playerId, out Event3PlayerState state)
                    && state != null && state.Subtype == THUNDER_SUBTYPE;
            }

            /// <summary>【联调埋点】拼装 event3_states 全量 dump 文本</summary>
            private static string BuildStatesDump(IDictionary<string, Event3PlayerState> states)
            {
                NetworkManager net = NetworkManager.Instance;
                var sb = new StringBuilder($"[ThunderSpellEffect] event3_states dump（count={states.Count}）local={(net != null ? net.LocalPlayerId : "null")}");
                foreach (KeyValuePair<string, Event3PlayerState> pair in states)
                {
                    Event3PlayerState s = pair.Value;
                    sb.Append(s == null
                        ? $"\n  {pair.Key}: null"
                        : $"\n  {pair.Key}: subtype={s.Subtype} detect={s.DetectVoice} strike={s.Strike} remaining={s.RemainingMs} loud=[{string.Join(",", s.LoudPlayers)}]");
                }
                return sb.ToString();
            }

            /// <summary>清理服务端驱动表现：销毁全部施法者雷光并复位标记（已发出的预警/落雷协程照常收尾）</summary>
            public void Clear()
            {
                foreach (KeyValuePair<string, GameObject> pair in _casterFx)
                {
                    if (pair.Value != null)
                    {
                        Object.Destroy(pair.Value);
                    }
                }
                _casterFx.Clear();
                _casterLastActivity.Clear();
                _lastAnyActivity = 0f;
                _bSeenAnyActivity = false;
                _prevStrike.Clear();
                _bLoudReported = false;
                _bDetectTipShown = false;
            }

            /// <summary>
            /// 同步施法者雷光：为 subtype==3 的玩家化身挂载雷光特效（化身尚未生成时下一帧快照重试）；
            /// 子类型变更/状态清空/事件结束时移除，或活动（detect_voice/strike）停止超过宽限时长时
            /// 视为效果结束一并移除（服务端在效果结束后仍可能保留 subtype==3 条目）
            /// </summary>
            private void SyncCasterFx(IDictionary<string, Event3PlayerState> states)
            {
                // 记录活动时刻：detect_voice/strike 任一为 true 即视为效果存续中
                // （全局记录不限 subtype——宽松兼容服务端把活动信号挂在施法者条目或全员条目上）
                foreach (KeyValuePair<string, Event3PlayerState> pair in states)
                {
                    Event3PlayerState state = pair.Value;
                    if (state != null && (state.DetectVoice || state.Strike))
                    {
                        _lastAnyActivity = Time.time;
                        _bSeenAnyActivity = true;
                        if (state.Subtype == THUNDER_SUBTYPE)
                        {
                            _casterLastActivity[pair.Key] = Time.time;
                        }
                    }
                }

                if (_casterFx.Count > 0)
                {
                    _removeCache.Clear();
                    foreach (KeyValuePair<string, GameObject> pair in _casterFx)
                    {
                        // 【联调埋点】记录移除原因（判定语义与原逻辑完全一致，仅拆分条件便于打日志）
                        string removeReason = null;
                        if (!states.TryGetValue(pair.Key, out Event3PlayerState state)
                            || state == null || state.Subtype != THUNDER_SUBTYPE)
                        {
                            removeReason = "条目消失或 subtype 变更";
                        }
                        else if (IsEffectExpired(pair.Key))
                        {
                            removeReason = $"活动停止超 {_config._casterFxIdleTimeout:F1}s，判定效果结束";
                        }

                        if (removeReason != null)
                        {
                            _removeCache.Add(pair.Key);
                            Debug.Log($"[ThunderSpellEffect] 移除施法者 {pair.Key} 的雷光：{removeReason}");
                        }
                    }
                    for (int i = 0; i < _removeCache.Count; i++)
                    {
                        if (_casterFx[_removeCache[i]] != null)
                        {
                            Object.Destroy(_casterFx[_removeCache[i]]);
                        }
                        _casterFx.Remove(_removeCache[i]);
                        // 注意：不清除 _casterLastActivity 记录——保留"该施法者效果已结束"的判定依据，
                        // 避免其他玩家施法期间（全局有活动）已结束的施法者被重新挂载雷光
                    }
                }

                if (_config._thunderPrefab == null)
                {
                    return;
                }

                foreach (KeyValuePair<string, Event3PlayerState> pair in states)
                {
                    Event3PlayerState state = pair.Value;
                    if (state == null || state.Subtype != THUNDER_SUBTYPE || _casterFx.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    // 效果已结束（活动停止超宽限）的施法者不再重新挂载
                    if (IsEffectExpired(pair.Key))
                    {
                        continue;
                    }

                    PlayerController player = FindPlayerById(pair.Key);
                    if (player == null)
                    {
                        continue;
                    }

                    GameObject fx = Object.Instantiate(_config._thunderPrefab, player.transform);
                    fx.transform.localPosition = _config._thunderOffset;
                    _casterFx.Add(pair.Key, fx);
                    Debug.Log($"[ThunderSpellEffect] 为施法者 {pair.Key} 挂载雷光特效");
                }
            }

            /// <summary>
            /// 施法者效果是否已结束：见过活动（首轮检测已开始）且活动停止超过宽限时长。
            /// 优先按施法者自己条目的活动判定（精确到个人）；自己条目从未见过活动时按全局活动兜底
            /// （兼容服务端把 detect/strike 挂在其他玩家条目上的情况）。尚未见过任何活动时不判定过期
            /// </summary>
            private bool IsEffectExpired(string playerId)
            {
                if (_casterLastActivity.TryGetValue(playerId, out float ownLastActivity))
                {
                    return Time.time - ownLastActivity > _config._casterFxIdleTimeout;
                }

                return _bSeenAnyActivity && Time.time - _lastAnyActivity > _config._casterFxIdleTimeout;
            }

            /// <summary>检测窗口内本机音量超标则上报（每窗口至多一次；本机玩家不在场或麦克风未运行时跳过）。
            /// 仅非施法者客户端会调用——施法者不会被标记为攻击目标，不参与上报</summary>
            private void TryReportLoud()
            {
                // 防御性确保开麦：非施法者从不吟唱，若该端麦克风因权限/启动时序等原因未在采集，
                // Volume 恒 0 会导致永远上报不出去（StartMic 幂等，内部含权限请求与失败自动重试）
                MicVolumeManager mic = MicVolumeManager.EnsureExists();
                if (!mic.IsRunning)
                {
                    mic.StartMic();
                }
                float volume = mic.Volume;

                // 【联调埋点】检测窗口内每 0.5s 打一次音量状态（分析为何未上报）
                if (Time.time - _lastVolumeLogTime >= 0.5f)
                {
                    _lastVolumeLogTime = Time.time;
                    Debug.Log($"[ThunderSpellEffect] 检测窗口中：mic={(mic.IsRunning ? "running" : "stopped")} volume={volume:F2}/阈值{_config._volumeThreshold:F2} aliveLocal={HasAliveLocalPlayer()} 本窗口已上报={_bLoudReported}");
                }

                if (_bLoudReported)
                {
                    return;
                }

                if (!mic.IsRunning || mic.Volume < _config._volumeThreshold)
                {
                    return;
                }

                if (!HasAliveLocalPlayer())
                {
                    return;
                }

                _bLoudReported = true;
                NetEventSync.ReportEvent3LoudPlayer();
                Debug.Log($"[ThunderSpellEffect] 音量超标（{volume:F2} ≥ {_config._volumeThreshold:F2}），已上报 report_event3_loud_player");
            }

            /// <summary>
            /// strike 边沿：按音量超标玩家列表（已剔除施法者）在其当前位置启动 预警→落雷→范围伤害
            /// （与单机版同一套流程与配置）。落点取边沿时刻的玩家位置，预警期间玩家仍可躲避
            /// </summary>
            private void StrikeLoudPlayers(IEnumerable<string> loudPlayers, LevelEventContext eventContext)
            {
                if (loudPlayers == null)
                {
                    return;
                }

                MonoBehaviour runner = eventContext != null ? eventContext.CoroutineRunner : null;
                if (runner == null)
                {
                    Debug.LogWarning("[ThunderSpellEffect] 服务端驱动落雷失败：无协程宿主。");
                    return;
                }

                foreach (string loudId in loudPlayers)
                {
                    PlayerController player = FindPlayerById(loudId);
                    if (player == null)
                    {
                        Debug.LogWarning($"[ThunderSpellEffect] 落雷目标 {loudId} 在 LevelPlayerRegistry 中找不到化身，跳过");
                        continue;
                    }

                    Vector2 position = player.transform.position;
                    runner.StartCoroutine(_config.StrikeRoutine(position, eventContext.SceneRoot, true));
                    Debug.Log($"[ThunderSpellEffect] 服务端驱动落雷：目标={loudId} 落点=({position.x:F1},{position.y:F1})");
                }
            }
        }

        /// <summary>
        /// 雷电效果运行时实例：挂特效、跑攻击循环（检测→预警→雷击）、监听目标玩家状态以提前结束
        /// </summary>
        private sealed class ThunderInstance : SpellEffectInstance
        {
            // 配置引用（来自 SO，实例不持有可变副本）
            private readonly ThunderSpellEffect _config;

            private GameObject _thunderInstance;
            private Coroutine _expireCoroutine;
            private Coroutine _attackCoroutine;

            public ThunderInstance(SpellEffectContext context, ThunderSpellEffect config) : base(context)
            {
                _config = config;

                if (config._thunderPrefab != null)
                {
                    _thunderInstance = Instantiate(config._thunderPrefab, Target.transform);
                    _thunderInstance.transform.localPosition = config._thunderOffset;
                }

                // 订阅玩家状态：目标玩家死亡/通关/化身销毁时提前结束
                if (LevelPlayerRegistry.Instance != null)
                {
                    LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                    LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
                }

                if (Runner != null)
                {
                    _expireCoroutine = Runner.StartCoroutine(ExpireRoutine(config._duration));
                    _attackCoroutine = Runner.StartCoroutine(AttackRoutine());
                }
            }

            /// <summary>
            /// 计时协程：持续时长到期后自动结束
            /// </summary>
            private IEnumerator ExpireRoutine(float duration)
            {
                yield return new WaitForSeconds(duration);
                End();
            }

            // ==================== 攻击循环 ====================

            /// <summary>
            /// 攻击循环：首轮延迟后按间隔逐轮执行 分贝检测 → 落雷预警 → 雷击伤害，
            /// 直到效果结束（End 时协程被停止，本轮未完成的检测不再执行）
            /// </summary>
            private IEnumerator AttackRoutine()
            {
                yield return new WaitForSeconds(_config._firstDetectDelay);

                while (BIsActive)
                {
                    RunDetectRound();
                    yield return new WaitForSeconds(_config._roundInterval);
                }
            }

            /// <summary>
            /// 执行一轮检测：找出分贝超阈值的玩家并记录其当前位置，逐位置启动预警→雷击流程
            /// （落雷流程与联机服务端驱动模式共用配置层实现）
            /// </summary>
            private void RunDetectRound()
            {
                // 【临时测试】测试开关开启时：不判断分贝，直接以本地玩家当前位置为落点
                if (_config._bTestStrikeLocalPlayer)
                {
                    StrikeAtLocalPlayer();
                    return;
                }

                List<PlayerController> loudPlayers = CollectLoudPlayers();
                for (int i = 0; i < loudPlayers.Count; i++)
                {
                    Vector2 position = loudPlayers[i].transform.position;
                    if (Runner != null)
                    {
                        Runner.StartCoroutine(_config.StrikeRoutine(position, Context.SceneRoot, false));
                    }
                }
            }

            /// <summary>
            /// 【临时测试】以本地玩家在检测时刻的所在位置为落点执行 预警→雷击（不判断分贝）。
            /// 落点在检测时刻即锁定，预警期间走开即可躲避，用于纯客户端本地测试雷公助我效果
            /// </summary>
            private void StrikeAtLocalPlayer()
            {
                if (Runner == null)
                {
                    return;
                }

                LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
                if (registry == null)
                {
                    return;
                }

                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerController player = players[i];
                    if (player != null && player.BIsLocal
                        && registry.GetPlayerState(player) == PlayerStateType.Alive)
                    {
                        Vector2 position = player.transform.position;
                        Runner.StartCoroutine(_config.StrikeRoutine(position, Context.SceneRoot, false));
                        // 【临时测试】日志便于确认攻击循环在跑、落点在哪
                        Debug.Log($"[ThunderSpellEffect] 测试落雷：落点=({position.x:F1},{position.y:F1})，预警 {_config._warningDuration:F1}s 后雷击（预警Prefab={(_config._warningPrefab != null ? "已配置" : "未配置")} 雷电Prefab={(_config._lightningPrefab != null ? "已配置" : "未配置")}）");
                    }
                    else if (player != null && player.BIsLocal)
                    {
                        // 【临时测试】本地玩家存在但状态非 Alive 时明确提示（如未进入游玩阶段/被冻结）
                        Debug.LogWarning($"[ThunderSpellEffect] 测试落雷跳过：本地玩家状态为 {registry.GetPlayerState(player)}（需 Alive）");
                    }
                }
            }

            /// <summary>
            /// 收集当前分贝超阈值的玩家：
            /// 麦克风是设备级采集（MicVolumeManager），本地设备超阈值时视本机所有存活本地玩家为"大声者"
            /// （联机下远端玩家的分贝由其各自客户端判定——接入网络同步后替换本方法的采集来源即可）
            /// </summary>
            private List<PlayerController> CollectLoudPlayers()
            {
                var loudPlayers = new List<PlayerController>();

                MicVolumeManager mic = MicVolumeManager.Instance;
                if (mic == null || !mic.IsRunning || mic.Volume < _config._volumeThreshold)
                {
                    return loudPlayers;
                }

                LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
                if (registry == null)
                {
                    return loudPlayers;
                }

                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerController player = players[i];
                    if (player != null && player.BIsLocal
                        && registry.GetPlayerState(player) == PlayerStateType.Alive)
                    {
                        loudPlayers.Add(player);
                    }
                }
                return loudPlayers;
            }

            // ==================== 提前结束 ====================

            /// <summary>
            /// 玩家状态变化：目标玩家离开在场状态（Alive/Frozen）时提前结束
            /// </summary>
            private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
            {
                if (player != Target)
                {
                    return;
                }

                if (stateType != PlayerStateType.Alive && stateType != PlayerStateType.Frozen)
                {
                    End();
                }
            }

            /// <summary>
            /// 玩家集合变化：目标玩家化身已销毁或注销时提前结束
            /// </summary>
            private void HandlePlayersChanged()
            {
                if (Target == null)
                {
                    End();
                    return;
                }

                LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
                if (registry == null)
                {
                    return;
                }

                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] == Target)
                    {
                        return;
                    }
                }
                End();
            }

            protected override void OnEnd()
            {
                if (LevelPlayerRegistry.Instance != null)
                {
                    LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
                    LevelPlayerRegistry.Instance.OnPlayersChanged -= HandlePlayersChanged;
                }

                if (Runner != null)
                {
                    if (_expireCoroutine != null)
                    {
                        Runner.StopCoroutine(_expireCoroutine);
                        _expireCoroutine = null;
                    }
                    if (_attackCoroutine != null)
                    {
                        Runner.StopCoroutine(_attackCoroutine);
                        _attackCoroutine = null;
                    }
                }

                if (_thunderInstance != null)
                {
                    // 玩家化身销毁时特效作为子物体已随之销毁，此处判空后兜底销毁
                    Object.Destroy(_thunderInstance);
                    _thunderInstance = null;
                }
            }
        }
    }
}

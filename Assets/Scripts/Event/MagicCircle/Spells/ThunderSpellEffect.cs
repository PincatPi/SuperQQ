using System.Collections;
using System.Collections.Generic;
using SuperQQ.Microphone;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

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
            return new ThunderInstance(context, this);
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
            /// </summary>
            private void RunDetectRound()
            {
                List<PlayerController> loudPlayers = CollectLoudPlayers();
                for (int i = 0; i < loudPlayers.Count; i++)
                {
                    Vector2 position = loudPlayers[i].transform.position;
                    if (Runner != null)
                    {
                        Runner.StartCoroutine(StrikeRoutine(position));
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

            /// <summary>
            /// 单个落点的预警→雷击流程：生成预警，等待预警时长，销毁预警并落雷+范围伤害
            /// </summary>
            private IEnumerator StrikeRoutine(Vector2 position)
            {
                GameObject warning = null;
                if (_config._warningPrefab != null)
                {
                    Transform parent = Context.SceneRoot != null ? Context.SceneRoot : null;
                    warning = Instantiate(_config._warningPrefab, position, Quaternion.identity, parent);
                }

                yield return new WaitForSeconds(_config._warningDuration);

                if (warning != null)
                {
                    Object.Destroy(warning);
                }

                if (_config._lightningPrefab != null)
                {
                    Transform parent = Context.SceneRoot != null ? Context.SceneRoot : null;
                    GameObject lightning = Instantiate(_config._lightningPrefab, position, Quaternion.identity, parent);
                    Object.Destroy(lightning, _config._lightningLifetime);
                }

                DamagePlayersInRange(position);
            }

            /// <summary>
            /// 雷击伤害：以落点为中心做范围判定，命中仍在场（存活/冻结）的玩家即死
            /// 走 PlayerDie——无敌金身可免疫，掉落出界等强制死亡不受影响
            /// </summary>
            private void DamagePlayersInRange(Vector2 position)
            {
                LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
                if (registry == null)
                {
                    return;
                }

                float sqrRadius = _config._strikeRadius * _config._strikeRadius;
                IReadOnlyList<PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerController player = players[i];
                    if (player == null)
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

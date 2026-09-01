using System.Collections;
using System.Collections.Generic;
using SuperQQ.Audio;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 中国人能飞（飞行）咒语效果 — ScriptableObject 资产
    /// 触发时在目标玩家身上挂载飞行特效（作为玩家子节点随其移动），持续配置时长后自动移除；
    /// 效果期间玩家的跳跃逻辑替换为飞行：按住跳跃键持续向上飞行（封顶最大速度），
    /// 左右移动、击退、死亡等其余逻辑与普通状态一致；
    /// 玩家死亡/通关/化身销毁时特效提前移除（冻结不移除）
    /// </summary>
    [CreateAssetMenu(fileName = "FlightSpellEffect", menuName = "SuperQQ/Event/Spells/Flight Spell Effect")]
    public class FlightSpellEffect : SpellEffect
    {
        [Tooltip("飞行特效 Prefab（挂载为玩家子节点）；留空则只计时无视觉")]
        [SerializeField] private GameObject _flightPrefab;

        [Tooltip("飞行特效在玩家本地坐标系下的位置偏移")]
        [SerializeField] private Vector2 _flightOffset = Vector2.zero;

        [Tooltip("飞行持续时长（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _duration = 20f;

        [Tooltip("飞行最大上升速度（单位/秒）：按住跳跃键持续加速，封顶为该速度")]
        [Min(0.1f)]
        [SerializeField] private float _maxFlySpeed = 5f;

        [Tooltip("飞行上升加速度（单位/秒²）：按住跳跃键时的上升提速快慢")]
        [Min(0.1f)]
        [SerializeField] private float _flyAcceleration = 30f;

        [Tooltip("效果生效时播放的 Tips 文本内容（经 PopupManager 播放，留空则不播放）")]
        [SerializeField] private string _activateTipText = "中国人能飞！";

        [Tooltip("生效 Tips 展示时长（秒）：到时长自动关闭。非正时使用 Tips 注册表默认时长")]
        [Min(0f)]
        [SerializeField] private float _activateTipDuration = 3f;

        [Header("音效")]
        [Tooltip("飞行循环音效：按住跳跃键开始循环播放，松开后音量渐小直至消失（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _flightLoopSfx = SfxId.FlightLoop;

        [Tooltip("松开跳跃键（或效果结束）后音效淡出时长（秒）")]
        [SerializeField, Min(0.05f)] private float _sfxFadeOutTime = 0.5f;

        // 与服务端 Event3 子类型约定：中国人能飞 = 1（SpellDefinition_FlyChinese 资产同步配置 _subtype=1）
        private const int FLIGHT_SUBTYPE = 1;

        // 服务端驱动的远端特效同步（非序列化）：联机时为远端施法者挂载/移除飞行特效
        private RemoteSpellFxSync _remoteFxSync;

        /// <summary>
        /// 联机：应用服务端下发的事件3玩家状态——为 subtype 匹配的远端玩家同步飞行特效
        /// （本地玩家的飞行特效由本地实例管理；快照全量重复下发，RemoteSpellFxSync 内部幂等）
        /// </summary>
        public override void ApplyServerEvent3States(
            System.Collections.Generic.IDictionary<string, Minigame.Room.V1.Event3PlayerState> states,
            LevelEventContext eventContext)
        {
            if (states == null)
            {
                return;
            }

            _remoteFxSync ??= new RemoteSpellFxSync(_flightPrefab, _flightOffset, FLIGHT_SUBTYPE, _duration);
            _remoteFxSync.Apply(states);
        }

        /// <summary>联机：事件结束，清理远端同步的飞行特效</summary>
        public override void EndServerDrivenEffects()
        {
            _remoteFxSync?.Clear();
            _remoteFxSync = null;
        }

        /// <summary>
        /// 激活飞行效果：在目标玩家身上挂载特效并启动计时
        /// </summary>
        protected override SpellEffectInstance OnActivate(SpellEffectContext context)
        {
            if (context == null || context.Target == null)
            {
                Debug.LogWarning("[FlightSpellEffect] 上下文或目标玩家为空，效果不生效。");
                return null;
            }

            if (_flightPrefab == null)
            {
                Debug.LogWarning("[FlightSpellEffect] 飞行特效 Prefab 未配置，本次仅有计时无视觉表现。");
            }

            ShowActivateTip();
            return new FlightInstance(context, _flightPrefab, _flightOffset, _duration, _maxFlySpeed, _flyAcceleration,
                _flightLoopSfx, _sfxFadeOutTime);
        }

        /// <summary>
        /// 播放效果生效 Tips（通用 Tips 类型，按 _activateTipDuration 时长自动关闭）；
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
                Debug.LogWarning("[FlightSpellEffect] PopupManager 不存在，跳过生效 Tips 播放。");
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _activateTipText, _activateTipDuration);
        }

        /// <summary>
        /// 飞行效果运行时实例：挂特效、计时、监听目标玩家状态以提前结束
        /// </summary>
        private sealed class FlightInstance : SpellEffectInstance
        {
            private GameObject _flightInstance;
            private Coroutine _expireCoroutine;
            private readonly SfxId _loopSfx;
            private readonly float _sfxFadeOutTime;
            private bool _bSfxPlaying;   // 循环音效当前是否处于播放态（边沿触发起停）

            public FlightInstance(SpellEffectContext context, GameObject flightPrefab, Vector2 offset,
                float duration, float maxFlySpeed, float flyAcceleration,
                SfxId loopSfx, float sfxFadeOutTime) : base(context)
            {
                _loopSfx = loopSfx;
                _sfxFadeOutTime = sfxFadeOutTime;

                // 开启飞行模式：跳跃逻辑替换为按住持续上升（End 时复位）
                Target.SetFlying(true, flyAcceleration, maxFlySpeed);

                if (flightPrefab != null)
                {
                    _flightInstance = Instantiate(flightPrefab, Target.transform);
                    _flightInstance.transform.localPosition = offset;
                }

                // 订阅玩家状态：目标玩家死亡/通关/化身销毁时提前结束
                if (LevelPlayerRegistry.Instance != null)
                {
                    LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                    LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
                }

                if (Runner != null)
                {
                    _expireCoroutine = Runner.StartCoroutine(ExpireRoutine(duration));
                }
            }

            /// <summary>
            /// 每帧轮询跳跃键（由事件 Modifier 的效果驱动协程调用）：按下边沿开始循环音效（循环由音频系统保证），松开边沿淡出停止
            /// </summary>
            public override void Tick()
            {
                if (_loopSfx == SfxId.None || Target == null)
                {
                    return;
                }

                bool held = Target.JumpHeld;
                if (held && !_bSfxPlaying)
                {
                    _bSfxPlaying = true;
                    AudioManager.StartLoopSfx(_loopSfx);
                }
                else if (!held && _bSfxPlaying)
                {
                    _bSfxPlaying = false;
                    AudioManager.StopLoopSfx(_loopSfx, _sfxFadeOutTime);
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
                // 效果提前结束（死亡/通关/化身销毁/到期）时淡出循环音效，避免残留
                if (_bSfxPlaying)
                {
                    _bSfxPlaying = false;
                    AudioManager.StopLoopSfx(_loopSfx, _sfxFadeOutTime);
                }

                // 关闭飞行模式，恢复普通跳跃逻辑（玩家已销毁时跳过）
                if (Target != null)
                {
                    Target.SetFlying(false);
                }

                if (LevelPlayerRegistry.Instance != null)
                {
                    LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
                    LevelPlayerRegistry.Instance.OnPlayersChanged -= HandlePlayersChanged;
                }

                if (_expireCoroutine != null && Runner != null)
                {
                    Runner.StopCoroutine(_expireCoroutine);
                    _expireCoroutine = null;
                }

                if (_flightInstance != null)
                {
                    // 玩家化身销毁时特效作为子物体已随之销毁，此处判空后兜底销毁
                    Object.Destroy(_flightInstance);
                    _flightInstance = null;
                }
            }
        }
    }
}

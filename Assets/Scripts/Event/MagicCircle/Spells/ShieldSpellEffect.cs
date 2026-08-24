using System.Collections;
using System.Collections.Generic;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 无敌金身（护盾）咒语效果 — ScriptableObject 资产
    /// 触发时在目标玩家身上挂载护盾特效（作为玩家子节点随其移动），持续配置时长后自动移除；
    /// 效果期间玩家获得无敌：免疫伤害、不会进入死亡/幽灵状态，但物理效果（如击退击飞）仍正常作用；
    /// 玩家化身销毁时护盾提前移除
    /// </summary>
    [CreateAssetMenu(fileName = "ShieldSpellEffect", menuName = "SuperQQ/Event/Spells/Shield Spell Effect")]
    public class ShieldSpellEffect : SpellEffect
    {
        [Tooltip("护盾特效 Prefab（挂载为玩家子节点）；留空则只计时无视觉")]
        [SerializeField] private GameObject _shieldPrefab;

        [Tooltip("护盾特效在玩家本地坐标系下的位置偏移")]
        [SerializeField] private Vector2 _shieldOffset = Vector2.zero;

        [Tooltip("护盾持续时长（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _duration = 10f;

        [Tooltip("效果生效时播放的 Tips 文本内容（经 PopupManager 播放，留空则不播放）")]
        [SerializeField] private string _activateTipText = "无敌金身！";

        /// <summary>
        /// 激活护盾效果：在目标玩家身上挂载特效并启动计时
        /// </summary>
        protected override SpellEffectInstance OnActivate(SpellEffectContext context)
        {
            if (context == null || context.Target == null)
            {
                Debug.LogWarning("[ShieldSpellEffect] 上下文或目标玩家为空，效果不生效。");
                return null;
            }

            if (_shieldPrefab == null)
            {
                Debug.LogWarning("[ShieldSpellEffect] 护盾特效 Prefab 未配置，本次仅有计时无视觉表现。");
            }

            ShowActivateTip();
            return new ShieldInstance(context, _shieldPrefab, _shieldOffset, _duration);
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
                Debug.LogWarning("[ShieldSpellEffect] PopupManager 不存在，跳过生效 Tips 播放。");
                return;
            }

            PopupManager.Instance.ShowTips(TipsType.Common, _activateTipText);
        }

        /// <summary>
        /// 护盾效果运行时实例：挂特效、计时、监听目标玩家状态以提前结束
        /// </summary>
        private sealed class ShieldInstance : SpellEffectInstance
        {
            private GameObject _shieldInstance;
            private Coroutine _expireCoroutine;

            public ShieldInstance(SpellEffectContext context, GameObject shieldPrefab, Vector2 offset, float duration) : base(context)
            {
                // 无敌金身：效果期间免疫死亡（引用计数，End 时移除）
                Target.AddInvincibility();

                if (shieldPrefab != null)
                {
                    _shieldInstance = Instantiate(shieldPrefab, Target.transform);
                    _shieldInstance.transform.localPosition = offset;
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
                // 移除无敌标记（玩家已销毁时跳过）
                if (Target != null)
                {
                    Target.RemoveInvincibility();
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

                if (_shieldInstance != null)
                {
                    // 玩家化身销毁时护盾作为子物体已随之销毁，此处判空后兜底销毁
                    Object.Destroy(_shieldInstance);
                    _shieldInstance = null;
                }
            }
        }
    }
}

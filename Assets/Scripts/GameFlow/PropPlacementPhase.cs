using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 道具放置阶段。
    /// 进入阶段时开启场景内的放置玩法（显示网格、发放本轮手牌、屏蔽角色移动），
    /// 倒计时结束或提前完成条件成立后进入配置的下一阶段，退出时清场。
    /// 转移配置建议：[0] 道具摆放阶段条件（PropPlacementCondition）-> 下一阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Prop Placement Phase")]
    public class PropPlacementPhase : GamePhaseBase
    {
        private bool _bPhaseStarted;
        private PropPlacementCondition _placementCondition;

        /// <summary>
        /// 阶段剩余时间（秒），供倒计时 UI 显示；未配置道具摆放条件时恒为 0。
        /// </summary>
        public float RemainingTime => _placementCondition != null ? _placementCondition.RemainingTime : 0f;

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bPhaseStarted = false;

            // 必须在 base.OnEnter 之后解析：此时转移才完成运行时条件实例化，
            // 缓存到的才是累计计时的那份副本而非共享资产
            _placementCondition = ResolvePlacementCondition();

            // 目标场景与当前场景相同时 LoadScene 会跳过加载，不会触发 sceneLoaded，
            // 因此必须在此直接尝试启动；跨场景加载的情况由 RefreshSceneRuntimeBindings 兜底
            TryBeginPlacement(context);
        }

        public override void RefreshSceneRuntimeBindings(GamePhaseContext context)
        {
            base.RefreshSceneRuntimeBindings(context);

            // 场景异步加载完成后 Director 才存在，此处补启动（由 _bPhaseStarted 保证不重复）
            TryBeginPlacement(context);
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);

            context.PlacementDirector?.EndPhase();
            _bPhaseStarted = false;
            _placementCondition = null;
        }

        /// <summary>
        /// 从转移配置中找到道具摆放条件（计时数据在其上），供 UI 查询剩余时间。
        /// </summary>
        private PropPlacementCondition ResolvePlacementCondition()
        {
            IReadOnlyList<GamePhaseTransition> transitions = Transitions;
            for (int i = 0; i < transitions.Count; i++)
            {
                if (transitions[i] != null && transitions[i].Condition is PropPlacementCondition condition)
                {
                    return condition;
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试开启放置玩法（幂等）。Director 不在场时仅告警，不阻断流程状态机流转。
        /// </summary>
        private void TryBeginPlacement(GamePhaseContext context)
        {
            if (_bPhaseStarted)
            {
                return;
            }

            if (context.PlacementDirector == null)
            {
                Debug.LogWarning($"[{LogName}] 场景中缺少 PropPlacementDirector，放置玩法不会开启（阶段倒计时仍照常推进）。");
                return;
            }

            context.PlacementDirector.BeginPhase();
            _bPhaseStarted = true;
        }
    }
}

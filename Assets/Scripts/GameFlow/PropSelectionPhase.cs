using System.Collections.Generic;
using SuperQQ.Item;
using SuperQQ.Selection.Runtime;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 道具选择阶段。
    /// 进入阶段时开启场景内的选择玩法（随机抽取候选道具、展示选择面板），
    /// 本地玩家确认选中或倒计时结束后进入配置的下一阶段；
    /// 退出时把本地选择结果推入放置阶段门面作为下一次放置的待放置道具，并清场。
    /// 转移配置建议：[0] 道具选择阶段条件（PropSelectCondition）-> 道具放置阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Prop Selection Phase")]
    public class PropSelectionPhase : GamePhaseBase
    {
        private bool _bPhaseStarted;
        private PropSelectCondition _selectCondition;

        /// <summary>
        /// 阶段剩余时间（秒），供倒计时 UI 显示；未配置道具选择条件时恒为 0。
        /// </summary>
        public float RemainingTime => _selectCondition != null ? _selectCondition.RemainingTime : 0f;

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bPhaseStarted = false;

            // 新一轮开始即复活本地玩家并回出生点（不能等到游玩阶段）：
            // InputReporter 全阶段持续上报 player_state，若选择/放置阶段仍是上一轮的
            // 幽灵/通关状态，服务器会在进入游玩阶段时误判全员出局并秒切结算。
            // 本方法幂等（已存活为空操作，单机新场景新实例亦为空操作）。
            Player.LevelPlayerRegistry.Instance?.ReviveLocalPlayersForNewRound();

            // 必须在 base.OnEnter 之后解析：此时转移才完成运行时条件实例化，
            // 缓存到的才是累计计时的那份副本而非共享资产
            _selectCondition = ResolveSelectCondition();

            // 目标场景与当前场景相同时 LoadScene 会跳过加载，不会触发 sceneLoaded，
            // 因此必须在此直接尝试启动；跨场景加载的情况由 RefreshSceneRuntimeBindings 兜底
            TryBeginSelection(context);
        }

        public override void RefreshSceneRuntimeBindings(GamePhaseContext context)
        {
            base.RefreshSceneRuntimeBindings(context);

            // 场景异步加载完成后 Director 才存在，此处补启动（由 _bPhaseStarted 保证不重复）
            TryBeginSelection(context);
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);

            PropSelectionDirector selectionDirector = context.SelectionDirector;
            if (selectionDirector != null)
            {
                // 把本地选择结果推入放置阶段；未选中时跳过，放置阶段回退为候选池随机发放
                ItemBase selected = selectionDirector.LocalSelectedItem;
                if (selected != null)
                {
                    if (context.PlacementDirector != null)
                    {
                        context.PlacementDirector.SetPendingItem(selected);
                    }
                    else
                    {
                        Debug.LogWarning($"[{LogName}] 场景中缺少 PropPlacementDirector，本地选择结果未能推入放置阶段。");
                    }
                }

                selectionDirector.EndPhase();
            }

            _bPhaseStarted = false;
            _selectCondition = null;
        }

        /// <summary>
        /// 从转移配置中找到道具选择条件（计时数据在其上），供 UI 查询剩余时间。
        /// </summary>
        private PropSelectCondition ResolveSelectCondition()
        {
            IReadOnlyList<GamePhaseTransition> transitions = Transitions;
            for (int i = 0; i < transitions.Count; i++)
            {
                if (transitions[i] != null && transitions[i].Condition is PropSelectCondition condition)
                {
                    return condition;
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试开启选择玩法（幂等）。Director 不在场时仅告警，不阻断流程状态机流转。
        /// </summary>
        private void TryBeginSelection(GamePhaseContext context)
        {
            if (_bPhaseStarted)
            {
                return;
            }

            if (context.SelectionDirector == null)
            {
                Debug.LogWarning($"[{LogName}] 场景中缺少 PropSelectionDirector，选择玩法不会开启（阶段倒计时仍照常推进）。");
                return;
            }

            context.SelectionDirector.BeginPhase();
            _bPhaseStarted = true;
        }
    }
}

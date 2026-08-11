using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 全员出局条件。
    /// 订阅当前关卡玩家注册表的全员出局事件，事件触发后条件成立。
    /// </summary>
    [CreateAssetMenu(fileName = "AllPlayersOutCondition", menuName = "SuperQQ/Game Flow/Conditions/All Players Out Condition")]
    public class AllPlayersOutCondition : GamePhaseCondition
    {
        private LevelPlayerRegistry _currentRegistry;
        private bool _bAllPlayersOut;

        public override bool Evaluate(GamePhaseContext context)
        {
            return _bAllPlayersOut;
        }

        public override void OnPhaseEnter(GamePhaseContext context)
        {
            _bAllPlayersOut = false;
            TrySubscribeToLevelRegistry(context);
        }

        public override void OnPhaseExit(GamePhaseContext context)
        {
            UnsubscribeFromLevelRegistry();
            _bAllPlayersOut = false;
        }

        public override void OnSceneBindingsRefresh(GamePhaseContext context)
        {
            TrySubscribeToLevelRegistry(context);
        }

        public override string GetReason()
        {
            return "全部玩家出局";
        }

        private void TrySubscribeToLevelRegistry(GamePhaseContext context)
        {
            if (context == null)
            {
                return;
            }

            LevelPlayerRegistry registry = context.LevelRegistry;
            if (registry == null || registry == _currentRegistry)
            {
                return;
            }

            UnsubscribeFromLevelRegistry();
            _currentRegistry = registry;
            _currentRegistry.OnAllPlayersOut += HandleAllPlayersOut;
        }

        private void UnsubscribeFromLevelRegistry()
        {
            if (_currentRegistry == null)
            {
                return;
            }

            _currentRegistry.OnAllPlayersOut -= HandleAllPlayersOut;
            _currentRegistry = null;
        }

        private void HandleAllPlayersOut()
        {
            _bAllPlayersOut = true;
        }
    }
}

using SuperQQ.GameFlow;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 道具阶段钩子派发器 — 把 GamePhaseManager 的阶段切换翻译成 ItemBase 的阶段钩子：
    /// 进入 PlayingPhase → 对场上所有 ItemBase 调 OnRunPhaseStart（旋转吐司/流星锤/磁铁等启动）
    /// 进入 PropSelection/PropPlacementPhase → 调 OnBuildPhaseStart（停止并复位）
    /// 自动创建，常驻场景；无需手动挂载
    /// </summary>
    public class ItemPhaseHookDispatcher : MonoBehaviour
    {
        private static ItemPhaseHookDispatcher _instance;
        private GamePhaseManager _subscribedFlow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("[ItemPhaseHookDispatcher]");
            _instance = go.AddComponent<ItemPhaseHookDispatcher>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            // GamePhaseManager 随场景加载可能晚于本组件创建，轮询到出现再订阅
            GamePhaseManager flow = GamePhaseManager.Instance;
            if (flow == _subscribedFlow)
            {
                return;
            }
            if (_subscribedFlow != null)
            {
                _subscribedFlow.OnPhaseChanged -= HandlePhaseChanged;
            }
            _subscribedFlow = flow;
            if (_subscribedFlow != null)
            {
                _subscribedFlow.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        private void OnDestroy()
        {
            if (_subscribedFlow != null)
            {
                _subscribedFlow.OnPhaseChanged -= HandlePhaseChanged;
                _subscribedFlow = null;
            }
        }

        private void HandlePhaseChanged(GamePhaseBase previous, GamePhaseBase next)
        {
            if (next is PlayingPhase)
            {
                Dispatch(runPhase: true);
            }
            else if (next is PropSelectionPhase || next is PropPlacementPhase)
            {
                Dispatch(runPhase: false);
            }
        }

        /// <summary>对场上所有 ItemBase 派发阶段钩子（不含未激活物体）</summary>
        private static void Dispatch(bool runPhase)
        {
            // FindObjectsByType 不指定场景：覆盖主场景与 additive 关卡场景中的全部道具
            ItemBase[] items = FindObjectsByType<ItemBase>(FindObjectsSortMode.None);
            foreach (ItemBase item in items)
            {
                if (runPhase)
                {
                    item.OnRunPhaseStart();
                }
                else
                {
                    item.OnBuildPhaseStart();
                }
            }
        }
    }
}

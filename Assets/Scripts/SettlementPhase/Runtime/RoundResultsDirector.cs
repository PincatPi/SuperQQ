using SuperQQ.UI.RoundResults;
using UnityEngine;

namespace SuperQQ.Settlement.Runtime
{
    /// <summary>
    /// 单轮结算面板场景门面（场景级单例）。
    /// 对 GameFlow 只暴露 <see cref="BeginPhase"/> / <see cref="EndPhase"/> 与只读状态查询，
    /// 内部负责结算面板的弹出与隐藏（数据构建由 RoundResultsDataAdapter 完成）。
    ///
    /// 流程：RoundSettlementPhase 进入时调用 <see cref="BeginPhase"/> 弹出结算面板，
    ///       展示本轮各玩家的得分明细；阶段退出时调用 <see cref="EndPhase"/> 隐藏面板。
    ///
    /// Editor 搭建步骤：
    ///   1. Level1 场景新建空物体挂载本组件；
    ///   2. 将场景中已搭建好的 RoundResultsPanel（可整体初始置为 Inactive）拖入 Results Panel 引用；
    ///   3. 面板 Continue 按钮是否推进阶段流程由 Notify Game Flow On Continue 控制：
    ///      当前结算流程仍由 SettlementController 结算动画结束自动推进，
    ///      面板按钮默认只负责关闭面板，不重复通知阶段状态机。
    /// </summary>
    public sealed class RoundResultsDirector : MonoBehaviour
    {
        private const string LOG_TAG = "[RoundResults]";

        private static RoundResultsDirector _instance;

        /// <summary>场景内的结算面板门面实例</summary>
        public static RoundResultsDirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<RoundResultsDirector>();
                }
                return _instance;
            }
        }

        [Header("结算面板")]
        [Tooltip("单轮结算阶段弹出的面板（场景内预置，SetActive 切换显示）")]
        [SerializeField] private RoundResultsPanel resultsPanel;

        [Header("玩家记分行")]
        [Tooltip("记分行 prefab（挂 RoundResultRowView 的 RoundResultRow）")]
        [SerializeField] private RoundResultRowView rowPrefab;
        [Tooltip("记分行挂载容器（挂 VerticalLayoutGroup 的 RectTransform）")]
        [SerializeField] private RectTransform rowsContainer;

        [Header("流程推进")]
        [Tooltip("勾选后面板 Continue 按钮点击时通知阶段状态机推进；当前由 SettlementController 结算动画自动推进，默认关闭避免重复推进")]
        [SerializeField] private bool notifyGameFlowOnContinue = false;

        /// <summary>当前是否处于结算面板展示中</summary>
        public bool BIsActive { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"{LOG_TAG} 场景中存在多个 RoundResultsDirector，已销毁重复实例。", this);
                Destroy(this);
                return;
            }
            _instance = this;

            if (resultsPanel != null)
            {
                resultsPanel.HideImmediate();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ==================== 阶段接口（供 GameFlow 调用） ====================

        /// <summary>
        /// 开启结算面板（幂等）：激活面板并按当前轮得分数据填充展示。
        /// </summary>
        public void BeginPhase()
        {
            if (BIsActive)
            {
                return;
            }

            if (resultsPanel == null)
            {
                Debug.LogWarning($"{LOG_TAG} 未配置结算面板（Results Panel），本阶段将无结算 UI 展示。", this);
                return;
            }

            resultsPanel.SetNotifyGameFlowOnContinue(notifyGameFlowOnContinue);

            // 一体展示：面板框架 + 由 Row Prefab / Rows Container 实例化的玩家记分行
            // （不走面板内建行路径，避免两套行同时出现）
            bool shown = resultsPanel.ShowCurrentRoundPlayerRows(rowPrefab, rowsContainer);
            if (!shown)
            {
                Debug.LogWarning($"{LOG_TAG} 本轮无结算数据，面板未弹出。", this);
                return;
            }

            BIsActive = true;
            Debug.Log($"{LOG_TAG} 进入单轮结算，结算面板已弹出");
        }

        /// <summary>
        /// 联机：服务器 Settlement 通常晚于面板弹出到达（面板由 GamePhaseSync{ROUND_SETTLEMENT}
        /// 触发），到达时若面板正开着则用最新分数重建记分行（不重复弹窗动画），
        /// 否则本次面板永远看不到服务器分数。面板未打开时为空操作。
        /// </summary>
        public void RefreshIfOpen()
        {
            if (!BIsActive || resultsPanel == null)
            {
                return;
            }

            resultsPanel.ShowCurrentRoundPlayerRows(rowPrefab, rowsContainer);
            Debug.Log($"{LOG_TAG} 服务器结算到达，已按最新分数重建记分行");
        }

        /// <summary>
        /// 结束结算面板（幂等）：隐藏面板。
        /// </summary>
        public void EndPhase()
        {
            if (!BIsActive)
            {
                return;
            }

            if (resultsPanel != null)
            {
                resultsPanel.HideImmediate();
            }

            BIsActive = false;
            Debug.Log($"{LOG_TAG} 退出单轮结算，结算面板已隐藏");
        }
    }
}

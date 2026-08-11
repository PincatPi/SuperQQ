using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 游戏流程配置。
    /// 通过手动配置的阶段资产列表定义整局游戏流程。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Game Flow Config")]
    public class GameFlowConfig : ScriptableObject
    {
        [Header("状态机")]
        [SerializeField] private GamePhaseBase _initialPhase;
        [SerializeField] private List<GamePhaseBase> _phases = new();

        /// <summary>
        /// 初始阶段。
        /// </summary>
        public GamePhaseBase InitialPhase => _initialPhase;

        /// <summary>
        /// 阶段资产列表。
        /// </summary>
        public IReadOnlyList<GamePhaseBase> Phases => _phases;

        /// <summary>
        /// 校验流程配置是否合法。
        /// </summary>
        /// <param name="errorMessage">错误信息。</param>
        public bool ValidateConfig(out string errorMessage)
        {
            if (_initialPhase == null)
            {
                errorMessage = "初始阶段为空，请在 GameFlowConfig 中挂载初始阶段资产。";
                return false;
            }

            if (_phases == null || _phases.Count == 0)
            {
                errorMessage = "阶段列表为空，请在 GameFlowConfig 中手动配置阶段资产。";
                return false;
            }

            HashSet<GamePhaseBase> phaseSet = new HashSet<GamePhaseBase>();

            for (int i = 0; i < _phases.Count; i++)
            {
                GamePhaseBase phase = _phases[i];
                if (phase == null)
                {
                    errorMessage = $"阶段列表第 {i} 项为空。";
                    return false;
                }

                if (!phaseSet.Add(phase))
                {
                    errorMessage = $"阶段资产重复：{phase.name}";
                    return false;
                }
            }

            if (!phaseSet.Contains(_initialPhase))
            {
                errorMessage = $"初始阶段不在阶段列表中：{_initialPhase.name}";
                return false;
            }

            List<GamePhaseBase> referencedPhases = new List<GamePhaseBase>();
            for (int i = 0; i < _phases.Count; i++)
            {
                referencedPhases.Clear();
                GamePhaseBase phase = _phases[i];
                phase.CollectReferencedPhases(referencedPhases);

                for (int refIndex = 0; refIndex < referencedPhases.Count; refIndex++)
                {
                    GamePhaseBase referencedPhase = referencedPhases[refIndex];
                    if (referencedPhase == null)
                    {
                        errorMessage = $"阶段 {phase.LogName} 引用了空目标阶段。";
                        return false;
                    }

                    if (!phaseSet.Contains(referencedPhase))
                    {
                        errorMessage = $"阶段 {phase.LogName} 引用了未加入阶段列表的目标阶段：{referencedPhase.name}";
                        return false;
                    }
                }
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
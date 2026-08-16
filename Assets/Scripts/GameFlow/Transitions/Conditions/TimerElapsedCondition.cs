using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 倒计时条件。
    /// 进入阶段后开始计时，累计时间达到配置时长后条件成立。
    /// 时长小于等于 0 时进入阶段立即成立。
    /// </summary>
    [CreateAssetMenu(fileName = "TimerElapsedCondition", menuName = "SuperQQ/Game Flow/Conditions/Timer Elapsed Condition")]
    public class TimerElapsedCondition : GamePhaseCondition
    {
        [Tooltip("倒计时时长（秒），小于等于 0 时进入阶段立即成立")]
        [SerializeField] private float _duration = 0f;

        private float _elapsedTime;

        /// <summary>
        /// 倒计时时长（秒）。
        /// </summary>
        public float Duration => _duration;

        /// <summary>
        /// 剩余时间（秒），供倒计时 UI 显示；时长小于等于 0 时恒为 0。
        /// </summary>
        public float RemainingTime => _duration <= 0f ? 0f : Mathf.Max(0f, _duration - _elapsedTime);

        public override bool Evaluate(GamePhaseContext context)
        {
            if (_duration <= 0f || _elapsedTime >= _duration)
            {
                return true;
            }

            return EvaluateEarlyComplete(context);
        }

        /// <summary>
        /// 提前完成判定。倒计时未结束时调用，返回 true 则不等待倒计时结束，直接视为本条件成立。
        /// 基类默认不提前完成，由子类按需重写。
        /// </summary>
        /// <param name="context">阶段运行时上下文。</param>
        /// <returns>提前完成返回 true。</returns>
        protected virtual bool EvaluateEarlyComplete(GamePhaseContext context)
        {
            return false;
        }

        public override void OnPhaseEnter(GamePhaseContext context)
        {
            _elapsedTime = 0f;
        }

        public override void OnPhaseExit(GamePhaseContext context)
        {
            _elapsedTime = 0f;
        }

        public override void OnPhaseTick(GamePhaseContext context, float deltaTime)
        {
            if (_duration <= 0f)
            {
                return;
            }

            _elapsedTime += deltaTime;
        }

        public override string GetReason()
        {
            return $"倒计时结束：{_duration:F1}s";
        }
    }
}

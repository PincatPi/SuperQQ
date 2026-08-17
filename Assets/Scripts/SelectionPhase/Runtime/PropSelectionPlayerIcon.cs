using System;
using UnityEngine;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 选择阶段的玩家图标（纯表现，由 PropSelectionDirector 生成与驱动）。
    /// 移动采用梯形速度曲线：先以恒定加速度平滑加速到最大速度，临近目标时平滑减速，
    /// 到达目标位置时速度恰好归零；未到达前改目标速度保持连续、不跳变。
    /// 到达目标时通过 <see cref="OnArrived"/> 对外通知，认领等逻辑据此在到达后才执行。
    /// </summary>
    public class PropSelectionPlayerIcon : MonoBehaviour
    {
        /// <summary>距目标小于该距离（像素）时视为到达</summary>
        private const float ARRIVE_EPSILON = 0.5f;

        private float maxSpeed = 1600f;     // 像素/秒
        private float acceleration = 6000f; // 像素/秒²
        private Vector3 targetPos;
        private float currentSpeed;
        private bool bMoving;

        /// <summary>到达目标位置时触发</summary>
        public event Action<PropSelectionPlayerIcon> OnArrived;

        /// <summary>归属位置（出现位），认领失败时飞回；由 Director 在生成定位后通过 <see cref="MarkHome"/> 记录</summary>
        public Vector3 HomePos { get; private set; }

        /// <summary>当前是否正在移动</summary>
        public bool BIsMoving => bMoving;

        /// <summary>初始化运动参数（由 Director 在生成时调用）</summary>
        /// <param name="moveMaxSpeed">最大速度（像素/秒）</param>
        /// <param name="moveAcceleration">加/减速度（像素/秒²）</param>
        public void Init(float moveMaxSpeed, float moveAcceleration)
        {
            maxSpeed = Mathf.Max(1f, moveMaxSpeed);
            acceleration = Mathf.Max(1f, moveAcceleration);
        }

        /// <summary>
        /// 向目标位置发起平滑移动（屏幕/世界坐标）。
        /// 移动中重复调用只会改变目标点，当前速度保持连续。
        /// </summary>
        public void MoveTo(Vector3 worldPos)
        {
            targetPos = worldPos;
            bMoving = true;
        }

        /// <summary>立即停止在当前位置</summary>
        public void Stop()
        {
            bMoving = false;
            currentSpeed = 0f;
        }

        /// <summary>把当前位置记录为归属位置（出现位）</summary>
        public void MarkHome()
        {
            HomePos = transform.position;
        }

        private void Update()
        {
            if (!bMoving)
            {
                return;
            }

            Vector3 pos = transform.position;
            Vector3 toTarget = targetPos - pos;
            float remaining = toTarget.magnitude;
            if (remaining <= ARRIVE_EPSILON)
            {
                Arrive(pos);
                return;
            }

            // 梯形速度曲线：能在剩余距离内恰好停下的速度上限为 v = sqrt(2as)，
            // 期望速度取其与最大速度的较小者 —— 远离目标时加速到最大速度巡航，临近目标时被迫减速
            float stoppableSpeed = Mathf.Sqrt(2f * acceleration * remaining);
            float desiredSpeed = Mathf.Min(maxSpeed, stoppableSpeed);
            currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);

            float step = currentSpeed * Time.deltaTime;
            if (step >= remaining)
            {
                // 本帧恰好到达：直接落点并归零速度，避免越过目标抖动
                Arrive(targetPos);
                return;
            }

            transform.position = pos + toTarget / remaining * step;
        }

        private void Arrive(Vector3 finalPos)
        {
            transform.position = finalPos;
            bMoving = false;
            currentSpeed = 0f;
            OnArrived?.Invoke(this);
        }
    }
}

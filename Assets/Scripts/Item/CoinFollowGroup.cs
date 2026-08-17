using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 金币跟随组 — 常驻组件：预挂在玩家的 CoinsFollowPoint 子物体上（跟随锚点），
    /// 缺失时由首枚被拾取的金币自动补挂到玩家根物体；队列清空后驻留不销毁（轨迹持续记录，
    /// 下次拾取立即有平滑轨迹可用）
    /// 统一管理跟随同一玩家的金币队列：
    /// - 集中记录玩家的移动轨迹（避免每枚金币重复采样同一玩家）
    /// - 按入队位次为金币提供"轨迹错峰"：第 N 枚金币取样更早 N×spacing 秒的位置，
    ///   多枚金币沿玩家行进路线自然排成一列，不再重叠在同一跟随点上
    /// - 玩家静止时轨迹采样点收敛（时间错峰失效），此时按位次叠加散布偏移：
    ///   首枚金币锚定在玩家身后（相对位置与间隔保持不变），后续金币依次向外排成一字纵队
    /// - 玩家转身（面朝方向反转）时队列方向随之反转；移动中 U 型转身时旧轨迹点位于身前，
    ///   采样点被钳制回玩家位置（金币先收拢、轨迹落后再重新拖尾），任何时刻目标不挂在身前
    /// - 队列中金币离场（通关/死亡消失）时，后续金币位次自动前移，经 SmoothDamp 平滑收拢
    ///
    /// 职责边界：本组件只负责"轨迹记录与队列位次"这一空间协调问题；
    /// 金币的生命周期（拾取/消失）与加分逻辑仍由 Coin 自身管理
    /// </summary>
    public class CoinFollowGroup : MonoBehaviour
    {
        [Header("静止散布")]
        [Tooltip("静止时相邻金币的水平间隔（米）；首枚金币不偏移，后续金币沿水平方向依次排在前一枚外侧")]
        [SerializeField, Min(0f)] private float idleSpreadSpacing = 0.4f;
        [Tooltip("低于该速度（米/秒）视为静止，开始散布；高于则收敛回轨迹单列")]
        [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.5f;
        [Tooltip("散布展开/收拢的过渡时长（秒）")]
        [SerializeField, Min(0.01f)] private float spreadBlendTime = 0.3f;

        /// <summary>轨迹点：时间戳 + 玩家位置</summary>
        private struct TrailPoint
        {
            public float Time;
            public Vector2 Pos;
        }

        private readonly List<TrailPoint> trail = new();
        private readonly List<Coin> coins = new();
        private float speedEstimate;    // 平滑后的玩家速度估计（米/秒）
        private float spreadFactor;     // 静止散布权重：0=纯轨迹单列，1=完全散布
        private PlayerController owner;         // 所在玩家（读取面朝方向）
        private float outwardDir = 1f;          // 队列外侧方向（始终 = 玩家面朝的反方向）

        private void Awake()
        {
            // 组挂在玩家的 CoinsFollowPoint 子物体上，玩家组件在父级
            owner = GetComponentInParent<PlayerController>();
        }

        /// <summary>当前跟随中的金币数</summary>
        public int Count => coins.Count;

        /// <summary>队列外侧方向（玩家面朝方向的反方向，±1）</summary>
        public float OutwardDir => outwardDir;

        private void Update()
        {
            // 队列方向跟随玩家面朝方向：金币始终在玩家身后；
            // 玩家转身时外侧方向反转，整列金币的目标位置交换（见 GetSlotTarget）
            float facing = owner != null ? owner.FacingDir : 1f;
            outwardDir = facing >= 0f ? -1f : 1f;

            // 集中记录轨迹；裁剪窗口按最慢金币的延迟 + 采样余量保留
            trail.Add(new TrailPoint { Time = Time.time, Pos = transform.position });
            float cutoff = Time.time - MaxDelay() - 0.5f;
            while (trail.Count > 1 && trail[1].Time < cutoff)
            {
                trail.RemoveAt(0);
            }

            // 速度估计：对比 0.25 秒前的轨迹点（指数平滑抑制帧率抖动）
            float instant = ((Vector2)transform.position - SampleTrail(Time.time - 0.25f)).magnitude / 0.25f;
            speedEstimate = Mathf.Lerp(speedEstimate, instant, 0.15f);

            // 静止散布权重：低速趋向 1（展开），移动趋向 0（收拢），按过渡时长渐变
            float target = speedEstimate < idleSpeedThreshold ? 1f : 0f;
            spreadFactor = Mathf.MoveTowards(spreadFactor, target, Time.deltaTime / spreadBlendTime);
        }

        // ==================== 队列 ====================

        /// <summary>金币入队（拾取时调用），位次即入队顺序</summary>
        public void Register(Coin coin)
        {
            if (coin != null && !coins.Contains(coin))
            {
                coins.Add(coin);
            }
        }

        /// <summary>金币离队（消失时调用）；后续金币位次自动前移，轨迹延迟变小而平滑收拢</summary>
        public void Unregister(Coin coin)
        {
            coins.Remove(coin);
        }

        /// <summary>金币的队列位次（0 起）；不在队列返回 0（不落后于队首）</summary>
        public int SlotOf(Coin coin)
        {
            int index = coins.IndexOf(coin);
            return index >= 0 ? index : 0;
        }

        // ==================== 轨迹取样 ====================

        /// <summary>
        /// 取金币的跟随目标点：轨迹错峰取样 + 静止散布偏移
        /// 首枚金币锚定在玩家身后（零偏移，与玩家的间隔保持不变）；
        /// 后续金币每枚沿队列外侧偏移 N 个间隔，跟前一枚差固定 1 个间隔，
        /// 且不影响前序金币与玩家的相对位置；玩家移动时权重为 0 不影响轨迹
        ///
        /// 身前钳制：U 型转身后旧轨迹点位于玩家来路（即面朝方向/身前），
        /// 此时把采样点压回玩家位置——金币先向玩家收拢，待轨迹落到身后自然重新拖尾，
        /// 保证任何时刻金币的目标都不会挂在玩家身前
        /// </summary>
        public Vector2 GetSlotTarget(Coin coin, float delay)
        {
            int slot = SlotOf(coin);
            Vector2 basePos = SampleTrail(Time.time - delay);

            float facing = -outwardDir;   // 面朝方向（队列外侧的反方向）
            if ((basePos.x - transform.position.x) * facing > 0f)
            {
                basePos.x = transform.position.x;
            }

            if (slot > 0)
            {
                basePos += Vector2.right * (outwardDir * slot * idleSpreadSpacing * spreadFactor);
            }
            return basePos;
        }

        /// <summary>按时间戳在轨迹缓冲中线性插值取样（缓冲为空时返回玩家当前位置）</summary>
        public Vector2 SampleTrail(float sampleTime)
        {
            if (trail.Count == 0)
            {
                return transform.position;
            }
            if (sampleTime <= trail[0].Time)
            {
                return trail[0].Pos;
            }
            for (int i = trail.Count - 1; i > 0; i--)
            {
                if (trail[i - 1].Time <= sampleTime)
                {
                    TrailPoint a = trail[i - 1];
                    TrailPoint b = trail[i];
                    float span = b.Time - a.Time;
                    float t = span > 0.0001f ? (sampleTime - a.Time) / span : 1f;
                    return Vector2.LerpUnclamped(a.Pos, b.Pos, t);
                }
            }
            return trail[trail.Count - 1].Pos;
        }

        /// <summary>队列中最慢金币的轨迹延迟（裁剪窗口用）</summary>
        private float MaxDelay()
        {
            float max = 0f;
            for (int i = 0; i < coins.Count; i++)
            {
                if (coins[i] != null)
                {
                    max = Mathf.Max(max, coins[i].EffectiveDelay(i));
                }
            }
            return max;
        }
    }
}

using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家状态接口
    /// 每个状态拥有独立的运行时数据与生命周期
    /// </summary>
    public interface IPlayerState
    {
        /// <summary>
        /// 进入状态时调用
        /// </summary>
        void Enter();

        /// <summary>
        /// 退出状态时调用
        /// </summary>
        void Exit();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update();

        /// <summary>
        /// 物理帧更新
        /// </summary>
        void FixedUpdate();

        /// <summary>
        /// 是否在地面上
        /// </summary>
        bool BIsGrounded { get; }

        /// <summary>
        /// 是否正在跳跃（可变高度跳跃的加速窗口期）
        /// </summary>
        bool BIsJumping { get; }

        /// <summary>
        /// 是否滞空（离地）：跳跃或自然坠落均为 true，落地为 false
        /// 供动画层驱动跳跃/滞空动画；与 BIsJumping 不同，松键/长按结束不会提前退出
        /// </summary>
        bool BIsJumpAirborne { get; }

        /// <summary>
        /// 水平速度
        /// </summary>
        float HorizontalVelocity { get; }
    }
}

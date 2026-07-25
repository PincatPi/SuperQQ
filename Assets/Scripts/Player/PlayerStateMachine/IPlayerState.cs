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
        /// 是否正在跳跃
        /// </summary>
        bool BIsJumping { get; }

        /// <summary>
        /// 水平速度
        /// </summary>
        float HorizontalVelocity { get; }
    }
}

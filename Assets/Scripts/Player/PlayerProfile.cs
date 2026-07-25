using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家身份档案
    /// 跨场景持久化的纯数据结构，不持有任何 MonoBehaviour 引用
    /// 包含玩家名称、颜色和键位配置，由 PlayerSessionManager 持有
    /// 关卡场景加载时由 LevelPlayerRegistry 读取并应用到 PlayerController 化身
    /// </summary>
    [System.Serializable]
    public class PlayerProfile
    {
        /// <summary>
        /// 玩家名称（如 "P1"），作为玩家身份的唯一标识
        /// </summary>
        public string PlayerName;

        /// <summary>
        /// 玩家专属颜色，用于角色识别和结算柱体配色
        /// </summary>
        public Color PlayerColor;

        /// <summary>
        /// 左移按键
        /// </summary>
        public KeyCode LeftKey;

        /// <summary>
        /// 右移按键
        /// </summary>
        public KeyCode RightKey;

        /// <summary>
        /// 跳跃按键（主）
        /// </summary>
        public KeyCode JumpKey;

        /// <summary>
        /// 跳跃按键（备用，存活状态专用）
        /// </summary>
        public KeyCode JumpKeyAlt;

        /// <summary>
        /// 下蹲/幽灵下移按键
        /// </summary>
        public KeyCode DownKey;
    }
}

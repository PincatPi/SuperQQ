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
        /// 网络唯一玩家ID（联机主键，由服务器分配）
        /// 单机模式可为空，此时回退以 PlayerName 作为标识
        /// </summary>
        public string PlayerId;

        /// <summary>
        /// 是否为本机控制的玩家
        /// true=本地键盘输入+上报状态；false=远程玩家，由网络快照驱动
        /// </summary>
        public bool IsLocal = true;

        /// <summary>
        /// 玩家名称（如 "P1"），用于展示；单机模式下兼作身份标识
        /// </summary>
        public string PlayerName;

        /// <summary>
        /// 玩家专属颜色，用于角色识别和结算柱体配色
        /// </summary>
        public Color PlayerColor;

        /// <summary>
        /// 角色索引（-1=未指定，使用默认预制体）
        /// 联机模式等于房间座位号（进房顺序，两端一致且互斥），
        /// 由 LevelPlayerRegistry 的角色预制体列表按下标选取化身预制体
        /// </summary>
        public int CharacterIndex = -1;

        /// <summary>
        /// 玩家图标（选择阶段标识图）：运行时由化身（PlayerController）回写缓存，
        /// 供最终结算等无化身的场景展示玩家 PlayerIcon；资产引用，跨场景有效
        /// </summary>
        [System.NonSerialized] public Sprite SelectionIcon;

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

        /// <summary>
        /// 嘲讽按键（PC 端，触发嘲讽表情）
        /// </summary>
        public KeyCode TauntKey;

        /// <summary>
        /// 身份主键：联机模式为 PlayerId，单机模式回退为 PlayerName
        /// 需要按身份查找/去重时统一使用此属性
        /// </summary>
        public string IdentityKey => string.IsNullOrEmpty(PlayerId) ? PlayerName : PlayerId;
    }
}

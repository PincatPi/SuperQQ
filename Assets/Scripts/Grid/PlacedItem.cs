using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 已放置到网格上的物体
    /// 由 GridManager.Place 实例化时自动附加/初始化
    /// 记录锚点格子与占位信息，供占据查询、移除、网络回放使用
    /// </summary>
    public class PlacedItem : MonoBehaviour
    {
        /// <summary>道具定义</summary>
        public PlacableItemDef Def { get; private set; }
        /// <summary>锚点格子（footprint 左下角）</summary>
        public Vector2Int AnchorCell { get; private set; }
        /// <summary>是否旋转了90度</summary>
        public bool Rotated { get; private set; }
        /// <summary>放置者玩家ID（计分/撤回权限用；关卡初始物体为 -1）</summary>
        public int OwnerPlayerId { get; private set; }

        /// <summary>
        /// 放置者身份标识（联机为 playerId，单机为 PlayerController.IdentityKey；关卡初始物体为空串）。
        /// 陷阱击杀计分归属以此为准，由摆放确认路径写入
        /// </summary>
        public string OwnerKey { get; private set; } = string.Empty;

        /// <summary>写入放置者身份标识（仅摆放确认/远端生成/快照恢复路径调用）</summary>
        public void SetOwnerKey(string ownerKey)
        {
            OwnerKey = ownerKey ?? string.Empty;
        }

        /// <summary>
        /// 初始化放置数据（仅由 GridManager 调用）
        /// </summary>
        public void Init(PlacableItemDef def, Vector2Int anchorCell, bool rotated, int ownerPlayerId)
        {
            Def = def;
            AnchorCell = anchorCell;
            Rotated = rotated;
            OwnerPlayerId = ownerPlayerId;
        }
    }
}

using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 披萨盒 — 搭路类宽平台
    /// 较宽的稳定站立面（如 3x1），无减速等表面效果，纯粹提供通行/站立
    ///
    /// 放置规则：
    /// - 需要吸附：放置时必须与已有地形/道具相邻（吸附校验在 GridManager.CanPlace 层完成）
    /// - 可旋转：允许 0°/90°/180°/270° 四档朝向（PlacableItemDef.facingSteps = 4）
    /// - 围绕中心点旋转：锚点虽在 footprint 左下角，视觉上绕占位中心旋转，
    ///   避免旋转后平台"跑偏"，OnPlaced 时按 Facing 校正位置与角度
    /// </summary>
    public class PizzaBox : ItemBase
    {
        public override ItemCategory Category => ItemCategory.Path;

        /// <summary>
        /// 放置完成后，将视觉与碰撞体绕 footprint 中心点旋转到当前朝向
        /// GridManager 放置时按锚点（左下角）定位，这里补偿中心旋转带来的位移
        /// </summary>
        public override void OnPlaced()
        {
            ApplyCenterRotation();
        }

        /// <summary>
        /// 绕占位中心应用当前朝向：
        /// 1. 计算 footprint 中心（未旋转）相对锚点的偏移
        /// 2. 将该偏移绕锚点旋转 FacingAngle，得到新的根节点位置
        /// 3. 根节点自身再旋转 FacingAngle
        /// 结果：视觉上等价于绕中心点旋转，且锚点吸附逻辑不受影响
        /// </summary>
        private void ApplyCenterRotation()
        {
            if (Facing == 0 || Placed == null || Placed.Def == null)
            {
                return;
            }

            GridManager gm = GridManager.Instance;
            if (gm == null)
            {
                return;
            }

            float cell = gm.PublicCellSize;
            Vector2Int footprint = Placed.Def.Footprint;

            // 未旋转时，中心相对根节点（锚点左下角）的本地偏移
            Vector3 centerOffset = new Vector3(footprint.x * cell * 0.5f, footprint.y * cell * 0.5f, 0f);

            // 偏移绕 Z 轴旋转 FacingAngle 后，根节点需要反向补偿
            Vector3 rotatedOffset = Quaternion.Euler(0f, 0f, FacingAngle) * centerOffset;
            transform.position += centerOffset - rotatedOffset;
            transform.rotation = Quaternion.Euler(0f, 0f, FacingAngle);
        }
    }
}

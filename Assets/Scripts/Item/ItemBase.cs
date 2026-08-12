using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 道具基类 — 所有可摆放道具 prefab 的根组件
    /// 薄基类，只定义"作为网格道具"的契约与生命周期钩子；
    /// 具体行为（伤害、力场、移动等）由独立行为组件组合实现，不做深继承
    ///
    /// 朝向约定：Facing 为 0/1/2/3，对应 0°/90°/180°/270°（绕 Z 轴逆时针），
    /// 由摆放时的旋转操作设置；90° 的奇数次会使 footprint 宽高互换（GridManager 已处理）
    /// </summary>
    public abstract class ItemBase : MonoBehaviour
    {
        /// <summary>道具类别（策划分类：搭路/伤害/控制/拆除）</summary>
        public abstract ItemCategory Category { get; }

        /// <summary>放置信息（锚点格子、旋转、放置者），由 GridManager.Place 注入</summary>
        public PlacedItem Placed { get; private set; }

        /// <summary>当前朝向档位（0~3，每档90度）</summary>
        public int Facing { get; private set; }

        /// <summary>朝向对应的世界角度（度）</summary>
        public float FacingAngle => Facing * 90f;

        /// <summary>
        /// 初始化放置数据（仅由 GridManager.Place 调用）
        /// </summary>
        internal void InitPlaced(PlacedItem placed, int facing)
        {
            Placed = placed;
            Facing = facing;
        }

        // ==================== 生命周期钩子（按需重写） ====================

        /// <summary>被放置到网格后调用（GridManager.Place 完成时）</summary>
        public virtual void OnPlaced() { }

        /// <summary>被移除（拾回/拆除）前调用，用于清理运行状态</summary>
        public virtual void OnRemoved() { }

        /// <summary>跑动阶段开始：机关启动（开始攻击循环、启动力场等）</summary>
        public virtual void OnRunPhaseStart() { }

        /// <summary>建造阶段开始：机关复位（停止攻击、回到初始状态）</summary>
        public virtual void OnBuildPhaseStart() { }
    }
}

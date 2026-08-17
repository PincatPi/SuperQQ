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
    ///
    /// </summary>
    public abstract class ItemBase : MonoBehaviour
    {
        [Header("展示")]
        [Tooltip("道具在选择面板中的展示名称；留空时使用物体名")]
        [SerializeField] private string displayName = "";
        [Tooltip("道具图标，用于选择面板等 UI 展示；留空时回退为自身首个 SpriteRenderer 的 Sprite")]
        [SerializeField] private Sprite icon;

        /// <summary>道具类别（策划分类：搭路/伤害/控制/拆除）</summary>
        public abstract ItemCategory Category { get; }

        /// <summary>展示名称（选择面板等 UI 使用）；未配置时回退为物体名</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>道具图标；未配置时回退为自身首个 SpriteRenderer 的 Sprite</summary>
        public Sprite Icon => icon != null ? icon : FindFallbackIcon();

        /// <summary>放置信息（锚点格子、旋转、放置者），由 GridManager.Place 注入</summary>
        public PlacedItem Placed { get; private set; }

        /// <summary>当前朝向档位（0~3，每档90度）</summary>
        public int Facing { get; private set; }

        /// <summary>朝向对应的世界角度（度）</summary>
        public float FacingAngle => Facing * 90f;

        // ==================== 占位策略（按需重写） ====================

        /// <summary>
        /// 放置确认后是否把 footprint 覆盖的格子登记为已占据（持久占位）。
        /// 即放即消的道具（拆除类）重写为 false：只借用落点定位，不持久占位，
        /// 由 GridManager.Place / PlacementController.RegisterAt 在登记时遵守
        /// </summary>
        public virtual bool RegistersOccupancy => true;

        /// <summary>
        /// 摆放时是否允许落在其它道具已占据的格子上。
        /// 拆除类重写为 true：爆破范围即自身 footprint，必须能叠放到目标上方才能清除
        /// </summary>
        public virtual bool AllowsOccupiedOverlap => false;

        /// <summary>
        /// 初始化放置数据（仅由 GridManager.Place 调用）
        /// </summary>
        internal void InitPlaced(PlacedItem placed, int facing)
        {
            Placed = placed;
            Facing = facing;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器中挂载本组件（或手动 Reset）时自动连带挂载依赖组件，
        /// 补齐网格道具的标准组合：FootprintBoxView + BoxCollider2D + PlacementController
        /// </summary>
        protected virtual void Reset()
        {
            if (GetComponent<FootprintBoxView>() == null)
                gameObject.AddComponent<FootprintBoxView>();
            if (GetComponent<BoxCollider2D>() == null)
                gameObject.AddComponent<BoxCollider2D>();
            if (GetComponent<PlacementController>() == null)
                gameObject.AddComponent<PlacementController>();
        }
#endif

        // ==================== 生命周期钩子（按需重写） ====================

        /// <summary>未配置图标时回退取自身首个 SpriteRenderer 的 Sprite；均无则为 null</summary>
        private Sprite FindFallbackIcon()
        {
            SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>(true);
            return renderer != null ? renderer.sprite : null;
        }

        /// <summary>被放置到网格后调用（GridManager.Place 完成时）</summary>
        public virtual void OnPlaced()
        {
            // 联机登记：金币进拾取注册表，所有道具进生命周期注册表（离线时为空操作）
            if (this is Coin coin)
            {
                SuperQQ.Network.PickupRegistry.Register(coin);
            }
            SuperQQ.Network.ItemLifecycleSync.Register(this);
        }

        /// <summary>被移除（拾回/拆除）前调用，用于清理运行状态</summary>
        public virtual void OnRemoved()
        {
            if (this is Coin coin)
            {
                SuperQQ.Network.PickupRegistry.Unregister(coin);
            }
            SuperQQ.Network.ItemLifecycleSync.Unregister(this);
        }

        /// <summary>跑动阶段开始：机关启动（开始攻击循环、启动力场等）</summary>
        public virtual void OnRunPhaseStart() { }

        /// <summary>建造阶段开始：机关复位（停止攻击、回到初始状态）</summary>
        public virtual void OnBuildPhaseStart() { }
    }
}

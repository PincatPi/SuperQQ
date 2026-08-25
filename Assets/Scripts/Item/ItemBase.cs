using SuperQQ.Audio;
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

        [Header("音效")]
        [Tooltip("放置确认音效：OnPlaced 时在道具位置 3D 播放（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId placeSfx = SfxId.Place;

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
        /// 摆放时容忍的区域掩码：命中这些区域类型时不视为"禁止布置"（默认无容忍）。
        /// 附着类道具重写：如黄油块需附着在关卡预占（Occupied）的地形边缘格上，
        /// 豁免 Occupied 拦截——配合 ValidatePlacement 的 AttachSurface 要求保证落点仍然受控
        /// </summary>
        public virtual GridZoneType ToleratedZoneMask => GridZoneType.None;

        /// <summary>
        /// 是否可被黄油块黏住（默认可以）。
        /// 自身有独立运动逻辑的道具重写为 false：如流星锤按自身摆锤轨道运动，
        /// 被黏住会与其运动逻辑冲突
        /// </summary>
        public virtual bool CanBeStuck => true;

        /// <summary>
        /// 指定格子上的黏着是否生效（默认整道具可黏，等同 CanBeStuck）。
        /// 需要限定吸附点的道具重写：如流星锤仅底座挂点格可被黄油黏住，
        /// 黏住点以外的格子命中时不黏
        /// </summary>
        /// <param name="stickyCell">黄油黏性边相邻格（世界格坐标）</param>
        public virtual bool CanBeStuckAt(Vector2Int stickyCell) => CanBeStuck;

        /// <summary>
        /// 被黄油黏住时调用（默认空实现，纯父子层级跟随）。
        /// 需要自定义黏住行为的道具重写：如流星锤以吸附点为钉点跟随、自身朝向锁定
        /// </summary>
        /// <param name="butter">黏住来源的黄油块 transform</param>
        /// <param name="stickyCell">黄油黏性边相邻格（世界格坐标）</param>
        public virtual void OnStuckTo(Transform butter, Vector2Int stickyCell) { }

        /// <summary>解除黏住时调用（默认空实现）</summary>
        public virtual void OnUnstuck() { }

        /// <summary>
        /// 摆放落点的附加合法性校验（在格子占据检查之后执行，默认通过）。
        /// 附着类道具重写：如黄油块要求落点格内必须存在平台类道具或地形承载物。
        /// 拖拽红绿提示、放置登记、已放置旋转共用此判定
        /// </summary>
        public virtual bool ValidatePlacement(GridManager grid, Vector2Int anchor, int rotation)
        {
            return true;
        }

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

        /// <summary>播放放置确认音效（若已配置）；重写 OnPlaced 的子类需在自身实现中显式调用</summary>
        protected void PlayPlaceSfx()
        {
            if (placeSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(placeSfx, transform.position);
            }
        }

        /// <summary>
        /// 被放置到网格后调用（GridManager.Place / PlacementController 放置完成时）
        /// 重写注意：必须在子类实现开头调用 base.OnPlaced()，或至少调用 PlayPlaceSfx()，否则放置音效与联机登记不生效
        /// </summary>
        public virtual void OnPlaced()
        {
            PlayPlaceSfx();

            // 联机登记：金币进拾取注册表，所有道具进生命周期注册表（离线时为空操作）
            if (this is Coin coin)
            {
                SuperQQ.Network.PickupRegistry.Register(coin);
            }
            SuperQQ.Network.ItemLifecycleSync.Register(this);

            // 注册昼夜色调：道具是运行时动态生成的，不在 MapDayNightController 的 Awake 缓存里，
            // 需主动注册才能在夜晚随地图一起变蓝（无昼夜控制器的场景为空操作）
            SuperQQ.Map.MapDayNightController.Instance?.RegisterExternalRenderers(gameObject);
        }

        /// <summary>销毁时反注册昼夜色调（移除/拆毁/场景切换均走 OnDestroy，无需各路径单独处理）</summary>
        protected virtual void OnDestroy()
        {
            if (SuperQQ.Map.MapDayNightController.Instance != null)
            {
                SuperQQ.Map.MapDayNightController.Instance.UnregisterExternalRenderers(gameObject);
            }
        }

        /// <summary>
        /// 联机模式下道具自毁前调用：经 ItemStateEvent{DESTROYED} 上报服务器并广播，
        /// 其他端收到后按锚点 RemoveAt 同步移除（含占据释放）。
        /// 内部已判联机就绪，单机/未进房时为空操作；上报后本地仍按自身流程销毁。
        /// </summary>
        protected void ReportNetDestroyed()
        {
            SuperQQ.Network.ItemLifecycleSync.ReportDestroyed(this);
        }

        /// <summary>
        /// 切换左右镜像（默认空实现，无可镜像语义的道具不响应）。
        /// 有朝向/方向语义的道具重写：樱桃发射器切换发射方向、流星锤切换摆动方向。
        /// 摆放阶段对不可旋转的道具按旋转键时调用（PlacementSession.Rotate 的回退路径）
        /// </summary>
        public virtual void ToggleMirror() { }

        /// <summary>当前是否镜像（无镜像语义的道具恒 false；联机同步读取）</summary>
        public virtual bool Mirrored => false;

        /// <summary>设置镜像状态（联机同步写入：远端生成/快照恢复时调用）</summary>
        public virtual void SetMirrored(bool value) { }

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

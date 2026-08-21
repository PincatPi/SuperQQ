using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 网格管理器 — 关卡场景单例
    /// 职责：
    /// 1. 世界坐标 与 格子坐标 的双向换算（原点 + cellSize）
    /// 2. 维护格子占据表，提供放置/移除/合法性检测
    /// 3. Scene 视图绘制网格线与可摆放区域
    ///
    /// 摆放只发生在建造阶段；跑动阶段的移动与碰撞不依赖网格
    /// 本地操作与网络回放统一走 Place/RemoveAt 入口，保证各端状态一致
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        /// <summary>当前场景实例（场景内唯一）</summary>
        public static GridManager Instance { get; private set; }

        [Header("配置")]
        [Tooltip("全局网格配置（格子尺寸）")]
        [SerializeField] private GridConfig config;

        [Header("网格原点")]
        [Tooltip("格子(0,0)中心对准的世界位置；建议放一个空物体在关卡左下角，随关卡移动")]
        [SerializeField] private Transform gridOrigin;

        [Header("可摆放区域（格子坐标）")]
        [Tooltip("允许摆放的格子范围，对应建造阶段的虚线框；超出即不合法")]
        [SerializeField] private RectInt placeableBounds = new RectInt(-20, 0, 40, 15);

        [Header("可视化（Scene 视图 Gizmos）")]
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private Color gridLineColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color boundsColor = new Color(0f, 1f, 1f, 0.6f);
        [SerializeField] private Color occupiedColor = new Color(1f, 0.3f, 0.3f, 0.35f);

        [Header("网格可视化（运行时）")]
        [Tooltip("小格子贴图（50x50 虚线框，PPU=100，1 格一张平铺）")]
        [SerializeField] private Sprite smallCellSprite;
        [Tooltip("大格子贴图（200x200 实线框，PPU=100，每 4x4 小格一张平铺）")]
        [SerializeField] private Sprite bigCellSprite;
        [Tooltip("网格显示的 Sorting Order（需高于背景、低于道具）")]
        [SerializeField] private int gridSortingOrder = -1;

        // 占据表：格子 -> 占据该格子的物体（一个 PlacedItem 会登记其 footprint 覆盖的所有格子）
        private readonly Dictionary<Vector2Int, PlacedItem> occupiedCells = new Dictionary<Vector2Int, PlacedItem>();

        [Header("区域配置")]
        [Tooltip("本关卡的区域配置资产（由编辑器工具从场景标记烘焙生成）")]
        [SerializeField] private LevelZoneConfig zoneConfig;

        // 运行时网格可视化根物体（ShowGrid 时懒构建）
        private GameObject gridVisualRoot;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            Instance = this;
            this.ShowGrid();    // 先默认开启网格可视化 - 调试
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ==================== 坐标换算 ====================

        private Vector2 Origin => gridOrigin != null ? (Vector2)gridOrigin.position : Vector2.zero;
        private float CellSize => config != null ? config.CellSize : 0.5f;

        // 供 GridView 等外部组件读取的只读属性
        /// <summary>格子边长（米）</summary>
        public float PublicCellSize => CellSize;
        /// <summary>格子(0,0)中心的世界坐标</summary>
        public Vector2 PublicOrigin => Origin;
        /// <summary>可摆放区域（格子坐标）</summary>
        public RectInt PlaceableBounds => placeableBounds;

        /// <summary>
        /// 世界坐标 -> 所在格子坐标
        /// </summary>
        public Vector2Int WorldToCell(Vector2 worldPos)
        {
            Vector2 local = (worldPos - Origin) / CellSize;
            return new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
        }

        /// <summary>
        /// 格子坐标 -> 该格子中心的世界坐标
        /// </summary>
        public Vector2 CellToWorld(Vector2Int cell)
        {
            return Origin + (new Vector2(cell.x + 0.5f, cell.y + 0.5f)) * CellSize;
        }

        /// <summary>
        /// 计算 footprint 覆盖的所有格子（锚点为左下角，旋转后宽高互换）
        /// </summary>
        public List<Vector2Int> GetFootprintCells(Vector2Int anchorCell, Vector2Int footprint, bool rotated)
        {
            return GetFootprintCells(anchorCell, footprint, rotated ? 1 : 0);
        }

        /// <summary>
        /// 计算 footprint 覆盖的所有格子（锚点为左下角，按四档旋转：0=0° 1=顺时针90° 2=180° 3=270°）
        /// </summary>
        public List<Vector2Int> GetFootprintCells(Vector2Int anchorCell, Vector2Int footprint, int rotationSteps)
        {
            Vector2Int size = GetRotatedSize(footprint, rotationSteps);
            var cells = new List<Vector2Int>(size.x * size.y);
            for (int dx = 0; dx < size.x; dx++)
            {
                for (int dy = 0; dy < size.y; dy++)
                {
                    cells.Add(new Vector2Int(anchorCell.x + dx, anchorCell.y + dy));
                }
            }
            return cells;
        }

        /// <summary>
        /// 锚点格子 + footprint -> 物体中心的世界坐标（用于实例化摆放位置）
        /// </summary>
        public Vector2 GetPlacementWorldPos(Vector2Int anchorCell, Vector2Int footprint, bool rotated)
        {
            return GetPlacementWorldPos(anchorCell, footprint, rotated ? 1 : 0);
        }

        /// <summary>
        /// 锚点格子 + footprint + 四档旋转 -> 物体中心的世界坐标
        /// </summary>
        public Vector2 GetPlacementWorldPos(Vector2Int anchorCell, Vector2Int footprint, int rotationSteps)
        {
            Vector2Int size = GetRotatedSize(footprint, rotationSteps);
            return Origin + new Vector2(anchorCell.x + size.x * 0.5f, anchorCell.y + size.y * 0.5f) * CellSize;
        }

        /// <summary>四档旋转对应的 transform 旋转（Unity Z 轴正角为逆时针，顺时针取负）</summary>
        public static Quaternion GetRotationQuaternion(int rotationSteps)
        {
            rotationSteps = ((rotationSteps % 4) + 4) % 4;
            return rotationSteps == 0 ? Quaternion.identity : Quaternion.Euler(0f, 0f, -90f * rotationSteps);
        }

        /// <summary>
        /// 逆时针 90° 旋转后锚点格子在新占位矩形内的索引（宽高互换后的坐标）
        /// </summary>
        public static Vector2Int GetRotatedPivot(Vector2Int pivot, Vector2Int footprint)
        {
            return new Vector2Int(footprint.y - 1 - pivot.y, pivot.x);
        }

        /// <summary>按旋转档（0=0° 1=90° 2=180° 3=270°）求占位尺寸；90/270 宽高互换</summary>
        public static Vector2Int GetRotatedSize(Vector2Int footprint, int rotationSteps)
        {
            return (rotationSteps % 2 == 1) ? new Vector2Int(footprint.y, footprint.x) : footprint;
        }

        /// <summary>按旋转档求锚点格子在新占位矩形内的索引（90° 步进顺时针）</summary>
        public static Vector2Int GetRotatedPivot(Vector2Int pivot, Vector2Int footprint, int rotationSteps)
        {
            rotationSteps = ((rotationSteps % 4) + 4) % 4;
            Vector2Int p = pivot;
            Vector2Int size = footprint;
            for (int i = 0; i < rotationSteps; i++)
            {
                // 顺时针 90°：(x,y) -> (size.y-1-y, x)，新尺寸宽高互换
                p = new Vector2Int(size.y - 1 - p.y, p.x);
                size = new Vector2Int(size.y, size.x);
            }
            return p;
        }

        /// <summary>
        /// 解析道具的锚点格子：优先读 prefab 上 FootprintBoxView 的配置，缺省取中心格子
        /// </summary>
        public Vector2Int ResolvePivot(PlacableItemDef def, Vector2Int footprint)
        {
            if (def != null && def.Prefab != null)
            {
                FootprintBoxView box = def.Prefab.GetComponent<FootprintBoxView>();
                if (box != null)
                {
                    return box.PivotCell;
                }
            }
            return new Vector2Int((footprint.x - 1) / 2, (footprint.y - 1) / 2);
        }

        /// <summary>
        /// 锚点格子（左下角）+ footprint + 锚点 -> 根节点（框中心）的世界坐标
        /// 框中心对齐格子网格：偶数宽/高时落在格线上（半格偏移），奇数时落在格心
        /// </summary>
        public Vector2 GetPlacementWorldPos(Vector2Int anchorCell, Vector2Int footprint, bool rotated, Vector2Int pivot)
        {
            return GetPlacementWorldPos(anchorCell, footprint, rotated ? 1 : 0);
        }

        /// <summary>
        /// 锚点格子（左下角）+ footprint + 四档旋转 -> 根节点（框中心）的世界坐标
        /// </summary>
        public Vector2 GetPlacementWorldPos(Vector2Int anchorCell, Vector2Int footprint, int rotationSteps, Vector2Int pivot)
        {
            return GetPlacementWorldPos(anchorCell, footprint, rotationSteps);
        }

        // ==================== 查询 ====================

        /// <summary>
        /// 格子是否在可摆放区域内
        /// </summary>
        public bool IsInBounds(Vector2Int cell)
        {
            return placeableBounds.Contains(cell);
        }

        /// <summary>
        /// 获取占据某格子的物体；空闲返回 null
        /// </summary>
        public PlacedItem GetItemAt(Vector2Int cell)
        {
            occupiedCells.TryGetValue(cell, out PlacedItem item);
            return item;
        }

        /// <summary>
        /// 解析道具的实际占位：优先读 prefab 上 FootprintBoxView 组件的定义，无组件时用资产里的 footprint
        /// </summary>
        public Vector2Int ResolveFootprint(PlacableItemDef def)
        {
            if (def != null && def.Prefab != null)
            {
                FootprintBoxView box = def.Prefab.GetComponent<FootprintBoxView>();
                if (box != null)
                {
                    return box.Footprint;
                }
            }
            return def != null ? def.Footprint : Vector2Int.one;
        }

        /// <summary>
        /// 检测能否在指定锚点放置（区域内 + 全部占位格子空闲）
        /// allowOccupiedCells：允许落在被占据格子上（拆除类道具用）
        /// </summary>
        public bool CanPlace(PlacableItemDef def, Vector2Int anchorCell, bool rotated, bool allowOccupiedCells = false)
        {
            return CanPlace(def, anchorCell, rotated ? 1 : 0, allowOccupiedCells);
        }

        /// <summary>四档旋转版本的 CanPlace</summary>
        public bool CanPlace(PlacableItemDef def, Vector2Int anchorCell, int rotationSteps, bool allowOccupiedCells = false)
        {
            List<Vector2Int> cells = GetFootprintCells(anchorCell, ResolveFootprint(def), rotationSteps);
            foreach (Vector2Int cell in cells)
            {
                if (!placeableBounds.Contains(cell) || (!allowOccupiedCells && occupiedCells.ContainsKey(cell)))
                {
                    return false;
                }
            }
            // 区域限制：出生终点/水底/被占用区域不可放置
            if (GetZonesAt(cells).BlocksPlacement())
            {
                return false;
            }
            return true;
        }

        // ==================== 放置 / 移除（本地与网络回放共用入口） ====================

        /// <summary>
        /// 放置物体：实例化预制体并登记占据表
        /// 调用前应先通过 CanPlace 检测（网络回放时也建议检测，防止两端状态分叉）
        /// </summary>
        /// <returns>放置成功的物体；不合法返回 null</returns>
        public PlacedItem Place(PlacableItemDef def, Vector2Int anchorCell, bool rotated, int ownerPlayerId)
        {
            return Place(def, anchorCell, rotated ? 1 : 0, ownerPlayerId);
        }

        /// <summary>四档旋转版本的 Place（rotationSteps：0=0° 1=顺时针90° 2=180° 3=270°）</summary>
        public PlacedItem Place(PlacableItemDef def, Vector2Int anchorCell, int rotationSteps, int ownerPlayerId)
        {
            if (def == null || def.Prefab == null)
            {
                return null;
            }

            // 占位策略由道具自身声明（ItemBase）：拆除类允许叠放到目标上方
            SuperQQ.Item.ItemBase itemPolicy = def.Prefab.GetComponent<SuperQQ.Item.ItemBase>();
            if (!CanPlace(def, anchorCell, rotationSteps, itemPolicy != null && itemPolicy.AllowsOccupiedOverlap))
            {
                return null;
            }

            Vector2Int footprint = ResolveFootprint(def);
            Vector2Int pivot = ResolvePivot(def, footprint);
            Vector2 pos = GetPlacementWorldPos(anchorCell, footprint, rotationSteps, pivot);
            GameObject go = Instantiate(def.Prefab, pos, GetRotationQuaternion(rotationSteps), transform);

            PlacedItem item = go.GetComponent<PlacedItem>();
            if (item == null)
            {
                item = go.AddComponent<PlacedItem>();
            }
            item.Init(def, anchorCell, rotationSteps, ownerPlayerId);

            // 接入道具基类：注入放置信息并触发 OnPlaced 钩子
            SuperQQ.Item.ItemBase itemBase = go.GetComponent<SuperQQ.Item.ItemBase>();
            if (itemBase != null)
            {
                itemBase.InitPlaced(item, ((rotationSteps % 4) + 4) % 4);
                itemBase.OnPlaced();
            }

            // 登记占据：即放即消的道具（RegistersOccupancy = false，如拆除类）不持久占位
            if (itemBase == null || itemBase.RegistersOccupancy)
            {
                foreach (Vector2Int cell in GetFootprintCells(anchorCell, footprint, rotationSteps))
                {
                    occupiedCells[cell] = item;
                }
            }
            return item;
        }

        /// <summary>
        /// 检测一组格子是否可占用（区域内 + 全部空闲），供拖拽已有物体时使用
        /// </summary>
        public bool CanOccupy(Vector2Int anchorCell, Vector2Int footprint, bool rotated = false, bool allowOccupiedCells = false)
        {
            return CanOccupy(anchorCell, footprint, rotated ? 1 : 0, allowOccupiedCells);
        }

        /// <summary>四档旋转版本的 CanOccupy</summary>
        public bool CanOccupy(Vector2Int anchorCell, Vector2Int footprint, int rotationSteps, bool allowOccupiedCells = false)
        {
            List<Vector2Int> cells = GetFootprintCells(anchorCell, footprint, rotationSteps);
            foreach (Vector2Int cell in cells)
            {
                if (!placeableBounds.Contains(cell))
                {
                    return false;
                }
                // 拆除/附着类道具允许落在被占用格子上
                if (!allowOccupiedCells && occupiedCells.ContainsKey(cell))
                {
                    return false;
                }
            }
            // 区域限制：出生终点/水底/被占用区域不可放置
            if (GetZonesAt(cells).BlocksPlacement())
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 登记已有物体的占据（拖拽落点合法时调用，不实例化新物体）
        /// skipOccupiedCells：跳过已被其他物体占据的格子（拆除/附着类道具用，
        /// 避免覆盖被附着物体的占据记录）
        /// </summary>
        public void Occupy(Vector2Int anchorCell, Vector2Int footprint, PlacedItem owner, bool rotated = false, bool skipOccupiedCells = false)
        {
            Occupy(anchorCell, footprint, owner, rotated ? 1 : 0, skipOccupiedCells);
        }

        /// <summary>四档旋转版本的 Occupy</summary>
        public void Occupy(Vector2Int anchorCell, Vector2Int footprint, PlacedItem owner, int rotationSteps, bool skipOccupiedCells = false)
        {
            foreach (Vector2Int cell in GetFootprintCells(anchorCell, footprint, rotationSteps))
            {
                if (skipOccupiedCells && occupiedCells.ContainsKey(cell))
                {
                    continue;
                }
                occupiedCells[cell] = owner;
            }
        }

        /// <summary>
        /// 释放某物体登记的全部格子（开始拖拽已放置物体时调用）
        /// </summary>
        public void Release(PlacedItem owner)
        {
            if (owner == null)
            {
                return;
            }
            var toRemove = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, PlacedItem> pair in occupiedCells)
            {
                if (pair.Value == owner)
                {
                    toRemove.Add(pair.Key);
                }
            }
            foreach (Vector2Int cell in toRemove)
            {
                occupiedCells.Remove(cell);
            }
        }

        /// <summary>
        /// 移除占据某格子的物体（拾回/拆除），释放其全部占位格子
        /// </summary>
        public bool RemoveAt(Vector2Int cell)
        {
            PlacedItem item = GetItemAt(cell);
            if (item == null)
            {
                return false;
            }

            // 触发道具基类的移除钩子（清理运行状态）
            SuperQQ.Item.ItemBase itemBase = item.GetComponent<SuperQQ.Item.ItemBase>();
            if (itemBase != null)
            {
                itemBase.OnRemoved();
            }

            // 优先读实例上的 FootprintBoxView（与占据登记口径一致，Def 为 null 的拖拽放置也能正确释放），
            // 无组件时回退 Def 配置
            FootprintBoxView box = item.GetComponent<FootprintBoxView>();
            Vector2Int footprint = box != null ? box.Footprint : ResolveFootprint(item.Def);
            foreach (Vector2Int c in GetFootprintCells(item.AnchorCell, footprint, item.Rotation))
            {
                occupiedCells.Remove(c);
            }
            Destroy(item.gameObject);
            return true;
        }

        // ==================== 区域标记（Zone） ====================

        /// <summary>当前加载的区域配置（可能为 null）</summary>
        public LevelZoneConfig ZoneConfig => zoneConfig;

        // 水面垂直偏移（格数）：夜晚水位上升时由外部（MapDayNightController）设置，
        // 查询 Water 区域时把查询点向下偏移该格数，等效于水域整体上移
        private int waterYOffsetCells;

        /// <summary>
        /// 设置水面垂直偏移（格数，正值=水位上升）。
        /// 影响所有区域查询：Water 条目的判定范围随之上移。
        /// </summary>
        public void SetWaterYOffset(int cells)
        {
            waterYOffsetCells = cells;
        }

        /// <summary>当前水面垂直偏移（格数）</summary>
        public int WaterYOffsetCells => waterYOffsetCells;

        /// <summary>
        /// 加载区域配置资产（关卡初始化时可由外部注入，覆盖 Inspector 配置）
        /// </summary>
        public void LoadZoneConfig(LevelZoneConfig config)
        {
            zoneConfig = config;
        }

        /// <summary>
        /// 查询单个格子命中的区域类别（位或聚合，无命中返回 None）
        /// </summary>
        public GridZoneType GetZonesAt(Vector2Int cell)
        {
            GridZoneType result = GridZoneType.None;
            if (zoneConfig == null)
            {
                return result;
            }
            foreach (LevelZoneConfig.ZoneEntry zone in zoneConfig.Zones)
            {
                // 随水位移动的条目：查询点按水位偏移下移后判定，等效区域整体上移。
                // Water 条目始终随水位移动；riseWithWater 标记的条目（如 Boat 的占用区）同样随水位移动。
                Vector2Int queryCell = cell;
                if (waterYOffsetCells != 0 && ZoneMovesWithWater(zone))
                {
                    queryCell.y -= waterYOffsetCells;
                }
                if (zone.cells.Contains(queryCell))
                {
                    result |= zone.zoneType;
                }
            }
            return result;
        }

        /// <summary>
        /// 查询多个格子命中的区域类别（聚合结果）
        /// </summary>
        public GridZoneType GetZonesAt(IEnumerable<Vector2Int> cells)
        {
            GridZoneType result = GridZoneType.None;
            foreach (Vector2Int cell in cells)
            {
                result |= GetZonesAt(cell);
            }
            return result;
        }

        /// <summary>
        /// 查询一个格子矩形范围命中的区域类别（聚合结果）
        /// </summary>
        public GridZoneType GetZonesInRect(RectInt cellRect)
        {
            GridZoneType result = GridZoneType.None;
            if (zoneConfig == null)
            {
                return result;
            }
            foreach (LevelZoneConfig.ZoneEntry zone in zoneConfig.Zones)
            {
                // 随水位移动的条目：查询矩形按水位偏移下移后判定，与 GetZonesAt(cell) 口径一致
                RectInt queryRect = cellRect;
                if (waterYOffsetCells != 0 && ZoneMovesWithWater(zone))
                {
                    queryRect.y -= waterYOffsetCells;
                }
                if (RectOverlaps(zone.cells, queryRect))
                {
                    result |= zone.zoneType;
                }
            }
            return result;
        }

        /// <summary>
        /// 查询物体世界包围盒命中的区域类别（聚合结果）
        /// 包围盒换算成覆盖的格子范围后做矩形相交检测
        /// </summary>
        public GridZoneType GetZonesInBounds(Bounds worldBounds)
        {
            Vector2Int minCell = WorldToCell(worldBounds.min);
            // 减 1 像素防上/右边界多吃一格
            Vector2Int maxCell = WorldToCell(worldBounds.max - Vector3.one * 0.001f);
            var cellRect = new RectInt(
                minCell.x, minCell.y,
                maxCell.x - minCell.x + 1,
                maxCell.y - minCell.y + 1);
            return GetZonesInRect(cellRect);
        }

        /// <summary>该条目是否随夜晚水位移动：Water 类型始终移动；riseWithWater 标记的条目（如 Boat 占用区）同样移动</summary>
        private static bool ZoneMovesWithWater(LevelZoneConfig.ZoneEntry zone)
        {
            return (zone.zoneType & GridZoneType.Water) != 0 || zone.riseWithWater;
        }

        /// <summary>两个格子矩形是否相交</summary>
        private static bool RectOverlaps(RectInt a, RectInt b)
        {
            return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
        }

        // ==================== 网格可视化（运行时接口） ====================

        /// <summary>
        /// 显示网格（建造阶段调用）。首次调用时懒构建平铺层，之后仅开关显隐
        /// </summary>
        public void ShowGrid()
        {
            if (gridVisualRoot == null)
            {
                BuildGridVisual();
            }
            if (gridVisualRoot != null)
            {
                gridVisualRoot.SetActive(true);
            }
        }

        /// <summary>
        /// 关闭网格显示（跑动阶段调用）
        /// </summary>
        public void HideGrid()
        {
            if (gridVisualRoot != null)
            {
                gridVisualRoot.SetActive(false);
            }
        }

        /// <summary>网格当前是否可见</summary>
        public bool IsGridVisible => gridVisualRoot != null && gridVisualRoot.activeSelf;

        /// <summary>
        /// 构建运行时网格可视化：小格/大格两层平铺 + SpriteMask 裁切到可摆放区域
        /// </summary>
        private void BuildGridVisual()
        {
            float cs = CellSize;
            Vector2 boundsMin = Origin + new Vector2(placeableBounds.xMin, placeableBounds.yMin) * cs;
            Vector2 boundsSize = new Vector2(placeableBounds.width, placeableBounds.height) * cs;

            gridVisualRoot = new GameObject("GridVisual");
            gridVisualRoot.transform.SetParent(transform, false);

            // 遮罩：把平铺层裁切到可摆放区域内（1x1 白图程序生成，PPU=1 即 1 像素=1 米）
            var maskGo = new GameObject("GridMask");
            maskGo.transform.SetParent(gridVisualRoot.transform, false);
            maskGo.transform.position = boundsMin + boundsSize * 0.5f;
            var mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = CreateWhiteSprite();
            maskGo.transform.localScale = new Vector3(boundsSize.x, boundsSize.y, 1f);

            if (smallCellSprite != null)
            {
                CreateTiledLayer("SmallCells", smallCellSprite, boundsMin, boundsSize);
            }
            if (bigCellSprite != null)
            {
                CreateTiledLayer("BigCells", bigCellSprite, boundsMin, boundsSize);
            }
        }

        /// <summary>
        /// 创建一层平铺网格：tile 边界与格子线严格对齐（遮罩负责裁掉溢出部分）
        /// </summary>
        private void CreateTiledLayer(string name, Sprite tileSprite, Vector2 boundsMin, Vector2 boundsSize)
        {
            Vector2 tileSize = tileSprite.bounds.size;   // 由贴图 PPU 决定（0.5m / 2m）
            Vector2 pivotNorm = tileSprite.pivot / tileSprite.bounds.size;   // 归一化 pivot

            var go = new GameObject(name);
            go.transform.SetParent(gridVisualRoot.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tileSprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.sortingOrder = gridSortingOrder;
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

            // 对齐：Tiled 模式以 pivot 为平铺锚点，tile 线位于 pos + pivot偏移 + k*tileSize
            // 令一条 tile 线恰好压在 bounds 角上，再向四周各多铺 1 格防露边
            Vector2 pivotOffset = new Vector2(pivotNorm.x * tileSize.x, pivotNorm.y * tileSize.y);
            float posX = boundsMin.x + boundsSize.x * 0.5f;
            float posY = boundsMin.y + boundsSize.y * 0.5f;
            posX += Mathf.Repeat(boundsMin.x - pivotOffset.x - posX, tileSize.x);
            posY += Mathf.Repeat(boundsMin.y - pivotOffset.y - posY, tileSize.y);
            go.transform.position = new Vector3(posX, posY, 0f);

            sr.size = boundsSize + tileSize * 2f;
        }

        /// <summary>
        /// 程序生成 1x1 白色 Sprite（PPU=1，配合 transform.scale 充当任意尺寸矩形遮罩）
        /// </summary>
        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        // ==================== 场景可视化 ====================

        private void OnDrawGizmos()
        {
            if (!drawGrid)
            {
                return;
            }

            float cs = CellSize;
            Vector2 origin = Origin;

            // 网格线（仅画可摆放区域范围）
            Gizmos.color = gridLineColor;
            for (int x = 0; x <= placeableBounds.width; x++)
            {
                Vector3 a = new Vector3(origin.x + (placeableBounds.xMin + x) * cs, origin.y + placeableBounds.yMin * cs, 0f);
                Vector3 b = new Vector3(a.x, origin.y + placeableBounds.yMax * cs, 0f);
                Gizmos.DrawLine(a, b);
            }
            for (int y = 0; y <= placeableBounds.height; y++)
            {
                Vector3 a = new Vector3(origin.x + placeableBounds.xMin * cs, origin.y + (placeableBounds.yMin + y) * cs, 0f);
                Vector3 b = new Vector3(origin.x + placeableBounds.xMax * cs, a.y, 0f);
                Gizmos.DrawLine(a, b);
            }

            // 可摆放区域外框
            Gizmos.color = boundsColor;
            Vector3 bl = new Vector3(origin.x + placeableBounds.xMin * cs, origin.y + placeableBounds.yMin * cs, 0f);
            Vector3 tr = new Vector3(origin.x + placeableBounds.xMax * cs, origin.y + placeableBounds.yMax * cs, 0f);
            Vector3 br = new Vector3(tr.x, bl.y, 0f);
            Vector3 tl = new Vector3(bl.x, tr.y, 0f);
            Gizmos.DrawLine(bl, br);
            Gizmos.DrawLine(br, tr);
            Gizmos.DrawLine(tr, tl);
            Gizmos.DrawLine(tl, bl);

            // 被占据格子高亮
            if (Application.isPlaying)
            {
                Gizmos.color = occupiedColor;
                foreach (Vector2Int cell in occupiedCells.Keys)
                {
                    Vector3 center = CellToWorld(cell);
                    Gizmos.DrawCube(center, new Vector3(cs, cs, 0.01f));
                }
            }
        }
    }
}

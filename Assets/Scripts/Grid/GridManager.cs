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

        [Header("可视化")]
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private Color gridLineColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color boundsColor = new Color(0f, 1f, 1f, 0.6f);
        [SerializeField] private Color occupiedColor = new Color(1f, 0.3f, 0.3f, 0.35f);

        // 占据表：格子 -> 占据该格子的物体（一个 PlacedItem 会登记其 footprint 覆盖的所有格子）
        private readonly Dictionary<Vector2Int, PlacedItem> occupiedCells = new Dictionary<Vector2Int, PlacedItem>();

        // ==================== 生命周期 ====================

        private void Awake()
        {
            Instance = this;
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
            Vector2Int size = rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;
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
            Vector2Int size = rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;
            return Origin + new Vector2(anchorCell.x + size.x * 0.5f, anchorCell.y + size.y * 0.5f) * CellSize;
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
        /// </summary>
        public bool CanPlace(PlacableItemDef def, Vector2Int anchorCell, bool rotated)
        {
            List<Vector2Int> cells = GetFootprintCells(anchorCell, ResolveFootprint(def), rotated);
            foreach (Vector2Int cell in cells)
            {
                if (!placeableBounds.Contains(cell) || occupiedCells.ContainsKey(cell))
                {
                    return false;
                }
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
            if (def == null || def.Prefab == null || !CanPlace(def, anchorCell, rotated))
            {
                return null;
            }

            Vector2Int footprint = ResolveFootprint(def);
            Vector2 pos = GetPlacementWorldPos(anchorCell, footprint, rotated);
            Quaternion rot = rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
            GameObject go = Instantiate(def.Prefab, pos, rot, transform);

            PlacedItem item = go.GetComponent<PlacedItem>();
            if (item == null)
            {
                item = go.AddComponent<PlacedItem>();
            }
            item.Init(def, anchorCell, rotated, ownerPlayerId);

            // 接入道具基类：注入放置信息并触发 OnPlaced 钩子
            SuperQQ.Item.ItemBase itemBase = go.GetComponent<SuperQQ.Item.ItemBase>();
            if (itemBase != null)
            {
                itemBase.InitPlaced(item, rotated ? 1 : 0);
                itemBase.OnPlaced();
            }

            foreach (Vector2Int cell in GetFootprintCells(anchorCell, footprint, rotated))
            {
                occupiedCells[cell] = item;
            }
            return item;
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

            foreach (Vector2Int c in GetFootprintCells(item.AnchorCell, ResolveFootprint(item.Def), item.Rotated))
            {
                occupiedCells.Remove(c);
            }
            Destroy(item.gameObject);
            return true;
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

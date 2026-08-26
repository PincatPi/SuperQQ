using System.Collections.Generic;
using SuperQQ.Grid;
using SuperQQ.Item;
using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// 铃铛吸附点 — 挂在 Map 的 Bell.000 上
    /// 定义一个随铃铛移动的格子作为吸附点（吸附格 = 铃铛当前位置 + 格偏移，
    /// 铃铛白天升起/夜晚下降时吸附格同步移动）。
    /// 吸附判定：占用吸附格的道具，以及占用吸附格四边相邻格的道具（贴着吸附格边缘，
    /// 与黄油黏性边语义一致）都会被吸附为铃铛的子物体
    /// （CanBeStuckAt 过滤 + OnStuckTo/OnUnstuck 钩子），
    /// 白天钟收起时被吸附道具随之一同上升，夜晚回落随之下降。
    /// </summary>
    public class BellStickyPoint : MonoBehaviour
    {
        [Header("吸附点")]
        [Tooltip("吸附点格子相对铃铛位置的格偏移（x 向右，y 向上；吸附格随铃铛移动）")]
        [SerializeField] private Vector2Int attachCellOffset = Vector2Int.zero;
        [Tooltip("吸附检测间隔（秒），检测吸附格中新出现的道具并吸附")]
        [SerializeField, Range(0.02f, 0.5f)] private float checkInterval = 0.1f;

        [Header("调试")]
        [Tooltip("Scene 视图绘制吸附点格子（青色线框）")]
        [SerializeField] private bool drawGizmo = true;

        private float nextCheckTime;
        private readonly List<Transform> stuckItems = new List<Transform>();

        /// <summary>
        /// 解析网格管理器：运行时用单例；编辑模式单例未初始化（Awake 未执行），
        /// 直接在场景中查找组件实例（坐标换算只读其序列化配置，编辑模式同样有效）
        /// </summary>
        private GridManager ResolveGrid()
        {
            if (GridManager.Instance != null)
            {
                return GridManager.Instance;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return UnityEngine.Object.FindFirstObjectByType<GridManager>();
            }
#endif
            return null;
        }

        /// <summary>吸附点格子的世界中心坐标（未换算格子前的连续位置）</summary>
        public Vector2 AttachWorldPos
        {
            get
            {
                GridManager grid = ResolveGrid();
                float cs = grid != null ? grid.PublicCellSize : 1f;
                return (Vector2)transform.position
                    + new Vector2(attachCellOffset.x * cs, attachCellOffset.y * cs);
            }
        }

        /// <summary>当前吸附点所在格子（随铃铛移动而变化）</summary>
        public Vector2Int AttachCell
        {
            get
            {
                GridManager grid = ResolveGrid();
                return grid != null ? grid.WorldToCell(AttachWorldPos) : Vector2Int.zero;
            }
        }

        private void LateUpdate()
        {
            if (Time.time < nextCheckTime)
            {
                return;
            }
            nextCheckTime = Time.time + checkInterval;

            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return;
            }

            // 铃铛升降过渡中不吸附：吸附格随铃铛扫过中间格，会误吸经过的道具并带入巨大偏移
            MapDayNightController dayNight = MapDayNightController.Instance;
            if (dayNight != null && dayNight.Blend > 0.001f && dayNight.Blend < 0.999f)
            {
                return;
            }

            // 清理已被拆除/销毁的吸附对象
            stuckItems.RemoveAll(t => t == null);

            // 吸附判定：吸附格本身 + 四边相邻格（贴着吸附格边缘的道具也算被吸附，同黄油黏性边语义）
            Vector2Int cell = AttachCell;
            TryStickAt(grid, cell);
            TryStickAt(grid, cell + Vector2Int.up);
            TryStickAt(grid, cell + Vector2Int.down);
            TryStickAt(grid, cell + Vector2Int.left);
            TryStickAt(grid, cell + Vector2Int.right);
        }

        /// <summary>检测指定格子，有合法道具则吸附（占用该格即视为候选；受限道具按 CanBeStuckAt 判定吸附点）</summary>
        private void TryStickAt(GridManager grid, Vector2Int cell)
        {
            PlacedItem candidate = grid.GetItemAt(cell);
            if (candidate == null || stuckItems.Contains(candidate.transform))
            {
                return;
            }
            // 跳过声明不可被吸附、或吸附点不匹配的道具（与黄油同一过滤规则）
            ItemBase item = candidate.GetComponent<ItemBase>();
            if (item != null && !item.CanBeStuckAt(cell))
            {
                return;
            }

            StickItem(candidate, item, cell, grid);
        }

        /// <summary>
        /// 吸附一个道具：设为铃铛子物体并补偿父级缩放
        /// （Bell.000 自带约 0.5 缩放，直接挂上去道具会缩一半；
        /// 道具的匹配格==吸附格是吸附成立的前提，位置天然已对齐，无需再挪动）
        /// </summary>
        private void StickItem(PlacedItem candidate, ItemBase item, Vector2Int cell, GridManager grid)
        {
            Transform t = candidate.transform;

            Vector3 worldScale = t.lossyScale;
            t.SetParent(transform, worldPositionStays: true);
            Vector3 parentScale = transform.lossyScale;
            t.localScale = new Vector3(
                parentScale.x != 0f ? worldScale.x / parentScale.x : t.localScale.x,
                parentScale.y != 0f ? worldScale.y / parentScale.y : t.localScale.y,
                parentScale.z != 0f ? worldScale.z / parentScale.z : t.localScale.z);

            stuckItems.Add(t);
            item?.OnStuckTo(transform, cell);
            Debug.Log($"[BellStickyPoint] 吸附道具: {candidate.name} 吸附格={cell}", this);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo)
            {
                return;
            }
            GridManager grid = ResolveGrid();
            if (grid != null)
            {
                // 画换算后的实际吸附格区域（与判定完全一致：整格对齐网格，编辑/运行模式一致）
                float cs = grid.PublicCellSize;
                Vector2Int cell = AttachCell;
                Vector2 cellCenter = grid.GetPlacementWorldPos(cell, Vector2Int.one, 0);
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
                Gizmos.DrawWireCube(cellCenter, new Vector3(cs, cs, 0.1f));
                // 四边相邻格（贴着边缘也会被吸附）画淡色线框
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
                Gizmos.DrawWireCube(grid.GetPlacementWorldPos(cell + Vector2Int.up, Vector2Int.one, 0), new Vector3(cs, cs, 0.1f));
                Gizmos.DrawWireCube(grid.GetPlacementWorldPos(cell + Vector2Int.down, Vector2Int.one, 0), new Vector3(cs, cs, 0.1f));
                Gizmos.DrawWireCube(grid.GetPlacementWorldPos(cell + Vector2Int.left, Vector2Int.one, 0), new Vector3(cs, cs, 0.1f));
                Gizmos.DrawWireCube(grid.GetPlacementWorldPos(cell + Vector2Int.right, Vector2Int.one, 0), new Vector3(cs, cs, 0.1f));
                // 连续锚点（铃铛位置+偏移）画小叉，便于调整偏移量时对照落格
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                Vector2 anchor = AttachWorldPos;
                float r = cs * 0.15f;
                Gizmos.DrawLine(anchor + new Vector2(-r, -r), anchor + new Vector2(r, r));
                Gizmos.DrawLine(anchor + new Vector2(-r, r), anchor + new Vector2(r, -r));
            }
            else
            {
                // 场景中无 GridManager：画 1x1 示意框
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
                Gizmos.DrawWireCube(AttachWorldPos, new Vector3(1f, 1f, 0.1f));
            }
        }
    }
}

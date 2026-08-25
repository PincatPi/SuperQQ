using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 网格区域标记 — 编辑期辅助组件（不进入运行时数据流）
    /// 在场景中圈定一片格子区域的类别（出生终点/水底/被占用），
    /// 搭好所有标记后，通过 GridManager 的"生成区域资产"按钮烘焙为 LevelZoneConfig 资产；
    /// 运行时的区域判定以资产为准，场景中可保留标记做参照（Gizmos 常显）
    /// </summary>
    public class GridZoneMarker : MonoBehaviour
    {
        [Header("区域定义")]
        [Tooltip("区域类别（可多选）")]
        [SerializeField] private GridZoneType zoneType = GridZoneType.SpawnGoal;
        [Tooltip("区域覆盖的格子范围（格子坐标，x/y 为左下角，width/height 为格数）")]
        [SerializeField] private RectInt cells = new RectInt(0, 0, 2, 1);

        /// <summary>区域类别</summary>
        public GridZoneType ZoneType => zoneType;
        /// <summary>区域覆盖的格子范围</summary>
        public RectInt Cells => cells;

        /// <summary>区域颜色（Gizmos / 编辑器显示用）</summary>
        public Color DisplayColor
        {
            get
            {
                if ((zoneType & GridZoneType.AttachSurface) != 0) return new Color(1f, 0.6f, 0.2f, 0.35f);
                if ((zoneType & GridZoneType.Water) != 0) return new Color(0.2f, 0.5f, 1f, 0.35f);
                if ((zoneType & GridZoneType.SpawnGoal) != 0) return new Color(0.3f, 1f, 0.3f, 0.35f);
                return new Color(0.6f, 0.6f, 0.6f, 0.35f);
            }
        }

        // ==================== 编辑期可视化 ====================

        private void OnDrawGizmos()
        {
            GridManager gm = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
            if (gm == null)
            {
                return;
            }

            float cs = gm.PublicCellSize;
            Vector2 origin = gm.PublicOrigin;
            Vector3 min = new Vector3(origin.x + cells.xMin * cs, origin.y + cells.yMin * cs, 0f);
            Vector3 max = new Vector3(origin.x + cells.xMax * cs, origin.y + cells.yMax * cs, 0f);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;

            Gizmos.color = DisplayColor;
            Gizmos.DrawCube(center, size);

            Color edge = DisplayColor;
            edge.a = 0.9f;
            Gizmos.color = edge;
            Gizmos.DrawWireCube(center, size);
        }
    }
}

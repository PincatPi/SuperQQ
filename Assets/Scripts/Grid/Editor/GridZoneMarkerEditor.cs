using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// GridZoneMarker 编辑器工具
    /// Scene 视图提供两个角的拖拽控制柄，拖动即圈定区域（自动吸附格子线）；
    /// Inspector 提供"按场景物体包围盒生成"按钮，方便直接框选已有地形
    /// </summary>
    [CustomEditor(typeof(GridZoneMarker))]
    public class GridZoneMarkerEditor : Editor
    {
        private GridZoneMarker Marker => (GridZoneMarker)target;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("在 Scene 视图中拖拽区域的左下角/右上角控制柄来圈定范围（自动对齐格子）。", MessageType.Info);

            if (GUILayout.Button("选中场景物体 → 按其包围盒生成区域"))
            {
                GenerateFromSelection();
            }
        }

        private void OnSceneGUI()
        {
            GridManager gm = GridManager.Instance != null ? GridManager.Instance : Object.FindObjectOfType<GridManager>();
            if (gm == null)
            {
                return;
            }

            float cs = gm.PublicCellSize;
            Vector2 origin = gm.PublicOrigin;
            RectInt cells = Marker.Cells;

            Vector3 minPos = new Vector3(origin.x + cells.xMin * cs, origin.y + cells.yMin * cs, 0f);
            Vector3 maxPos = new Vector3(origin.x + cells.xMax * cs, origin.y + cells.yMax * cs, 0f);

            // 中心移动柄：整体平移区域，按格吸附（大小不变）
            Vector3 centerPos = (minPos + maxPos) * 0.5f;
            EditorGUI.BeginChangeCheck();
            Handles.color = Marker.DisplayColor;
            Handles.Label(centerPos, $"{cells.width}x{cells.height}");
            Vector3 newCenter = Handles.PositionHandle(centerPos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 delta = newCenter - centerPos;
                int dx = Mathf.RoundToInt(delta.x / cs);
                int dy = Mathf.RoundToInt(delta.y / cs);
                if (dx != 0 || dy != 0)
                {
                    Undo.RecordObject(Marker, "移动网格区域");
                    SetCells(new RectInt(cells.x + dx, cells.y + dy, cells.width, cells.height));
                }
            }

            // 角控制柄：调整区域大小
            EditorGUI.BeginChangeCheck();
            Vector3 newMin = CornerHandle(minPos, "左下角");
            Vector3 newMax = CornerHandle(maxPos, "右上角");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Marker, "调整网格区域");

                // 世界坐标 → 格子边界序号（Round 吸附格子线），min/max 自动排序
                int x1 = Mathf.RoundToInt((newMin.x - origin.x) / cs);
                int y1 = Mathf.RoundToInt((newMin.y - origin.y) / cs);
                int x2 = Mathf.RoundToInt((newMax.x - origin.x) / cs);
                int y2 = Mathf.RoundToInt((newMax.y - origin.y) / cs);

                int xMin = Mathf.Min(x1, x2);
                int yMin = Mathf.Min(y1, y2);
                int xMax = Mathf.Max(x1, x2);
                int yMax = Mathf.Max(y1, y2);

                // 保证至少 1x1 格
                if (xMax <= xMin) xMax = xMin + 1;
                if (yMax <= yMin) yMax = yMin + 1;

                SetCells(new RectInt(xMin, yMin, xMax - xMin, yMax - yMin));
            }
        }

        /// <summary>
        /// 绘制一个可拖拽的角控制柄并返回新位置
        /// </summary>
        private Vector3 CornerHandle(Vector3 position, string label)
        {
            Handles.color = Marker.DisplayColor;
            Handles.Label(position + Vector3.up * 0.2f, label);
            var fmh_77_53_639221444917525907 = Quaternion.identity; return Handles.FreeMoveHandle(position, 0.15f, Vector3.zero, Handles.RectangleHandleCap);
        }

        /// <summary>
        /// 按当前选中的场景物体包围盒生成区域（取第一个带 Renderer/Collider2D 的选中物）
        /// </summary>
        private void GenerateFromSelection()
        {
            GridManager gm = GridManager.Instance != null ? GridManager.Instance : Object.FindObjectOfType<GridManager>();
            if (gm == null || Selection.activeGameObject == null || Selection.activeGameObject == Marker.gameObject)
            {
                Debug.LogWarning("[GridZoneMarker] 请先在 Hierarchy 中选中一个地形/参照物体");
                return;
            }

            Bounds? bounds = null;
            var renderer = Selection.activeGameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
            }
            else
            {
                var col = Selection.activeGameObject.GetComponent<Collider2D>();
                if (col != null)
                {
                    bounds = col.bounds;
                }
            }
            if (!bounds.HasValue)
            {
                Debug.LogWarning("[GridZoneMarker] 选中物体没有 Renderer 或 Collider2D，无法计算包围盒");
                return;
            }

            float cs = gm.PublicCellSize;
            Vector2 origin = gm.PublicOrigin;
            Bounds b = bounds.Value;

            int xMin = Mathf.FloorToInt((b.min.x - origin.x) / cs);
            int yMin = Mathf.FloorToInt((b.min.y - origin.y) / cs);
            int xMax = Mathf.CeilToInt((b.max.x - origin.x) / cs);
            int yMax = Mathf.CeilToInt((b.max.y - origin.y) / cs);

            Undo.RecordObject(Marker, "按包围盒生成区域");
            SetCells(new RectInt(xMin, yMin, Mathf.Max(xMax - xMin, 1), Mathf.Max(yMax - yMin, 1)));
            Debug.Log($"[GridZoneMarker] 已按 {Selection.activeGameObject.name} 的包围盒生成区域");
        }

        private void SetCells(RectInt cells)
        {
            SerializedProperty prop = serializedObject.FindProperty("cells");
            serializedObject.Update();
            prop.rectIntValue = cells;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(Marker);
        }
    }
}

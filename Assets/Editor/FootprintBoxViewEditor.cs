using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// FootprintBoxView 的编辑器扩展：在 Scene 视图中直接点击格子拾取锚点。
    /// Inspector 中勾选"拾取锚点格子"后，Scene 视图内 footprint 每个格子显示可点击按钮，
    /// 点击哪个格子就把 pivotCell 设为哪个；再次点击已选中的锚点可恢复自动（-1,-1）。
    /// </summary>
    [CustomEditor(typeof(FootprintBoxView))]
    public class FootprintBoxViewEditor : Editor
    {
        private bool picking;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var view = (FootprintBoxView)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("锚点格子", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("当前锚点", view.PivotCell.ToString());

            picking = GUILayout.Toggle(picking, picking ? "拾取中…（点击 Scene 视图格子）" : "在 Scene 视图中拾取锚点格子", "Button");
            if (!picking)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.HelpBox("拾取模式下点击 Scene 视图中的格子即确认设置锚点（包围盒保持原位，锚点移到点击的格子）；再次点击已选中的锚点恢复自动（中心格子）；Ctrl+Z 可撤销。", MessageType.Info);
            GUI.enabled = true;

            if (GUILayout.Button("恢复自动锚点（中心格子）"))
            {
                Undo.RecordObject(view, "Reset Pivot Cell");
                SerializedProperty prop = serializedObject.FindProperty("pivotCell");
                prop.vector2IntValue = new Vector2Int(-1, -1);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(view);
            }
        }

        private void OnSceneGUI()
        {
            if (!picking)
            {
                return;
            }

            var view = (FootprintBoxView)target;
            float cs = ResolveCellSize(view);
            Vector2Int footprint = view.Footprint;
            Vector2Int currentPivot = view.PivotCell;

            // footprint 矩形以根节点（sprite 中心）为基准排布，与锚点选择无关
            Vector3 rectMin = view.transform.position - new Vector3(footprint.x * cs * 0.5f, footprint.y * cs * 0.5f, 0f);

            // 拾取模式下面板不穿透点击
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            for (int x = 0; x < footprint.x; x++)
            {
                for (int y = 0; y < footprint.y; y++)
                {
                    Vector3 cellCenter = rectMin + new Vector3((x + 0.5f) * cs, (y + 0.5f) * cs, 0f);
                    bool isPivot = x == currentPivot.x && y == currentPivot.y;

                    Handles.color = isPivot ? Color.yellow : new Color(1f, 1f, 1f, 0.35f);
                    if (Handles.Button(cellCenter, Quaternion.identity, cs * 0.3f, cs * 0.4f, Handles.CubeHandleCap))
                    {
                        // 再点一次已选中的锚点 → 恢复自动（中心格子）
                        Vector2Int newPivot = isPivot
                            ? new Vector2Int((footprint.x - 1) / 2, (footprint.y - 1) / 2)
                            : new Vector2Int(x, y);

                        Undo.RecordObject(view, "Pick Pivot Cell");
                        Undo.RecordObject(view.transform, "Pick Pivot Cell");

                        SerializedObject so = new SerializedObject(view);
                        SerializedProperty prop = so.FindProperty("pivotCell");
                        prop.vector2IntValue = isPivot ? new Vector2Int(-1, -1) : newPivot;
                        so.ApplyModifiedProperties();

                        // 移动根节点，使新锚点格子落在当前根节点位置：
                        // 框随 transform 平移，点击的格子成为旋转中心（对齐当前根节点处）
                        Vector2Int delta = newPivot - currentPivot;
                        view.transform.position -= new Vector3(delta.x * cs, delta.y * cs, 0f);

                        EditorUtility.SetDirty(view);
                    }

                    Handles.color = Color.white;
                    Handles.Label(cellCenter + new Vector3(-cs * 0.35f, -cs * 0.35f, 0f), $"({x},{y})");
                }
            }

            SceneView.RepaintAll();
        }

        private static float ResolveCellSize(FootprintBoxView view)
        {
            if (GridManager.Instance != null)
            {
                return GridManager.Instance.PublicCellSize;
            }
            GridManager gm = FindObjectOfType<GridManager>();
            return gm != null ? gm.PublicCellSize : 0.5f;
        }
    }
}

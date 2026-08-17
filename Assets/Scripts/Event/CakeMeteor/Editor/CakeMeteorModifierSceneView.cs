using UnityEditor;
using UnityEngine;

namespace SuperQQ.Event.EditorTools
{
    /// <summary>
    /// CakeMeteorModifier 的 Scene 视图可视化（编辑器专用，不进入打包）
    /// 在 Project 窗口选中 CakeMeteorModifier 资产时（无需进入 Play 模式），
    /// 在 Scene 视图中绘制陨石生成源的中心位置与左右随机偏移范围，
    /// 并提供拖拽手柄直接调节中心位置与左右偏移距离（支持 Undo）
    /// </summary>
    [InitializeOnLoad]
    public static class CakeMeteorModifierSceneView
    {
        static CakeMeteorModifierSceneView()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            // 仅在选中 CakeMeteorModifier 资产时绘制
            // 注意：需全限定 UnityEditor.Selection，项目中的 SuperQQ.Selection 会遮蔽该类型
            if (UnityEditor.Selection.activeObject is not CakeMeteorModifier modifier)
            {
                return;
            }

            SerializedObject so = new SerializedObject(modifier);
            SerializedProperty centerProp = so.FindProperty("_spawnCenter");
            SerializedProperty leftProp = so.FindProperty("_leftOffset");
            SerializedProperty rightProp = so.FindProperty("_rightOffset");
            if (centerProp == null || leftProp == null || rightProp == null)
            {
                return;
            }

            so.Update();

            Vector3 center = centerProp.vector2Value;
            Vector3 leftEnd = center + Vector3.left * leftProp.floatValue;
            Vector3 rightEnd = center + Vector3.right * rightProp.floatValue;

            // ==================== 可拖拽手柄（分别检测变更，互不干扰） ====================

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                centerProp.vector2Value = (Vector2)newCenter;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newLeftEnd = Handles.PositionHandle(leftEnd, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                leftProp.floatValue = Mathf.Max(0f, center.x - newLeftEnd.x);
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newRightEnd = Handles.PositionHandle(rightEnd, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                rightProp.floatValue = Mathf.Max(0f, newRightEnd.x - center.x);
            }

            so.ApplyModifiedProperties();

            // ==================== 可视化绘制 ====================

            // 随机生成区间：中心到左右端点的连线与端点刻度
            Handles.color = Color.cyan;
            Handles.DrawLine(leftEnd, rightEnd, 2f);
            DrawEndTick(leftEnd);
            DrawEndTick(rightEnd);

            // 中心标记与下落方向指示
            Handles.color = Color.yellow;
            float handleSize = HandleUtility.GetHandleSize(center) * 0.15f;
            Handles.SphereHandleCap(0, center, Quaternion.identity, handleSize, EventType.Repaint);
            Handles.DrawLine(center, center + Vector3.down * (handleSize * 6f), 1.5f);

            // 标注文字
            GUIStyle labelStyle = new GUIStyle(EditorStyles.whiteBoldLabel);
            Handles.Label(center + Vector3.up * (handleSize * 3f), "陨石生成中心", labelStyle);
            Handles.Label(leftEnd + Vector3.down * (handleSize * 3f), $"左偏移 {leftProp.floatValue:F1}", labelStyle);
            Handles.Label(rightEnd + Vector3.down * (handleSize * 3f), $"右偏移 {rightProp.floatValue:F1}", labelStyle);
        }

        /// <summary>
        /// 在区间端点绘制竖直刻度线
        /// </summary>
        private static void DrawEndTick(Vector3 end)
        {
            float tickHalf = HandleUtility.GetHandleSize(end) * 0.25f;
            Handles.DrawLine(end + Vector3.up * tickHalf, end + Vector3.down * tickHalf, 2f);
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace SuperQQ.Event.EditorTools
{
    /// <summary>
    /// MagicCircleModifier 的 Scene 视图可视化（编辑器专用，不进入打包）
    /// 在 Project 窗口选中 MagicCircleModifier 资产时（无需进入 Play 模式），
    /// 在 Scene 视图中标注法阵的固定位置与吟唱提示框的相对偏移，
    /// 并提供拖拽手柄直接调节（支持 Undo）
    /// </summary>
    [InitializeOnLoad]
    public static class MagicCircleModifierSceneView
    {
        static MagicCircleModifierSceneView()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            // 仅在选中 MagicCircleModifier 资产时绘制
            // 注意：需全限定 UnityEditor.Selection，项目中的 SuperQQ.Selection 会遮蔽该类型
            if (UnityEditor.Selection.activeObject is not MagicCircleModifier modifier)
            {
                return;
            }

            SerializedObject so = new SerializedObject(modifier);
            SerializedProperty positionProp = so.FindProperty("_circlePosition");
            SerializedProperty offsetProp = so.FindProperty("_promptOffset");
            if (positionProp == null || offsetProp == null)
            {
                return;
            }

            so.Update();

            Vector3 position = positionProp.vector2Value;
            Vector3 promptPosition = position + (Vector3)offsetProp.vector2Value;

            // ==================== 可拖拽手柄（分别检测变更，互不干扰） ====================

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                positionProp.vector2Value = (Vector2)newPosition;
            }

            // 提示框手柄显示在 法阵位置+偏移 处，拖动手柄即修改偏移量
            EditorGUI.BeginChangeCheck();
            Vector3 newPromptPosition = Handles.PositionHandle(promptPosition, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                offsetProp.vector2Value = (Vector2)(newPromptPosition - position);
            }

            so.ApplyModifiedProperties();

            // ==================== 可视化绘制 ====================

            GUIStyle labelStyle = new GUIStyle(EditorStyles.whiteBoldLabel);

            // 法阵位置标记（黄色圆点 + 定位十字线）
            float handleSize = HandleUtility.GetHandleSize(position) * 0.15f;
            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, position, Quaternion.identity, handleSize, EventType.Repaint);
            float crossHalf = handleSize * 4f;
            Handles.DrawLine(position + Vector3.left * crossHalf, position + Vector3.right * crossHalf, 1.5f);
            Handles.DrawLine(position + Vector3.down * crossHalf, position + Vector3.up * crossHalf, 1.5f);
            Handles.Label(position + Vector3.up * crossHalf, "法阵位置", labelStyle);

            // 提示框偏移标记（青色圆点 + 与法阵的连线）
            float promptHandleSize = HandleUtility.GetHandleSize(promptPosition) * 0.12f;
            Handles.color = Color.cyan;
            Handles.DrawLine(position, promptPosition, 1.5f);
            Handles.SphereHandleCap(0, promptPosition, Quaternion.identity, promptHandleSize, EventType.Repaint);
            Handles.Label(promptPosition + Vector3.up * (promptHandleSize * 4f), "提示框位置", labelStyle);
        }
    }
}

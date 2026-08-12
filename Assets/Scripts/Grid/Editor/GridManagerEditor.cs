using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// GridManager 编辑器工具
    /// 提供"从场景标记生成区域资产"按钮：收集场景中全部 GridZoneMarker，
    /// 烘焙为 LevelZoneConfig 资产并自动赋给 GridManager
    /// </summary>
    [CustomEditor(typeof(GridManager))]
    public class GridManagerEditor : Editor
    {
        private GridManager Manager => (GridManager)target;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("区域烘焙", EditorStyles.boldLabel);

            int markerCount = Object.FindObjectsOfType<GridZoneMarker>(true).Length;
            EditorGUILayout.HelpBox($"当前场景有 {markerCount} 个 GridZoneMarker 标记。", MessageType.None);

            GUI.enabled = markerCount > 0;
            if (GUILayout.Button("从场景标记生成区域资产"))
            {
                BakeZoneConfig();
            }
            GUI.enabled = true;
        }

        /// <summary>
        /// 收集场景中全部 GridZoneMarker，烘焙为 LevelZoneConfig 资产并赋给 GridManager
        /// </summary>
        private void BakeZoneConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存区域配置资产",
                "LevelZoneConfig",
                "asset",
                "选择区域配置资产的保存位置",
                "Assets/ScriptableObject/Grid");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var entries = new List<LevelZoneConfig.ZoneEntry>();
            foreach (GridZoneMarker marker in Object.FindObjectsOfType<GridZoneMarker>(true))
            {
                entries.Add(new LevelZoneConfig.ZoneEntry
                {
                    zoneType = marker.ZoneType,
                    cells = marker.Cells
                });
            }

            // 已存在资产则覆盖，否则新建
            LevelZoneConfig config = AssetDatabase.LoadAssetAtPath<LevelZoneConfig>(path);
            if (config == null)
            {
                config = CreateInstance<LevelZoneConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            config.SetZones(entries);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            // 自动赋给 GridManager
            SerializedProperty prop = serializedObject.FindProperty("zoneConfig");
            serializedObject.Update();
            prop.objectReferenceValue = config;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(Manager);

            Debug.Log($"[GridManager] 已烘焙 {entries.Count} 个区域到 {path} 并赋值给 GridManager");
        }
    }
}

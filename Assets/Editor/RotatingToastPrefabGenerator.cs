using SuperQQ.Grid;
using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// 旋转吐司 prefab 生成器：菜单 SuperQQ/生成旋转吐司Prefab
    /// 结构：根(道具逻辑+占位+站立碰撞+拖拽吸附)
    ///   └─ Visual（素材，1x1 基准 0.5m，SetSize 时按比例放大）
    /// 默认 1x1；每轮尺寸由 RotatingToastSizeSync 决定后自动应用
    /// </summary>
    public static class RotatingToastPrefabGenerator
    {
        private const string SpritePath = "Assets/Arts/Item/Bread.png";
        private const string PrefabPath = "Assets/Prefab/Item/RotatingToast.prefab";

        // 1x1 基准尺寸，cellSize=0.5 → 0.5m x 0.5m
        private const float baseSize = 0.5f;

        [MenuItem("SuperQQ/生成旋转吐司Prefab")]
        public static void Generate()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError($"[RotatingToastPrefabGenerator] 找不到素材 {SpritePath}");
                return;
            }

            var root = new GameObject("RotatingToast");
            try
            {
                root.layer = 9; // 站立地面层

                root.AddComponent<Item.RotatingToast>();

                // 注意：编辑器下 AddComponent<PlacementController>() 会因 RequireComponent
                // 重复执行导致补挂两份，这里先查再补，保证只有一个
                if (root.GetComponent<PlacementController>() == null)
                {
                    root.AddComponent<PlacementController>();
                }
                // 若 RequireComponent 已补挂出多余实例，移除多余的只留一个
                var pcs = root.GetComponents<PlacementController>();
                for (int i = 1; i < pcs.Length; i++)
                {
                    Object.DestroyImmediate(pcs[i]);
                }

                var box = root.GetComponent<FootprintBoxView>();
                var boxSo = new SerializedObject(box);
                boxSo.FindProperty("footprint").vector2IntValue = Vector2Int.one;
                boxSo.FindProperty("canRotate").boolValue = false;           // 放置不可旋转（自身持续转动）
                boxSo.FindProperty("pivotCell").vector2IntValue = new Vector2Int(-1, -1);
                boxSo.ApplyModifiedPropertiesWithoutUndo();

                var solid = root.GetComponent<BoxCollider2D>();
                solid.size = Vector2.one * baseSize;

                // ---------- Visual ----------
                var visual = new GameObject("Visual");
                visual.layer = 8;
                visual.transform.SetParent(root.transform, false);
                var sr = visual.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                Vector2 spriteSize = sprite.bounds.size;
                // 以短边对齐 0.5m，保持宽高比
                float scale = baseSize / Mathf.Max(spriteSize.x, spriteSize.y);
                visual.transform.localScale = new Vector3(scale, scale, 1f);

                // ---------- 保存（先删旧资产，避免覆盖时残留历史组件） ----------
                AssetDatabase.DeleteAsset(PrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[RotatingToastPrefabGenerator] 已生成 {PrefabPath}", saved);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

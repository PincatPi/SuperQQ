using SuperQQ.Grid;
using SuperQQ.Item;
using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// 肥皂道具 prefab 生成器：菜单 SuperQQ/生成肥皂Prefab
    /// 结构对齐 ButterBlock：根(肥皂逻辑+占位+站立碰撞+拖拽吸附)
    ///   ├─ Visual（素材，缩放到 1x0.5 米）
    ///   └─ HitZones/StandZone（Trigger + SoapSurface 无摩擦表面）
    /// </summary>
    public static class SoapPrefabGenerator
    {
        private const string SpritePath = "Assets/Arts/Item/Soap.png";
        private const string PrefabPath = "Assets/Prefab/Item/Soap.prefab";

        // 2x1 格，cellSize=0.5 → 世界尺寸 1m x 0.5m
        private static readonly Vector2 worldSize = new Vector2(1f, 0.5f);

        [MenuItem("SuperQQ/生成肥皂Prefab")]
        public static void Generate()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError($"[SoapPrefabGenerator] 找不到素材 {SpritePath}");
                return;
            }

            // ---------- 根 ----------
            var root = new GameObject("Soap");
            try
            {
                root.layer = 9; // 站立地面层（与 ButterBlock 一致）

                root.AddComponent<SoapItem>();

                var box = root.AddComponent<FootprintBoxView>();
                var boxSo = new SerializedObject(box);
                boxSo.FindProperty("footprint").vector2IntValue = new Vector2Int(2, 1);
                boxSo.FindProperty("canRotate").boolValue = false;
                boxSo.FindProperty("pivotCell").vector2IntValue = new Vector2Int(-1, -1);
                boxSo.ApplyModifiedPropertiesWithoutUndo();

                var solid = root.AddComponent<BoxCollider2D>();
                solid.size = worldSize;

                root.AddComponent<PlacementController>();

                // ---------- Visual ----------
                var visual = new GameObject("Visual");
                visual.layer = 8;
                visual.transform.SetParent(root.transform, false);
                var sr = visual.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                // 缩放到 1m x 0.5m，中心对齐根节点
                Vector2 spriteSize = sprite.bounds.size;
                visual.transform.localScale = new Vector3(
                    worldSize.x / spriteSize.x,
                    worldSize.y / spriteSize.y,
                    1f);

                // ---------- HitZones / StandZone ----------
                var hitZones = new GameObject("HitZones");
                hitZones.transform.SetParent(root.transform, false);

                var standZone = new GameObject("StandZone");
                standZone.transform.SetParent(hitZones.transform, false);

                var trigger = standZone.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                // 略宽于站立面顶部，玩家踏入即触发；高度 0.3，底部没入块内防止漏检
                trigger.size = new Vector2(worldSize.x * 0.95f, 0.3f);
                trigger.offset = new Vector2(0f, worldSize.y * 0.5f); // 中心贴在顶面上沿

                standZone.AddComponent<SoapSurface>();

                // ---------- 保存 ----------
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[SoapPrefabGenerator] 已生成 {PrefabPath}", saved);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

using SuperQQ.Grid;
using UnityEditor;
using UnityEngine;

namespace SuperQQ.Grid.EditorTools
{
    /// <summary>
    /// 排气扇道具 prefab 生成器：菜单 SuperQQ/生成排气扇Prefab
    /// 结构：根(道具逻辑+占位+拖拽吸附，可旋转绕中心)
    ///   ├─ Visual（素材，缩放到 1x1 米）
    ///   └─ HitZones/WindZone（Trigger 吹风区域 + WindZone 风力）
    /// 吹风方向 = transform.right（素材按"向右吹"制作），旋转时自动联动
    /// </summary>
    public static class ExhaustFanPrefabGenerator
    {
        private const string PrefabPath = "Assets/Prefab/Item/ExhaustFan.prefab";

        // 2x2 格，cellSize=0.5 → 世界尺寸 1m x 1m
        private static readonly Vector2 worldSize = Vector2.one;
        // 吹风区域：扇面右侧，宽 6 格（3m），高同扇体
        private static readonly Vector2 windSize = new Vector2(3f, 1f);

        [MenuItem("SuperQQ/生成排气扇Prefab")]
        public static void Generate()
        {
            string spritePath = FindFanSprite();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

            var root = new GameObject("ExhaustFan");
            try
            {
                root.layer = 9;

                root.AddComponent<Item.ExhaustFanItem>();

                var box = root.AddComponent<FootprintBoxView>();
                var boxSo = new SerializedObject(box);
                boxSo.FindProperty("footprint").vector2IntValue = new Vector2Int(2, 2);
                boxSo.FindProperty("canRotate").boolValue = true;             // 可旋转
                boxSo.FindProperty("pivotCell").vector2IntValue = new Vector2Int(-1, -1); // 自动中心格子
                boxSo.ApplyModifiedPropertiesWithoutUndo();

                // 与 ButterBlock 一致：实体碰撞供 PlacementController 拖拽拾取
                var solid = root.AddComponent<BoxCollider2D>();
                solid.size = worldSize;

                root.AddComponent<PlacementController>();

                // ---------- Visual ----------
                var visual = new GameObject("Visual");
                visual.layer = 8;
                visual.transform.SetParent(root.transform, false);
                var sr = visual.AddComponent<SpriteRenderer>();
                if (sprite != null)
                {
                    sr.sprite = sprite;
                    Vector2 spriteSize = sprite.bounds.size;
                    visual.transform.localScale = new Vector3(
                        worldSize.x / spriteSize.x,
                        worldSize.y / spriteSize.y,
                        1f);
                }
                else
                {
                    Debug.LogWarning("[ExhaustFanPrefabGenerator] 未找到素材，Visual 为空 SpriteRenderer，请手动指定贴图");
                }

                // ---------- HitZones / WindZone ----------
                var hitZones = new GameObject("HitZones");
                hitZones.transform.SetParent(root.transform, false);

                var windZone = new GameObject("WindZone");
                windZone.transform.SetParent(hitZones.transform, false);
                // 素材吹风口朝左：区域中心位于扇体左侧；
                // 子物体转 180° 使 transform.right 指向局部 -X（吹风方向向左），
                // 排气扇旋转时风区位置与风向仍随 transform 联动
                windZone.transform.localPosition = new Vector3(-(worldSize.x * 0.5f + windSize.x * 0.5f), 0f, 0f);
                windZone.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

                var trigger = windZone.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = windSize;

                windZone.AddComponent<Item.WindZone>();

                // ---------- 保存 ----------
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[ExhaustFanPrefabGenerator] 已生成 {PrefabPath}（素材：{spritePath}）", saved);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>素材按"向右吹"约定挑选：优先 hairdryer，其次 Slidinggear_1</summary>
        private static string FindFanSprite()
        {
            const string hairdryer = "Assets/Arts/Item/hairdryer.png";
            const string slidingGear = "Assets/Arts/Item/Slidinggear_1.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(hairdryer) != null)
            {
                return hairdryer;
            }
            return slidingGear;
        }
    }
}

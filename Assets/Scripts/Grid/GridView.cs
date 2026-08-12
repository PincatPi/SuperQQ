using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 游戏内网格可视化 — 布置道具阶段向玩家展示的网格
    /// 用程序生成的"1格"贴图 + SpriteRenderer Tiled 模式铺满可摆放区域
    /// 建造阶段 Show()，跑动阶段 Hide()
    /// 美术精修时可替换 GenerateLineTexture 为正式贴图资源
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class GridView : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("留空则自动查找场景中的 GridManager")]
        [SerializeField] private GridManager gridManager;

        [Header("外观")]
        [Tooltip("网格线颜色")]
        [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.35f);
        [Tooltip("网格线宽度（像素，按 PPU=100 换算）")]
        [SerializeField] private int lineWidthPixels = 2;
        [Tooltip("贴图像素/米，与项目 PPU 保持一致")]
        [SerializeField] private int pixelsPerUnit = 100;
        [Tooltip("Sorting Order，需低于道具、高于背景")]
        [SerializeField] private int sortingOrder = -1;

        [Header("调试")]
        [Tooltip("调试模式：每个格子填充不同实心颜色（用于实机核对格子尺寸），并默认显示网格")]
        [SerializeField] private bool debugFillCells;
        [Tooltip("调试色块的透明度")]
        [SerializeField, Range(0f, 1f)] private float debugFillAlpha = 0.5f;

        private SpriteRenderer spriteRenderer;
        private Texture2D generatedTexture;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (gridManager == null)
            {
                gridManager = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
            }

            BuildSprite();
            if (debugFillCells)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        private void OnDestroy()
        {
            if (generatedTexture != null)
            {
                Destroy(generatedTexture);
            }
        }

        // ==================== 公开接口 ====================

        /// <summary>
        /// 显示网格（进入建造阶段时调用）
        /// </summary>
        public void Show()
        {
            spriteRenderer.enabled = true;
        }

        /// <summary>
        /// 隐藏网格（进入跑动阶段时调用）
        /// </summary>
        public void Hide()
        {
            spriteRenderer.enabled = false;
        }

        // ==================== 构建 ====================

        /// <summary>
        /// 生成平铺 Sprite 并摆放到正确位置与尺寸
        /// </summary>
        private void BuildSprite()
        {
            if (gridManager == null)
            {
                Debug.LogWarning("[GridView] 未找到 GridManager，网格无法显示");
                return;
            }

            float cellSize = gridManager.PublicCellSize;
            RectInt bounds = gridManager.PlaceableBounds;
            Vector2 origin = gridManager.PublicOrigin;

            int cellPixels = Mathf.RoundToInt(cellSize * pixelsPerUnit);
            Sprite sprite;

            if (debugFillCells)
            {
                // 调试模式：整张大贴图，每格填充不同颜色，不做平铺
                generatedTexture = GenerateFillTexture(bounds.width, bounds.height, cellPixels, debugFillAlpha);
                sprite = Sprite.Create(
                    generatedTexture,
                    new Rect(0, 0, generatedTexture.width, generatedTexture.height),
                    new Vector2(0f, 0f),
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                spriteRenderer.sprite = sprite;
                spriteRenderer.drawMode = SpriteDrawMode.Simple;
            }
            else
            {
                generatedTexture = GenerateLineTexture(cellPixels, lineWidthPixels, lineColor);
                sprite = Sprite.Create(
                    generatedTexture,
                    new Rect(0, 0, cellPixels, cellPixels),
                    new Vector2(0f, 0f),          // pivot 设左下，方便与网格原点对齐
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                spriteRenderer.sprite = sprite;
                spriteRenderer.drawMode = SpriteDrawMode.Tiled;
                spriteRenderer.size = new Vector2(bounds.width * cellSize, bounds.height * cellSize);
            }
            spriteRenderer.sortingOrder = sortingOrder;

            // 左下角对准可摆放区域的世界左下角
            transform.position = new Vector3(
                origin.x + bounds.xMin * cellSize,
                origin.y + bounds.yMin * cellSize,
                0f);
        }

        /// <summary>
        /// 程序生成 1 格的网格线贴图（左、下两边画线，平铺后即成网格）
        /// </summary>
        private static Texture2D GenerateLineTexture(int size, int lineWidth, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Repeat;

            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = clear;
            }

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (x < lineWidth || y < lineWidth)
                    {
                        pixels[y * size + x] = color;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// 程序生成调试填色贴图：每个格子填充不同实心颜色（按格子坐标哈希取色，稳定可复现）
        /// </summary>
        private static Texture2D GenerateFillTexture(int cellsX, int cellsY, int cellPixels, float alpha)
        {
            int width = cellsX * cellPixels;
            int height = cellsY * cellPixels;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var pixels = new Color[width * height];
            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    // 由格子坐标哈希出稳定色相，保证相邻格子颜色差异明显
                    float hue = Mathf.Repeat(cx * 0.23f + cy * 0.41f, 1f);
                    Color color = Color.HSVToRGB(hue, 0.6f, 0.9f);
                    color.a = alpha;

                    for (int y = 0; y < cellPixels; y++)
                    {
                        int rowStart = (cy * cellPixels + y) * width + cx * cellPixels;
                        for (int x = 0; x < cellPixels; x++)
                        {
                            pixels[rowStart + x] = color;
                        }
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}

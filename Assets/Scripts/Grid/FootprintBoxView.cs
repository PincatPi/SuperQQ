using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SuperQQ.Grid
{
    /// <summary>
    /// 道具网格占位定义 + 虚线包围盒
    /// 制作道具 prefab 时挂载，在 Inspector 中定义该物体占据的格子数（宽x高），
    /// 编辑期即在 Scene 视图绘制虚线包围盒，辅助美术对齐素材与格子；
    /// 运行时（摆放阶段）由 PlacementController 调 Init/Show 显示实体虚线框，
    /// 框的边缘与场景网格线重合即表示对齐
    /// GridManager 的放置判定优先读取本组件的 footprint
    /// </summary>
    public class FootprintBoxView : MonoBehaviour
    {
        [Header("占位定义")]
        [Tooltip("该物体占据的格子数（宽x高），如平台 2x1；放置判定以此为准")]
        [SerializeField] private Vector2Int footprint = Vector2Int.one;
        [Tooltip("运行时是否自动生成并显示虚线框（摆放流程由外部控制时保持关闭）")]
        [SerializeField] private bool autoInitOnStart;
        [Tooltip("独立使用时是否按旋转90度生成（宽高互换）")]
        [SerializeField] private bool startRotated;
        [Tooltip("是否允许旋转（仅部分道具开启；宽高相等时旋转无占位意义，仅视觉旋转）")]
        [SerializeField] private bool canRotate;
        [Tooltip("锚点格子（footprint 内的格子索引，左下角为 0,0）：prefab 根节点对齐该格子中心，旋转绕该格子进行；(-1,-1)=自动取中心格子")]
        [SerializeField] private Vector2Int pivotCell = new Vector2Int(-1, -1);
        [Tooltip("编辑期（不运行）是否在 Scene 视图绘制虚线包围盒与锚点标记")]
        [SerializeField] private bool drawBoxInEditor = true;

        [Header("外观")]
        [Tooltip("虚线颜色")]
        [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("线宽（像素，按 PPU=100）")]
        [SerializeField] private int lineWidthPixels = 3;
        [Tooltip("虚线实段长度（像素）")]
        [SerializeField] private int dashPixels = 10;
        [Tooltip("虚线间隔长度（像素）")]
        [SerializeField] private int gapPixels = 6;
        [Tooltip("贴图像素/米，与项目 PPU 保持一致")]
        [SerializeField] private int pixelsPerUnit = 100;
        [Tooltip("Sorting Order，需高于道具本体")]
        [SerializeField] private int sortingOrder = 10;

        private SpriteRenderer boxRenderer;
        private Texture2D generatedTexture;

        /// <summary>该物体占据的格子数（宽x高，未旋转）</summary>
        public Vector2Int Footprint => footprint;
        /// <summary>是否允许旋转90度</summary>
        public bool CanRotate => canRotate;

        /// <summary>
        /// 锚点格子（footprint 内的格子索引，已钳制到合法范围；未配置时自动取中心格子）
        /// prefab 根节点需对齐该格子中心，旋转围绕它进行
        /// </summary>
        public Vector2Int PivotCell
        {
            get
            {
                int x = pivotCell.x >= 0 ? pivotCell.x : (footprint.x - 1) / 2;
                int y = pivotCell.y >= 0 ? pivotCell.y : (footprint.y - 1) / 2;
                return new Vector2Int(
                    Mathf.Clamp(x, 0, Mathf.Max(footprint.x - 1, 0)),
                    Mathf.Clamp(y, 0, Mathf.Max(footprint.y - 1, 0)));
            }
        }

        /// <summary>
        /// 获取当前格子尺寸（编辑期单例未初始化时回退查找场景实例，再找不到用默认值 0.5）
        /// </summary>
        private float ResolveCellSize()
        {
            if (GridManager.Instance != null)
            {
                return GridManager.Instance.PublicCellSize;
            }
            GridManager gm = FindObjectOfType<GridManager>();
            return gm != null ? gm.PublicCellSize : 0.5f;
        }

        // ==================== 生命周期 ====================

        private void Start()
        {
            if (autoInitOnStart)
            {
                Init(footprint, startRotated);
                Show();
            }
        }

        // ==================== 公开接口 ====================

        /// <summary>
        /// 按占位尺寸生成/更新虚线框（自动处理旋转的宽高互换）
        /// </summary>
        /// <param name="footprint">未旋转的占位（宽x高格子数）</param>
        /// <param name="rotated">是否旋转90度</param>
        public void Init(Vector2Int footprint, bool rotated)
        {
            Vector2Int size = rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;
            Build(size, ResolveCellSize());
        }

        /// <summary>显示虚线框（未生成过则先按组件 footprint 自动生成）</summary>
        public void Show()
        {
            EnsureBuilt();
            boxRenderer.enabled = true;
        }

        /// <summary>隐藏虚线框</summary>
        public void Hide()
        {
            if (boxRenderer != null)
            {
                boxRenderer.enabled = false;
            }
        }

        /// <summary>设置虚线框可见性（等价于 Show/Hide）</summary>
        public void SetVisible(bool visible)
        {
            if (visible)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        /// <summary>虚线框当前是否可见</summary>
        public bool IsVisible => boxRenderer != null && boxRenderer.enabled;

        /// <summary>
        /// 确保虚线框已生成（未生成时按组件自身 footprint 构建）
        /// </summary>
        private void EnsureBuilt()
        {
            if (boxRenderer == null)
            {
                Init(footprint, startRotated);
            }
        }

        /// <summary>设置虚线颜色（合法/非法提示可用绿/红切换）</summary>
        public void SetColor(Color color)
        {
            if (boxRenderer != null)
            {
                boxRenderer.color = color;
            }
        }

        // ==================== 构建 ====================

        private void Build(Vector2Int sizeInCells, float cellSize)
        {
            if (boxRenderer == null)
            {
                var go = new GameObject("FootprintBox");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                boxRenderer = go.AddComponent<SpriteRenderer>();
                boxRenderer.sortingOrder = sortingOrder;
            }

            // 抵消父物体旋转：道具本体旋转 90° 时虚线框保持与世界网格轴对齐，
            // 宽高互换仅由贴图重建（Init 的 rotated 参数）负责，避免双重旋转。
            // 框始终以根节点（sprite 中心）为基准排布，与锚点选择无关；
            // 锚点仅作为逻辑旋转中心，通过 GridManager 的世界坐标换算生效。
            boxRenderer.transform.localRotation = Quaternion.Inverse(transform.rotation);
            boxRenderer.transform.localPosition = Vector3.zero;

            if (generatedTexture != null)
            {
                Destroy(generatedTexture);
            }

            int widthPx = Mathf.Max(sizeInCells.x * Mathf.RoundToInt(cellSize * pixelsPerUnit), lineWidthPixels * 2);
            int heightPx = Mathf.Max(sizeInCells.y * Mathf.RoundToInt(cellSize * pixelsPerUnit), lineWidthPixels * 2);
            generatedTexture = GenerateDashedRectTexture(widthPx, heightPx, lineWidthPixels, dashPixels, gapPixels, Color.white);

            Sprite sprite = Sprite.Create(
                generatedTexture,
                new Rect(0, 0, widthPx, heightPx),
                new Vector2(0.5f, 0.5f),      // pivot 居中：与道具 prefab 的摆放中心一致
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);

            boxRenderer.sprite = sprite;
            boxRenderer.drawMode = SpriteDrawMode.Simple;
            boxRenderer.color = lineColor;
        }

        /// <summary>
        /// 程序生成虚线矩形边框贴图（白色，显示颜色由 SpriteRenderer.color 控制）
        /// </summary>
        private static Texture2D GenerateDashedRectTexture(int width, int height, int lineWidth, int dash, int gap, Color color)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[width * height];
            int period = dash + gap;

            // 沿矩形四条边按周长位置画虚线，转角处虚线连续
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool onHorizontalEdge = y < lineWidth || y >= height - lineWidth;
                    bool onVerticalEdge = x < lineWidth || x >= width - lineWidth;
                    if (!onHorizontalEdge && !onVerticalEdge)
                    {
                        continue;
                    }

                    // 计算该像素在边框周长上的位置（从左上角顺时针）
                    int perimeterPos;
                    if (y >= height - lineWidth)          // 上边：左→右
                    {
                        perimeterPos = x;
                    }
                    else if (x >= width - lineWidth)      // 右边：上→下
                    {
                        perimeterPos = width + (height - 1 - y);
                    }
                    else if (y < lineWidth)               // 下边：右→左
                    {
                        perimeterPos = width + height + (width - 1 - x);
                    }
                    else                                   // 左边：下→上
                    {
                        perimeterPos = width * 2 + height + y;
                    }

                    if (perimeterPos % period < dash)
                    {
                        pixels[y * width + x] = color;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (generatedTexture != null)
            {
                Destroy(generatedTexture);
            }
        }

        // ==================== 编辑期可视化 ====================

        /// <summary>
        /// 编辑期在 Scene 视图绘制虚线包围盒（不进 Play 也能在制作 prefab 时核对占位）
        /// </summary>
        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!drawBoxInEditor)
            {
                return;
            }
            float cs = ResolveCellSize();
            Vector2 size = new Vector2(footprint.x * cs, footprint.y * cs);
            // 框以根节点（sprite 中心）为基准排布，与锚点选择无关
            Vector3 center = transform.position;
            Vector3 half = new Vector3(size.x * 0.5f, size.y * 0.5f, 0f);

            Vector3 tl = center + new Vector3(-half.x, half.y, 0f);
            Vector3 tr = center + new Vector3(half.x, half.y, 0f);
            Vector3 br = center + new Vector3(half.x, -half.y, 0f);
            Vector3 bl = center + new Vector3(-half.x, -half.y, 0f);

            Handles.color = lineColor;
            float dashSize = Mathf.Max((dashPixels + gapPixels) / (float)pixelsPerUnit * 0.5f, 0.05f);
            Handles.DrawDottedLine(tl, tr, dashSize);
            Handles.DrawDottedLine(tr, br, dashSize);
            Handles.DrawDottedLine(br, bl, dashSize);
            Handles.DrawDottedLine(bl, tl, dashSize);

            // 锚点格子标记：在锚点所在位置画小十字（旋转围绕此点；锚点相对根节点按格子索引偏移）
            Handles.color = Color.yellow;
            float cross = cs * 0.25f;
            Vector2Int pivot = PivotCell;
            Vector3 p = transform.position + new Vector3(
                (pivot.x - (footprint.x - 1) * 0.5f) * cs,
                (pivot.y - (footprint.y - 1) * 0.5f) * cs,
                0f);
            Handles.DrawLine(p + new Vector3(-cross, 0f, 0f), p + new Vector3(cross, 0f, 0f));
            Handles.DrawLine(p + new Vector3(0f, -cross, 0f), p + new Vector3(0f, cross, 0f));
#endif
        }
    }
}

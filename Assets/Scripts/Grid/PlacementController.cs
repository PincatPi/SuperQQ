using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 网格吸附拖拽控制器 — 挂在每个需要网格吸附的道具上
    /// 玩家拖拽道具本体时，根据自身的包围盒（FootprintBoxView.Footprint）自动对齐场景网格：
    ///   按下：若已放置则先释放占据格子，记录原锚点
    ///   拖拽：位置完全由格子坐标重建（吸附），虚线框按合法性变绿/红
    ///   抬起：落点合法则登记占据；非法则吸附回原锚点
    /// 依赖碰撞体接收拖拽输入（OnMouseDown/Drag/Up，触屏兼容）
    /// </summary>
    [RequireComponent(typeof(FootprintBoxView))]
    [RequireComponent(typeof(Collider2D))]
    public class PlacementController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("留空则自动使用 Camera.main")]
        [SerializeField] private Camera inputCamera;

        [Header("拖拽")]
        [Tooltip("拖拽时道具相对指针上移的格数，避免手指遮挡")]
        [SerializeField] private float pointerLiftCells = 1f;
        [Tooltip("拖拽时是否显示虚线包围盒")]
        [SerializeField] private bool showBoxWhileDragging = true;

        [Header("虚化")]
        [Tooltip("虚化时 Sprtie 的透明度（可拖拽/待确认状态的视觉提示）")]
        [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.5f;

        [Header("提示颜色")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.3f, 0.9f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        private FootprintBoxView box;
        private PlacedItem placedItem;        // 占据登记凭证（登记过时存在）
        private bool draggable;               // 可拖拽状态（由 EnterDraggableState 开启）
        private bool dragging;
        private bool currentValid;
        private Vector2Int originalAnchor;    // 拖拽起始锚点（落点非法时回退用）

        // 虚化状态：缓存各 SpriteRenderer 原始透明度，结束时恢复
        private SpriteRenderer[] cachedRenderers;
        private float[] originalAlphas;
        private bool ghosting;

        private GridManager Grid => GridManager.Instance;

        /// <summary>是否处于可拖拽状态</summary>
        public bool IsDraggable => draggable;
        /// <summary>是否正在被拖拽</summary>
        public bool IsDragging => dragging;
        /// <summary>当前拖拽落点是否合法</summary>
        public bool CurrentValid => currentValid;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            box = GetComponent<FootprintBoxView>();
            placedItem = GetComponent<PlacedItem>();
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
        }

        /// <summary>
        /// 吸附到最近的合法格子位置（不登记，仅对齐位置）
        /// </summary>
        public void SnapToNearestCell()
        {
            Vector2Int anchor = Grid.WorldToCell(transform.position);
            transform.position = Grid.GetPlacementWorldPos(anchor, box.Footprint, false);
        }

        // ==================== 状态接口 ====================

        /// <summary>
        /// 进入可拖拽状态：开启拖拽响应并虚化显示（摆放阶段由外部调用）
        /// </summary>
        public void EnterDraggableState()
        {
            draggable = true;
            GhostOn();
        }

        /// <summary>
        /// 完成放置：结束拖拽（若正在拖拽则按当前位置结算），
        /// 把包围盒占据的格子登记为已占领，关闭虚化，锁定道具
        /// </summary>
        public void CompletePlacement()
        {
            if (dragging)
            {
                EndDrag();
            }

            // 兜底登记：未被拖拽过（或拖拽结算前）直接完成放置时，
            // 按当前位置吸附并把 footprint 覆盖的格子标记为已占领
            if (placedItem == null)
            {
                SnapToNearestCell();
                RegisterAt(Grid.WorldToCell(transform.position));
            }

            draggable = false;
            GhostOff();
        }

        // ==================== 拖拽输入（手动轮询，编辑器鼠标/真机触屏通用） ====================

        private void Update()
        {
            if (Grid == null || !draggable)
            {
                return;
            }

            bool pressed = GetPointerPressed(out Vector2 pointerScreen);
            if (!dragging)
            {
                // 按下瞬间做 2D 射线检测：点到本道具才开始拖拽
                if (pressed && IsPointerDownThisFrame() && HitSelf(ScreenToWorld(pointerScreen)))
                {
                    BeginDrag();
                }
                return;
            }

            if (pressed)
            {
                DragStep(ScreenToWorld(pointerScreen));
            }
            else
            {
                EndDrag();
            }
        }

        private void BeginDrag()
        {
            dragging = true;
            originalAnchor = Grid.WorldToCell(transform.position);

            // 已放置的物体：先释放占据的格子，腾空后拖动
            if (placedItem != null)
            {
                Grid.Release(placedItem);
            }

            if (showBoxWhileDragging)
            {
                box.Init(box.Footprint, false);
                box.Show();
            }
        }

        private void DragStep(Vector2 pointerWorld)
        {
            // 指针位置上移后换算格子，吸附锚点与道具位置完全由格子重建
            Vector2 liftedWorld = pointerWorld + Vector2.up * (pointerLiftCells * Grid.PublicCellSize);
            Vector2Int anchor = Grid.WorldToCell(liftedWorld);

            transform.position = Grid.GetPlacementWorldPos(anchor, box.Footprint, false);

            currentValid = Grid.CanOccupy(anchor, box.Footprint);
            box.SetColor(currentValid ? validColor : invalidColor);
        }

        private void EndDrag()
        {
            dragging = false;

            Vector2Int anchor = Grid.WorldToCell(transform.position);
            if (currentValid)
            {
                RegisterAt(anchor);
            }
            else
            {
                // 落点非法：吸附回原锚点并恢复登记
                transform.position = Grid.GetPlacementWorldPos(originalAnchor, box.Footprint, false);
                RegisterAt(originalAnchor);
            }

            box.Hide();
        }

        // ==================== 内部逻辑 ====================

        /// <summary>
        /// 射线检测指针是否点中了本道具（含子物体的碰撞体）
        /// </summary>
        private bool HitSelf(Vector2 pointerWorld)
        {
            RaycastHit2D hit = Physics2D.Raycast(pointerWorld, Vector2.zero);
            return hit.collider != null && hit.collider.GetComponentInParent<PlacementController>() == this;
        }

        /// <summary>
        /// 在指定锚点登记占据（首次登记时自动补挂 PlacedItem）
        /// </summary>
        private void RegisterAt(Vector2Int anchor)
        {
            if (placedItem == null)
            {
                placedItem = gameObject.AddComponent<PlacedItem>();
                placedItem.Init(null, anchor, false, -1);
            }
            Grid.Occupy(anchor, box.Footprint, placedItem);
        }

        // ==================== 虚化接口 ====================

        /// <summary>
        /// 开启虚化：所有 SpriteRenderer 透明度降为 ghostAlpha（原始值已缓存，可安全重复调用）
        /// </summary>
        public void GhostOn()
        {
            if (ghosting)
            {
                return;
            }
            ghosting = true;

            cachedRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = new float[cachedRenderers.Length];
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                originalAlphas[i] = cachedRenderers[i].color.a;
                Color c = cachedRenderers[i].color;
                c.a = ghostAlpha;
                cachedRenderers[i].color = c;
            }
        }

        /// <summary>
        /// 结束虚化：恢复所有 SpriteRenderer 的原始透明度
        /// </summary>
        public void GhostOff()
        {
            if (!ghosting)
            {
                return;
            }
            ghosting = false;

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] == null)
                {
                    continue;
                }
                Color c = cachedRenderers[i].color;
                c.a = originalAlphas[i];
                cachedRenderers[i].color = c;
            }
            cachedRenderers = null;
            originalAlphas = null;
        }

        /// <summary>
        /// 指针当前是否按下（触屏或鼠标），并输出屏幕坐标
        /// </summary>
        private bool GetPointerPressed(out Vector2 screenPos)
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                {
                    screenPos = touch.position;
                    return true;
                }
            }
            else if (Input.GetMouseButton(0))
            {
                screenPos = Input.mousePosition;
                return true;
            }
            screenPos = Vector2.zero;
            return false;
        }

        /// <summary>
        /// 本帧是否为指针刚按下的一瞬
        /// </summary>
        private bool IsPointerDownThisFrame()
        {
            if (Input.touchCount > 0)
            {
                return Input.GetTouch(0).phase == TouchPhase.Began;
            }
            return Input.GetMouseButtonDown(0);
        }

        private Vector2 ScreenToWorld(Vector2 screenPos)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }
    }
}

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
        [Tooltip("允许放置在被占用的格子上（拆除类/附着类道具开启）；开启后登记时跳过已被占据的格子，不覆盖原有道具的占据记录")]
        [SerializeField] private bool allowPlaceOnOccupied;

        [Header("虚化")]
        [Tooltip("虚化时 Sprtie 的透明度（可拖拽/待确认状态的视觉提示）")]
        [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.5f;

        [Header("提示颜色")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.3f, 0.9f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        [Header("旋转")]
        [Tooltip("旋转状态对应的 Z 轴角度（切换 0°/该角度）")]
        [SerializeField] private float rotatedAngle = 90f;

        [Header("调试")]
        [Tooltip("启用后：P 键进入可拖拽状态，R 键旋转")]
        [SerializeField] private bool debugHotkeys = true;

        private FootprintBoxView box;
        private PlacedItem placedItem;        // 占据登记凭证（登记过时存在）
        private bool draggable;               // 可拖拽状态（由 EnterDraggableState 开启）
        private bool dragging;
        private bool currentValid;
        private bool rotated;                 // 当前旋转状态（false=0°，true=rotatedAngle）
        private bool registered;              // 占据是否已登记（false=未合规放置，可再拖拽）
        private Vector2Int currentPivotCell;  // 拖拽中最近一次吸附的锚点格子（旋转围绕它进行）

        /// <summary>当前朝向下锚点格子在占位矩形内的索引</summary>
        private Vector2Int PivotInRect => rotated
            ? GridManager.GetRotatedPivot(box.PivotCell, box.Footprint)
            : box.PivotCell;

        /// <summary>锚点格子 -> 占位矩形左下角锚点（占据登记用）</summary>
        private Vector2Int AnchorFromPivot(Vector2Int pivotGridCell) => pivotGridCell - PivotInRect;

        /// <summary>左下角锚点 -> 根节点（框中心）的世界坐标</summary>
        private Vector2 RootPosFromAnchor(Vector2Int anchor)
        {
            return Grid.GetPlacementWorldPos(anchor, box.Footprint, rotated, box.PivotCell);
        }

        /// <summary>根节点（框中心）世界坐标 -> 左下角锚点格子</summary>
        private Vector2Int AnchorFromRootPos(Vector2 worldPos)
        {
            Vector2Int size = rotated
                ? new Vector2Int(box.Footprint.y, box.Footprint.x)
                : box.Footprint;
            Vector2 local = (worldPos - Grid.PublicOrigin) / Grid.PublicCellSize;
            return new Vector2Int(
                Mathf.RoundToInt(local.x - size.x * 0.5f),
                Mathf.RoundToInt(local.y - size.y * 0.5f));
        }

        /// <summary>根节点世界坐标 -> 锚点所在格子（锚点格子的世界中心）</summary>
        private Vector2Int PivotCellFromRootPos(Vector2 worldPos)
        {
            return AnchorFromRootPos(worldPos) + PivotInRect;
        }

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
        /// <summary>当前是否处于旋转状态</summary>
        public bool IsRotated => rotated;
        /// <summary>本道具是否允许旋转（由 FootprintBoxView 配置）</summary>
        public bool CanRotate => box != null && box.CanRotate;
        /// <summary>当前摆放是否合规（占据已登记到网格；拖拽到非法区域停留时为 false）</summary>
        public bool IsPlacementValid => registered;

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
            // 框对齐格子网格：偶数宽/高时根节点落在格线上
            transform.position = RootPosFromAnchor(AnchorFromRootPos(transform.position));
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
            // 按当前位置吸附并把 footprint 覆盖的格子标记为已占领；落点非法则保持不合规状态
            if (!registered)
            {
                SnapToNearestCell();
                Vector2Int anchor = AnchorFromRootPos(transform.position);
                if (Grid.CanOccupy(anchor, box.Footprint, rotated, allowPlaceOnOccupied))
                {
                    RegisterAt(anchor);
                }
                else
                {
                    box.Init(box.Footprint, rotated);
                    box.Show();
                    box.SetColor(invalidColor);
                }
            }

            draggable = false;
            GhostOff();
        }

        // ==================== 旋转接口 ====================

        /// <summary>
        /// 切换旋转状态（0° ↔ rotatedAngle），供双击/按钮等外部输入调用。
        /// 连带旋转：transform（碰撞体/贴图/特效挂点）、虚线框、占位宽高互换。
        /// </summary>
        /// <returns>旋转是否生效（道具不可旋转或已放置且新朝向落点非法时返回 false）</returns>
        public bool ToggleRotate()
        {
            return SetRotated(!rotated);
        }

        /// <summary>
        /// 设置旋转状态。
        /// 拖拽中：按当前锚点重新吸附并刷新合法性提示；
        /// 已放置：在原锚点以新朝向重新登记，落点非法则整体回退；
        /// 未放置：仅旋转表现，占位在后续吸附时生效。
        /// </summary>
        public bool SetRotated(bool target)
        {
            if (target == rotated)
            {
                return true;
            }
            if (!CanRotate || Grid == null)
            {
                return false;
            }

            bool prev = rotated;

            if (registered && placedItem != null && !dragging)
            {
                // 已合规放置：锚点格子世界位置不动，占位矩形绕它重排，根节点物理移动到新框中心
                Vector2Int pivotGridCell = PivotCellFromRootPos(transform.position);
                Vector2Int oldAnchor = AnchorFromPivot(pivotGridCell);
                rotated = target;
                Vector2Int newAnchor = AnchorFromPivot(pivotGridCell);

                Grid.Release(placedItem);
                if (Grid.CanOccupy(newAnchor, box.Footprint, rotated, allowPlaceOnOccupied))
                {
                    ApplyRotation();
                    transform.position = RootPosFromAnchor(newAnchor);
                    placedItem.Init(placedItem.Def, newAnchor, rotated, placedItem.OwnerPlayerId);
                    Grid.Occupy(newAnchor, box.Footprint, placedItem, rotated, allowPlaceOnOccupied);
                    return true;
                }
                // 落点非法：回退旋转状态并恢复原登记
                rotated = prev;
                Grid.Occupy(oldAnchor, box.Footprint, placedItem, rotated, allowPlaceOnOccupied);
                return false;
            }

            // 未放置/拖拽中：绕锚点格子旋转——锚点格子世界位置不动，
            // 根节点物理移动到新朝向的框中心，外观即围绕锚点旋转
            Vector2Int pivotCell = PivotCellFromRootPos(transform.position);
            rotated = target;
            ApplyRotation();
            transform.position = RootPosFromAnchor(AnchorFromPivot(pivotCell));
            box.Init(box.Footprint, rotated);

            if (dragging)
            {
                currentValid = Grid.CanOccupy(AnchorFromPivot(pivotCell), box.Footprint, rotated, allowPlaceOnOccupied);
                box.SetColor(currentValid ? validColor : invalidColor);
            }
            return true;
        }

        /// <summary>按当前旋转状态应用 transform 旋转（碰撞体/视觉/挂点随层级一并旋转）</summary>
        private void ApplyRotation()
        {
            transform.rotation = rotated ? Quaternion.Euler(0f, 0f, rotatedAngle) : Quaternion.identity;
        }

        // ==================== 拖拽输入（手动轮询，编辑器鼠标/真机触屏通用） ====================

        private void Update()
        {
            if (Grid == null)
            {
                return;
            }

            // 调试快捷键：P 进入可拖拽状态，R 旋转
            if (debugHotkeys)
            {
                if (Input.GetKeyDown(KeyCode.P) && !draggable)
                {
                    EnterDraggableState();
                }
                if (Input.GetKeyDown(KeyCode.R) && draggable)
                {
                    ToggleRotate();
                }
            }

            if (!draggable)
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
            currentPivotCell = PivotCellFromRootPos(transform.position);

            // 已登记的物体：先释放占据的格子，腾空后拖动
            if (registered && placedItem != null)
            {
                Grid.Release(placedItem);
            }
            registered = false;

            if (showBoxWhileDragging)
            {
                box.Init(box.Footprint, rotated);
                box.Show();
            }
        }

        private void DragStep(Vector2 pointerWorld)
        {
            // 指针位置上移后换算锚点格子，位置完全由格子重建（框中心对齐网格）
            Vector2 liftedWorld = pointerWorld + Vector2.up * (pointerLiftCells * Grid.PublicCellSize);
            currentPivotCell = Grid.WorldToCell(liftedWorld);

            transform.position = RootPosFromAnchor(AnchorFromPivot(currentPivotCell));

            currentValid = Grid.CanOccupy(AnchorFromPivot(currentPivotCell), box.Footprint, rotated, allowPlaceOnOccupied);
            box.SetColor(currentValid ? validColor : invalidColor);
        }

        private void EndDrag()
        {
            dragging = false;

            if (currentValid)
            {
                RegisterAt(AnchorFromRootPos(transform.position));
                box.Hide();
            }
            else
            {
                // 落点非法：停留在当前位置（不弹回、不登记），保持红色包围盒提示不合规
                box.SetColor(invalidColor);
            }
        }

        // ==================== 内部逻辑 ====================

        /// <summary>
        /// 射线检测指针是否点中了本道具（含子物体的碰撞体）
        /// </summary>
        /// <summary>
        /// 点选检测：指针是否落在本道具的包围盒（footprint 矩形）内
        /// 不依赖碰撞体——素材不填满占位、细长道具也能整框点选
        /// </summary>
        private bool HitSelf(Vector2 pointerWorld)
        {
            Vector2Int size = rotated
                ? new Vector2Int(box.Footprint.y, box.Footprint.x)
                : box.Footprint;
            Vector2 halfSize = new Vector2(size.x, size.y) * (Grid.PublicCellSize * 0.5f);

            // 根节点位置即包围盒中心（RootPosFromAnchor 的约定）
            Vector2 center = transform.position;
            return Mathf.Abs(pointerWorld.x - center.x) <= halfSize.x
                && Mathf.Abs(pointerWorld.y - center.y) <= halfSize.y;
        }

        /// <summary>
        /// 在指定锚点登记占据（首次登记时自动补挂 PlacedItem）
        /// </summary>
        private void RegisterAt(Vector2Int anchor)
        {
            if (placedItem == null)
            {
                placedItem = gameObject.AddComponent<PlacedItem>();
                placedItem.Init(null, anchor, rotated, -1);
            }
            else
            {
                placedItem.Init(placedItem.Def, anchor, rotated, placedItem.OwnerPlayerId);
            }
            Grid.Occupy(anchor, box.Footprint, placedItem, rotated, allowPlaceOnOccupied);
            registered = true;
            box.Hide();
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

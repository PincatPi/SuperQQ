using System;
using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 摆放控制器 — 场景单例，建造阶段激活
    /// 负责摆放交互全流程：生成幽灵体 → 触屏拖拽吸附 → 合法性提示 → UI 按钮确认/旋转/取消
    ///
    /// 同一时刻只有一个幽灵体在被拖拽，已放置的物体无需吸附（放下时已对齐）
    /// 触屏与编辑器鼠标双兼容，便于真机与编辑器调试
    /// 网络同步挂点：订阅 OnPlaced / OnRemoved 事件发送消息（本地权威，先放置再广播）
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        /// <summary>当前场景实例</summary>
        public static PlacementController Instance { get; private set; }

        [Header("引用")]
        [Tooltip("留空则自动使用 Camera.main")]
        [SerializeField] private Camera inputCamera;

        [Header("幽灵体外观")]
        [Tooltip("幽灵体半透明度")]
        [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.5f;
        [Tooltip("可放置时虚线框颜色")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.3f, 0.9f);
        [Tooltip("不可放置时虚线框颜色")]
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        [Header("触屏")]
        [Tooltip("幽灵体相对手指的上移格数，避免手指挡住预览")]
        [SerializeField] private float fingerOffsetCells = 2f;

        // 放置/移除事件（网络同步层订阅）
        /// <summary>本地确认放置后触发（参数：放置结果）</summary>
        public event Action<PlacedItem> OnPlaced;
        /// <summary>本地拾回移除后触发（参数：被移除物体原本的锚点格子）</summary>
        public event Action<Vector2Int> OnRemoved;

        // 当前摆放状态
        private PlacableItemDef currentDef;
        private GameObject ghost;
        private FootprintBoxView ghostBox;
        private bool rotated;

        /// <summary>是否正处于摆放状态（有幽灵体）</summary>
        public bool IsPlacing => ghost != null;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            Instance = this;
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsPlacing)
            {
                return;
            }

            // 按住屏幕/鼠标即拖拽幽灵体；松手不确认，幽灵体驻留当前格子，
            // 由 UI 确认按钮调 ConfirmPlacement() 完成放置
            Vector2? pointerWorld = GetPointerWorldPosition();
            if (pointerWorld.HasValue)
            {
                UpdateGhost(pointerWorld.Value);
            }
        }

        // ==================== 公开接口（UI / 道具栏调用） ====================

        /// <summary>
        /// 开始摆放一个道具：生成幽灵体，进入拖拽吸附状态
        /// 重复调用会先取消上一次摆放
        /// </summary>
        public void BeginPlacement(PlacableItemDef def)
        {
            if (def == null || def.Prefab == null)
            {
                return;
            }

            CancelPlacement();
            currentDef = def;
            rotated = false;

            ghost = CreateGhost(def);
            ghostBox = ghost.GetComponent<FootprintBoxView>();
            if (ghostBox == null)
            {
                ghostBox = ghost.AddComponent<FootprintBoxView>();
            }
            ghostBox.Init(GridManager.Instance.ResolveFootprint(def), rotated);
            ghostBox.Show();
        }

        /// <summary>
        /// 取消摆放：销毁幽灵体，不放置
        /// </summary>
        public void CancelPlacement()
        {
            if (ghost != null)
            {
                Destroy(ghost);
            }
            ghost = null;
            ghostBox = null;
            currentDef = null;
            rotated = false;
        }

        /// <summary>
        /// 旋转幽灵体90度（道具不允许旋转时无效）；接 UI 旋转按钮
        /// </summary>
        public void RotateGhost()
        {
            if (!IsPlacing || currentDef == null || !currentDef.Rotatable)
            {
                return;
            }

            rotated = !rotated;
            ghost.transform.rotation = rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
            ghostBox.Init(GridManager.Instance.ResolveFootprint(currentDef), rotated);
        }

        /// <summary>
        /// 拾回已放置的物体：移除并以其定义重新进入摆放状态
        /// </summary>
        public bool PickUpAt(Vector2 worldPos)
        {
            Vector2Int cell = GridManager.Instance.WorldToCell(worldPos);
            PlacedItem item = GridManager.Instance.GetItemAt(cell);
            if (item == null)
            {
                return false;
            }

            PlacableItemDef def = item.Def;
            Vector2Int anchor = item.AnchorCell;
            if (GridManager.Instance.RemoveAt(cell))
            {
                OnRemoved?.Invoke(anchor);
                BeginPlacement(def);
                return true;
            }
            return false;
        }

        // ==================== 内部逻辑 ====================

        /// <summary>
        /// 幽灵体跟随指针并吸附到格子，按合法性切换虚线框颜色
        /// </summary>
        private void UpdateGhost(Vector2 pointerWorld)
        {
            GridManager gm = GridManager.Instance;
            Vector2Int footprint = gm.ResolveFootprint(currentDef);

            // 幽灵体显示位置相对指针上移，避免手指遮挡；吸附锚点按偏移后的位置计算
            Vector2 ghostWorld = pointerWorld + Vector2.up * (fingerOffsetCells * gm.PublicCellSize);
            Vector2Int anchor = gm.WorldToCell(ghostWorld);
            ghost.transform.position = gm.GetPlacementWorldPos(anchor, footprint, rotated);
            ghost.transform.rotation = rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;

            bool canPlace = gm.CanPlace(currentDef, anchor, rotated);
            ghostBox.SetColor(canPlace ? validColor : invalidColor);
        }

        /// <summary>
        /// 确认放置：在幽灵体当前格子正式生成道具并退出摆放状态；接 UI 确认按钮
        /// 位置非法时不放置（幽灵体保留，玩家可继续拖拽调整）
        /// </summary>
        /// <returns>是否成功放置</returns>
        public bool ConfirmPlacement()
        {
            if (!IsPlacing)
            {
                return false;
            }

            GridManager gm = GridManager.Instance;
            Vector2Int anchor = gm.WorldToCell(ghost.transform.position);

            PlacedItem item = gm.Place(currentDef, anchor, rotated, -1);
            if (item == null)
            {
                return false;
            }

            OnPlaced?.Invoke(item);
            CancelPlacement();
            return true;
        }

        /// <summary>
        /// 幽灵体当前位置是否可放置（UI 可用它控制确认按钮的置灰/高亮）
        /// </summary>
        public bool CanConfirm
        {
            get
            {
                if (!IsPlacing)
                {
                    return false;
                }
                Vector2Int anchor = GridManager.Instance.WorldToCell(ghost.transform.position);
                return GridManager.Instance.CanPlace(currentDef, anchor, rotated);
            }
        }

        /// <summary>
        /// 生成幽灵体：实例化 prefab 后"降级"——禁碰撞、关物理、半透明
        /// </summary>
        private GameObject CreateGhost(PlacableItemDef def)
        {
            GameObject go = Instantiate(def.GhostPrefab != null ? def.GhostPrefab : def.Prefab);
            go.name = def.ItemId + "_Ghost";

            foreach (Collider2D col in go.GetComponentsInChildren<Collider2D>(true))
            {
                col.enabled = false;
            }
            foreach (Rigidbody2D rb in go.GetComponentsInChildren<Rigidbody2D>(true))
            {
                rb.simulated = false;
            }
            foreach (SpriteRenderer sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Color c = sr.color;
                c.a = ghostAlpha;
                sr.color = c;
            }
            return go;
        }

        /// <summary>
        /// 读取指针世界坐标（触屏优先，编辑器回退鼠标）；无有效指针时返回 null
        /// </summary>
        private Vector2? GetPointerWorldPosition()
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    return null;
                }
                return ScreenToWorld(touch.position);
            }

            if (Input.GetMouseButton(0))
            {
                return ScreenToWorld(Input.mousePosition);
            }
            return null;
        }

        private Vector2 ScreenToWorld(Vector2 screenPos)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }
    }
}

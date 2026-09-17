using System.Collections.Generic;
using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 旋转吐司 — 固定 3x3（尺寸写死于 RotatingToastSizeSync.FixedSize，忽略服务器种子/广播）
    /// 放置后持续旋转的方块：平滑转动 90°（2 秒）→ 停滞 1 秒 → 继续，循环往复
    /// 建造阶段可决定旋转方向（顺时针/逆时针，默认顺时针），见 SetClockwise/ToggleRotationDirection
    /// 生命周期：OnRunPhaseStart 启动旋转，OnBuildPhaseStart 停止并复位
    /// </summary>
    public class RotatingToast : ItemBase
    {
        [Header("尺寸")]
        [Tooltip("当前边长（格）：固定 3（历史随机已移除，运行期由 RotatingToastSizeSync 统一驱动）")]
        [SerializeField, Range(1, 3)] private int sizeInCells = 3;

        [Header("旋转参数")]
        [Tooltip("转动 90° 的动画时长（秒）")]
        [SerializeField, Range(0.1f, 10f)] private float rotateDuration = 2f;
        [Tooltip("每转完 90° 后的停滞时长（秒）")]
        [SerializeField, Range(0f, 10f)] private float pauseDuration = 1f;
        [Tooltip("默认顺时针（建造阶段可通过 SetClockwise/ToggleRotationDirection 修改）")]
        [SerializeField] private bool clockwise = true;

        [Header("引用")]
        [Tooltip("视觉物体（随尺寸缩放），留空则查找子物体 Visual")]
        [SerializeField] private Transform visual;
        [Tooltip("站立碰撞体（随尺寸缩放），留空则取自身 BoxCollider2D")]
        [SerializeField] private BoxCollider2D solidCollider;

        [Header("调试")]
        [Tooltip("运行即开始旋转（无 GameFlow 的测试场景使用；阶段系统接入后关闭）")]
        [SerializeField] private bool debugAutoRotate;

        [Header("黏性表面")]
        [Tooltip("黏住检测间隔（秒），检测外表面相邻格新出现的道具并黏住（与黄油块一致）")]
        [SerializeField, Range(0.02f, 0.5f)] private float stickCheckInterval = 0.1f;

        private FootprintBoxView box;
        private int appliedSize;          // 已应用到视觉/碰撞的尺寸（0=未初始化，取当前字段值）
        private Quaternion baseRotation;

        // ==================== 黏性表面（与 ButterBlock 行为一致） ====================
        private readonly List<Transform> stuckItems = new List<Transform>();
        private float nextStickCheckTime;

        private bool rotating;
        private float cycleTime;          // 当前周期已进行时间
        private float accumulatedAngle;   // 已累计转过的角度（带方向符号）
        private float cycleStartAngle;    // 本周期起始角度

        /// <summary>搭路：可站立的旋转方块</summary>
        public override ItemCategory Category => ItemCategory.Path;

        /// <summary>当前边长（格）</summary>
        public int SizeInCells => sizeInCells;
        /// <summary>当前是否顺时针</summary>
        public bool Clockwise => clockwise;
        /// <summary>是否正在旋转（跑动阶段）</summary>
        public bool IsRotating => rotating;

        private void Awake()
        {
            box = GetComponent<FootprintBoxView>();
            // visual 误配为根节点时按未配置处理：缩放根节点会连带 FootprintBox/碰撞体一起放大，
            // 导致虚线包围盒视觉尺寸失真（2x2 的框渲染得更大）
            if (visual == null || visual == transform)
            {
                Transform found = transform.Find("Visual");
                visual = found != null ? found : transform;
            }
            if (solidCollider == null)
            {
                solidCollider = GetComponent<BoxCollider2D>();
            }
            baseRotation = transform.rotation;
            appliedSize = Mathf.Clamp(sizeInCells, 1, 3); // 以 prefab 当前配置为已应用基准

            // 应用固定尺寸（CurrentSize 恒为 FixedSize，覆盖 prefab 上可能残留的旧尺寸）
            if (RotatingToastSizeSync.CurrentSize != sizeInCells)
            {
                SetSize(RotatingToastSizeSync.CurrentSize);
            }
            RotatingToastSizeSync.Register(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            RotatingToastSizeSync.Unregister(this);
        }

        // ==================== 尺寸接口 ====================

        /// <summary>
        /// 设置边长（1/2/3 格）：同步更新 footprint、站立碰撞体、视觉缩放
        /// 每轮开始时由 RotatingToastSizeSync 调用（本地与远端一致）
        /// </summary>
        public void SetSize(int cells)
        {
            int newSize = Mathf.Clamp(cells, 1, 3);
            // 增量缩放：按新旧尺寸比例调整视觉/碰撞，与历史调用次数无关
            if (appliedSize <= 0)
            {
                appliedSize = Mathf.Clamp(sizeInCells, 1, 3); // 首次调用前的 prefab 原值
            }
            float factor = (float)newSize / appliedSize;
            appliedSize = newSize;
            sizeInCells = newSize;

            if (box != null)
            {
                box.SetFootprint(new Vector2Int(newSize, newSize));
            }
            if (solidCollider != null)
            {
                solidCollider.size *= factor;
            }
            if (visual != null)
            {
                visual.localScale = new Vector3(
                    visual.localScale.x * factor,
                    visual.localScale.y * factor,
                    visual.localScale.z);
            }
        }

        /// <summary>
        /// Inspector 中直接修改 Size In Cells 时同步缩放碰撞体与 Visual。
        /// 仅编辑期生效：运行期 OnValidate 与 Awake 执行时机不定，可能与 Awake 的尺寸应用
        /// 叠加造成二次缩放；运行期尺寸统一由 Awake / RotatingToastSizeSync 驱动。
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }
            if (visual == null)
            {
                Transform found = transform.Find("Visual");
                visual = found != null ? found : transform;
            }
            if (solidCollider == null)
            {
                solidCollider = GetComponent<BoxCollider2D>();
            }
            if (box == null)
            {
                box = GetComponent<FootprintBoxView>();
            }
            // 以固定尺寸为准：prefab 上残留的旧 sizeInCells（如 1）不得反向覆盖
            // FootprintBoxView.footprint——否则刚改的 3x3 会被 OnValidate 弹回 1x1
            SetSize(RotatingToastSizeSync.FixedSize);
        }

        // ==================== 旋转方向接口（建造阶段调用） ====================

        /// <summary>
        /// 设置旋转方向（true=顺时针，false=逆时针；默认顺时针）
        /// 建造阶段的 UI 按钮调用；联机时由摆放仲裁结果同步到各端
        /// </summary>
        public void SetClockwise(bool value)
        {
            clockwise = value;
        }

        /// <summary>联机同步读取：以 mirrored 语义携带旋转方向（true=逆时针）</summary>
        public override bool Mirrored => !clockwise;

        /// <summary>联机同步写入：远端生成/快照恢复时应用旋转方向</summary>
        public override void SetMirrored(bool value)
        {
            SetClockwise(!value);
        }

        /// <summary>
        /// 切换旋转方向（顺时针 ↔ 逆时针）
        /// </summary>
        public void ToggleRotationDirection()
        {
            clockwise = !clockwise;
        }

        /// <summary>
        /// 摆放阶段旋转键/旋转按钮的"换向"入口（道具不可旋转时由 PlacementSession.Rotate 回退调用）：
        /// 吐司的朝向语义是旋转方向，映射为切换顺/逆时针
        /// </summary>
        public override void ToggleMirror()
        {
            ToggleRotationDirection();
        }

        // ==================== 黏性表面（与 ButterBlock 行为一致） ====================

        /// <summary>
        /// 上表面（footprint 顶边上方一行相邻格）集合：只有这些格子里的道具会被黏住，
        /// 左/右/下表面无黏性；被黏道具成为吐司子物体后随吐司旋转一起运动
        /// （与黄油块黏住道具的机制完全一致，仅黏性面限定为上表面）
        /// </summary>
        private List<Vector2Int> GetStickyCells()
        {
            GridManager grid = GridManager.Instance;
            var cells = new List<Vector2Int>();
            if (grid == null || Placed == null)
            {
                return cells;
            }
            Vector2Int footprint = box != null ? box.Footprint : new Vector2Int(sizeInCells, sizeInCells);
            // 仅上表面有黏性：只有 footprint 顶边一行的上方相邻格是黏性格，
            // 左/右/下表面均无黏性（世界坐标 +y 方向，与吐司旋转角度无关）
            foreach (Vector2Int own in grid.GetFootprintCells(Placed.AnchorCell, footprint, Placed.Rotation))
            {
                cells.Add(own + Vector2Int.up);
            }
            // 去掉与自身 footprint 重叠的格子（自身占位格不算外表面）
            HashSet<Vector2Int> ownCells = new HashSet<Vector2Int>(
                grid.GetFootprintCells(Placed.AnchorCell, footprint, Placed.Rotation));
            cells.RemoveAll(c => ownCells.Contains(c));
            return cells;
        }

        /// <summary>周期检测外表面相邻格的道具并黏住（成为子物体，随吐司旋转）</summary>
        private void StickNewItemsOnStickyCells()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return;
            }

            foreach (Vector2Int stickyCell in GetStickyCells())
            {
                PlacedItem candidate = grid.GetItemAt(stickyCell);
                if (candidate == null)
                {
                    continue;
                }
                // 跳过自身与已黏住的
                if (candidate == Placed || stuckItems.Contains(candidate.transform))
                {
                    continue;
                }
                // 跳过祖先物体（避免互相黏住形成环）
                if (transform.IsChildOf(candidate.transform))
                {
                    continue;
                }
                // 跳过不可黏目标：整道具不可黏（CanBeStuck=false），
                // 或限定了吸附点但本格不是吸附点（如流星锤仅底座挂点格可黏）
                ItemBase candidateItem = candidate.GetComponent<ItemBase>();
                if (candidateItem != null && !candidateItem.CanBeStuckAt(stickyCell))
                {
                    continue;
                }

                candidate.transform.SetParent(transform, worldPositionStays: true);
                stuckItems.Add(candidate.transform);
                // 黏住钩子：需要自定义跟随行为的道具（如流星锤）在此记录参数
                candidateItem?.OnStuckTo(transform, stickyCell);
            }
        }

        /// <summary>解除全部黏住（吐司被拆时道具恢复独立，停在原地）</summary>
        private void UnstickAll()
        {
            foreach (Transform stuck in stuckItems)
            {
                if (stuck != null)
                {
                    stuck.GetComponent<ItemBase>()?.OnUnstuck();
                    stuck.SetParent(null, worldPositionStays: true);
                }
            }
            stuckItems.Clear();
        }

        public override void OnRemoved()
        {
            UnstickAll();
            base.OnRemoved();
        }

        // ==================== 阶段钩子 ====================

        /// <summary>跑动阶段开始：启动持续旋转</summary>
        public override void OnRunPhaseStart()
        {
            rotating = true;
        }
        /// <summary>建造阶段开始：停止旋转并复位角度</summary>
        public override void OnBuildPhaseStart()
        {
            rotating = false;
            cycleTime = 0f;
            accumulatedAngle = 0f;
            cycleStartAngle = 0f;
            transform.rotation = baseRotation;
        }

        // ==================== 旋转驱动 ====================

        private void Update()
        {
            // 黏性表面检测（与黄油块一致：周期检测外表面相邻格，已放置后持续生效）
            if (Placed != null && Time.time >= nextStickCheckTime)
            {
                nextStickCheckTime = Time.time + stickCheckInterval;
                StickNewItemsOnStickyCells();
            }

            if (!rotating && !debugAutoRotate)
            {
                return;
            }

            cycleTime += Time.deltaTime;

            if (cycleTime <= rotateDuration)
            {
                // 转动段：SmoothStep 缓入缓出，2 秒内转完 90°
                float t = Mathf.SmoothStep(0f, 1f, cycleTime / rotateDuration);
                float stepAngle = (clockwise ? -90f : 90f) * t;
                transform.rotation = baseRotation * Quaternion.Euler(0f, 0f, cycleStartAngle + stepAngle);
            }
            else if (cycleTime >= rotateDuration + pauseDuration)
            {
                // 停滞段结束：累计 90°，进入下一周期
                accumulatedAngle = cycleStartAngle + (clockwise ? -90f : 90f);
                cycleStartAngle = accumulatedAngle;
                cycleTime = 0f;
            }
            // 停滞段：什么都不做，角度保持
        }
    }
}

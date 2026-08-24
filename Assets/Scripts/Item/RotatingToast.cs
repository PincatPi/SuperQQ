using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 旋转吐司 — 大小 1x1 / 2x2 / 3x3（每轮开始时随机决定，见 RotatingToastSizeSync）
    /// 放置后持续旋转的方块：平滑转动 90°（2 秒）→ 停滞 1 秒 → 继续，循环往复
    /// 建造阶段可决定旋转方向（顺时针/逆时针，默认顺时针），见 SetClockwise/ToggleRotationDirection
    /// 生命周期：OnRunPhaseStart 启动旋转，OnBuildPhaseStart 停止并复位
    /// </summary>
    public class RotatingToast : ItemBase
    {
        [Header("尺寸")]
        [Tooltip("当前边长（格）：1 / 2 / 3，世界尺寸 = 格数 x cellSize")]
        [SerializeField, Range(1, 3)] private int sizeInCells = 1;

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

        private FootprintBoxView box;
        private int appliedSize;          // 已应用到视觉/碰撞的尺寸（0=未初始化，取当前字段值）
        private Quaternion baseRotation;

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
            if (visual == null)
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

            // 应用本轮已决定的尺寸（尺寸同步先于实例化发生时生效）
            if (RotatingToastSizeSync.CurrentSize > 0 && RotatingToastSizeSync.CurrentSize != sizeInCells)
            {
                SetSize(RotatingToastSizeSync.CurrentSize);
            }
            RotatingToastSizeSync.Register(this);
        }

        private void OnDestroy()
        {
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
            if (sizeInCells > 0)
            {
                SetSize(sizeInCells);
            }
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

        /// <summary>
        /// 切换旋转方向（顺时针 ↔ 逆时针）
        /// </summary>
        public void ToggleRotationDirection()
        {
            clockwise = !clockwise;
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

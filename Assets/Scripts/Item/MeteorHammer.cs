using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 无敌流星锤 — 伤害类道具（6x3）
    /// 摆锤围绕底座挂点做钟摆运动：总弧角 130°，单向扫击 1.2s（往返 2.4s）
    /// 仅锤头（Arm 末端的 CircleCollider2D Trigger）致命，链条与底座安全
    /// 左右镜像调整摆动方向（ToggleMirror），不换 footprint
    /// 生命周期：OnRunPhaseStart 启动摆动，OnBuildPhaseStart 停止并复位
    /// </summary>
    public class MeteorHammer : ItemBase
    {
        [Header("摆动参数")]
        [Tooltip("摆臂（绕挂点旋转的物体）")]
        [SerializeField] private Transform arm;
        [Tooltip("摆动幅度：总弧角（度），锤头在 ±一半 之间摆动")]
        [SerializeField, Range(10f, 360f)] private float swingArcDegrees = 130f;
        [Tooltip("摆动速度：单向扫击时长（秒），越小摆得越快；完整往返 = 两倍")]
        [SerializeField, Range(0.1f, 10f)] private float sweepDuration = 1.2f;
        [Tooltip("摆臂长度（米），即挂点到锤头的距离；调整时同步移动锤头判定圈")]
        [SerializeField, Range(0.2f, 5f)] private float armLength = 1.03f;
        [Tooltip("锤头判定圈半径（米）")]
        [SerializeField, Range(0.05f, 1f)] private float headRadius = 0.24f;
        [Tooltip("锤头判定圈（留空则自动查找 Arm 下的 HammerHead）")]
        [SerializeField] private CircleCollider2D headCollider;
        [Tooltip("初始是否从左向右摆（镜像开关会反转）")]
        [SerializeField] private bool startFromLeft = true;
        [Tooltip("调试：运行即开始摆动（阶段系统接入后关闭，由 OnRunPhaseStart/OnBuildPhaseStart 控制）")]
        [SerializeField] private bool debugAutoSwing = true;

        /// <summary>命中效果参数：弹飞仅作死亡表现</summary>
        [Header("命中表现")]
        [Tooltip("命中后沿锤头运动方向的弹飞速度（仅死亡表现）")]
        [SerializeField] private float knockbackSpeed = 8f;

        private bool swinging;
        private float phaseTime;      // 当前在 [0, 2*sweepDuration] 相位中的时间
        private bool mirrored;
        private Vector3 armBaseScale = Vector3.one;

        public override ItemCategory Category => ItemCategory.Hazard;

        [Header("吸附")]
        [Tooltip("可被黄油黏住的吸附点（footprint 内的本地格坐标，通常设为底座挂点所在格）")]
        [SerializeField] private Vector2Int stickPointCell = new Vector2Int(3, 0);

        /// <summary>
        /// 仅吸附点格可被黄油黏住：摆锤有独立的钟摆运动逻辑，
        /// 只有底座挂点被黏住随承载物运动才是合理的（锤臂/锤头格命中不黏）
        /// </summary>
        public override bool CanBeStuckAt(Vector2Int stickyCell)
        {
            // 锤子不做四档旋转（仅镜像，格子布局不变），吸附点 = 锚点 + 本地格坐标
            return Placed != null && stickyCell == Placed.AnchorCell + stickPointCell;
        }

        /// <summary>锤头当前线速度方向（供命中表现查询）</summary>
        public Vector2 HeadVelocityDir { get; private set; } = Vector2.right;
        /// <summary>命中弹飞速度</summary>
        public float KnockbackSpeed => knockbackSpeed;

        private void Awake()
        {
            if (arm != null)
            {
                armBaseScale = arm.localScale;
            }
            ApplyHeadLayout();
            // 采样摆臂"竖直下挂"的世界旋转（摆放朝向；锤子不可旋转，此值在生命周期内有效）
            if (arm != null)
            {
                armBaseWorldRotation = arm.rotation;
            }
        }

        // 黏住跟随模型：底座纯父子刚性跟随（永远贴着黄油，位置/旋转都随承载物）；
        // 摆臂单独锁定世界摆动平面——被旋转的承载物带着公转时，链条始终竖直、左右摆
        private Quaternion armBaseWorldRotation;  // 摆臂"竖直下挂"对应的世界旋转（Awake 采样）
        private float currentSwingAngle;          // 当前摆动角（局部平面内的角度）

        private void LateUpdate()
        {
            // 被黏住（有父物体）时，把摆臂世界旋转锁回"竖直平面 + 当前摆角"，
            // 抵消承载物（旋转吐司经黄油）传给根节点的旋转，摆锤方向始终不变
            if (transform.parent != null && arm != null)
            {
                arm.rotation = armBaseWorldRotation * Quaternion.Euler(0f, 0f, currentSwingAngle);
            }
        }

        /// <summary>
        /// 编辑期调参实时生效（摆长/判定圈半径）
        /// </summary>
        private void OnValidate()
        {
            ApplyHeadLayout();
        }

        /// <summary>
        /// 按摆长/半径参数摆放锤头判定圈位置与大小
        /// </summary>
        private void ApplyHeadLayout()
        {
            if (headCollider == null && arm != null)
            {
                Transform head = arm.Find("HammerHead");
                if (head != null)
                {
                    headCollider = head.GetComponent<CircleCollider2D>();
                }
            }
            if (headCollider != null)
            {
                headCollider.transform.localPosition = new Vector3(0f, -armLength, 0f);
                headCollider.radius = headRadius;
            }
        }

        private void Start()
        {
            if (debugAutoSwing)
            {
                swinging = true;
            }
        }

        /// <summary>
        /// 切换左右镜像（改变摆动方向：从左到右扫 / 从右到左扫；摆放阶段对不可旋转道具按 R 触发）
        /// </summary>
        public override void ToggleMirror()
        {
            SetMirrored(!mirrored);
        }

        /// <summary>当前是否镜像（从左往右扫 / 从右往左扫）</summary>
        public override bool Mirrored => mirrored;

        /// <summary>设置镜像状态（联机同步写入）</summary>
        public override void SetMirrored(bool value)
        {
            mirrored = value;
            if (arm != null)
            {
                Vector3 s = armBaseScale;
                s.x = mirrored ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
                arm.localScale = s;
            }
        }

        // ==================== 阶段钩子 ====================

        public override void OnRunPhaseStart()
        {
            swinging = true;
            phaseTime = 0f;
        }

        public override void OnBuildPhaseStart()
        {
            swinging = false;
            phaseTime = 0f;
            ApplySwingAngle(0f);
        }

        // ==================== 摆动驱动 ====================

        private void Update()
        {
            if (!swinging || arm == null)
            {
                return;
            }

            float period = sweepDuration * 2f;
            phaseTime = Mathf.Repeat(phaseTime + Time.deltaTime, period);

            // PingPong 0→1→0，映射到 -arc/2 → +arc/2 → -arc/2
            float t = Mathf.PingPong(phaseTime, sweepDuration) / sweepDuration;
            float halfArc = swingArcDegrees * 0.5f;
            float angle = Mathf.Lerp(-halfArc, halfArc, t);
            if (!startFromLeft)
            {
                angle = -angle;
            }
            ApplySwingAngle(angle);

            // 角速度方向（供命中弹飞表现）：t 递增段与递减段方向相反
            bool risingEdge = phaseTime < sweepDuration;
            float dirSign = (risingEdge ? 1f : -1f) * (startFromLeft ? 1f : -1f) * (mirrored ? -1f : 1f);
            // 摆锤切向 ≈ 垂直于摆臂，取水平分量符号即可满足"沿运动方向弹飞"的表现
            HeadVelocityDir = new Vector2(dirSign, 0f);
        }

        private void ApplySwingAngle(float angle)
        {
            currentSwingAngle = angle;
            if (arm != null)
            {
                arm.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
    }
}

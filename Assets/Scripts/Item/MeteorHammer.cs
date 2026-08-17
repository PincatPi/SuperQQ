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
        /// 切换左右镜像（改变摆动方向：从左到右扫 / 从右到左扫）
        /// </summary>
        public void ToggleMirror()
        {
            mirrored = !mirrored;
            if (arm != null)
            {
                Vector3 s = armBaseScale;
                s.x = mirrored ? -s.x : s.x;
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
            if (arm != null)
            {
                arm.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
    }
}

using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 樱桃发射器 — 伤害类道具（1x1）
    /// 跑动阶段按固定节奏发射樱桃弹体：朝斜上方 45° 抛物线射出，可左右镜像切换发射方向。
    /// 弹体命中玩家即死，碰到地形消失（见 CherryProjectile）。
    /// 生命周期：OnRunPhaseStart 开始发射循环，OnBuildPhaseStart 停止。
    ///
    /// 联机同步模型：发射循环由服务器阶段同步驱动，各端在同一阶段起点开始按相同
    /// 节奏与确定性弹道本地模拟，弹体轨迹天然一致（差异仅阶段消息延迟的百毫秒级）；
    /// 击杀只在受害者本地端生效并上报，无需弹体级网络消息。
    /// </summary>
    public class CherryLauncher : ItemBase
    {
        [Header("发射")]
        [Tooltip("樱桃弹体 prefab（挂 CherryProjectile）")]
        [SerializeField] private CherryProjectile cherryPrefab;
        [Tooltip("发射间隔（秒）")]
        [SerializeField, Range(0.2f, 10f)] private float fireInterval = 1.8f;
        [Tooltip("发射角度（度，斜向上）")]
        [SerializeField, Range(10f, 80f)] private float launchAngle = 45f;
        [Tooltip("初速度（单位/秒）")]
        [SerializeField, Range(1f, 30f)] private float launchSpeed = 7f;
        [Tooltip("出膛点相对根节点的偏移（镜像时 x 自动取反）")]
        [SerializeField] private Vector2 muzzleOffset = new Vector2(0.3f, 0.2f);

        [Header("镜像")]
        [Tooltip("初始朝右发射（关闭则朝左；ToggleMirror 切换）")]
        [SerializeField] private bool startFacingRight = true;
        [Tooltip("视觉物体（镜像时翻转 x 缩放），留空则查找子物体 Visual")]
        [SerializeField] private Transform visual;

        [Header("发射形变动画")]
        [Tooltip("蓄力时长（秒）：发射前压扁蓄力的时间窗")]
        [SerializeField, Range(0.05f, 1f)] private float chargeDuration = 0.3f;
        [Tooltip("蓄力压扁程度：y 压缩到该比例（x 微鼓，挤压感）")]
        [SerializeField, Range(0.5f, 1f)] private float chargeSquashY = 0.78f;
        [Tooltip("弹出时长（秒）：发射瞬间拉伸后回弹的时间窗")]
        [SerializeField, Range(0.05f, 1f)] private float releaseDuration = 0.35f;
        [Tooltip("弹出拉伸程度：发射瞬间 y 拉伸到该比例（x 收缩）")]
        [SerializeField, Range(1f, 2f)] private float releaseStretchY = 1.3f;

        /// <summary>伤害类：发射致命弹体的陷阱</summary>
        public override ItemCategory Category => ItemCategory.Hazard;

        private bool mirrored;
        private bool firing;
        private long nextFireServerMs;        // 下一发对应的（估算）服务器时刻；按绝对时间网格对齐各端
        private Vector3 visualBaseScale = Vector3.one;

        /// <summary>当前是否朝左发射（镜像状态）</summary>
        public override bool Mirrored => mirrored;

        private void Awake()
        {
            if (visual == null)
            {
                Transform found = transform.Find("Visual");
                visual = found != null ? found : transform;
            }
            visualBaseScale = visual.localScale;
            mirrored = !startFacingRight;
            ApplyMirror();
        }

        // ==================== 镜像接口（建造阶段调用） ====================

        /// <summary>切换发射方向（朝左 ↔ 朝右；摆放阶段对不可旋转道具按 R 触发）</summary>
        public override void ToggleMirror()
        {
            mirrored = !mirrored;
            ApplyMirror();
        }

        /// <summary>设置发射方向（true=朝左）</summary>
        public override void SetMirrored(bool value)
        {
            mirrored = value;
            ApplyMirror();
        }

        // 当前形变系数（发射动画：蓄力压扁/弹出拉伸），由 UpdateDeform 每帧驱动
        private float deformX = 1f;
        private float deformY = 1f;

        private void ApplyMirror()
        {
            ApplyVisualScale();
        }

        /// <summary>合成视觉缩放：基础缩放 × 发射形变 × 镜像符号</summary>
        private void ApplyVisualScale()
        {
            if (visual != null)
            {
                Vector3 s = visualBaseScale;
                s.x = (mirrored ? -Mathf.Abs(s.x) : Mathf.Abs(s.x)) * deformX;
                s.y *= deformY;
                visual.localScale = s;
            }
        }

        /// <summary>
        /// 发射形变驱动（蓄力压扁 → 弹出拉伸回弹）：
        /// 蓄力窗内加速压扁（吸一口气），发射瞬间拉到最伸，随后带一点过冲弹回原形
        /// </summary>
        private void UpdateDeform(long nowMs)
        {
            float intervalMs = fireInterval * 1000f;
            float timeToFireMs = nextFireServerMs - nowMs;

            if (firing && timeToFireMs <= chargeDuration * 1000f && timeToFireMs > 0f)
            {
                // 蓄力段：ease-in 加速下压（x 微鼓、y 压扁），越临近发射压得越狠
                float t = 1f - timeToFireMs / (chargeDuration * 1000f);
                float eased = t * t;
                deformY = Mathf.Lerp(1f, chargeSquashY, eased);
                deformX = Mathf.Lerp(1f, 2f - chargeSquashY, eased); // 面积守恒感的横向微鼓
            }
            else
            {
                // 弹出段：从发射时刻起，拉伸偏移按阻尼余弦衰减（带过冲，回弹感）
                float sinceFireMs = intervalMs - timeToFireMs;
                if (sinceFireMs < releaseDuration * 1000f)
                {
                    float t = sinceFireMs / (releaseDuration * 1000f);
                    float damp = (1f - t) * Mathf.Cos(t * Mathf.PI * 1.5f); // 衰减+一次轻微过冲
                    deformY = 1f + (releaseStretchY - 1f) * damp;
                    deformX = 1f - (releaseStretchY - 1f) * 0.6f * damp;
                }
                else
                {
                    deformX = 1f;
                    deformY = 1f;
                }
            }
            ApplyVisualScale();
        }

        // ==================== 阶段钩子 ====================

        /// <summary>
        /// 跑动阶段开始：启动发射循环。
        /// 发射时刻对齐（估算）服务器绝对时间网格——各端在同一服务器时刻出弹，
        /// 不受阶段切换消息到达延迟差影响（残差仅对时误差，~10ms 级）
        /// </summary>
        public override void OnRunPhaseStart()
        {
            firing = true;
            nextFireServerMs = SuperQQ.Network.NetworkManager.EstimatedServerNowMs(); // 立即射出第一发
        }

        /// <summary>建造阶段开始：停止发射（场上存活弹体自然飞完，不强制清除）</summary>
        public override void OnBuildPhaseStart()
        {
            firing = false;
            // 复位发射形变，避免停在压扁/拉伸的中间态
            deformX = 1f;
            deformY = 1f;
            ApplyVisualScale();
        }

        // ==================== 发射驱动 ====================

        private void Update()
        {
            if (!firing || cherryPrefab == null)
            {
                return;
            }

            long nowMs = SuperQQ.Network.NetworkManager.EstimatedServerNowMs();
            UpdateDeform(nowMs);

            if (nowMs < nextFireServerMs)
            {
                return;
            }
            // 按绝对时间网格推进（不用 += dt 累加，避免各端帧率/启动时刻差造成相位漂移）
            nextFireServerMs += (long)(fireInterval * 1000f);
            Fire();
        }

        /// <summary>
        /// 发射一颗樱桃：沿发射器自身朝向的斜上 45°（发射器被黄油黏住随承载物旋转时，
        /// 发射方向与出膛点随之整体旋转），镜像决定左右
        /// </summary>
        private void Fire()
        {
            float dirSign = mirrored ? -1f : 1f;
            float rad = launchAngle * Mathf.Deg2Rad;
            // 局部空间发射方向（朝右为基准），经当前世界旋转变换到世界空间
            Vector2 localDir = new Vector2(Mathf.Cos(rad) * dirSign, Mathf.Sin(rad));
            Vector2 velocity = (Vector2)(transform.rotation * localDir) * launchSpeed;

            // 出膛点同样随发射器旋转
            Vector3 muzzle = transform.position
                + transform.rotation * new Vector3(muzzleOffset.x * dirSign, muzzleOffset.y, 0f);
            // 弹体独立存在（不挂子级）：发射器随承载物运动不会拖动已出膛的弹体；
            // 击杀归属通过 owner 引用传递（TrapKillReporter 链路）
            CherryProjectile cherry = Instantiate(cherryPrefab, muzzle, Quaternion.identity);
            cherry.Launch(velocity, this);
            activeCherries.Add(cherry);
        }

        // 存活弹体登记表：发射器被拆时统一清理
        private readonly System.Collections.Generic.List<CherryProjectile> activeCherries
            = new System.Collections.Generic.List<CherryProjectile>();

        /// <summary>被移除（拆除/拾回）：清理全部存活弹体</summary>
        public override void OnRemoved()
        {
            for (int i = activeCherries.Count - 1; i >= 0; i--)
            {
                if (activeCherries[i] != null)
                {
                    Destroy(activeCherries[i].gameObject);
                }
            }
            activeCherries.Clear();
            base.OnRemoved();
        }
    }
}

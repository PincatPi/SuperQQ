using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// 背景云漂动 — 挂在云朵 SpriteRenderer 物体上
    /// 沿水平方向匀速漂移，飞出 LevelBounds 边界（含云身宽度余量）后从另一侧绕回；
    /// 附带多组"假动画"：上下浮动、缩放呼吸、透明度呼吸、边界淡入淡出，
    /// 各组周期不同且互相错相，叠加出云在自然变幻的感觉
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CloudDrift : MonoBehaviour
    {
        [Header("漂移")]
        [Tooltip("水平漂移速度（单位/秒），正数向右、负数向左；真云很慢，建议 0.05~0.15")]
        [SerializeField] private float speed = 0.1f;
        [Tooltip("飞出边界外多少距离后才从另一侧绕回（按云身宽度留出余量，避免闪现）")]
        [SerializeField] private float wrapMargin = 1.5f;

        [Header("上下浮动")]
        [Tooltip("上下浮动幅度（单位）；真云几乎不浮动，建议 0.03~0.08")]
        [SerializeField] private float bobAmplitude = 0.05f;
        [Tooltip("上下浮动周期（秒）；越长越沉稳")]
        [SerializeField] private float bobPeriod = 20f;

        [Header("缩放呼吸")]
        [Tooltip("缩放呼吸幅度（0.05 = 最大放大 5%）")]
        [SerializeField, Range(0f, 0.2f)] private float scaleAmplitude = 0.06f;
        [Tooltip("缩放呼吸周期（秒）")]
        [SerializeField] private float scalePeriod = 14f;

        [Header("透明度呼吸")]
        [Tooltip("透明度基准值（云整体偏透更像远处）")]
        [SerializeField, Range(0f, 1f)] private float baseAlpha = 0.9f;
        [Tooltip("透明度呼吸幅度（0.1 = 在基准值上下浮动 0.1）")]
        [SerializeField, Range(0f, 0.5f)] private float alphaAmplitude = 0.08f;
        [Tooltip("透明度呼吸周期（秒）")]
        [SerializeField] private float alphaPeriod = 11f;

        [Header("边界淡入淡出")]
        [Tooltip("进入边界后多少距离内从透明渐变到正常（绕回不再硬切）")]
        [SerializeField] private float edgeFadeDistance = 2f;

        private SpriteRenderer spriteRenderer;
        private Vector3 baseScale;
        private float baseY;
        private float bobPhase;
        private float scalePhase;
        private float alphaPhase;

        private void Start()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            baseScale = transform.localScale;
            baseY = transform.position.y;
            // 各朵云相位错开（按初始 x 取哈希），且三组动画用不同倍率进一步错相
            float hash = transform.position.x * 0.7f;
            bobPhase = hash;
            scalePhase = hash * 1.618f;
            alphaPhase = hash * 2.718f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            pos.x += speed * dt;

            // 绕回：超出边界一侧（含余量）则从另一侧重新进入
            LevelBounds levelBounds = LevelBounds.Instance;
            Bounds bounds = default;
            bool hasBounds = levelBounds != null;
            if (hasBounds)
            {
                bounds = levelBounds.Bounds;
                if (speed > 0f && pos.x > bounds.max.x + wrapMargin)
                {
                    pos.x = bounds.min.x - wrapMargin;
                }
                else if (speed < 0f && pos.x < bounds.min.x - wrapMargin)
                {
                    pos.x = bounds.max.x + wrapMargin;
                }
            }

            // 上下浮动
            bobPhase += dt * (Mathf.PI * 2f) / Mathf.Max(0.01f, bobPeriod);
            pos.y = baseY + Mathf.Sin(bobPhase) * bobAmplitude;
            transform.position = pos;

            // 缩放呼吸
            scalePhase += dt * (Mathf.PI * 2f) / Mathf.Max(0.01f, scalePeriod);
            float scaleMul = 1f + Mathf.Sin(scalePhase) * scaleAmplitude;
            transform.localScale = baseScale * scaleMul;

            // 透明度呼吸 + 边界淡入淡出
            alphaPhase += dt * (Mathf.PI * 2f) / Mathf.Max(0.01f, alphaPeriod);
            float alpha = baseAlpha + Mathf.Sin(alphaPhase) * alphaAmplitude;
            if (hasBounds && edgeFadeDistance > 0.001f)
            {
                // 距两侧边界的较小距离决定淡入程度：刚绕回/即将绕出时趋近透明
                float distToEdge = Mathf.Min(pos.x - bounds.min.x, bounds.max.x - pos.x);
                alpha *= Mathf.Clamp01(distToEdge / edgeFadeDistance);
            }
            Color c = spriteRenderer.color;
            c.a = Mathf.Clamp01(alpha);
            spriteRenderer.color = c;
        }
    }
}

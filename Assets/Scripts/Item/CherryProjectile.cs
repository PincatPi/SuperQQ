using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 樱桃弹体 — 樱桃发射器射出的抛物线弹体
    /// 手动弹道积分（不用物理重力，保证各端轨迹一致）：初速按发射角分解，y 向受重力加速度。
    /// 命中玩家：玩家死亡（复用陷阱击杀归属链路，经父级找到发射器 ItemBase）；
    /// 碰到地形/障碍物（实心非 Trigger 碰撞体）：播放消失效果后销毁。
    /// 联机同步模型：弹体由在场各端基于同一发射节奏本地模拟（确定性弹道），
    /// 击杀只在受害者本地端生效并自行上报（与现有陷阱触发模型一致）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class CherryProjectile : MonoBehaviour
    {
        [Header("弹道")]
        [Tooltip("重力加速度（单位/秒²），决定抛物线下坠速度")]
        [SerializeField] private float gravity = 9.8f;
        [Tooltip("最长存活时间（秒），兜底销毁（飞出边界等极端情况）")]
        [SerializeField] private float maxLifetime = 8f;

        [Header("朝向")]
        [Tooltip("飞行朝向跟随速度方向（羽毛球式：樱桃朝前、果酱拖尾朝后，随抛物线自然翻转）")]
        [SerializeField] private bool alignToVelocity = true;
        [Tooltip("贴图朝向补偿角（度）：贴图竖直向上为 0；若头部在贴图上方则 -90 使头部对准速度方向")]
        [SerializeField] private float facingOffset = -90f;

        [Header("消失效果")]
        [Tooltip("碰到地形后的消失时长（秒）：缩小+淡出动画")]
        [SerializeField] private float vanishDuration = 0.25f;
        [Tooltip("碰到地形时播放的音效（樱桃爆炸音）")]
        [SerializeField] private AudioClip vanishSfx;

        [Header("传送")]
        [Tooltip("被传送门传送后的触发冷却（秒）：防止几何重叠导致同帧/连续重复传送")]
        [SerializeField, Range(0.05f, 2f)] private float teleportCooldown = 0.5f;

        private Vector2 velocity;         // 当前速度（x 恒定，y 受重力）
        private float age;
        private bool vanishing;
        private float vanishStartTime;
        private Vector3 vanishStartScale;
        private SpriteRenderer[] renderers;
        private ItemBase ownerItem;       // 发射器（击杀归属解析用；弹体不挂其子级，避免被发射器运动拖动）
        private float lastTeleportTime = -999f; // 上次被传送的时刻

        /// <summary>
        /// 由发射器调用：以初速向量发射（方向已含左右镜像与发射器旋转）
        /// </summary>
        /// <param name="owner">发射器 ItemBase（击杀归属；发射器被拆时由其清理存活弹体）</param>
        public void Launch(Vector2 initialVelocity, ItemBase owner = null)
        {
            velocity = initialVelocity;
            ownerItem = owner;
            age = 0f;
        }

        /// <summary>是否处于传送冷却中（传送门据此避免重复触发）</summary>
        public bool BTeleportCoolingDown => Time.time - lastTeleportTime < teleportCooldown;

        /// <summary>
        /// 被传送门传送：瞬移到目标点，保持速度向量继续飞行（击杀归属不变）。
        /// 传送发生在各端本地（轨迹确定，各端同一时机碰门），天然各端同步
        /// </summary>
        public void TeleportTo(Vector2 target)
        {
            if (vanishing)
            {
                return;
            }
            lastTeleportTime = Time.time;
            transform.position = target;
        }

        private Animator animator;
        private Color[] baseColors;       // 各渲染器的基础色（乘昼夜色调的基准）
        private static readonly int VanishStateHash = Animator.StringToHash("Vanish");

        private void Awake()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            animator = GetComponent<Animator>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                baseColors[i] = renderers[i].color;
            }
        }

        private void Update()
        {
            if (vanishing)
            {
                // 消失中：冻结原地，到期销毁；无帧动画时回退缩小淡出补间
                float t = (Time.time - vanishStartTime) / Mathf.Max(0.01f, vanishDuration);
                if (t >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    transform.localScale = vanishStartScale * (1f - t);
                    SetAlpha(1f - t);
                }
                return;
            }

            // 弹道积分：y 向受重力
            velocity.y -= gravity * Time.deltaTime;
            transform.position += (Vector3)(velocity * Time.deltaTime);

            // 朝向跟随速度：上升段头朝前上方，顶点放平，下落段头朝前下方（羽毛球式自然翻转）
            if (alignToVelocity && velocity.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle + facingOffset);
            }

            // 昼夜色调：弹体是运行时生成的不在 Map 层级下，采样全局色调同步变暗（保留自身 alpha）
            ApplyNightTint();

            age += Time.deltaTime;
            if (age >= maxLifetime)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (vanishing)
            {
                return;
            }

            // 玩家：接触即死（陷阱模型：只在受害者本地端触发，归属经父级发射器解析）
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                if (!player.BIsDead && !player.BIsGhost)
                {
                    // 归属传给发射器组件（GetComponentInParent 解析到发射器 ItemBase → Placed.OwnerKey）
                    TrapKillReporter.ReportKill(ownerItem != null ? ownerItem : this, player);
                    player.PlayerDie();
                }
                // 命中玩家同样播放消失动画（弹体不穿透玩家）
                Vanish();
                return;
            }

            // 地形/障碍物：实心非 Trigger 碰撞体（排除发射器自身，避免出膛即自毁）
            if (other.isTrigger)
            {
                return;
            }
            if (other.GetComponentInParent<CherryLauncher>() != null)
            {
                return;
            }

            Vanish();
        }

        /// <summary>
        /// 碰到地形/玩家：播放消失效果后销毁。
        /// 优先播放 CherryVanish 帧动画（bomb 序列帧）；无状态机时回退缩小淡出补间
        /// </summary>
        private void Vanish()
        {
            vanishing = true;
            vanishStartTime = Time.time;
            vanishStartScale = transform.localScale;

            // 帧动画路径：冻结运动（留在碰撞点原位播爆炸），不缩放淡出
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.CrossFadeInFixedTime(VanishStateHash, 0.02f);
            }

            if (vanishSfx != null)
            {
                AudioSource.PlayClipAtPoint(vanishSfx, transform.position);
            }
        }

        private void SetAlpha(float alpha)
        {
            if (renderers == null)
            {
                return;
            }
            foreach (SpriteRenderer r in renderers)
            {
                if (r == null)
                {
                    continue;
                }
                Color c = r.color;
                c.a = alpha;
                r.color = c;
            }
        }

        /// <summary>把基础色乘上当前昼夜色调（rgb 变暗，alpha 不动）</summary>
        private void ApplyNightTint()
        {
            if (renderers == null || baseColors == null)
            {
                return;
            }
            Color tint = SuperQQ.Map.MapDayNightController.CurrentTint;
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }
                Color day = baseColors[i];
                r.color = new Color(day.r * tint.r, day.g * tint.g, day.b * tint.b, r.color.a);
            }
        }
    }
}

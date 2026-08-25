using SuperQQ.Grid;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 爆米花拳王 — 伤害类道具（2x2）
    /// 朝左/朝右（镜像切换）。以朝右为例：攻击范围为身体右侧延伸的 2x2 区域。
    /// 跑动阶段检测到攻击范围内有玩家时出拳（播放 Punch 动画）；
    /// 出拳动画播放完毕时，仍在攻击范围内的玩家死亡（本地玩家由各端本地判定，与陷阱模型一致）。
    /// 生命周期：OnRunPhaseStart 激活，OnBuildPhaseStart 停回 Idle。
    ///
    /// 联机同步模型：各端基于同步的玩家位置各自检测与播放出拳动画
    /// （远端玩家位置 30Hz 同步，动画相位差异在百毫秒级，观感一致）；
    /// 击杀只在受害者本地端判定并上报（TrapKillReporter 链路），无需额外协议。
    /// </summary>
    public class PopcornBoxer : ItemBase
    {
        [Header("动画")]
        [Tooltip(" Animator（Idle/Punch 两状态）")]
        [SerializeField] private Animator animator;
        [Tooltip("出拳动画时长（秒），应与 Punch 剪辑长度一致")]
        [SerializeField, Range(0.2f, 5f)] private float punchDuration = 1.5f;
        [Tooltip("出拳冷却（秒）：一拳打完到可以再次出拳的间隔")]
        [SerializeField, Range(0f, 3f)] private float punchCooldown = 0.3f;
        [Tooltip("出拳时拳头的横向拉伸倍率（让拳头视觉覆盖攻击范围远端）")]
        [SerializeField, Range(1f, 3f)] private float punchStretchX = 1.6f;

        [Header("攻击范围")]
        [Tooltip("攻击范围向朝向侧延伸的格数（高固定 2 格）")]
        [SerializeField, Range(1, 6)] private int attackRangeCells = 3;

        [Header("朝向")]
        [Tooltip("待机站姿朝右（关闭则朝左；仅站姿，出拳方向由目标侧自动决定）")]
        [SerializeField] private bool startFacingRight = true;
        [Tooltip("视觉物体（朝向翻转 x 缩放），留空则查找子物体 Visual")]
        [SerializeField] private Transform visual;

        [Header("调试")]
        [Tooltip(" Scene 视图绘制攻击范围（红色=出拳中，黄色=待机）")]
        [SerializeField] private bool drawAttackZone = true;

        /// <summary>伤害类：出拳击杀的陷阱</summary>
        public override ItemCategory Category => ItemCategory.Hazard;

        private static readonly int IdleStateHash = Animator.StringToHash("Idle");
        private static readonly int PunchStateHash = Animator.StringToHash("Punch");

        private bool active;
        private bool punching;
        private float punchEndTime;      // Time.time 时刻：本拳结算点
        private float nextPunchTime;     // Time.time 时刻：冷却结束可再出拳
        private Vector3 visualBaseScale = Vector3.one;

        // 运行时朝向（仅出拳表现用：攻击哪边就面朝哪边）：
        // 双侧攻击模型下不再有摆放朝向，默认站姿朝右
        private bool facingLeft;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
            if (visual == null)
            {
                Transform found = transform.Find("Visual");
                visual = found != null ? found : transform;
            }
            visualBaseScale = visual.localScale;
            facingLeft = !startFacingRight;
            ApplyVisualScale();
            baseWorldRotation = transform.rotation;
        }

        // 被黏住期间的世界朝向锁定：位置随承载物公转（父子层级），但身体保持竖直不倒立；
        // 攻击区读同一 transform，锁定后始终指向水平朝向侧，与身体姿态一致
        private Quaternion baseWorldRotation;

        private void LateUpdate()
        {
            if (transform.parent != null)
            {
                transform.rotation = baseWorldRotation;
            }
            else
            {
                // 未黏住时持续采样自身朝向为基准（摆放朝向被尊重）
                baseWorldRotation = transform.rotation;
            }
        }

        // 出拳拉伸系数（x 向）：出拳过程中 1 → punchStretchX，拳头视觉覆盖攻击范围远端
        private float punchStretchMul = 1f;

        /// <summary>合成视觉缩放：基础缩放 × 朝向符号 × 出拳横向拉伸</summary>
        private void ApplyVisualScale()
        {
            if (visual != null)
            {
                Vector3 s = visualBaseScale;
                s.x = (facingLeft ? -Mathf.Abs(s.x) : Mathf.Abs(s.x)) * punchStretchMul;
                visual.localScale = s;
            }
        }

        // ==================== 阶段钩子 ====================

        public override void OnRunPhaseStart()
        {
            active = true;
            punching = false;
            nextPunchTime = 0f;
            leftOccupiedSince = 0f;
            rightOccupiedSince = 0f;
            Debug.Log($"[PopcornBoxer] 激活: 锚点={(Placed != null ? Placed.AnchorCell.ToString() : "null")} pos={transform.position}", this);
        }

        public override void OnBuildPhaseStart()
        {
            active = false;
            punching = false;
            if (animator != null)
            {
                animator.CrossFadeInFixedTime(IdleStateHash, 0.05f);
            }
        }

        // ==================== 攻击驱动 ====================

        /// <summary>
        /// 攻击范围（中心/尺寸/世界角度）：定义在自身局部坐标系（指定侧延伸 range×2），
        /// 随 transform 旋转整体旋转——被黄油黏住随承载物（旋转吐司）转动时攻击区同步转动
        /// </summary>
        /// <param name="side">+1=右侧攻击区，-1=左侧攻击区</param>
        public void GetAttackZone(int side, out Vector2 center, out Vector2 size, out float angleDeg)
        {
            float cs = GridManager.Instance != null ? GridManager.Instance.PublicCellSize : 0.5f;
            // 局部偏移：攻击区紧贴身体边缘（距根节点 1 格）向外延伸 attackRangeCells 格，
            // 中心 = 1 + range/2 格；高固定 2 格
            float offsetCells = 1f + attackRangeCells * 0.5f;
            Vector3 localOffset = new Vector3(side * offsetCells * cs, 0f, 0f);
            center = transform.TransformPoint(localOffset);
            size = new Vector2(cs * attackRangeCells, cs * 2f);
            angleDeg = transform.eulerAngles.z;
        }

        /// <summary>点是否在指定侧攻击范围内（旋转矩形判定）</summary>
        public bool IsInAttackZone(Vector2 point, int side)
        {
            GetAttackZone(side, out Vector2 center, out Vector2 size, out float angleDeg);
            Vector2 local = Quaternion.Inverse(Quaternion.Euler(0f, 0f, angleDeg)) * (point - center);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
        }

        private void Update()
        {
            if (!active || Placed == null)
            {
                return;
            }

            if (punching)
            {
                // 出拳过程：拳头随动画进度横向拉伸，动画末尾（结算点）伸到最远覆盖攻击区
                float progress = 1f - (punchEndTime - Time.time) / Mathf.Max(0.01f, punchDuration);
                punchStretchMul = Mathf.Lerp(1f, punchStretchX, Mathf.Clamp01(progress));
                ApplyVisualScale();

                // 出拳动画播完：结算——仍在攻击范围内的本地玩家死亡
                if (Time.time >= punchEndTime)
                {
                    punching = false;
                    nextPunchTime = Time.time + punchCooldown;
                    SettlePunchKill();
                    if (animator != null)
                    {
                        animator.CrossFadeInFixedTime(IdleStateHash, 0.1f);
                    }
                }
                return;
            }

            // 非出拳：拉伸系数快速回落
            if (punchStretchMul != 1f)
            {
                punchStretchMul = Mathf.MoveTowards(punchStretchMul, 1f, Time.deltaTime * 6f);
                ApplyVisualScale();
            }

            // 待机：刷新两侧有人时刻，先到者先得（任一侧有人且冷却结束 → 朝该侧出拳）
            UpdateSideOccupancy();
            if (Time.time >= nextPunchTime)
            {
                int side = DetectPlayerSide();
                if (side != 0)
                {
                    punching = true;
                    punchSide = side;
                    punchEndTime = Time.time + punchDuration;
                    // 出拳朝向目标侧（仅表现：面朝出拳方向）
                    facingLeft = side < 0;
                    ApplyVisualScale();
                    GetAttackZone(side, out Vector2 zc, out _, out _);
                    Debug.Log($"[PopcornBoxer] 出拳: 方向={(side < 0 ? "左" : "右")} 攻击区中心={zc}", this);
                    if (animator != null)
                    {
                        animator.CrossFadeInFixedTime(PunchStateHash, 0.05f);
                    }
                }
            }
        }

        // 本拳攻击侧（+1=右，-1=左）：结算只判该侧攻击区
        private int punchSide = 1;

        // 两侧攻击区"首次有人"的时刻（0=当前无人；先到先得判定用）
        private float leftOccupiedSince;
        private float rightOccupiedSince;

        /// <summary>每帧刷新两侧攻击区的首次有人时刻（有人且未记录则记为现在，无人则清零）</summary>
        private void UpdateSideOccupancy()
        {
            bool leftHas = SideHasPlayer(-1);
            bool rightHas = SideHasPlayer(1);
            leftOccupiedSince = leftHas ? (leftOccupiedSince > 0f ? leftOccupiedSince : Time.time) : 0f;
            rightOccupiedSince = rightHas ? (rightOccupiedSince > 0f ? rightOccupiedSince : Time.time) : 0f;
        }

        /// <summary>指定侧攻击区是否有存活玩家（本地+远端表现都计入）</summary>
        private bool SideHasPlayer(int side)
        {
            if (LevelPlayerRegistry.Instance == null)
            {
                return false;
            }
            System.Collections.Generic.IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController p = players[i];
                if (p == null || p.BIsDead || p.BIsGhost)
                {
                    continue;
                }
                if (IsInAttackZone(p.transform.position, side))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 先到先得：返回先有人的一侧（+1=右 / -1=左 / 0=两侧皆无）。
        /// 两侧都有人时取首次有人时刻更早的一侧（一侧无人则另一侧先到先得）
        /// </summary>
        private int DetectPlayerSide()
        {
            if (leftOccupiedSince <= 0f && rightOccupiedSince <= 0f)
            {
                return 0;
            }
            if (leftOccupiedSince <= 0f)
            {
                return 1;
            }
            if (rightOccupiedSince <= 0f)
            {
                return -1;
            }
            return leftOccupiedSince <= rightOccupiedSince ? -1 : 1;
        }

        /// <summary>出拳结算：本地玩家仍在攻击范围内则死亡（各端只判本地玩家，与陷阱模型一致）</summary>
        private void SettlePunchKill()
        {
            if (LevelPlayerRegistry.Instance == null)
            {
                return;
            }
            System.Collections.Generic.IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController p = players[i];
                if (p == null || p.BIsDead || p.BIsGhost || !p.BIsLocal)
                {
                    continue;
                }
                // 只结算本拳攻击侧的攻击区（另一侧有人不受这一拳影响）
                if (IsInAttackZone(p.transform.position, punchSide))
                {
                    Debug.Log($"[PopcornBoxer] 击杀: {p.name} pos={p.transform.position}", this);
                    TrapKillReporter.ReportKill(this, p);
                    p.PlayerDie();
                }
                else
                {
                    GetAttackZone(punchSide, out Vector2 zc, out _, out _);
                    Debug.Log($"[PopcornBoxer] 结算未命中: {p.name} pos={p.transform.position} 攻击区中心={zc}", this);
                }
            }
        }

        // ==================== 编辑期可视化 ====================

        private void OnDrawGizmos()
        {
            if (!drawAttackZone || !Application.isPlaying || Placed == null)
            {
                return;
            }
            Matrix4x4 prev = Gizmos.matrix;
            for (int s = -1; s <= 1; s += 2)
            {
                GetAttackZone(s, out Vector2 center, out Vector2 size, out float angleDeg);
                // 出拳侧红色、待机侧黄色
                bool isPunchSide = punching && punchSide == s;
                Gizmos.color = isPunchSide ? new Color(1f, 0.2f, 0.2f, 0.35f) : new Color(1f, 0.85f, 0.2f, 0.25f);
                Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angleDeg), Vector3.one);
                Gizmos.DrawCube(Vector3.zero, size);
            }
            Gizmos.matrix = prev;
        }
    }
}

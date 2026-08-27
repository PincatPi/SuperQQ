using SuperQQ.GameFlow;
using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// Level2 柱子自动升降控制器 — 挂在 Map 的 Pillar 父节点上。
    ///
    /// PLAYING 阶段循环（每段时长 legDuration，到位后进入下一段）：
    ///   A: Pillar1 上升 pillar1Cells 格 + Pillar3 上升 pillar3Cells 格，同时 Pillar2 下降 pillar2Cells 格（三柱同步）
    ///   B: 三根柱子同步回到原位 → 回到 A 循环
    /// 非 PLAYING 阶段：三根柱子以 restoreDuration 时长平滑恢复原位并停住。
    /// </summary>
    public class PillarElevatorController : MonoBehaviour
    {
        [Header("柱子节点")]
        [SerializeField] private Transform pillar1;
        [SerializeField] private Transform pillar2;
        [SerializeField] private Transform pillar3;

        [Header("升降幅度（格）")]
        [SerializeField] private float pillar1Cells = 10f;
        [SerializeField] private float pillar3Cells = 8f;
        [SerializeField] private float pillar2Cells = 12f;

        [Header("节奏")]
        [Tooltip("每段升降时长（秒）")]
        [SerializeField, Min(0.1f)] private float legDuration = 2f;
        [Tooltip("非 PLAYING 阶段恢复原位的时长（秒）")]
        [SerializeField, Min(0.05f)] private float restoreDuration = 1f;

        // 段序列：0=A(P1↑+P3↑+P2↓ 同步) 1=B(三柱同步回原位)
        private int legIndex;
        private float legTimer;
        private Vector3 origin1, origin2, origin3;
        private Vector3 from1, from2, from3;
        private bool playing;

        private void Awake()
        {
            CacheOrigins();
        }

        private void OnEnable()
        {
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        private void Start()
        {
            // Awake 时 OnEnable 可能早于 Manager 单例就绪，Start 兜底补订阅
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                GamePhaseManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
            playing = IsPlayingPhase();
            if (!playing)
            {
                SnapToOrigins();
            }
        }

        private void OnDisable()
        {
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            }
        }

        private void HandlePhaseChanged(GamePhaseBase previous, GamePhaseBase next)
        {
            bool nowPlaying = next is PlayingPhase;
            if (nowPlaying && !playing)
            {
                // 进入 PLAYING：从原位开始 A 段
                playing = true;
                legIndex = 0;
                legTimer = 0f;
                BeginLeg();
            }
            else if (!nowPlaying && playing)
            {
                // 离开 PLAYING：进入原位恢复
                playing = false;
                legTimer = 0f;
                BeginLeg(); // from = 当前位置，目标 = 原位（由 Update 的恢复分支处理）
            }
        }

        private static bool IsPlayingPhase()
        {
            return GamePhaseManager.Instance != null
                && GamePhaseManager.Instance.CurrentPhaseAsset is PlayingPhase;
        }

        private void CacheOrigins()
        {
            if (pillar1 != null) origin1 = pillar1.position;
            if (pillar2 != null) origin2 = pillar2.position;
            if (pillar3 != null) origin3 = pillar3.position;
        }

        private void SnapToOrigins()
        {
            if (pillar1 != null) pillar1.position = origin1;
            if (pillar2 != null) pillar2.position = origin2;
            if (pillar3 != null) pillar3.position = origin3;
        }

        private float CellSize => GridManager.Instance != null ? GridManager.Instance.PublicCellSize : 0.5f;

        /// <summary>记录段起点位置（每次进入新段/恢复时调用）</summary>
        private void BeginLeg()
        {
            if (pillar1 != null) from1 = pillar1.position;
            if (pillar2 != null) from2 = pillar2.position;
            if (pillar3 != null) from3 = pillar3.position;
        }

        /// <summary>当前段各柱子的目标位置</summary>
        private void GetLegTargets(out Vector3 t1, out Vector3 t2, out Vector3 t3)
        {
            float cs = CellSize;
            if (legIndex == 0)
            {
                // A: P1↑ P3↑ P2↓ 三柱同步
                t1 = origin1 + Vector3.up * (pillar1Cells * cs);
                t2 = origin2 + Vector3.down * (pillar2Cells * cs);
                t3 = origin3 + Vector3.up * (pillar3Cells * cs);
            }
            else
            {
                // B: 三柱同步回原位
                t1 = origin1;
                t2 = origin2;
                t3 = origin3;
            }
        }

        private void Update()
        {
            if (!playing)
            {
                // 非 PLAYING：平滑恢复原位后停住
                if (restoreDuration <= 0f)
                {
                    SnapToOrigins();
                    return;
                }
                legTimer += Time.deltaTime;
                float rt = Mathf.Clamp01(legTimer / restoreDuration);
                rt = rt * rt * (3f - 2f * rt); // smoothstep
                if (pillar1 != null) pillar1.position = Vector3.Lerp(from1, origin1, rt);
                if (pillar2 != null) pillar2.position = Vector3.Lerp(from2, origin2, rt);
                if (pillar3 != null) pillar3.position = Vector3.Lerp(from3, origin3, rt);
                return;
            }

            legTimer += Time.deltaTime;
            float t = Mathf.Clamp01(legTimer / legDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep 起止缓动

            GetLegTargets(out Vector3 t1, out Vector3 t2, out Vector3 t3);
            if (pillar1 != null) pillar1.position = Vector3.Lerp(from1, t1, eased);
            if (pillar2 != null) pillar2.position = Vector3.Lerp(from2, t2, eased);
            if (pillar3 != null) pillar3.position = Vector3.Lerp(from3, t3, eased);

            if (t >= 1f)
            {
                legIndex = (legIndex + 1) % 2;
                legTimer = 0f;
                BeginLeg();
            }
        }
    }
}

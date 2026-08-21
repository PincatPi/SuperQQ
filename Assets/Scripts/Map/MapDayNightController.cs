using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// 地图昼夜切换 — 挂在 Map prefab 根节点
    /// 按 PlayerScoreManager.CurrentRoundIndex 判定：奇数回合白天、偶数回合黑夜。
    /// 夜晚表现：水面与水面上的船上升指定格数、全体 SpriteRenderer 色调压暗（乘算夜色）；
    /// 白天全部还原（原始位置与颜色在首次应用前缓存，可反复切换无累积误差）。
    /// 联机模式下各端轮次由服务器驱动的阶段流转推进，本组件纯表现、不联网。
    /// </summary>
    public class MapDayNightController : MonoBehaviour
    {
        [Header("夜晚抬升")]
        [Tooltip("夜晚抬升的节点（水面及水面上的船）")]
        [SerializeField] private Transform[] nightRiseTargets;
        [Tooltip("夜晚抬升的格数（按 GridManager 格尺寸换算，无网格时按 1 单位/格）")]
        [SerializeField] private float riseCells = 6f;
        [Tooltip("抬升/下沉的过渡时长（秒）")]
        [SerializeField] private float riseFadeDuration = 1.5f;

        [Header("夜晚色调")]
        [Tooltip("夜晚色调（与原颜色乘算，暗蓝色）")]
        [SerializeField] private Color nightTint = new Color(0.35f, 0.45f, 0.75f, 1f);
        [Tooltip("色调过渡时长（秒）")]
        [SerializeField] private float tintFadeDuration = 1.5f;

        [Header("调试")]
        [Tooltip("强制覆盖昼夜状态：-1=按轮次自动，0=强制白天，1=强制黑夜")]
        [SerializeField] private int debugOverride = -1;

        // 当前是否黑夜（目标状态）
        private bool _isNight;
        // 0=白天 1=黑夜 的连续过渡进度
        private float _blend;

        // 缓存：抬升节点的白天位置
        private Vector3[] _dayPositions;
        // 缓存：渲染器的白天颜色
        private SpriteRenderer[] _renderers;
        private Color[] _dayColors;

        private void Awake()
        {
            CacheDayState();
            Debug.Log($"[MapDayNight] Awake：缓存白天状态完成（抬升目标 {CountValidTargets()} 个，渲染器 {_renderers.Length} 个）", this);
        }

        private int CountValidTargets()
        {
            int count = 0;
            if (nightRiseTargets != null)
            {
                foreach (Transform t in nightRiseTargets)
                {
                    if (t != null)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>当前是否处于黑夜（含调试覆盖后的目标状态）</summary>
        public bool IsNight => _isNight;

        private void Update()
        {
            bool targetNight = ResolveTargetNight();
            if (targetNight != _isNight)
            {
                _isNight = targetNight;
                Debug.Log($"[MapDayNight] 切换到{(_isNight ? "夜晚" : "白天")}（推断轮次：{GetActualRound()}，调试覆盖：{debugOverride}）", this);
            }
            // 网格水域判定每帧幂等同步（GridManager 随场景加载可能晚于本组件，
            // 幂等写入保证就绪后自动补上；判定不做渐变，避免过渡中间态模糊）
            ApplyWaterZoneOffset();

            float target = _isNight ? 1f : 0f;
            if (Mathf.Approximately(_blend, target))
            {
                return;
            }

            // 取两种过渡中较短的速率驱动统一进度，保证位置与色调同步
            float duration = Mathf.Max(0.01f, Mathf.Min(riseFadeDuration, tintFadeDuration));
            _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime / duration);
            ApplyBlend(_blend);
        }

        /// <summary>
        /// 判定目标昼夜：调试覆盖优先，否则按轮次奇偶（0=未开始按白天）
        /// </summary>
        private bool ResolveTargetNight()
        {
            if (debugOverride >= 0)
            {
                return debugOverride == 1;
            }
            int round = GetActualRound();
            return round % 2 == 0 && round > 0;
        }

        /// <summary>
        /// 获取真实轮次：联机模式以服务器 GamePhaseSync 下发的轮次为准
        /// （联机下 PlayerScoreManager.CurrentRoundIndex 不被推进，恒为 1）；单机回退计分管理器
        /// </summary>
        private int GetActualRound()
        {
            int serverRound = SuperQQ.Network.NetGameFlowGate.CurrentServerRound;
            if (serverRound > 0)
            {
                return serverRound;
            }
            SuperQQ.Score.PlayerScoreManager sm = SuperQQ.Score.PlayerScoreManager.Instance;
            return sm != null ? sm.CurrentRoundIndex : 0;
        }

        /// <summary>
        /// 把当前昼夜对应的水位偏移写入 GridManager（夜晚=riseCells 格，白天=0）。
        /// GridManager 可能因场景加载晚于本组件初始化，此处每帧幂等设置一次直到写入成功。
        /// </summary>
        private void ApplyWaterZoneOffset()
        {
            SuperQQ.Grid.GridManager grid = SuperQQ.Grid.GridManager.Instance;
            if (grid == null)
            {
                return;
            }
            int targetCells = _isNight ? Mathf.RoundToInt(riseCells) : 0;
            if (grid.WaterYOffsetCells != targetCells)
            {
                grid.SetWaterYOffset(targetCells);
            }
        }

        /// <summary>按 0~1 进度应用位置与色调（供 Update 逐帧过渡）</summary>
        private void ApplyBlend(float blend)
        {
            float riseHeight = riseCells * GetCellSize();

            if (nightRiseTargets != null)
            {
                for (int i = 0; i < nightRiseTargets.Length; i++)
                {
                    if (nightRiseTargets[i] == null)
                    {
                        continue;
                    }
                    Vector3 dayPos = _dayPositions[i];
                    nightRiseTargets[i].position = dayPos + Vector3.up * (riseHeight * blend);
                }
            }

            Color tint = Color.Lerp(Color.white, nightTint, blend);
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    continue;
                }
                Color c = _dayColors[i];
                _renderers[i].color = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);
            }
        }

        /// <summary>缓存白天的位置与颜色（切换循环的还原基准）</summary>
        private void CacheDayState()
        {
            if (nightRiseTargets != null)
            {
                _dayPositions = new Vector3[nightRiseTargets.Length];
                for (int i = 0; i < nightRiseTargets.Length; i++)
                {
                    _dayPositions[i] = nightRiseTargets[i] != null ? nightRiseTargets[i].position : Vector3.zero;
                }
            }
            else
            {
                _dayPositions = new Vector3[0];
            }

            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _dayColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _dayColors[i] = _renderers[i].color;
            }
        }

        private float GetCellSize()
        {
            SuperQQ.Grid.GridManager grid = SuperQQ.Grid.GridManager.Instance;
            return grid != null ? grid.PublicCellSize : 1f;
        }
    }
}

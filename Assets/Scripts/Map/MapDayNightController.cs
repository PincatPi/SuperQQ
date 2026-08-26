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

        [Header("白天抬升")]
        [Tooltip("白天抬升的节点（如 Bell.000）；其当前摆放位置即夜晚（低位）基准")]
        [SerializeField] private Transform[] dayRiseTargets;
        [Tooltip("白天抬升的格数（夜晚回落到基准位置）")]
        [SerializeField] private float dayRiseCells = 13f;

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
        // 缓存：白天抬升节点的夜晚（低位）基准位置
        private Vector3[] _nightBasePositions;
        // 缓存：渲染器的白天颜色
        private SpriteRenderer[] _renderers;
        private Color[] _dayColors;

        // 外部注册的渲染器（运行时动态生成的道具等）：渲染器 -> 原始颜色。
        // 与 _renderers/_dayColors 同等参与夜晚色调乘算，注册时立即应用当前色调。
        private readonly Dictionary<SpriteRenderer, Color> _externalRenderers = new();

        /// <summary>当前场景实例（Map 预制体根节点上唯一）</summary>
        public static MapDayNightController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            CacheDayState();
            Debug.Log($"[MapDayNight] Awake：缓存白天状态完成（抬升目标 {CountValidTargets()} 个，渲染器 {_renderers.Length} 个）", this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 注册一个物体的所有子渲染器参与昼夜色调（道具放置时调用）。
        /// 以当前色调立即应用一次，避免等到下一帧才变色。
        /// </summary>
        public void RegisterExternalRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer r = renderers[i];
                if (r == null || _externalRenderers.ContainsKey(r))
                {
                    continue;
                }

                _externalRenderers.Add(r, r.color);
                ApplyTintTo(r, _externalRenderers[r], _blend);
            }
        }

        /// <summary>反注册一个物体的所有子渲染器（道具销毁/移除时调用）</summary>
        public void UnregisterExternalRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    _externalRenderers.Remove(renderers[i]);
                }
            }
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

        /// <summary>昼夜过渡进度（0=白天 1=黑夜；白天静止时为 0）</summary>
        public float Blend => _blend;

        /// <summary>
        /// 当前全局色调（白=白天，随昼夜过渡渐变）。
        /// 供运行时生成的物体（樱桃弹体等非 Map 层级的 SpriteRenderer）采样，
        /// 乘算自身基础色即可与地图同步变暗
        /// </summary>
        public static Color CurrentTint { get; private set; } = Color.white;

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

            // 白天静止期持续刷新白天基准位置：抬升目标（船）若被网格吸附等外部逻辑
            // 调整过位置，涨潮始终以吸附后的正确位置为基准上升
            if (!_isNight && _blend <= 0.001f)
            {
                CacheDayPositions();
            }
            // 夜晚静止期持续刷新夜晚基准位置（白天抬升组的低位还原基准）
            if (_isNight && _blend >= 0.999f)
            {
                CacheNightBasePositions();
            }

            float target = _isNight ? 1f : 0f;
            if (Mathf.Approximately(_blend, target))
            {
                // 过渡结束后位置不再经 ApplyBlend 刷新：夜晚抬升组静止位置=缓存基准，
                // 不刷新无影响；但白天抬升组白天位置=基准+抬升量，必须在静止期持续应用，
                // 否则进白天回合时道具停在夜晚基准（低位）不升起
                ApplyDayRisePositions(target);
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

            // 白天抬升组：白天（blend=0）在基准上方 dayRiseCells 格，夜晚（blend=1）回落至基准
            ApplyDayRisePositions(blend);

            Color tint = Color.Lerp(Color.white, nightTint, blend);
            CurrentTint = tint; // 运行时生成物体（樱桃弹体等）经此采样同步变暗
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    continue;
                }
                ApplyTintTo(_renderers[i], _dayColors[i], blend);
            }

            // 外部注册渲染器（运行时生成的道具）：同样按白天原始色乘算色调
            if (_externalRenderers.Count > 0)
            {
                _staleExternal.Clear();
                foreach (KeyValuePair<SpriteRenderer, Color> pair in _externalRenderers)
                {
                    if (pair.Key == null)
                    {
                        _staleExternal.Add(pair.Key); // 已销毁未反注册，清理
                        continue;
                    }
                    ApplyTintTo(pair.Key, pair.Value, blend);
                }
                for (int i = 0; i < _staleExternal.Count; i++)
                {
                    _externalRenderers.Remove(_staleExternal[i]);
                }
            }
        }

        // 应用色调时发现的已销毁外部渲染器（复用避免每帧分配）
        private readonly List<SpriteRenderer> _staleExternal = new();

        /// <summary>按昼夜进度把原始颜色乘上夜色（保留 alpha），写入渲染器</summary>
        private void ApplyTintTo(SpriteRenderer r, Color dayColor, float blend)
        {
            Color tint = Color.Lerp(Color.white, nightTint, blend);
            r.color = new Color(dayColor.r * tint.r, dayColor.g * tint.g, dayColor.b * tint.b, r.color.a);
        }

        /// <summary>缓存白天的位置与颜色（切换循环的还原基准）</summary>
        private void CacheDayState()
        {
            CacheDayPositions();
            CacheNightBasePositions();

            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _dayColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _dayColors[i] = _renderers[i].color;
            }
        }

        /// <summary>仅刷新抬升目标的白天基准位置（白天静止期每帧调用，跟踪网格吸附等外部位置调整）</summary>
        private void CacheDayPositions()
        {
            if (nightRiseTargets == null)
            {
                _dayPositions = new Vector3[0];
                return;
            }
            if (_dayPositions == null || _dayPositions.Length != nightRiseTargets.Length)
            {
                _dayPositions = new Vector3[nightRiseTargets.Length];
            }
            for (int i = 0; i < nightRiseTargets.Length; i++)
            {
                _dayPositions[i] = nightRiseTargets[i] != null ? nightRiseTargets[i].position : Vector3.zero;
            }
        }

        /// <summary>按昼夜进度应用白天抬升组位置（blend=0 全升起，blend=1 回落夜晚基准）</summary>
        private void ApplyDayRisePositions(float blend)
        {
            if (dayRiseTargets == null || _nightBasePositions == null)
            {
                return;
            }
            float dayRiseHeight = dayRiseCells * GetCellSize() * (1f - blend);
            for (int i = 0; i < dayRiseTargets.Length; i++)
            {
                if (dayRiseTargets[i] == null)
                {
                    continue;
                }
                dayRiseTargets[i].position = _nightBasePositions[i] + Vector3.up * dayRiseHeight;
            }
        }

        /// <summary>刷新白天抬升组的夜晚（低位）基准位置（Awake 与夜晚静止期调用）</summary>
        private void CacheNightBasePositions()
        {
            if (dayRiseTargets == null)
            {
                _nightBasePositions = new Vector3[0];
                return;
            }
            if (_nightBasePositions == null || _nightBasePositions.Length != dayRiseTargets.Length)
            {
                _nightBasePositions = new Vector3[dayRiseTargets.Length];
            }
            for (int i = 0; i < dayRiseTargets.Length; i++)
            {
                _nightBasePositions[i] = dayRiseTargets[i] != null ? dayRiseTargets[i].position : Vector3.zero;
            }
        }

        private float GetCellSize()
        {
            SuperQQ.Grid.GridManager grid = SuperQQ.Grid.GridManager.Instance;
            return grid != null ? grid.PublicCellSize : 1f;
        }
    }
}

using System.Collections.Generic;
using Cinemachine;
using SuperQQ.Audio;
using SuperQQ.Grid;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 闹钟 — 控制类道具（2x2，不旋转）
    /// 激活期间：本地模拟的玩家进入作用范围后进入 windUpTime 秒前摇，
    /// 前摇结束时仍在范围内才响铃震屏（震动时长/幅度由 Impulse Source 配置）；
    /// 前摇途中离开范围则取消，离开后再进入重新计时
    /// 作用范围 = 自身 footprint 各边外延 rangeExtendCells 格的矩形（围着 footprint 一圈，
    /// 与磁铁的范围口径一致：2x2 外延 1.5 格 → 5x5 格矩形）
    ///
    /// 多钟联动：footprint 各边外延 linkExtendCells 的矩形与其他闹钟相交即视为"摆在一起"，
    /// 任一闹钟响铃时整组一起响（沿连接传递：A 连 B、B 连 C 则 ABC 同组），
    /// 震屏振幅 = 组内数量（上限 maxAmplitudeStack 倍）；整组只发一次冲量（触发钟代发），
    /// 成员仅同步触发状态，避免同帧/同玩家重复震屏叠爆
    ///
    /// 震屏实现：调用本物体上 CinemachineImpulseSource 的 GenerateImpulse 发出冲量，
    /// 由虚拟相机上的 CinemachineImpulseListener 响应（缺失时运行时自动补挂）；
    /// 震动时长/波形/幅度在 Impulse Source 的 Impulse Definition 中配置
    ///
    /// 联机说明（客户端权威 + 纯转发架构）：
    /// 屏幕震动是纯本地表现。各端只检测本地模拟的玩家并震动自己的相机；
    /// 远端玩家的受震由其客户端上的同一个闹钟实例自行判定模拟，经快照呈现位置。
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = (2,2)，canRotate = false
    /// - PlacableItemDef：category = Control，facingSteps = 0
    /// - 不挂实体碰撞体（触发判定为数学矩形检测，玩家可穿过）
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class AlarmClock : ItemBase
    {
        [Header("响铃")]
        [Tooltip("作用范围：自身 footprint 各边向外扩展的格数（默认 1.5 → 2x2 外延为 5x5 格）")]
        [SerializeField, Min(0f)] private float rangeExtendCells = 1.5f;
        [Tooltip("响铃前摇（秒）：玩家进入范围后经过该时长且仍在范围内才触发震屏")]
        [SerializeField, Min(0f)] private float windUpTime = 1f;
        [Tooltip("震屏冲量源；留空则取本物体上的 CinemachineImpulseSource。震动时长/幅度在该组件的 Impulse Definition 中配置")]
        [SerializeField] private CinemachineImpulseSource impulseSource;

        [Header("多钟联动")]
        [Tooltip("联动判定外延（格）：两闹钟 footprint 各边外延该格数后矩形相交即视为摆在一起，任一响铃整组一起响（0.5 格 ≈ 贴边/贴角相邻即联动）")]
        [SerializeField, Min(0f)] private float linkExtendCells = 0.5f;
        [Tooltip("振幅叠加上限：N 个闹钟一起响时震屏振幅为 N 倍，最高不超过该倍数")]
        [SerializeField, Min(1)] private int maxAmplitudeStack = 3;

        [Header("音效")]
        [Tooltip("响铃循环音效：震屏生效时开始循环播放，震屏结束时音量渐弱至停止（Clip 在 AudioCatalog 资产中按 Id 拖配，需无缝循环素材）；None 表示静默")]
        [SerializeField] private SfxId ringSfx = SfxId.AlarmRing;

        [Tooltip("响铃循环时长（秒）：应等于震屏持续时长（Impulse Source 的 Impulse Definition → Impulse Duration，当前 prefab 为 1s），到时音效自动淡出")]
        [SerializeField, Min(0.1f)] private float ringSfxDuration = 1f;

        [Tooltip("震屏结束后响铃音效淡出时长（秒）")]
        [SerializeField, Min(0.05f)] private float ringSfxFadeOut = 0.5f;

        private Coroutine _ringSfxRoutine;   // 响铃定时淡出协程（重复触发时取消重开）

        [Header("调试")]
        [Tooltip("始终激活检测（无 GameFlow 的测试场景使用；阶段系统接入后关闭，由 OnRunPhaseStart/OnBuildPhaseStart 控制）")]
        [SerializeField] private bool debugAlwaysActive = true;

        private bool active;
        private static readonly List<AlarmClock> s_instances = new();     // 场景内全部闹钟（联动判定用，OnEnable/OnDisable 维护）
        private readonly List<AlarmClock> clusterScratch = new();         // 联动组收集临时列表（BFS）
        private readonly List<PlayerController> frameRingPlayers = new(); // 本帧完成前摇的玩家（触发者）
        private readonly HashSet<PlayerController> insidePlayers = new();   // 已触发响铃且仍在范围内的玩家（离开后重进重新前摇）
        private readonly Dictionary<PlayerController, float> pendingPlayers = new();   // 前摇中的玩家 → 剩余前摇秒数
        private readonly List<PlayerController> pendingKeys = new();        // 遍历前摇字典的临时列表（避免迭代中修改）
        private readonly List<PlayerController> frameInside = new();        // 本帧范围判定临时集合
        private PlayerController[] scenePlayersCache;   // 无注册表的简易场景（Level1 测试）的退化缓存
        private bool impulseListenerChecked;

        /// <summary>控制：改变玩家移动/状态</summary>
        public override ItemCategory Category => ItemCategory.Control;

        private void OnEnable()
        {
            s_instances.Add(this);
        }

        private void OnDisable()
        {
            s_instances.Remove(this);
        }

        /// <summary>跑动阶段开始：激活检测</summary>
        public override void OnRunPhaseStart()
        {
            active = true;
        }

        /// <summary>建造阶段开始：关闭检测并清空范围/前摇记录（下次跑动重新前摇）</summary>
        public override void OnBuildPhaseStart()
        {
            active = false;
            insidePlayers.Clear();
            pendingPlayers.Clear();
        }

        private void Update()
        {
            if (!active && !debugAlwaysActive)
            {
                return;
            }

            Rect range = ResolveRangeRect();

            // 收集本帧在范围内的本地模拟玩家（远端化身 simulated=false 跳过，见联机说明；
            // 死亡过渡/幽灵不受影响——不入前摇，已在前摇/已触发的随 frameInside 剔除自动取消）
            frameInside.Clear();
            foreach (PlayerController player in EnumeratePlayers())
            {
                if (player == null || !player.BAffectedByItems)
                {
                    continue;
                }
                Rigidbody2D rb = player.Rb;
                if (rb == null || !rb.simulated)
                {
                    continue;
                }
                if (range.Contains(rb.position))
                {
                    frameInside.Add(player);
                }
            }

            // 前摇推进：计时结束且仍在范围内 → 响铃；中途离开/销毁 → 取消前摇
            frameRingPlayers.Clear();
            if (pendingPlayers.Count > 0)
            {
                pendingKeys.Clear();
                foreach (PlayerController key in pendingPlayers.Keys)
                {
                    pendingKeys.Add(key);
                }
                foreach (PlayerController player in pendingKeys)
                {
                    if (player == null || !frameInside.Contains(player))
                    {
                        pendingPlayers.Remove(player);
                        continue;
                    }
                    pendingPlayers[player] -= Time.deltaTime;
                    if (pendingPlayers[player] <= 0f)
                    {
                        pendingPlayers.Remove(player);
                        insidePlayers.Add(player);
                        frameRingPlayers.Add(player);
                    }
                }
            }

            // 本帧新进入范围的玩家进入前摇（不立即触发）
            foreach (PlayerController player in frameInside)
            {
                if (!insidePlayers.Contains(player) && !pendingPlayers.ContainsKey(player))
                {
                    pendingPlayers.Add(player, windUpTime);
                }
            }
            // 已触发玩家离开/销毁后移出记录，再次进入时重新前摇
            insidePlayers.IntersectWith(frameInside);

            if (frameRingPlayers.Count > 0)
            {
                Ring(frameRingPlayers);
            }
        }

        // ==================== 震屏 ====================

        /// <summary>
        /// 响铃：联动组（摆在一起的闹钟）一起响，震屏振幅 = 组内数量（上限 maxAmplitudeStack 倍）。
        /// 整组只由本钟代发一次冲量，成员仅同步"已触发"状态（成员里该玩家的前摇一并清除），
        /// 避免每个钟各发一次冲量叠爆、或玩家同时踩多钟时同帧双响
        /// </summary>
        private void Ring(List<PlayerController> triggerPlayers)
        {
            if (impulseSource == null)
            {
                impulseSource = GetComponent<CinemachineImpulseSource>();
            }
            if (impulseSource == null)
            {
                Debug.LogWarning("[AlarmClock] 未找到 CinemachineImpulseSource，无法震屏", this);
                return;
            }
            EnsureImpulseListener();

            int count = CollectCluster();
            foreach (AlarmClock member in clusterScratch)
            {
                foreach (PlayerController player in triggerPlayers)
                {
                    member.MarkRungByCluster(player);
                }
            }

            float force = Mathf.Min(count, maxAmplitudeStack);
            impulseSource.GenerateImpulse(force);
            PlayRingSfx();
            Debug.Log($"[AlarmClock] 前摇 {windUpTime}s 结束，{count} 个闹钟一起响，震屏振幅 x{force}（上限 {maxAmplitudeStack}）");
        }

        /// <summary>被联动组代响时同步触发状态：该玩家对本钟视为已触发（在范围内不再重复前摇/响铃）</summary>
        private void MarkRungByCluster(PlayerController player)
        {
            if (player == null)
            {
                return;
            }
            pendingPlayers.Remove(player);
            insidePlayers.Add(player);
        }

        /// <summary>
        /// 收集联动组（含自身）：footprint 外延 linkExtendCells 的矩形与其他闹钟相交即相连，
        /// 沿连接传递（BFS），返回组内闹钟数量
        /// </summary>
        private int CollectCluster()
        {
            clusterScratch.Clear();
            clusterScratch.Add(this);
            for (int i = 0; i < clusterScratch.Count; i++)
            {
                Rect rect = clusterScratch[i].ResolveLinkRect();
                foreach (AlarmClock other in s_instances)
                {
                    if (other == null || clusterScratch.Contains(other))
                    {
                        continue;
                    }
                    if (rect.Overlaps(other.ResolveLinkRect()))
                    {
                        clusterScratch.Add(other);
                    }
                }
            }
            return clusterScratch.Count;
        }

        // ==================== 响铃音效 ====================

        /// <summary>
        /// 响铃循环音效：震屏生效时开始循环，ringSfxDuration 秒后（= 震屏结束）音量渐弱至停止；
        /// 震屏未结束时重复触发则重新计时（铃声与震屏同步续长）
        /// </summary>
        private void PlayRingSfx()
        {
            if (ringSfx == SfxId.None)
            {
                return;
            }

            AudioManager.StartLoopSfx(ringSfx);
            if (_ringSfxRoutine != null)
            {
                StopCoroutine(_ringSfxRoutine);
            }
            _ringSfxRoutine = StartCoroutine(RingSfxFadeOutRoutine());
        }

        /// <summary>响铃定时淡出：等待震屏时长后停止循环音效（音量渐弱至 0）</summary>
        private System.Collections.IEnumerator RingSfxFadeOutRoutine()
        {
            yield return new WaitForSeconds(ringSfxDuration);
            AudioManager.StopLoopSfx(ringSfx, ringSfxFadeOut);
            _ringSfxRoutine = null;
        }

        /// <summary>销毁兜底：响铃期间被拆除/拾回/场景卸载时停止循环音效（AudioManager 跨场景常驻，不清理会残留）</summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_ringSfxRoutine != null)
            {
                _ringSfxRoutine = null;
                AudioManager.StopLoopSfx(ringSfx, ringSfxFadeOut);
            }
        }

        /// <summary>确保场景 vcam 挂有 Impulse Listener（缺失时自动补挂，否则收不到冲量、震屏无效）</summary>
        private void EnsureImpulseListener()
        {
            if (impulseListenerChecked)
            {
                return;
            }
            impulseListenerChecked = true;

            CinemachineVirtualCamera vcam = FindFirstObjectByType<CinemachineVirtualCamera>();
            if (vcam == null)
            {
                Debug.LogWarning("[AlarmClock] 场景中找不到 CinemachineVirtualCamera，无法震屏", this);
                return;
            }
            if (vcam.GetComponent<CinemachineImpulseListener>() == null)
            {
                vcam.gameObject.AddComponent<CinemachineImpulseListener>();
                Debug.Log("[AlarmClock] 已为虚拟相机自动补挂 CinemachineImpulseListener");
            }
        }

        // ==================== 范围检测 ====================

        /// <summary>
        /// 作用范围矩形（世界坐标）：footprint 各边外延 rangeExtendCells 格，中心与道具重合
        /// 根节点即 footprint 框中心（项目摆放约定）；2x2 不旋转，无需考虑朝向
        /// </summary>
        private Rect ResolveRangeRect()
        {
            return ResolveFootprintRect(rangeExtendCells);
        }

        /// <summary>联动判定矩形（世界坐标）：footprint 各边外延 linkExtendCells 格，两钟此矩形相交即"摆在一起"</summary>
        private Rect ResolveLinkRect()
        {
            return ResolveFootprintRect(linkExtendCells);
        }

        /// <summary>footprint 各边外延 extendCells 格的世界坐标矩形（中心与道具重合）</summary>
        private Rect ResolveFootprintRect(float extendCells)
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.PublicCellSize : 0.5f;
            FootprintBoxView box = GetComponent<FootprintBoxView>();
            Vector2 footprint = box != null ? box.Footprint : new Vector2Int(2, 2);
            Vector2 size = (footprint + Vector2.one * (extendCells * 2f)) * cellSize;
            return new Rect((Vector2)transform.position - size * 0.5f, size);
        }

        /// <summary>枚举本关玩家：优先关卡注册表（含远端化身），无注册表时退化为场景内全部 PlayerController</summary>
        private IEnumerable<PlayerController> EnumeratePlayers()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                return registry.Players;
            }
            if (scenePlayersCache == null || scenePlayersCache.Length == 0)
            {
                scenePlayersCache = FindObjectsOfType<PlayerController>();
            }
            return scenePlayersCache;
        }

        /// <summary>编辑期/运行期在 Scene 视图画出作用范围（青色矩形），便于策划核对 1.5 格外延</summary>
        private void OnDrawGizmos()
        {
            Rect range = ResolveRangeRect();
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireCube(range.center, range.size);
        }
    }
}

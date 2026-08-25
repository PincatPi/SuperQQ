using System.Collections.Generic;
using SuperQQ.Audio;
using SuperQQ.Grid;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 磁铁 — 控制类道具（2x2，不旋转）
    /// 激活期间持续把作用范围内的玩家拉向自身中心（恒定加速度，与玩家输入叠加，可挣脱）
    /// 作用范围 = 自身 footprint 各边外延 rangeExtendCells 格（2x2 外延 1.5 格 → 5x5 格矩形）
    ///
    /// 联机说明（客户端权威 + 纯转发架构）：
    /// 远端玩家化身由快照插值驱动（Rigidbody2D.simulated = false），本地无法也不应对其施力；
    /// 磁铁对远端玩家的生效路径是：各端经网络回放放置同一个磁铁，远端客户端的磁铁实例
    /// 拉动其本地模拟的玩家，结果位置经 InputReporter 上报后在各端呈现。
    /// 因此本组件只对"本地模拟中的玩家"施力，远端化身自动跟随。
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = (2,2)，canRotate = false
    /// - PlacableItemDef：category = Control，facingSteps = 0
    /// - 不挂实体碰撞体（力场非障碍，玩家可穿过）
    /// </summary>
    public class Magnet : ItemBase
    {
        [Header("吸引")]
        [Tooltip("作用范围：自身 footprint 各边向外扩展的格数（默认 1.5 → 2x2 外延为 5x5 格）")]
        [SerializeField, Min(0f)] private float rangeExtendCells = 1.5f;
        [Tooltip("恒定拉力加速度（米/秒²），与玩家移动力叠加，玩家可挣脱")]
        [SerializeField, Min(0f)] private float pullAcceleration = 12f;

        [Header("音效")]
        [Tooltip("磁吸循环音效：任一本地模拟玩家进入作用范围时开始循环播放，全部离开后音量渐弱至停止（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId pullLoopSfx = SfxId.MagnetPull;

        [Tooltip("玩家离开范围后音效淡出时长（秒）")]
        [SerializeField, Min(0.05f)] private float pullSfxFadeOut = 0.5f;

        [Header("调试")]
        [Tooltip("始终激活吸引（无 GameFlow 的测试场景使用；阶段系统接入后关闭，由 OnRunPhaseStart/OnBuildPhaseStart 控制）")]
        [SerializeField] private bool debugAlwaysActive = true;

        private bool active;
        private PlayerController[] scenePlayersCache;   // 无注册表的简易场景（Level1 测试）的退化缓存
        private bool _bAttracting;      // 本帧是否有本地模拟玩家在范围内受吸（音效起停的判定依据）
        private bool _bSfxPlaying;      // 循环音效当前播放态（边沿触发起停，避免每帧重复调用）

        /// <summary>控制：改变玩家移动/状态</summary>
        public override ItemCategory Category => ItemCategory.Control;

        /// <summary>跑动阶段开始：激活吸引</summary>
        public override void OnRunPhaseStart()
        {
            active = true;
        }

        /// <summary>建造阶段开始：关闭吸引并淡出循环音效</summary>
        public override void OnBuildPhaseStart()
        {
            active = false;
            _bAttracting = false;
            StopPullSfx();
        }

        /// <summary>被移除（拾回/拆除）前：淡出循环音效，防止通道残留</summary>
        public override void OnRemoved()
        {
            base.OnRemoved();
            _bAttracting = false;
            StopPullSfx();
        }

        /// <summary>销毁兜底：场景卸载/外部销毁时停止循环音效（AudioManager 跨场景常驻，不清理会残留）</summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            StopPullSfx();
        }

        private void FixedUpdate()
        {
            if (!active && !debugAlwaysActive)
            {
                return;
            }

            Rect range = ResolveRangeRect();
            bool attractingThisFrame = false;
            foreach (PlayerController player in EnumeratePlayers())
            {
                if (TryPull(player, range))
                {
                    attractingThisFrame = true;
                }
            }

            // 吸附状态边沿触发起停：进入范围开始循环，全部离开淡出停止
            if (attractingThisFrame && !_bAttracting)
            {
                _bAttracting = true;
                if (pullLoopSfx != SfxId.None)
                {
                    AudioManager.StartLoopSfx(pullLoopSfx);
                    _bSfxPlaying = true;
                }
            }
            else if (!attractingThisFrame && _bAttracting)
            {
                _bAttracting = false;
                StopPullSfx();
            }
        }

        // ==================== 内部 ====================

        /// <summary>对单个玩家施加指向中心的恒定拉力（仅当其在作用范围内且为本地模拟）；返回是否实际受吸</summary>
        private bool TryPull(PlayerController player, Rect range)
        {
            if (player == null || !player.BAffectedByItems)
            {
                return false;   // 死亡过渡/幽灵不受磁力影响
            }
            Rigidbody2D rb = player.Rb;
            // 远端化身 simulated = false（快照驱动），跳过——其受力由所属客户端自行模拟（见联机说明）
            if (rb == null || !rb.simulated || !range.Contains(rb.position))
            {
                return false;
            }

            Vector2 toCenter = (Vector2)transform.position - rb.position;
            if (toCenter.sqrMagnitude < 1e-4f)
            {
                return false;   // 已在中心，方向无意义
            }
            // 乘以质量使加速度与质量解耦，保证"恒定加速度"语义
            rb.AddForce(toCenter.normalized * (pullAcceleration * rb.mass), ForceMode2D.Force);
            return true;
        }

        /// <summary>淡出停止循环音效（若正在播放）</summary>
        private void StopPullSfx()
        {
            if (_bSfxPlaying)
            {
                _bSfxPlaying = false;
                AudioManager.StopLoopSfx(pullLoopSfx, pullSfxFadeOut);
            }
        }

        /// <summary>
        /// 作用范围矩形（世界坐标）：footprint 各边外延 rangeExtendCells 格，中心与道具重合
        /// 根节点即 footprint 框中心（项目摆放约定）；2x2 不旋转，无需考虑朝向
        /// </summary>
        private Rect ResolveRangeRect()
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.PublicCellSize : 0.5f;
            FootprintBoxView box = GetComponent<FootprintBoxView>();
            Vector2 footprint = box != null ? box.Footprint : new Vector2Int(2, 2);
            Vector2 size = (footprint + Vector2.one * (rangeExtendCells * 2f)) * cellSize;
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

        /// <summary>编辑期/运行期在 Scene 视图画出作用范围（品红），便于策划核对 1.5 格外延</summary>
        private void OnDrawGizmos()
        {
            Rect range = ResolveRangeRect();
            Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.3f);
            Gizmos.DrawCube(range.center, range.size);
        }
    }
}

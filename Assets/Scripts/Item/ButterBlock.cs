using System.Collections.Generic;
using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 黄油块 — 附着类道具（1x1，可旋转）
    /// 吸附在平台类道具/地形表面，可站立通行，走上去减速（0.5 倍，由 StandZone 上的 SurfaceModifier 实现）。
    /// 黏性边：四条边中有一条带黏性（随旋转档位改变：0=上 1=右 2=下 3=左，即顺时针步进），
    /// 黏性边共享的相邻格中放置的道具会被黏住，随承载平台/物体（传送带等）一起运动。
    /// 布置判定：黄油块所在格必须有承载物（平台/地形），且黏性边相邻格必须是可布置道具的格子。
    /// 不单独占用格子；可被拆除类道具拆除（经附着物注册表联动）。
    /// </summary>
    public class ButterBlock : ItemBase
    {
        [Header("吸附")]
        [Tooltip("黏住检测间隔（秒），检测黏性边相邻格新出现的道具并黏住")]
        [SerializeField, Range(0.02f, 0.5f)] private float stickCheckInterval = 0.1f;

        /// <summary>搭路类（附着在平台上的可站立面）</summary>
        public override ItemCategory Category => ItemCategory.Path;

        /// <summary>允许叠放：黄油块必须落在平台/地形所在格，格子可能已被承载物占据</summary>
        public override bool AllowsOccupiedOverlap => true;

        /// <summary>不单独占用格子：落点只借承载物定位，自身进附着物注册表管理</summary>
        public override bool RegistersOccupancy => false;

        /// <summary>
        /// 豁免关卡预占（Occupied）与水域（Water）区域拦截：
        /// 可附着地形边缘格通常同时被 Occupied 标记；船面格子被水域条目覆盖但船是合法承载物。
        /// 豁免后落点仍受 HasCarrierAt 承载物要求约束（开阔水面无承载物依然放不了），
        /// 落点合法性由 ValidatePlacement 的承载物判定兜底
        /// </summary>
        public override GridZoneType ToleratedZoneMask => GridZoneType.Occupied | GridZoneType.Water;

        [Header("渲染")]
        [Tooltip("渲染层级（黄油块必须永远显示在其他道具之上，保证小体积也能被看到）")]
        [SerializeField] private int topSortingOrder = 100;

        private void Awake()
        {
            // 强制最高渲染层级：黄油块是 1x1 附着小件，被大件道具遮住会完全看不见
            foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(true))
            {
                r.sortingOrder = topSortingOrder;
            }
        }

        private Transform carrier;            // 承载物（平台道具），跟随其移动/旋转
        private Vector3 carrierLocalOffset;   // 承载物局部坐标系下的位置偏移
        private Quaternion carrierLocalRotation = Quaternion.identity; // 承载物局部坐标系下的旋转偏移
        private PlacedItem carrierPlaced;     // 承载物是网格道具时的凭证（排除被误黏）
        private Vector2Int attachedCell;      // 附着格（注册表用）
        private bool attached;

        private readonly List<Transform> stuckItems = new List<Transform>();
        private float nextStickCheckTime;

        // ==================== 黏性边方向 ====================

        /// <summary>
        /// 黏性边对应的相邻格方向（随旋转档顺时针步进）：
        /// 0=上(+y) 1=右(+x) 2=下(-y) 3=左(-x)
        /// </summary>
        public static Vector2Int StickyDirection(int rotationSteps)
        {
            switch (((rotationSteps % 4) + 4) % 4)
            {
                case 0: return Vector2Int.up;
                case 1: return Vector2Int.right;
                case 2: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }

        /// <summary>当前黏性边共享的相邻格（读 Placed.Rotation，已放置后旋转也能跟随）</summary>
        public Vector2Int StickyCell => attachedCell + StickyDirection(Placed != null ? Placed.Rotation : 0);

        // ==================== 摆放校验 ====================

        /// <summary>
        /// 落点合法性：① 所在格必须存在承载物（平台类道具，或标记了 AttachSurface 的可附着地形表面）
        /// ② 黏性边共享的相邻格必须区域合法（在可布置边界内、不在封锁区域）；
        ///    相邻格已有道具同样合法——放下后立即黏住该道具（黄油滑到道具下方的场景）
        /// </summary>
        [Header("调试")]
        [Tooltip("摆放校验失败时在 Console 输出原因（排查标记/相邻格问题用）")]
        [SerializeField] private bool debugValidateLog = true;

        public override bool ValidatePlacement(GridManager grid, Vector2Int anchor, int rotation)
        {
            bool hasCarrier = HasCarrierAt(grid, anchor, out PlacedItem carrierItem);
            Vector2Int stickyCell = anchor + StickyDirection(rotation);
            // allowOccupiedCells: true——黏性边格允许已有道具（黏住对象），只保留边界与区域检查
            bool stickyPlaceable = grid != null && grid.CanOccupy(stickyCell, Vector2Int.one, 0, allowOccupiedCells: true);

            // 黏性边格已有道具时的附加限制：
            // ① 不能是承载物自己（如黄油贴在 2x2 吐司左下格、黏性边格仍是同一块吐司——不能黏自己的承载物）
            // ② 必须可被黏住（声明 CanBeStuck=false 或吸附点不匹配的道具不算合法黏住目标）
            PlacedItem stickyOccupant = grid != null ? grid.GetItemAt(stickyCell) : null;
            if (stickyPlaceable && stickyOccupant != null)
            {
                ItemBase occupantItem = stickyOccupant.GetComponent<ItemBase>();
                if (occupantItem == null
                    || (carrierItem != null && stickyOccupant == carrierItem)
                    || !occupantItem.CanBeStuckAt(stickyCell))
                {
                    stickyPlaceable = false;
                }
            }

            if (debugValidateLog && (!hasCarrier || !stickyPlaceable))
            {
                GridZoneType anchorZones = grid != null ? grid.GetZonesAt(anchor) : GridZoneType.None;
                GridZoneType stickyZones = grid != null ? grid.GetZonesAt(stickyCell) : GridZoneType.None;
                Debug.Log($"[ButterBlock] 落点非法: 锚点格{anchor}(区域={anchorZones}, 承载物={(hasCarrier ? "有" : "无")})" +
                    $" 黏性边格{stickyCell}(区域={stickyZones}, 占用={(stickyOccupant != null ? stickyOccupant.name : "空")}, 可布置={stickyPlaceable}) 旋转档={rotation}", this);
            }

            return hasCarrier && stickyPlaceable;
        }

        /// <summary>
        /// 所在格是否存在承载物：格内的搭路类平台道具，或被 Zone 标记为 AttachSurface 的地形表面
        /// </summary>
        private bool HasCarrierAt(GridManager grid, Vector2Int cell, out PlacedItem carrierItem)
        {
            carrierItem = null;
            if (grid == null)
            {
                return false;
            }
            // ① 平台类道具
            if (FindPlatformItem(grid, cell, out carrierItem))
            {
                return true;
            }
            // ② Zone 标记的可附着表面（权威标记）
            if ((grid.GetZonesAt(cell) & GridZoneType.AttachSurface) != 0)
            {
                return true;
            }
            // ③ 物理兜底：格中心有实心非 Trigger 地形碰撞体（未标记区域也能附着，
            //    与早期纯物理版本行为一致；排除玩家与其他道具，道具走①的路径）
            Vector2 cellCenter = grid.GetPlacementWorldPos(cell, Vector2Int.one, 0);
            foreach (Collider2D hit in Physics2D.OverlapPointAll(cellCenter))
            {
                if (hit == null || hit.isTrigger)
                {
                    continue;
                }
                if (hit.GetComponentInParent<SuperQQ.Player.PlayerController>() != null)
                {
                    continue;
                }
                if (hit.GetComponentInParent<ItemBase>() != null)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        // ==================== 生命周期 ====================

        public override void OnPlaced()
        {
            base.OnPlaced();

            GridManager grid = GridManager.Instance;
            if (grid == null || Placed == null)
            {
                return;
            }

            attachedCell = Placed.AnchorCell;
            grid.RegisterAttachment(attachedCell, this);
            attached = true;

            // 记录承载物：以承载物局部坐标系记录偏移（位置+旋转都跟随，
            // 如旋转吐司转动时黄油绕其 pivot 一起旋转）；
            // AttachSurface 标记的地形是静止的，无需跟随（carrier 留空即可）
            if (FindPlatformItem(grid, attachedCell, out PlacedItem foundPlaced))
            {
                carrier = foundPlaced.transform;
                carrierPlaced = foundPlaced;
                carrierLocalOffset = carrier.InverseTransformPoint(transform.position);
                carrierLocalRotation = Quaternion.Inverse(carrier.rotation) * transform.rotation;
            }
        }

        public override void OnRemoved()
        {
            if (attached && GridManager.Instance != null)
            {
                GridManager.Instance.UnregisterAttachment(attachedCell, this);
                attached = false;
            }
            UnstickAll();
            base.OnRemoved();
        }

        // ==================== 跟随与黏住 ====================

        private void LateUpdate()
        {
            // 跟随承载物：按承载物局部坐标系还原位置与朝向——承载物位移/旋转（旋转吐司、
            // 传送带、移动平台）时黄油整体随之运动；黏在黄油上的道具是黄油的子物体，自动连带
            if (carrier != null)
            {
                transform.position = carrier.TransformPoint(carrierLocalOffset);
                transform.rotation = carrier.rotation * carrierLocalRotation;
            }

            if (!attached || Time.time < nextStickCheckTime)
            {
                return;
            }
            nextStickCheckTime = Time.time + stickCheckInterval;
            StickNewItemsOnStickyCell();
        }

        /// <summary>检测黏性边相邻格新出现的道具并黏住（随承载物一起运动）</summary>
        private void StickNewItemsOnStickyCell()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return;
            }

            PlacedItem candidate = grid.GetItemAt(StickyCell);
            if (candidate == null)
            {
                return;
            }
            // 跳过承载物自身与已黏住的
            if (candidate == carrierPlaced || stuckItems.Contains(candidate.transform))
            {
                return;
            }
            // 跳过不可黏目标：整道具不可黏（CanBeStuck=false），
            // 或限定了吸附点但本格不是吸附点（如流星锤仅底座挂点格可黏）
            ItemBase candidateItem = candidate.GetComponent<ItemBase>();
            if (candidateItem != null && !candidateItem.CanBeStuckAt(StickyCell))
            {
                return;
            }

            candidate.transform.SetParent(transform, worldPositionStays: true);
            stuckItems.Add(candidate.transform);
            // 黏住钩子：需要吸附点钉住等自定义行为的道具（流星锤）在此记录跟随参数
            candidateItem?.OnStuckTo(transform, StickyCell);
        }

        /// <summary>解除全部黏住（黄油块被拆时道具恢复独立，停在原地）</summary>
        private void UnstickAll()
        {
            foreach (Transform stuck in stuckItems)
            {
                if (stuck != null)
                {
                    stuck.GetComponent<ItemBase>()?.OnUnstuck();
                    stuck.SetParent(null, worldPositionStays: true);
                }
            }
            stuckItems.Clear();
        }

        // ==================== 承载物查找 ====================

        /// <summary>查找指定格上的搭路类平台道具（PlacedItem + Path 类，且非自身）</summary>
        private bool FindPlatformItem(GridManager grid, Vector2Int cell, out PlacedItem foundPlaced)
        {
            foundPlaced = null;
            if (grid == null)
            {
                return false;
            }

            PlacedItem platform = grid.GetItemAt(cell);
            if (platform != null)
            {
                ItemBase platformItem = platform.GetComponent<ItemBase>();
                if (platformItem != null && platformItem != this && platformItem.Category == ItemCategory.Path)
                {
                    foundPlaced = platform;
                    return true;
                }
            }
            return false;
        }
    }
}

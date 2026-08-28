using SuperQQ.Grid;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 传送门 — 控制类道具
    /// 1x2 格占位，可旋转（绕中心点）；入口与出口分开摆放，先放入口再放出口
    /// 玩家从入口进入后从出口出来（单向传送，出口不反向传送）
    ///
    /// 角色判定（OnPlaced 时自动完成，无需手动配置）：
    /// - 场上不存在未配对的入口 → 本实例成为入口；摆放流程随后通过 SpawnChainedItem
    ///   取回出口实例并直接接管其摆放（入口→出口两次摆放衔接进行，不中断）
    /// - 场上存在未配对的入口 → 本实例成为出口，与其配对（优先匹配同一放置者）
    /// 该顺序约束天然保证"一定先放置入口再放置出口"
    ///
    /// 配对强制约束：场上不允许存在非成对的传送门——
    /// 一端被移除时 OnRemoved 会级联销毁另一端；
    /// 放置阶段结束/摆放取消时由摆放流程调用 DestroyAllUnpaired 清剿落单者
    /// （摆放出口进行中的入口属合法的瞬时未配对状态，出口确认/取消后即收敛）
    ///
    /// prefab 配置约定：
    /// - 根物体挂 Collider2D（isTrigger = true）：兼作拖拽点击与传送触发区，
    ///   玩家可走入触发传送；不建议做成实体碰撞挡住玩家
    /// - FootprintBoxView：footprint = (1,2)，canRotate = true
    /// - PlacableItemDef：category = Control，facingSteps 按需（1x2 旋转后为 2x1）
    ///
    /// 联机注意：角色由放置顺序决定，网络回放时只要各端按相同顺序回放 Place，
    /// 角色判定结果即一致；若入口/出口由不同消息通道下发，建议在消息中显式携带角色。
    /// 任何摆放流程（测试控制器 / GameFlow / 网络回放）在确认放置后都应经
    /// IChainedPlacement.SpawnChainedItem 衔接出口摆放，否则入口将一直处于未配对状态
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Portal : ItemBase, IChainedPlacement
    {
        /// <summary>传送门端点角色</summary>
        private enum PortalRole
        {
            Entrance,   // 入口：触发传送
            Exit,       // 出口：传送落点
        }

        [Header("传送")]
        [Tooltip("传送落点相对出口中心的偏移（避免玩家卡在出口几何体边缘）")]
        [SerializeField] private Vector2 exitOffset = Vector2.zero;

        [Header("出口生成")]
        [Tooltip("入口确认后生成的出口传送门预制体（由摆放流程经 SpawnChainedItem 取回）；留空则回退使用 Placed.Def.Prefab（场景手动摆放测试时 Def 为空，需在 Inspector 中配置本字段）")]
        [SerializeField] private GameObject exitPortalPrefab;
        [Tooltip("出口初始位置相对入口的格子偏移（错开入口，避免出生时占位冲突）")]
        [SerializeField] private Vector2Int exitSpawnCellOffset = new Vector2Int(3, 0);

        [Header("调试表现")]
        [Tooltip("按角色给贴图染色（入口/出口不同色），正式美术素材就位后关闭")]
        [SerializeField] private bool tintByRole = true;
        [SerializeField] private Color entranceColor = new Color(0.3f, 0.8f, 1f, 1f);
        [SerializeField] private Color exitColor = new Color(1f, 0.6f, 0.2f, 1f);

        private PortalRole role = PortalRole.Entrance;
        private Portal linkedPortal;

        /// <summary>控制：改变玩家移动/状态</summary>
        public override ItemCategory Category => ItemCategory.Control;

        /// <summary>本实例是否为入口</summary>
        public bool IsEntrance => role == PortalRole.Entrance;
        /// <summary>是否已完成入口-出口配对</summary>
        public bool IsLinked => linkedPortal != null;
        /// <summary>本实例是否为 SpawnChainedItem 生成的链生段（出口）</summary>
        public bool BIsChainedSpawn { get; private set; }

        /// <summary>标记为链生段（由 SpawnChainedItem 在生成时调用）</summary>
        public void MarkAsChainedSpawn()
        {
            BIsChainedSpawn = true;
        }

        /// <summary>
        /// 是否为"还有后续确认"的链式首段（非链生段，即玩家手摆的第一段）：
        /// 摆放确认时据此在 ItemPlaceConfirm 携带 expect_more，避免服务器过早计入"全员确认完毕"。
        /// 用链生标记而非运行时配对状态判定——确认点击可能先于 OnPlaced/配对建立（竞态安全）
        /// </summary>
        public bool HasChainedItem => !BIsChainedSpawn;
        /// <summary>配对的另一端（入口取出口，出口取入口）</summary>
        public Portal LinkedPortal => linkedPortal;

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[Portal] 所在碰撞体应为 Trigger，否则玩家无法走入触发传送", this);
            }
        }

        /// <summary>
        /// 放置完成：判定角色并建立配对
        /// </summary>
        public override void OnPlaced()
        {
            base.OnPlaced();   // 音效 + 联机生命周期登记（孤门清剿的销毁上报依赖该登记）+ 昼夜色调注册

            Portal pendingEntrance = FindPendingEntrance();
            if (pendingEntrance == null)
            {
                // 先放置的一端：成为入口；出口实例由摆放流程通过 SpawnChainedItem 取回并继续摆放
                role = PortalRole.Entrance;
            }
            else
            {
                // 后放置的一端：成为出口并与等待中的入口配对
                role = PortalRole.Exit;
                LinkTo(pendingEntrance);
            }
            ApplyTint();
        }

        /// <summary>
        /// 被移除（爆破等）前解除配对，避免另一端持有悬空引用；
        /// 配对强制约束：一端被移除时级联销毁另一端，不允许非成对传送门残留
        /// </summary>
        public override void OnRemoved()
        {
            Portal other = linkedPortal;
            Unlink();
            if (other != null)
            {
                other.DestroyIfUnpaired();
            }
        }

        // ==================== 传送 ====================

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 仅入口触发传送，且必须已配对出口
            if (role != PortalRole.Entrance || linkedPortal == null)
            {
                return;
            }
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && player.BAffectedByItems)   // 死亡过渡/幽灵不被传送
            {
                Teleport(player);
                return;
            }

            // 可传送弹体（樱桃等）：保持速度向量从出口继续飞。
            // 各端本地确定性模拟同一轨迹，必在同一时机碰门，本地传送即各端同步
            CherryProjectile projectile = other.GetComponentInParent<CherryProjectile>();
            if (projectile != null && !projectile.BTeleportCoolingDown)
            {
                projectile.TeleportTo((Vector2)linkedPortal.transform.position + linkedPortal.exitOffset);
            }
        }

        /// <summary>
        /// 把玩家传送到出口位置（写 Rigidbody2D.position，物理步进内安全瞬移）。
        /// 出传送门清零速度（玩家从出口静止落下，不带着进门前速度飞出）；
        /// 弹体（樱桃等）走 CherryProjectile.TeleportTo，保持速度不受影响
        /// </summary>
        private void Teleport(PlayerController player)
        {
            Vector2 target = (Vector2)linkedPortal.transform.position + linkedPortal.exitOffset;
            if (player.Rb != null)
            {
                player.Rb.position = target;
            }
            else
            {
                player.transform.position = target;
            }
            // 清零运动状态（刚体速度 + 状态机速度积分器），玩家静止留在出口
            player.ResetMotion();
        }

        // ==================== 配对 ====================

        /// <summary>
        /// 查找场上等待配对的入口（优先同一放置者，其次最早放置的）
        /// </summary>
        private Portal FindPendingEntrance()
        {
            int ownerId = Placed != null ? Placed.OwnerPlayerId : -1;
            Portal fallback = null;
            foreach (Portal portal in FindObjectsOfType<Portal>())
            {
                if (portal == this || !portal.IsEntrance || portal.IsLinked)
                {
                    continue;
                }
                if (portal.Placed != null && portal.Placed.OwnerPlayerId == ownerId)
                {
                    return portal;
                }
                if (fallback == null)
                {
                    fallback = portal;
                }
            }
            return fallback;
        }

        /// <summary>与另一端建立双向配对</summary>
        private void LinkTo(Portal other)
        {
            linkedPortal = other;
            other.linkedPortal = this;
            other.ApplyTint();    // 入口配对完成后可刷新表现（如从"待配对"色变为正常色）
        }

        /// <summary>解除双向配对（本端被移除时调用）</summary>
        private void Unlink()
        {
            if (linkedPortal != null)
            {
                Portal other = linkedPortal;
                linkedPortal = null;
                other.linkedPortal = null;
            }
        }

        // ==================== 配对校验 ====================

        /// <summary>
        /// 校验配对状态，落单则销毁自身：
        /// 配对另一端不存在（从未配对 / 已被销毁）时执行销毁——已登记占据的走网格移除流程
        /// 释放格子并触发 OnRemoved，未登记的（摆放中被取消等）直接销毁
        /// 配对强制约束的单元入口：场上不允许存在非成对的传送门
        /// </summary>
        public void DestroyIfUnpaired()
        {
            // Unity 重载的 == 可识别"引用还在但物体已销毁"的悬空配对
            if (linkedPortal != null)
            {
                return;
            }

            // 联机：所有者端清剿落单者（取消出口/阶段结束未摆出口）时上报销毁，
            // 远端按 ItemStateEvent{DESTROYED} 同步移除——否则远端残留永远无法配对的
            // 幽灵入口，还会被 FindPendingEntrance 兜底抢走后续出口的配对。
            // 仅所有者上报：爆破级联在各端本地已各自执行，非所有者上报会造成重复广播
            if (Placed != null
                && SuperQQ.Network.NetworkManager.Instance != null
                && Placed.OwnerKey == SuperQQ.Network.NetworkManager.Instance.LocalPlayerId)
            {
                ReportNetDestroyed();
            }

            GridManager grid = GridManager.Instance;
            if (Placed != null && grid != null && grid.GetItemAt(Placed.AnchorCell) == Placed)
            {
                grid.RemoveAt(Placed.AnchorCell);   // 释放占据格子并触发 OnRemoved
            }
            else
            {
                OnRemoved();   // 未登记占据：手动补齐移除钩子，保持生命周期对称
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 清剿全场未配对的传送门：逐个校验并销毁落单者（单机/测试用，联机请用按归属过滤的重载）
        /// </summary>
        public static void DestroyAllUnpaired()
        {
            DestroyAllUnpaired(null);
        }

        /// <summary>
        /// 按归属清剿未配对的传送门：只销毁【本地指定放置者】的落单者。
        /// 联机下远端玩家的入口可能正合法地等待其出口摆放结果到达（出口确认由该玩家
        /// 自行决定时机），本地取消/阶段结束绝不可越权清剿——否则远端入口被本地误删，
        /// 随后到达的出口找不到配对入口，表现为"传送门没有同步过来"。
        /// </summary>
        /// <param name="ownerKey">放置者标识（PlacementSession 的 playerKey）；null 表示不过滤（单机/测试）</param>
        public static void DestroyAllUnpaired(string ownerKey)
        {
            foreach (Portal portal in FindObjectsOfType<Portal>())
            {
                if (portal == null)   // 跳过本次清剿中已被级联销毁的
                {
                    continue;
                }
                if (ownerKey != null
                    && portal.Placed != null
                    && !string.IsNullOrEmpty(portal.Placed.OwnerKey)
                    && portal.Placed.OwnerKey != ownerKey)
                {
                    continue;   // 他人摆放的传送门：配对时机由摆放者端决定，本地不清剿
                }
                portal.DestroyIfUnpaired();
            }
        }

        // ==================== 内部 ====================

        /// <summary>
        /// 衔接摆放（IChainedPlacement 契约）：入口确认后生成出口实例，交还摆放流程接管其摆放交互
        /// 出口确认放置时走自身 OnPlaced：检索到本入口（未配对）即自动成为出口并配对
        /// </summary>
        public GameObject SpawnChainedItem()
        {
            if (role != PortalRole.Entrance || IsLinked)
            {
                return null;
            }

            GameObject prefab = exitPortalPrefab != null
                ? exitPortalPrefab
                : (Placed != null && Placed.Def != null ? Placed.Def.Prefab : null);
            if (prefab == null)
            {
                Debug.LogWarning("[Portal] 无法生成出口：未配置 exitPortalPrefab 且放置信息中无 Def", this);
                return null;
            }

            // 初始位置按格子偏移错开入口，避免出生时占位冲突；随后由摆放流程接管移动
            // 用 prefab 默认朝向（其内部旋转标记是未旋转状态）
            Vector3 spawnPos = transform.position;
            GridManager grid = GridManager.Instance;
            if (grid != null)
            {
                float cellSize = grid.PublicCellSize;
                spawnPos += new Vector3(exitSpawnCellOffset.x * cellSize, exitSpawnCellOffset.y * cellSize, 0f);
            }

            GameObject chained = Instantiate(prefab, spawnPos, prefab.transform.rotation);
            // 打上链生标记：expect_more 判定与 itemId 解析都依赖"是否链生段"（竞态安全）
            if (chained.TryGetComponent(out Portal exitPortal))
            {
                exitPortal.MarkAsChainedSpawn();
            }
            return chained;
        }

        /// <summary>按角色染色（调试用，正式素材就位后关闭 tintByRole）</summary>
        private void ApplyTint()
        {
            if (!tintByRole)
            {
                return;
            }
            Color color = role == PortalRole.Entrance ? entranceColor : exitColor;
            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.color = color;
            }
        }
    }
}

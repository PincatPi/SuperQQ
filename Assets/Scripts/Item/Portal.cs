using SuperQQ.Grid;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 传送门 — 控制类道具
    /// 1x2 格占位，可旋转（绕中心点）；两端分开摆放，先放首段再放次段
    /// 双向传送：玩家走入任一端即从另一端出来（角色 role 仅决定摆放顺序/配对，不再限制传送方向）。
    /// 防乒乓：传送后玩家在两端触发区内免疫，走出触发区（OnTriggerExit2D）后解除，
    /// 停留在落点门内不会触发回传，走出再走入可再次传送
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

        [Header("配对配色")]
        [Tooltip("按配对给整对传送门染同一浅色（每对不同色，便于玩家辨认哪两端是一对）；正式美术素材就位后关闭")]
        [SerializeField] private bool tintByPair = true;
        [Tooltip("配对配色板（浅色，循环取用）")]
        [SerializeField] private Color[] pairColors =
        {
            new Color(0.55f, 0.85f, 1.00f),   // 浅蓝
            new Color(1.00f, 0.72f, 0.78f),   // 浅粉
            new Color(0.62f, 0.92f, 0.68f),   // 浅绿
            new Color(1.00f, 0.90f, 0.60f),   // 浅黄
            new Color(0.82f, 0.72f, 1.00f),   // 浅紫
            new Color(0.60f, 0.92f, 0.88f),   // 浅青
        };

        private PortalRole role = PortalRole.Entrance;
        private Portal linkedPortal;

        /// <summary>本对配色的调色板序号（首段放置时从全局序列取号，次段配对时继承首段）</summary>
        private int pairColorIndex;
        /// <summary>全局配对取号序列：各端按相同顺序回放摆放结果，取号序列天然一致，联机下同色</summary>
        private static int _nextPairColorIndex;

        /// <summary>传送免疫中的玩家（刚被本对门传送过）：离开触发区前不再触发传送，防两端往返乒乓</summary>
        private readonly System.Collections.Generic.HashSet<PlayerController> _immunePlayers = new();
        private readonly System.Collections.Generic.List<PlayerController> _expiredImmunity = new();
        private Collider2D _triggerCollider;

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
            _triggerCollider = GetComponent<Collider2D>();
            if (!_triggerCollider.isTrigger)
            {
                Debug.LogWarning("[Portal] 所在碰撞体应为 Trigger，否则玩家无法走入触发传送", this);
            }
        }

        /// <summary>
        /// 免疫清理兜底：逐物理帧按实际重叠校验，玩家【两端触发区都不接触】才解除免疫。
        /// 1) 不能只依赖 OnTriggerExit2D——传送落点经 exitOffset 偏移后常在触发区之外，
        ///    玩家落地时从未进入过落点触发器，Exit 事件永远不会来，免疫将一直残留；
        /// 2) 也不能只看本端——落点偏移量不足以脱离目标门触发区时（跳入门体等场景），
        ///    玩家落岸仍压着目标门，若本端免疫先解除而人还压在另一端内，
        ///    残留的 Enter/时序竞争会形成两端互传死循环（无限传送）
        /// </summary>
        private void FixedUpdate()
        {
            if (_immunePlayers.Count == 0)
            {
                return;
            }

            _expiredImmunity.Clear();
            foreach (PlayerController player in _immunePlayers)
            {
                if (player == null || !IsPlayerTouching(player, this) && !IsPlayerTouching(player, linkedPortal))
                {
                    _expiredImmunity.Add(player);
                }
            }
            foreach (PlayerController player in _expiredImmunity)
            {
                _immunePlayers.Remove(player);
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
                // 先放置的一端：成为入口；出口实例由摆放流程通过 SpawnChainedItem 取回并继续摆放。
                // 同时从全局序列取本对配色号（各端摆放回放顺序一致，取号结果一致）
                role = PortalRole.Entrance;
                pairColorIndex = _nextPairColorIndex++;
            }
            else
            {
                // 后放置的一端：成为出口并与等待中的入口配对（LinkTo 内继承首段配色）
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
            // 双向传送：任一端都可触发，落点为配对另一端（role 仅用于摆放顺序与配对）。
            // 免疫中的玩家（刚被本对门传送、还停在落点触发区内）跳过
            if (linkedPortal == null)
            {
                return;
            }
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                if (player.BAffectedByItems && !_immunePlayers.Contains(player))   // 死亡过渡/幽灵不被传送
                {
                    Teleport(player);
                }
                return;
            }

            // 可传送弹体（樱桃等）：保持速度向量从另一端继续飞（其自身带传送冷却防乒乓）。
            // 各端本地确定性模拟同一轨迹，必在同一时机碰门，本地传送即各端同步
            CherryProjectile projectile = other.GetComponentInParent<CherryProjectile>();
            if (projectile != null && !projectile.BTeleportCoolingDown)
            {
                projectile.TeleportTo((Vector2)linkedPortal.transform.position + linkedPortal.exitOffset);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            // 走出本端且不再接触配对另一端时才解除免疫（与 FixedUpdate 兜底同口径，
            // 防止"压在一端内又走出另一端"时免疫被提前解除形成往返互传）
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && !IsPlayerTouching(player, linkedPortal))
            {
                _immunePlayers.Remove(player);
            }
        }

        /// <summary>玩家碰撞体是否正与指定门的触发区重叠</summary>
        private static bool IsPlayerTouching(PlayerController player, Portal portal)
        {
            return portal != null
                && portal._triggerCollider != null
                && player != null
                && player.Collider != null
                && portal._triggerCollider.IsTouching(player.Collider);
        }

        /// <summary>
        /// 把玩家传送到配对另一端位置（写 Rigidbody2D.position，物理步进内安全瞬移）。
        /// 传送后在两端都加免疫：玩家出现在落点触发区内会再触发一次 OnTriggerEnter2D，
        /// 不加免疫会被立刻传回，两端无限往返；免疫随走出触发区（OnTriggerExit2D）解除。
        /// 出传送门清零速度（玩家从落点静止落下，不带着进门前速度飞出）；
        /// 弹体（樱桃等）走 CherryProjectile.TeleportTo，保持速度不受影响
        /// </summary>
        private void Teleport(PlayerController player)
        {
            _immunePlayers.Add(player);
            linkedPortal._immunePlayers.Add(player);

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

        /// <summary>与另一端建立双向配对（次段继承首段配色，整对同色）</summary>
        private void LinkTo(Portal other)
        {
            linkedPortal = other;
            other.linkedPortal = this;
            pairColorIndex = other.pairColorIndex;
            other.ApplyTint();    // 配对完成后两端刷新表现（确保同色号）
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
        /// 联机远端补配对：远端玩家摆放首段（入口）后，其出口是【该玩家自己】衔接摆放的，
        /// 第二条 ItemPlaceResult 到达时会各自走 OnPlaced 自动配对——但当出口结果因时序/
        /// 阶段边界被本端丢弃、或快照只恢复出首段时，远端首段会永远停在未配对状态
        /// （表现：对方放的传送门自己用不了、道具也过不去）。
        /// 此处按首段已确认的事实，在远端原地补出配对端（不占新格子、不重复上报）
        /// </summary>
        public void LinkWithRemoteCounterpart()
        {
            if (IsLinked)
            {
                return;
            }

            GameObject prefab = exitPortalPrefab != null
                ? exitPortalPrefab
                : (Placed != null && Placed.Def != null ? Placed.Def.Prefab : null);
            if (prefab == null)
            {
                Debug.LogWarning("[Portal] 无法补建配对端：未配置 exitPortalPrefab 且 Def 无 Prefab", this);
                return;
            }

            // 补建端不重复走网格占据/网络上报：仅作为传送落点与触发端存在。
            // 真正的出口结果到达时，OnPlaced 会与其自动配对（本端已是 Linked 则不再抢配对）
            GameObject counterpart = Instantiate(prefab, transform.position, transform.rotation);
            counterpart.name = $"{gameObject.name}_Counterpart";
            var portal = counterpart.GetComponent<Portal>();
            if (portal == null)
            {
                Destroy(counterpart);
                return;
            }

            // 占位/交互组件：补建端不参与摆放与占据
            PlacementController pc = counterpart.GetComponent<PlacementController>();
            if (pc != null)
            {
                pc.DebugHotkeys = false;
                pc.enabled = false;
            }
            var box = counterpart.GetComponent<SuperQQ.Grid.FootprintBoxView>();
            if (box != null)
            {
                box.Hide();
            }
            Collider2D col = counterpart.GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = true;   // 保留触发区：双向传送需要它作为另一端触发点
            }

            portal.MarkAsChainedSpawn();   // 视为链生段（出口语义），不占用配色取号
            portal.NetItemId = NetItemId;
            role = PortalRole.Entrance;
            portal.role = PortalRole.Exit;
            LinkTo(portal);
            portal.ApplyTint();
            Debug.Log($"[Portal] 远端补建配对端完成: owner={Placed?.OwnerKey} anchor={Placed?.AnchorCell}");
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

        /// <summary>按配对染色：整对传送门同一浅色（正式素材就位后关闭 tintByPair）</summary>
        private void ApplyTint()
        {
            if (!tintByPair || pairColors == null || pairColors.Length == 0)
            {
                return;
            }
            Color color = pairColors[pairColorIndex % pairColors.Length];
            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.color = color;
            }
        }
    }
}

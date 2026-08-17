using SuperQQ.Grid;
using SuperQQ.Network;
using SuperQQ.Player;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 金币 — 得分类道具（1x1，不旋转）
    /// 生命周期：
    /// 1. 放置在场上占据格子，等待角色触碰
    /// 2. 本地模拟的玩家触碰后被获取：释放占据格子（永久离场，不可再被拾取），
    ///    转为跟随该玩家——由玩家身上的 CoinFollowGroup 统一记录轨迹并按入队位次错峰，
    ///    第 N 枚金币重现 delay + N×spacing 秒前的位置，多枚金币沿行进路线排成一列不重叠
    /// 3. 跟随的玩家通关 → 提交额外加分（计入本轮结算 ScoreItem"得分道具得分"项），金币消失
    ///    跟随的玩家死亡/进入幽灵状态 → 金币直接消失，不产生分数
    ///
    /// 联机说明（客户端权威 + 纯转发架构）：
    /// 各端只响应本地模拟玩家的触碰（远端化身物理不模拟，触发器不会回调）；
    /// 远端玩家的拾取/跟随/加分由其客户端上的同一个金币实例自行判定，经快照呈现。
    ///
    /// prefab 配置约定：
    /// - 根物体挂 Collider2D（isTrigger = true，约 1 格大小）：触碰检测，玩家可穿过
    /// - FootprintBoxView：footprint = (1,1)，canRotate = false
    /// - PlacableItemDef：category = Score，facingSteps = 0
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Coin : ItemBase
    {
        [Header("加分")]
        [Tooltip("跟随角色通关时提交的额外加分")]
        [SerializeField, Min(1)] private int bonusScore = 5;

        [Header("跟随")]
        [Tooltip("跟随延迟（秒）：队首金币重现玩家该时长之前的移动轨迹")]
        [SerializeField, Min(0f)] private float followDelay = 0.5f;
        [Tooltip("队列错峰间隔（秒）：每靠后一位的金币额外延迟该时长，沿轨迹自然排开不重叠")]
        [SerializeField, Min(0f)] private float spacingDelay = 0.12f;
        [Tooltip("跟随平滑时间（秒）：SmoothDamp 收敛时长，越大越绵软")]
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.08f;
        [Tooltip("跟随点相对玩家轨迹点的偏移（如让金币浮在角色头顶）")]
        [SerializeField] private Vector2 followOffset = new Vector2(0f, 0.5f);

        private bool collected;
        private PlayerController follower;
        private CoinFollowGroup followGroup;
        private Vector2 followVelocity;

        /// <summary>得分：被获取后跟随角色，通关时提供额外加分</summary>
        public override ItemCategory Category => ItemCategory.Score;

        /// <summary>指定位次下的有效轨迹延迟（供 CoinFollowGroup 计算轨迹裁剪窗口）</summary>
        public float EffectiveDelay(int slot)
        {
            return followDelay + Mathf.Max(slot, 0) * spacingDelay;
        }

        // ==================== 拾取 ====================

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (collected || Placed == null)
            {
                return;   // 已拾取 / 尚未确认放置（摆放中）不响应
            }
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }
            Rigidbody2D rb = player.Rb;
            if (rb == null || !rb.simulated)
            {
                return;   // 远端化身物理不模拟（也不会产生本回调），双保险
            }
            // 幽灵等已出局玩家不可拾取（无注册表的测试场景不做限制）
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null && registry.GetPlayerState(player) != PlayerStateType.Alive)
            {
                return;
            }

            // 联机：该金币已被他人认领（广播先行到达）时不再触发本地拾取
            if (SuperQQ.Network.PickupRegistry.BIsClaimed(Placed.AnchorCell))
            {
                return;
            }

            Collect(player, registry);

            // 联机：上报拾取请求，服务器裁决后广播（其他端据此移除这枚金币）
            SuperQQ.Network.NetEventSync.ReportPickup(
                SuperQQ.Network.PickupRegistry.MakeCoinId(Placed.AnchorCell));
            SuperQQ.Network.NetEventSync.ReportEvent(
                Minigame.Room.V1.PlayerEventType.Pickup, transform.position);
        }

        /// <summary>
        /// 被远端玩家认领：释放占据格子，转为跟随远端化身（纯表现，不计分）。
        /// 远端化身位置由快照插值驱动，跟随用简化轨迹（记录远端化身近期位置）。
        /// </summary>
        public void RemoveByRemoteClaim(string claimerPlayerId)
        {
            if (collected)
            {
                return;
            }
            collected = true;

            GridManager grid = GridManager.Instance;
            if (grid != null && Placed != null)
            {
                grid.Release(Placed);
            }

            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            // 找到认领者的远端化身，挂简化跟随组件
            PlayerController remote = FindPlayerByIdentity(claimerPlayerId);
            if (remote != null)
            {
                var follow = gameObject.AddComponent<RemoteCoinFollow>();
                follow.Init(remote.transform, followDelay, followOffset);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private static PlayerController FindPlayerByIdentity(string playerId)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null) return null;

            System.Collections.Generic.IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].IdentityKey == playerId)
                {
                    return players[i];
                }
            }
            return null;
        }

        /// <summary>被获取：释放占据格子永久离场，加入玩家的跟随组转为跟随</summary>
        private void Collect(PlayerController player, LevelPlayerRegistry registry)
        {
            collected = true;
            follower = player;

            // 只释放占据、不销毁自身（RemoveAt 会销毁物体，这里要留下跟随表现）
            GridManager grid = GridManager.Instance;
            if (grid != null && Placed != null)
            {
                grid.Release(Placed);
            }

            // 关闭触发器避免重复拾取；跟随由 Update 驱动
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            // 加入玩家身上的跟随组：优先复用预挂在 CoinsFollowPoint 子物体上的组（含子物体搜索），
            // 缺失时补挂到玩家根物体；轨迹记录由组统一管理
            followGroup = player.GetComponentInChildren<CoinFollowGroup>();
            if (followGroup == null)
            {
                followGroup = player.gameObject.AddComponent<CoinFollowGroup>();
            }
            followGroup.Register(this);

            if (registry != null)
            {
                registry.OnPlayerStateChanged += HandlePlayerStateChanged;
            }
            Debug.Log($"[Coin] 被 {player.PlayerName} 获取，开始跟随");
        }

        // ==================== 跟随 ====================

        private void Update()
        {
            if (!collected)
            {
                return;
            }
            if (follower == null)
            {
                Vanish();   // 跟随对象被销毁（退出关卡等），金币一并消失
                return;
            }
            if (followGroup == null)
            {
                // 兜底：组引用丢失时重新获取（含子物体搜索；正常流程不会发生）
                followGroup = follower.GetComponentInChildren<CoinFollowGroup>();
                if (followGroup == null)
                {
                    Vanish();
                    return;
                }
            }

            // 按队列位次计算错峰延迟，从组取跟随目标点（含静止散布），SmoothDamp 平滑逼近
            float delay = EffectiveDelay(followGroup.SlotOf(this));
            Vector2 target = followGroup.GetSlotTarget(this, delay) + followOffset;
            Vector2 pos = Vector2.SmoothDamp(transform.position, target, ref followVelocity, followSmoothTime);
            transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        }

        // ==================== 结局 ====================

        /// <summary>跟随的玩家状态变化：通关 → 提交额外加分后消失；死亡/幽灵 → 直接消失</summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType state)
        {
            if (player != follower)
            {
                return;
            }
            if (state == PlayerStateType.Finished)
            {
                if (PlayerScoreManager.Instance != null)
                {
                    PlayerScoreManager.Instance.RecordBonusScore(player.PlayerName, bonusScore);
                }
                Debug.Log($"[Coin] {player.PlayerName} 通关，提交额外加分 +{bonusScore}");
                Vanish();
            }
            else if (state == PlayerStateType.Ghost)
            {
                Debug.Log($"[Coin] {player.PlayerName} 已死亡，金币消失");
                Vanish();
            }
        }

        /// <summary>消失：离队、清理订阅并销毁（通关结算/玩家死亡/跟随对象销毁共用出口）</summary>
        private void Vanish()
        {
            if (followGroup != null)
            {
                followGroup.Unregister(this);
            }
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // 兜底：被炸毁/清空等外部销毁路径也要离队并解除订阅
            if (followGroup != null)
            {
                followGroup.Unregister(this);
            }
            if (collected && LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
            }
        }
    }
}

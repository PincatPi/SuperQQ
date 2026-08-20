using System.Collections;
using System.Collections.Generic;
using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 拆除类道具基类 — 即放即爆的消耗品（摔炮、黑炸弹、原子弹等）
    /// 共同行为：放置后自动引爆，清除自身 footprint 覆盖格子内的所有其它道具，随后自身销毁；
    /// 各子类仅通过占位尺寸（FootprintBoxView / DefaultFootprint）区分范围档位，无独立逻辑
    ///
    /// 占位策略（由 ItemBase 虚属性声明，GridManager / PlacementController 遵守）：
    /// - AllowsOccupiedOverlap = true：允许叠放到其它道具上方（爆破范围即自身 footprint，必须能覆盖目标）
    /// - RegistersOccupancy = false：不登记自身占据——即放即消不会持久存在，
    ///   落点格子只保留原道具的占据记录（目标被清除后随之释放）
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = 子类对应尺寸，canRotate = false（均为正方形占位）
    /// - PlacableItemDef：category = Demolition，facingSteps = 0
    /// - 不挂吸附类组件（测试流程会在运行时补挂 PlacementController）
    /// </summary>
    public abstract class DemolitionItemBase : ItemBase
    {
        [Header("引爆")]
        [Tooltip("放置后到引爆的延迟（秒），留给引线/预警表现；0 表示尽快引爆")]
        [SerializeField, Min(0f)] private float fuseDelay = 0f;

        [Header("表现（可选）")]
        [Tooltip("引爆时在中心生成的特效预制体（闪光/烟雾等），留空则无")]
        [SerializeField] private GameObject explosionEffectPrefab;

        /// <summary>拆除：即放即爆的消耗品</summary>
        public sealed override ItemCategory Category => ItemCategory.Demolition;

        /// <summary>允许叠放到其它道具上方：爆破范围即自身 footprint，必须能覆盖目标才能清除</summary>
        public sealed override bool AllowsOccupiedOverlap => true;

        /// <summary>不登记自身占据：即放即消的消耗品不会持久存在，落点不留占位</summary>
        public sealed override bool RegistersOccupancy => false;

        /// <summary>
        /// 占位解析失败时的兜底尺寸（FootprintBoxView 与 Def 均缺失时使用）
        /// 子类按策划表重写（摔炮 2x2 / 黑炸弹 3x3 / 原子弹 5x5）
        /// </summary>
        protected abstract Vector2Int DefaultFootprint { get; }

        // 联机模式下等待服务器拆除仲裁的本地炸弹（锚点 → 实例）：
        // 本地放置后挂起不引爆，ItemDemolishResult 到达时统一引爆，保证各端移除集合一致
        private static readonly Dictionary<Vector2Int, DemolitionItemBase> _pendingByAnchor = new();

        /// <summary>是否处于联机模式（已连接且已进房）</summary>
        private static bool BNetMode =>
            SuperQQ.Network.NetworkManager.Instance != null
            && SuperQQ.Network.NetworkManager.Instance.IsConnected
            && !string.IsNullOrEmpty(SuperQQ.Network.NetworkManager.Instance.RoomId);

        /// <summary>
        /// 放置完成后进入引爆流程（拆除类道具即放即爆）：
        /// 单机按引信计时引爆；联机挂起等待服务器 ItemDemolishResult 统一引爆
        /// </summary>
        public override void OnPlaced()
        {
            if (BNetMode && Placed != null)
            {
                _pendingByAnchor[Placed.AnchorCell] = this;
                return;
            }
            StartCoroutine(DetonateRoutine());
        }

        /// <summary>取走指定锚点上等待仲裁的本地炸弹（PropPlacementDirector 收到拆除结果时调用）</summary>
        public static bool TryTakePending(Vector2Int anchor, out DemolitionItemBase bomb)
        {
            if (_pendingByAnchor.TryGetValue(anchor, out bomb) && bomb != null)
            {
                _pendingByAnchor.Remove(anchor);
                return true;
            }
            _pendingByAnchor.Remove(anchor);
            bomb = null;
            return false;
        }

        /// <summary>
        /// 联机同步引爆：按服务器裁定的被拆锚点集合移除道具，播放特效后销毁自身。
        /// 投放者端与远端各端执行完全相同的移除集合，占据表不分叉。
        /// </summary>
        /// <param name="removedAnchors">服务器 ItemDemolishResult.removed_items 的锚点集合</param>
        public void DetonateSynced(IReadOnlyCollection<Vector2Int> removedAnchors)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || Placed == null)
            {
                Destroy(gameObject);
                return;
            }

            if (removedAnchors != null)
            {
                foreach (Vector2Int anchor in removedAnchors)
                {
                    // RemoveAt 会释放目标的全部占位格子并销毁物体（含 OnRemoved 钩子）
                    grid.RemoveAt(anchor);
                }
            }

            SpawnExplosionEffect();
            DestroySelf(grid);
        }

        /// <summary>
        /// 立即引爆：清除自身 footprint 覆盖格子内的所有其它道具，随后自身销毁
        /// 供外部系统（网络回放/调试）直接触发；正常流程由 OnPlaced 自动驱动
        /// </summary>
        public void Detonate()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || Placed == null)
            {
                // 无网格或未注入放置信息时无法定位爆破范围，仅销毁自身
                Destroy(gameObject);
                return;
            }

            // 先收集目标再逐个移除，避免在遍历中修改网格占据表
            foreach (PlacedItem target in CollectTargetsInArea(grid))
            {
                // RemoveAt 会释放目标的全部占位格子并销毁物体（含 OnRemoved 钩子）
                grid.RemoveAt(target.AnchorCell);
            }

            SpawnExplosionEffect();
            DestroySelf(grid);
        }

        /// <summary>
        /// 引爆协程：顺延到帧末（等待放置流程执行完毕）并经过引信延迟后引爆
        /// </summary>
        private IEnumerator DetonateRoutine()
        {
            // OnPlaced 由 GridManager.Place / PlacementController.CompletePlacement 在放置流程中调用，
            // 顺延到帧末再引爆，避免放置调用栈尚未返回就销毁自身与目标
            yield return new WaitForEndOfFrame();

            if (fuseDelay > 0f)
            {
                yield return new WaitForSeconds(fuseDelay);
            }

            Detonate();
        }

        /// <summary>
        /// 收集爆破范围内的所有目标（按 PlacedItem 去重，排除自身）
        /// 范围 = 自身 footprint 覆盖的格子，不外扩
        /// </summary>
        private HashSet<PlacedItem> CollectTargetsInArea(GridManager grid)
        {
            var targets = new HashSet<PlacedItem>();
            foreach (Vector2Int cell in grid.GetFootprintCells(Placed.AnchorCell, ResolveOwnFootprint(), Placed.Rotation))
            {
                PlacedItem item = grid.GetItemAt(cell);
                if (item != null && item != Placed)
                {
                    targets.Add(item);
                }
            }
            return targets;
        }

        /// <summary>
        /// 销毁自身：正常流程未登记占据，直接销毁（手动调用 OnRemoved 保持生命周期对称）；
        /// 兜底兼容外部系统曾为其登记占据的情况，走 RemoveAt 统一释放格子
        /// </summary>
        private void DestroySelf(GridManager grid)
        {
            if (grid.GetItemAt(Placed.AnchorCell) == Placed)
            {
                grid.RemoveAt(Placed.AnchorCell);
            }
            else
            {
                OnRemoved();
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 解析自身占位尺寸：与 GridManager 判定口径一致，优先读实例上的 FootprintBoxView
        /// </summary>
        private Vector2Int ResolveOwnFootprint()
        {
            FootprintBoxView box = GetComponent<FootprintBoxView>();
            if (box != null)
            {
                return box.Footprint;
            }
            if (Placed != null && Placed.Def != null)
            {
                return Placed.Def.Footprint;
            }
            return DefaultFootprint;
        }

        /// <summary>
        /// 生成爆破特效（可选），由特效预制体自身负责生命周期
        /// </summary>
        private void SpawnExplosionEffect()
        {
            if (explosionEffectPrefab != null)
            {
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            }
        }
    }
}

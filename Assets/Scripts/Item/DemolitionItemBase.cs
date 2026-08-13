using System.Collections;
using System.Collections.Generic;
using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 拆除类道具基类 — 即放即爆的消耗品（摔炮、黑炸弹、原子弹等）
    /// 共同行为：放置后自动引爆，摧毁爆破范围内的所有其它道具，随后自身销毁
    /// 各子类仅通过占位尺寸（FootprintBoxView / DefaultFootprint）与
    /// 爆破外扩格数（blastExpandCells）区分范围档位，无独立逻辑
    ///
    /// 注意：网格放置规则不允许与其它道具重叠，若爆破只查自身占位格将永远命中不到目标，
    /// 因此范围需在 footprint 基础上外扩（blastExpandCells >= 1）
    ///
    /// prefab 配置约定：
    /// - FootprintBoxView：footprint = 子类对应尺寸，canRotate = false（均为正方形占位）
    /// - PlacableItemDef：category = Demolition，facingSteps = 0
    /// - 不挂吸附类组件
    /// </summary>
    public abstract class DemolitionItemBase : ItemBase
    {
        [Header("引爆")]
        [Tooltip("放置后到引爆的延迟（秒），留给引线/预警表现；0 表示尽快引爆")]
        [SerializeField, Min(0f)] private float fuseDelay = 0f;
        [Tooltip("爆破范围相对自身占位向外扩展的格数；0=仅自身占位（因放置不允许重叠，将命中不到任何目标），1=含相邻一圈")]
        [SerializeField, Min(0)] private int blastExpandCells = 1;

        [Header("表现（可选）")]
        [Tooltip("引爆时在中心生成的特效预制体（闪光/烟雾等），留空则无")]
        [SerializeField] private GameObject explosionEffectPrefab;

        /// <summary>拆除：即放即爆的消耗品</summary>
        public sealed override ItemCategory Category => ItemCategory.Demolition;

        /// <summary>
        /// 占位解析失败时的兜底尺寸（FootprintBoxView 与 Def 均缺失时使用）
        /// 子类按策划表重写（摔炮 2x2 / 黑炸弹 3x3 / 原子弹 5x5）
        /// </summary>
        protected abstract Vector2Int DefaultFootprint { get; }

        /// <summary>
        /// 放置完成后自动进入引爆流程（拆除类道具即放即爆）
        /// </summary>
        public override void OnPlaced()
        {
            StartCoroutine(DetonateRoutine());
        }

        /// <summary>
        /// 立即引爆：摧毁爆破范围内的所有其它道具，随后自身销毁
        /// 供外部系统（网络回放/调试）直接触发；正常流程由 OnPlaced 自动驱动
        /// </summary>
        public void Detonate()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || Placed == null)
            {
                // 无网格或未登记放置信息时无法定位爆破范围，仅销毁自身
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

            // 消耗品自身一并销毁：走 RemoveAt 统一释放格子并触发 OnRemoved 钩子
            grid.RemoveAt(Placed.AnchorCell);
        }

        /// <summary>
        /// 引爆协程：等待占据登记完成与引信延迟后引爆
        /// </summary>
        private IEnumerator DetonateRoutine()
        {
            // GridManager.Place 中 OnPlaced 先于占据登记执行，
            // 顺延到帧末再引爆，保证 RemoveAt 能正确找到并释放自身格子
            yield return new WaitForEndOfFrame();

            if (fuseDelay > 0f)
            {
                yield return new WaitForSeconds(fuseDelay);
            }

            Detonate();
        }

        /// <summary>
        /// 收集爆破范围内的所有目标（去重，排除自身）
        /// 范围 = 自身 footprint 矩形按 blastExpandCells 向外扩展后的格子矩形
        /// </summary>
        private HashSet<PlacedItem> CollectTargetsInArea(GridManager grid)
        {
            var targets = new HashSet<PlacedItem>();
            Vector2Int footprint = ResolveOwnFootprint();
            Vector2Int size = Placed.Rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;

            // 爆破矩形：锚点（左下角）向外扩 blastExpandCells 格，宽高各加 2 倍
            Vector2Int min = Placed.AnchorCell - Vector2Int.one * blastExpandCells;
            Vector2Int max = Placed.AnchorCell + size + Vector2Int.one * blastExpandCells;

            // 一个道具可能跨多格，用 HashSet 按 PlacedItem 去重
            for (int x = min.x; x < max.x; x++)
            {
                for (int y = min.y; y < max.y; y++)
                {
                    PlacedItem item = grid.GetItemAt(new Vector2Int(x, y));
                    if (item != null && item != Placed)
                    {
                        targets.Add(item);
                    }
                }
            }
            return targets;
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

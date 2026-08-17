using System.Collections.Generic;

namespace SuperQQ.Event
{
    /// <summary>
    /// 关卡事件选取器 — 纯 C# 静态类
    /// 负责"本关执行哪些事件"的选取决策，不依赖 Unity 生命周期，可独立单元测试
    /// 选取规则：
    ///   1. 所有 BIsFixed 为 true 的固定事件全部选中
    ///   2. 从非固定事件中按权重随机抽取一个
    /// 随机源通过参数注入：传入固定种子的 System.Random 可复现选取结果（单元测试 / 联机种子同步用）
    /// </summary>
    public static class LevelEventSelector
    {
        /// <summary>
        /// 从事件池中选定本关事件
        /// </summary>
        /// <param name="pool">事件池（来自 LevelEventConfig.Events）</param>
        /// <param name="random">随机源；为 null 时使用时间种子创建</param>
        /// <returns>选中的事件条目列表（固定事件在前、随机事件在后）；事件池为空时返回空列表</returns>
        public static List<LevelEventEntry> SelectEvents(IReadOnlyList<LevelEventEntry> pool, System.Random random = null)
        {
            List<LevelEventEntry> selected = new List<LevelEventEntry>();
            if (pool == null || pool.Count == 0)
            {
                return selected;
            }

            // 步骤1：收集固定事件与非固定事件
            List<LevelEventEntry> flexibleEvents = new List<LevelEventEntry>();
            for (int i = 0; i < pool.Count; i++)
            {
                LevelEventEntry entry = pool[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.BIsFixed)
                {
                    selected.Add(entry);
                }
                else
                {
                    flexibleEvents.Add(entry);
                }
            }

            // 步骤2：从非固定事件中按权重随机抽取一个
            if (flexibleEvents.Count > 0)
            {
                LevelEventEntry picked = PickWeightedRandom(flexibleEvents, random ?? new System.Random());
                if (picked != null)
                {
                    selected.Add(picked);
                }
            }

            return selected;
        }

        /// <summary>
        /// 按权重随机抽取一个条目
        /// 权重越大被抽中的概率越高；权重为 0 的条目永远不会被抽中
        /// 全部条目权重都为 0 时退化为均匀随机
        /// </summary>
        private static LevelEventEntry PickWeightedRandom(List<LevelEventEntry> entries, System.Random random)
        {
            float totalWeight = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Weight > 0f)
                {
                    totalWeight += entries[i].Weight;
                }
            }

            // 未配置任何有效权重时退化为均匀随机
            if (totalWeight <= 0f)
            {
                return entries[random.Next(entries.Count)];
            }

            float roll = (float)random.NextDouble() * totalWeight;
            for (int i = 0; i < entries.Count; i++)
            {
                float weight = entries[i].Weight > 0f ? entries[i].Weight : 0f;
                if (roll < weight)
                {
                    return entries[i];
                }
                roll -= weight;
            }

            // 浮点误差兜底：返回最后一个条目
            return entries[entries.Count - 1];
        }
    }
}

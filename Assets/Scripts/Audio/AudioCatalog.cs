using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 音效目录（ScriptableObject 配置表）。
    /// SfxId → SfxEntry 的映射表，资产统一放 Assets/ScriptableObject/Audio/ 下，
    /// 由 AudioManager 持有并查询；外部模块不直接接触本类。
    ///
    /// 使用方式：Project 窗口右键 → Create → SuperQQ → Audio → Audio Catalog，
    /// 在 Inspector 中添加条目并拖配 AudioClip。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "SuperQQ/Audio/Audio Catalog")]
    public class AudioCatalog : ScriptableObject
    {
        [Tooltip("音效条目列表；同一 SfxId 只允许出现一次")]
        [SerializeField] private List<SfxEntry> _entries = new();

        /// <summary>全部条目（仅供编辑器工具遍历，运行逻辑请用 TryGet）</summary>
        public IReadOnlyList<SfxEntry> Entries => _entries;

        // SfxId → 条目的运行时索引，首次查询时惰性构建
        private Dictionary<SfxId, SfxEntry> _index;

        // ==================== 查询 ====================

        /// <summary>
        /// 按 SfxId 查询条目（O(1)）。
        /// 未注册时返回 false，调用方（AudioManager）负责警告与静默跳过。
        /// </summary>
        public bool TryGet(SfxId id, out SfxEntry entry)
        {
            EnsureIndex();
            return _index.TryGetValue(id, out entry);
        }

        /// <summary>惰性构建字典索引；OnValidate 后自动重建</summary>
        private void EnsureIndex()
        {
            if (_index != null)
            {
                return;
            }
            _index = new Dictionary<SfxId, SfxEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                SfxEntry entry = _entries[i];
                if (entry == null || entry.Id == SfxId.None)
                {
                    continue;
                }
                // 重复键后者覆盖；编辑期已由 OnValidate 提示
                _index[entry.Id] = entry;
            }
        }

        // ==================== 编辑期校验 ====================

        private void OnValidate()
        {
            // 配置变更后索引失效，下次查询时重建
            _index = null;

            ValidateSfxIdUniqueness();

            var seen = new HashSet<SfxId>();
            for (int i = 0; i < _entries.Count; i++)
            {
                SfxEntry entry = _entries[i];
                if (entry == null)
                {
                    continue;
                }
                if (entry.Id == SfxId.None)
                {
                    Debug.LogWarning($"[AudioCatalog] 第 {i} 条条目未指定 SfxId，运行时不参与索引。", this);
                    continue;
                }
                if (!seen.Add(entry.Id))
                {
                    Debug.LogWarning($"[AudioCatalog] SfxId.{entry.Id} 存在重复条目（第 {i} 条），运行时仅生效最后一条。", this);
                }
                if (!entry.HasValidClip)
                {
                    Debug.LogWarning($"[AudioCatalog] SfxId.{entry.Id} 未配置任何 AudioClip，播放时将被跳过。", this);
                }
            }
        }

        /// <summary>
        /// 枚举值重复检测：SfxId 显式编号靠人工保证唯一，撞值会导致 Inspector 选 A 存 B。
        /// 用反射扫描全部枚举值，发现同值多名字即报 Error（比条目重复更严重，需立即修枚举）。
        /// </summary>
        private static void ValidateSfxIdUniqueness()
        {
            SfxId[] values = (SfxId[])System.Enum.GetValues(typeof(SfxId));
            string[] names = System.Enum.GetNames(typeof(SfxId));

            var valueToNames = new Dictionary<int, List<string>>();
            for (int i = 0; i < values.Length; i++)
            {
                int v = (int)values[i];
                if (!valueToNames.TryGetValue(v, out List<string> list))
                {
                    list = new List<string>();
                    valueToNames[v] = list;
                }
                list.Add(names[i]);
            }

            foreach (KeyValuePair<int, List<string>> pair in valueToNames)
            {
                if (pair.Value.Count > 1)
                {
                    Debug.LogError(
                        $"[AudioCatalog] SfxId 枚举值 {pair.Key} 被重复定义：{string.Join("、", pair.Value)}。" +
                        "枚举按整数序列化，同值会导致选择显示错乱，请为每个键分配唯一编号。");
                }
            }
        }
    }
}

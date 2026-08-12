using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 关卡区域配置 — ScriptableObject 资产
    /// 由编辑器工具从场景中的 GridZoneMarker 烘焙生成（一键按钮），
    /// 运行时由 GridManager 加载并作为区域判定的唯一数据来源
    /// 每个关卡对应一份资产
    /// </summary>
    [CreateAssetMenu(fileName = "LevelZoneConfig", menuName = "SuperQQ/LevelZoneConfig")]
    public class LevelZoneConfig : ScriptableObject
    {
        /// <summary>一条区域记录</summary>
        [Serializable]
        public struct ZoneEntry
        {
            [Tooltip("区域类别（可多选）")]
            public GridZoneType zoneType;
            [Tooltip("区域覆盖的格子范围（格子坐标）")]
            public RectInt cells;
        }

        [SerializeField] private List<ZoneEntry> zones = new List<ZoneEntry>();

        /// <summary>所有区域记录（只读）</summary>
        public IReadOnlyList<ZoneEntry> Zones => zones;

        /// <summary>替换全部区域记录（编辑器烘焙用）</summary>
        public void SetZones(List<ZoneEntry> entries)
        {
            zones = entries ?? new List<ZoneEntry>();
        }
    }
}

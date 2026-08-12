using System;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 网格区域类型（位标志，一个区域可同时属于多个类型）
    /// </summary>
    [Flags]
    public enum GridZoneType
    {
        None = 0,
        /// <summary>出生/终点区域：不可布置道具</summary>
        SpawnGoal = 1 << 0,
        /// <summary>水底区域：不可布置道具，玩家掉入死亡</summary>
        Water = 1 << 1,
        /// <summary>被占用区域：不可布置道具（关卡预占等）</summary>
        Occupied = 1 << 2,
    }

    public static class GridZoneTypeExtensions
    {
        /// <summary>所有禁止布置道具的区域类型掩码</summary>
        public const GridZoneType BlockPlacementMask =
            GridZoneType.SpawnGoal | GridZoneType.Water | GridZoneType.Occupied;

        /// <summary>该区域组合是否禁止布置道具</summary>
        public static bool BlocksPlacement(this GridZoneType zones)
        {
            return (zones & BlockPlacementMask) != 0;
        }
    }
}

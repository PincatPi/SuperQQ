using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 玩家颜色色板：服务器按进房顺序分配 color_index，两端用同一张色板查表，
    /// 保证各端看到的玩家颜色一致且互不撞色。
    /// </summary>
    public static class PlayerColorPalette
    {
        private static readonly Color[] Colors =
        {
            new(0.90f, 0.30f, 0.30f), // 0 红
            new(0.30f, 0.55f, 0.95f), // 1 蓝
            new(0.30f, 0.80f, 0.40f), // 2 绿
            new(0.95f, 0.80f, 0.25f), // 3 黄
            new(0.70f, 0.40f, 0.90f), // 4 紫
            new(0.95f, 0.55f, 0.25f), // 5 橙
            new(0.35f, 0.85f, 0.85f), // 6 青
            new(0.95f, 0.50f, 0.70f), // 7 粉
        };

        /// <summary>按色号取颜色；越界时循环复用</summary>
        public static Color Get(int colorIndex)
        {
            if (colorIndex < 0) return Color.white;
            return Colors[colorIndex % Colors.Length];
        }
    }
}

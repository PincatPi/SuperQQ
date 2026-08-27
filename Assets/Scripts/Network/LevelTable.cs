namespace SuperQQ.Network
{
    /// <summary>
    /// 关卡表：服务器 levelId → 客户端场景名 / 展示名。
    /// levelId 由房主在房间内选择（SetRoomLevel），服务器随 Room/RoomUpdated/RoomSnapshot 下发；
    /// 0 或未识别 ID 一律回退默认第一关（Level1），保证旧服务器/未配置时行为不变。
    /// 新增关卡：在 Options 里加一行，并把对应场景加入 Build Settings。
    /// </summary>
    public static class LevelTable
    {
        public struct LevelOption
        {
            public int Id;
            public string SceneName;
            public string Label;
        }

        /// <summary>可选关卡（顺序即房间选关按钮的循环顺序）</summary>
        public static readonly LevelOption[] Options =
        {
            new LevelOption { Id = 1, SceneName = "Level1", Label = "橘汐双岛" },
            new LevelOption { Id = 2, SceneName = "Level2", Label = "可可熔崖" },
        };

        /// <summary>levelId → 场景名（0/未识别 → 默认第一关）</summary>
        public static string ResolveSceneName(int levelId)
        {
            for (int i = 0; i < Options.Length; i++)
            {
                if (Options[i].Id == levelId)
                {
                    return Options[i].SceneName;
                }
            }
            return Options[0].SceneName;
        }

        /// <summary>levelId → 展示名</summary>
        public static string ResolveLabel(int levelId)
        {
            for (int i = 0; i < Options.Length; i++)
            {
                if (Options[i].Id == levelId)
                {
                    return Options[i].Label;
                }
            }
            return Options[0].Label;
        }

        /// <summary>循环选关：返回当前关卡的下一关 ID（当前 ID 未识别时回到第一关）</summary>
        public static int NextLevelId(int currentLevelId)
        {
            for (int i = 0; i < Options.Length; i++)
            {
                if (Options[i].Id == currentLevelId)
                {
                    return Options[(i + 1) % Options.Length].Id;
                }
            }
            return Options[0].Id;
        }
    }
}

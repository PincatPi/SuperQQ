namespace SuperQQ.UI
{
    /// <summary>
    /// 局内弹窗类型 — PopupManager 注册表的索引键
    /// 游戏逻辑只引用本枚举，不持有任何 UI 资源引用
    /// 新增弹窗：在此添加枚举值 → 制作挂有 PopupView 的 Prefab → 在 PopupManager 注册表中登记
    /// </summary>
    public enum PopupType
    {
        /// <summary>未配置（占位值，传入时 PopupManager 拒绝播放并告警）</summary>
        None = 0,

        // ==================== 事件说明弹窗（与 LevelEventType 对应） ====================

        /// <summary>小蛋糕陨石事件说明弹窗</summary>
        CakeMeteorIntro = 1,

        /// <summary>液氮泄露事件说明弹窗</summary>
        NitrogenLeakIntro = 2,

        /// <summary>言出法随事件说明弹窗</summary>
        MagicCircleIntro = 3,

        /// <summary>Boss 巡逻事件说明弹窗</summary>
        BossPatrolIntro = 4,

        // ==================== 通用局内弹窗 ====================

        /// <summary>仅剩一名存活玩家时的提前结束提示</summary>
        EndEarly = 10,

        /// <summary>解冻进度条弹窗（手动关闭，Prefab 根节点挂 ThawProgressBar）</summary>
        ThawProgress = 12,
    }

    /// <summary>
    /// 局内提示（Tips）类型 — PopupManager 注册表的索引键
    /// Tips 与弹窗的区别：只能自动关闭（固定时长后隐藏回收），不提供手动关闭
    /// 新增 Tips：在此添加枚举值 → 制作挂有 TipsView 的 Prefab → 在 PopupManager 注册表中登记
    /// </summary>
    public enum TipsType
    {
        /// <summary>未配置（占位值，传入时 PopupManager 拒绝播放并告警）</summary>
        None = 0,

        /// <summary>通用 Tips：仅展示一段提示文本，固定时长后自动关闭</summary>
        Common = 1,
    }
}

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

        /// <summary>蜘蛛网事件说明弹窗</summary>
        SpiderWebIntro = 5,

        // ==================== 通用局内弹窗 ====================

        /// <summary>仅剩一名存活玩家时的提前结束提示</summary>
        EndEarly = 10,

        /// <summary>通关弹窗（按注册表默认时长自动关闭）</summary>
        LevelClear = 11,

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

    /// <summary>
    /// 浮动文本（FloatingText）类型 — PopupManager 浮动文本注册表的索引键
    /// 浮动文本：在指定世界锚点展示一段临时文本，固定时长后自动关闭销毁；
    /// 文本内容、位置偏移与展示时长统一在 PopupManager 注册表中配置，调用方只传类型与锚点
    /// 新增浮动文本：在此添加枚举值 → 在 PopupManager 浮动文本注册表中登记
    /// </summary>
    public enum FloatingTextType
    {
        /// <summary>未配置（占位值，传入时 PopupManager 拒绝播放并告警）</summary>
        None = 0,

        /// <summary>道具放置阶段确认落点非法时的提示（如「该区域不可放置哦」）</summary>
        InvalidPlacement = 1,
    }
}

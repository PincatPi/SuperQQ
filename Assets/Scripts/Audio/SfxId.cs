using UnityEngine;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 音频总线分组。
    /// 与 MainAudioMixer 资产中的 Mixer 分组一一对应（Master → Music / SFX / UI），
    /// 用于音量独立控制与静音管理。
    /// </summary>
    public enum AudioBus
    {
        /// <summary>主总线：所有音频的最终输出，静音与总音量作用于本组</summary>
        Master = 0,

        /// <summary>音乐总线：BGM（大厅/关卡背景音乐）</summary>
        Music = 1,

        /// <summary>音效总线：场景内玩法音效</summary>
        SFX = 2,

        /// <summary>界面总线：UI 交互音效（点击、悬停、通知等），与场景音效分离便于独立调节</summary>
        UI = 3,
    }

    /// <summary>
    /// 音效标识（类型安全键）。
    /// 调用方经 AudioManager 静态 API 按本枚举播放音频，无需接触 AudioClip/AudioSource；
    /// 音效条目（Clip、音量、分组等）在 AudioCatalog 资产中配置。
    ///
    /// 新增音效步骤：
    ///   1. 在下方对应分组中添加一个枚举值（显式编号，保证序列化稳定）；
    ///   2. 在 AudioCatalog 资产中添加同名条目并拖配 AudioClip。
    /// </summary>
    public enum SfxId
    {
        /// <summary>无效占位（默认值的防线，不可播放）</summary>
        None = 0,

        // ==================== 道具与玩法（2xx，Clip 对应 Assets/Audio/Pickup_Place、Notification 等） ====================

        /// <summary>金币拾取（Coin 被获取时播放）</summary>
        CoinPickup = 201,

        /// <summary>放置道具（ItemBase.OnPlaced 统一播放）</summary>
        Place = 202,

        /// <summary>闹钟响铃（震屏生效时播放）</summary>
        AlarmRing = 209,

        /// <summary>玩家被命中（伤害型道具/事件命中致死或击飞时播放；坠落出界不播放）</summary>
        PlayerHit = 210,

        /// <summary>冰块碎裂（液氮泄露事件：解冻进度里程碑与完全解冻时播放）</summary>
        IceCrack = 211,

        // ==================== 阶段与反馈（3xx） ====================

        /// <summary>正式游玩阶段开始（PlayingPhase.OnEnter 播放）</summary>
        RoundStart = 310,

        /// <summary>正式游玩阶段结束（PlayingPhase.OnExit 播放）</summary>
        RoundFinish = 311,

        /// <summary>飞行咒语循环音效（按住跳跃键循环播放，松开淡出；经 StartLoopSfx/StopLoopSfx 控制）</summary>
        FlightLoop = 320,

        // ==================== 音乐（4xx，经 LoopChannel 循环播放） ====================

        /// <summary>大厅 BGM</summary>
        BgmLobby = 401,

        /// <summary>关卡 BGM</summary>
        BgmLevel = 402,
    }
}

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

        /// <summary>音效总线：场景内玩法音效与环境循环音（海浪等）</summary>
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

        // ==================== UI 交互（1xx，对应 Assets/Audio/Click、Hover、Type、Notification） ====================

        /// <summary>UI 点击</summary>
        UiClick = 101,

        /// <summary>UI 悬停</summary>
        UiHover = 102,

        /// <summary>打字/文本逐字音</summary>
        UiType = 103,

        /// <summary>通知提示（事件播报、系统提示等）</summary>
        Notify = 104,

        // ==================== 道具与玩法（2xx，对应 Pickup_Place、Combine、Buy_Sell、Equip_Unequip、Upgrade） ====================

        /// <summary>拾取道具</summary>
        Pickup = 201,

        /// <summary>放置道具</summary>
        Place = 202,

        /// <summary>合成</summary>
        Combine = 203,

        /// <summary>购买</summary>
        Buy = 204,

        /// <summary>出售</summary>
        Sell = 205,

        /// <summary>装备</summary>
        Equip = 206,

        /// <summary>卸下装备</summary>
        Unequip = 207,

        /// <summary>升级</summary>
        Upgrade = 208,

        // ==================== 表现反馈（3xx，对应 Appear_Swoosh、Success_Bonus） ====================

        /// <summary>出现/掠过（物体入场、转场扫音）</summary>
        Appear = 301,

        /// <summary>成功</summary>
        Success = 302,

        /// <summary>奖励/加分</summary>
        Bonus = 303,

        // ==================== 音乐（4xx，经 LoopChannel 循环播放，Clip 资源待补充） ====================

        /// <summary>大厅 BGM</summary>
        BgmLobby = 401,

        /// <summary>关卡 BGM</summary>
        BgmLevel = 402,
    }
}

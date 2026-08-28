using SuperQQ.Microphone;
using SuperQQ.Network;
using SuperQQ.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 玩家信息面板 — 关卡内 PlayerInfoPanel 的 UI 总控脚本，挂载在 PlayerInfoPanel 上
    /// 面板在 Level1 场景中常驻显示，不做阶段显隐控制。
    ///
    /// 职责规划：
    ///   - VolumeBar：绑定 MicVolumeManager 实时分贝，驱动 Slider Handle 在固定背景条上移动（已实现）
    ///   - PlayerName：本地玩家名称显示（已实现：联机取账户昵称，单机退回场景配置名）
    ///   - PlayerIcon：玩家头像显示（已实现：取本地化身 SelectionIconSprite，按玩家色着色）
    ///
    /// VolumeBar 绑定方式：在 Inspector 中将 VolumeBar 的 Slider 拖入 Volume Slider 字段
    ///   - Slider 仅作展示：Awake 中自动设为不可交互，并固定 min=0 / max=1
    ///   - Handle 位置 = 当前声压级分贝 / 100（0~1，已平滑）
    ///
    /// 数据来源：
    ///   - MicVolumeManager.NormalizedSplDecibels（估算声压级分贝 / 100，0~1，已平滑）
    /// </summary>
    public class PlayerInfoPanel : MonoBehaviour
    {
        [Header("VolumeBar 绑定")]
        [SerializeField] private Slider _volumeSlider;                  // VolumeBar 上的 Slider（Handle 位置表示当前分贝）
        [SerializeField] private GameObject _volumeActiveIcon;          // 麦克风采集中显示的图标
        [SerializeField] private GameObject _volumeInactiveIcon;        // 麦克风未采集时显示的图标

        [Header("VolumeBar 行为")]
        [SerializeField] private float _volumeHandleLerpSpeed = 15f;    // Handle 跟随的平滑速度（越大越跟手）

        [Header("玩家信息")]
        [SerializeField] private TextMeshProUGUI _playerNameText;       // 玩家名称文本（联机显示账户昵称）
        [SerializeField] private Image _playerIconImage;                // 玩家头像（本地化身 SelectionIconSprite + 玩家色）

        private float _volumeDisplayValue;
        private bool _playerNameResolved;                               // 名称是否已解析显示（未成功时每帧重试）
        private bool _playerIconResolved;                               // 头像是否已解析显示（未成功时每帧重试）

        private void Awake()
        {
            if (_volumeSlider != null)
            {
                // 仅作展示：禁止拖动，固定 0~1 区间
                _volumeSlider.interactable = false;
                _volumeSlider.minValue = 0f;
                _volumeSlider.maxValue = 1f;
                _volumeSlider.wholeNumbers = false;
            }
        }

        private void Update()
        {
            UpdateVolumeBar();

            // 名称/头像未解析成功时每帧重试（等待联机数据/本地化身就绪），成功后不再轮询
            if (!_playerNameResolved)
            {
                TryResolvePlayerName();
            }
            if (!_playerIconResolved)
            {
                TryResolvePlayerIcon();
            }
        }

        // ==================== 玩家名称 ====================

        /// <summary>
        /// 解析并显示本地玩家名称：
        /// 联机模式取账户昵称（NetworkManager.JoinedRoom 按 LocalPlayerId 匹配，进房数据跨场景保留）；
        /// 单机或查不到时退回 LevelPlayerRegistry 本地玩家的 PlayerName（场景配置，如 "P1"）
        /// </summary>
        private void TryResolvePlayerName()
        {
            if (_playerNameText == null)
            {
                _playerNameResolved = true;
                return;
            }

            string resolvedName = null;

            // 联机：账户昵称
            NetworkManager net = NetworkManager.Instance;
            if (net != null && !string.IsNullOrEmpty(net.LocalPlayerId) && net.JoinedRoom != null)
            {
                foreach (Minigame.Room.V1.RoomPlayerState p in net.JoinedRoom.Players)
                {
                    if (p.Player != null && p.Player.PlayerId == net.LocalPlayerId
                        && !string.IsNullOrEmpty(p.Player.Nickname))
                    {
                        resolvedName = p.Player.Nickname;
                        break;
                    }
                }
            }

            // 单机/兜底：场景本地玩家的配置名
            if (string.IsNullOrEmpty(resolvedName))
            {
                LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
                if (registry != null)
                {
                    System.Collections.Generic.IReadOnlyList<PlayerController> players = registry.Players;
                    for (int i = 0; i < players.Count; i++)
                    {
                        if (players[i] != null && players[i].BIsLocal)
                        {
                            resolvedName = players[i].PlayerName;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(resolvedName))
            {
                _playerNameText.text = resolvedName;
                _playerNameResolved = true;
            }
        }

        // ==================== 玩家头像 ====================

        /// <summary>
        /// 解析并显示本地玩家头像：
        /// 取 LevelPlayerRegistry 中本地化身的 SelectionIconSprite（未配置时回退光标标识图 → 角色本体 Sprite），
        /// 并按玩家色着色（与选择阶段/结算面板的表现一致）；化身未就绪时每帧重试
        /// </summary>
        private void TryResolvePlayerIcon()
        {
            if (_playerIconImage == null)
            {
                _playerIconResolved = true;
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal)
                {
                    _playerIconImage.sprite = players[i].SelectionIconSprite;
                    _playerIconImage.color = players[i].PlayerColor;
                    _playerIconImage.preserveAspect = true;
                    // 无可用 Sprite 时隐藏 Image，避免显示纯色块
                    _playerIconImage.enabled = _playerIconImage.sprite != null;
                    _playerIconResolved = true;
                    return;
                }
            }
        }

        // ==================== VolumeBar ====================

        private void UpdateVolumeBar()
        {
            MicVolumeManager mic = MicVolumeManager.Instance;
            bool micRunning = mic != null && mic.IsRunning;

            // 切换麦克风状态图标：采集中显示 ActiveIcon，未采集显示 InactiveIcon
            UpdateVolumeIcons(micRunning);

            // 麦克风未采集：Handle 立即归零，不做平滑
            if (!micRunning)
            {
                _volumeDisplayValue = 0f;
                ApplyVolumeValue(0f);
                return;
            }

            // 平滑跟随实时分贝：Handle 位置 = 当前声压级分贝 / 100
            float target = mic.NormalizedSplDecibels;
            _volumeDisplayValue = Mathf.Lerp(_volumeDisplayValue, target, 1f - Mathf.Exp(-_volumeHandleLerpSpeed * Time.unscaledDeltaTime));
            ApplyVolumeValue(_volumeDisplayValue);
        }

        /// <summary>应用音量条数值：驱动 Slider Handle 在固定背景条上移动（0~1）</summary>
        private void ApplyVolumeValue(float value)
        {
            if (_volumeSlider != null)
            {
                _volumeSlider.value = Mathf.Clamp01(value);
            }
        }

        /// <summary>
        /// 切换麦克风状态图标：采集中 ActiveIcon 显示 / InactiveIcon 隐藏，未采集时相反
        /// </summary>
        private void UpdateVolumeIcons(bool micRunning)
        {
            if (_volumeActiveIcon != null && _volumeActiveIcon.activeSelf != micRunning)
            {
                _volumeActiveIcon.SetActive(micRunning);
            }
            if (_volumeInactiveIcon != null && _volumeInactiveIcon.activeSelf == micRunning)
            {
                _volumeInactiveIcon.SetActive(!micRunning);
            }
        }
    }
}

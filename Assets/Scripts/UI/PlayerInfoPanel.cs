using SuperQQ.Microphone;
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
    ///   - PlayerName / PlayerIcon：玩家名称与头像显示（待实现）
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

        [Header("玩家信息（待实现）")]
        [SerializeField] private TextMeshProUGUI _playerNameText;       // 预留：玩家名称文本
        [SerializeField] private Image _playerIconImage;                // 预留：玩家头像

        private float _volumeDisplayValue;

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

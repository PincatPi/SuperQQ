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
    ///   - VolumeBar：绑定 MicVolumeManager 实时分贝，驱动音量条填充（已实现）
    ///   - ScoreBar：玩家得分条控制（待实现）
    ///   - PlayerName / PlayerIcon：玩家名称与头像显示（待实现）
    ///
    /// VolumeBar 绑定方式：在 Inspector 中将 VolumeBarFill（Image）拖入 Volume Fill 字段
    ///   - Image 为 Filled 类型时：直接驱动 fillAmount（推荐，Image Type 设为 Filled / Horizontal / Left）
    ///   - 否则自动退化为按 X 轴缩放 RectTransform（需保证 pivot.x = 0，从左向右增长）
    ///
    /// 数据来源：
    ///   - 填充比例使用 MicVolumeManager.NormalizedPositiveDecibels（当前正值分贝 / 120，0~1，已平滑）
    ///   - 正值分贝使用 MicVolumeManager.PositiveDecibels（0 起，满量程 120）
    /// </summary>
    public class PlayerInfoPanel : MonoBehaviour
    {
        [Header("VolumeBar 绑定")]
        [SerializeField] private Image _volumeFillImage;                // VolumeBarFill 上的 Image
        [SerializeField] private TextMeshProUGUI _volumeDecibelText;    // 可选：显示正值分贝的文本
        [SerializeField] private GameObject _volumeActiveIcon;          // 麦克风采集中显示的图标
        [SerializeField] private GameObject _volumeInactiveIcon;        // 麦克风未采集时显示的图标

        [Header("VolumeBar 行为")]
        [SerializeField] private float _volumeFillLerpSpeed = 15f;      // 填充跟随的平滑速度（越大越跟手）

        [Header("ScoreBar（待实现）")]
        [SerializeField] private Image _scoreFillImage;                 // 预留：ScoreBarFill 上的 Image

        [Header("玩家信息（待实现）")]
        [SerializeField] private TextMeshProUGUI _playerNameText;       // 预留：玩家名称文本
        [SerializeField] private Image _playerIconImage;                // 预留：玩家头像

        private RectTransform _volumeFillRect;
        private bool _volumeUseFillAmount;
        private float _volumeDisplayFill;

        private void Awake()
        {
            if (_volumeFillImage != null)
            {
                _volumeFillRect = _volumeFillImage.rectTransform;
                _volumeUseFillAmount = _volumeFillImage.type == Image.Type.Filled;
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

            // 麦克风未采集：立即归零，不做平滑
            if (!micRunning)
            {
                _volumeDisplayFill = 0f;
                ApplyVolumeFill(0f, 1f);
                UpdateVolumeText(0f);
                return;
            }

            // 平滑跟随实时分贝：填充比例 = 当前正值分贝 / 120
            float target = mic.NormalizedPositiveDecibels;
            _volumeDisplayFill = Mathf.Lerp(_volumeDisplayFill, target, 1f - Mathf.Exp(-_volumeFillLerpSpeed * Time.unscaledDeltaTime));
            ApplyVolumeFill(_volumeDisplayFill, _volumeDisplayFill);
            UpdateVolumeText(mic.PositiveDecibels);
        }

        /// <summary>
        /// 应用音量条填充比例：Filled Image 用 fillAmount，否则用 X 轴缩放
        /// </summary>
        private void ApplyVolumeFill(float fillAmountValue, float scaleValue)
        {
            if (_volumeFillImage == null)
            {
                return;
            }

            if (_volumeUseFillAmount)
            {
                _volumeFillImage.fillAmount = fillAmountValue;
            }
            else if (_volumeFillRect != null)
            {
                Vector3 scale = _volumeFillRect.localScale;
                scale.x = Mathf.Clamp01(scaleValue);
                _volumeFillRect.localScale = scale;
            }
        }

        private void UpdateVolumeText(float positiveDb)
        {
            if (_volumeDecibelText != null)
            {
                _volumeDecibelText.text = $"{positiveDb:F0} dB";
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

using SuperQQ.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Event
{
    /// <summary>
    /// 解冻进度条 — 挂在解冻进度条弹窗 Prefab 的根节点上
    /// 作为 PopupView 子类经 PopupManager 播放（PopupType.ThawProgress，手动关闭）
    /// 对外仅暴露 SetProgress，由事件逻辑驱动；自身不含任何事件/玩法逻辑
    /// </summary>
    public class ThawProgressBar : PopupView
    {
        [Tooltip("填充图像（Image Type 需设为 Filled），按解冻进度更新 fillAmount")]
        [SerializeField] private Image _fillImage;

        [Tooltip("距画布底部的距离（像素）：显示时进度条强制归位到画布正下方居中，与 Prefab 锚点配置解耦")]
        [Min(0f)]
        [SerializeField] private float _bottomOffset = 40f;

        private void Awake()
        {
            NormalizeFillImage();
            SnapToBottomCenter();
        }

        /// <summary>
        /// 归一化填充图：SetProgress 的驱动契约是 fillAmount（0~1），
        /// 要求 Image 为 Filled/Horizontal/Left 且初始为空。
        /// Prefab 误配（如 Sliced/Tiled，或 FillMethod 为 Radial）时 fillAmount 不生效、
        /// 进度条静止不动，此处强制纠正——任何替换的 UI Prefab 只要绑定了填充图即可正常工作
        /// </summary>
        private void NormalizeFillImage()
        {
            if (_fillImage == null)
            {
                return;
            }

            _fillImage.type = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillImage.fillAmount = 0f;
        }

        /// <summary>
        /// 设置解冻进度（0~1，自动夹紧）
        /// </summary>
        public void SetProgress(float progress)
        {
            if (_fillImage != null)
            {
                _fillImage.fillAmount = Mathf.Clamp01(progress);
            }
        }

        /// <summary>
        /// 将进度条强制归位到画布正下方居中：
        /// PopupManager 实例化弹窗时保留 Prefab 原始 RectTransform 设置（不做定位），
        /// 位置统一在此修正——无论 Prefab 锚点如何配置都保证出现在画布正下方，且保持 Prefab 原始尺寸
        /// </summary>
        private void SnapToBottomCenter()
        {
            if (transform is not RectTransform rect)
            {
                return;
            }

            Vector2 size = rect.rect.size; // 先取当前渲染尺寸，防止锚点修改后变形
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, _bottomOffset);
        }
    }
}

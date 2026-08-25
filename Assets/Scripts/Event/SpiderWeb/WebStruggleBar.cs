using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Event
{
    /// <summary>
    /// 蛛网挣脱进度条 — 挂在挣脱进度条 UI Prefab 的根节点上（屏幕空间 UI）
    /// 由事件实例化到主 Canvas 下；对外仅暴露进度与提示设置接口，自身不含任何事件逻辑
    /// </summary>
    public class WebStruggleBar : MonoBehaviour
    {
        [Tooltip("填充图像（Image Type 需设为 Filled），按挣脱进度更新 fillAmount")]
        [SerializeField] private Image _fillImage;

        [Tooltip("单指提示文本（可选）：检测到单指滑动时显示，提示需要双指操作；未配置则无提示")]
        [SerializeField] private TMP_Text _hintText;

        /// <summary>
        /// 设置挣脱进度（0~1，自动夹紧）
        /// </summary>
        public void SetProgress(float progress)
        {
            if (_fillImage != null)
            {
                _fillImage.fillAmount = Mathf.Clamp01(progress);
            }
        }

        /// <summary>
        /// 显示/隐藏单指提示（提示内容由 Prefab 文本自身配置）
        /// </summary>
        public void SetHintVisible(bool visible)
        {
            if (_hintText != null && _hintText.gameObject.activeSelf != visible)
            {
                _hintText.gameObject.SetActive(visible);
            }
        }
    }
}

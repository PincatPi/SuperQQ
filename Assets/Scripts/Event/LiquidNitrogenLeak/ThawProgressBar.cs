using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Event
{
    /// <summary>
    /// 解冻进度条 — 挂在解冻进度条弹窗 Prefab 的根节点上
    /// 对外仅暴露 SetProgress，由事件逻辑驱动；自身不含任何事件/玩法逻辑
    /// </summary>
    public class ThawProgressBar : MonoBehaviour
    {
        [Tooltip("填充图像（Image Type 需设为 Filled），按解冻进度更新 fillAmount")]
        [SerializeField] private Image _fillImage;

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
    }
}

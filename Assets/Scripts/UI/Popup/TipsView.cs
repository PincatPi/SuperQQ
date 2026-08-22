using TMPro;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 提示（Tips）视图 — 挂在 Tips Prefab 的根节点上
    /// Tips 结构简单：仅承载一段提示文本，由 PopupManager.ShowTips 实例化播放，
    /// 固定时长后自动关闭销毁（不支持手动关闭，无标题/图片/关闭按钮等额外绑定）
    /// 配置了背景时，SetContent 后按文本实际宽度自动调整背景宽度（背景锚点建议居中）
    /// </summary>
    public class TipsView : MonoBehaviour
    {
        [Tooltip("Tips 文本组件；未配置时回退查找子级 TMP_Text")]
        [SerializeField] private TMP_Text _contentLabel;

        [Header("背景自适应")]
        [Tooltip("背景 RectTransform：宽度随文本内容动态调整；留空则不调整")]
        [SerializeField] private RectTransform _background;

        [Tooltip("背景宽度相对文本宽度的额外留白（左右内边距之和）")]
        [Min(0f)]
        [SerializeField] private float _horizontalPadding = 60f;

        private void Awake()
        {
            if (_contentLabel == null)
            {
                _contentLabel = GetComponentInChildren<TMP_Text>();
            }
            if (_contentLabel == null)
            {
                Debug.LogWarning("[TipsView] 未找到 TMP 文本组件，SetContent 将不生效。", this);
            }
        }

        /// <summary>
        /// 设置提示文本内容（由 PopupManager.ShowTips 调用），并同步调整背景宽度
        /// </summary>
        /// <param name="content">提示文本；为 null 时显示为空</param>
        public void SetContent(string content)
        {
            if (_contentLabel == null)
            {
                return;
            }

            _contentLabel.text = content ?? string.Empty;
            RefreshBackgroundWidth();
        }

        /// <summary>
        /// 按文本实际宽度刷新背景宽度：背景宽 = 文本无约束首选宽度 + 内边距
        /// GetPreferredValues 基于字体度量计算，无需等待 Canvas 渲染，实例化当帧即可生效
        /// </summary>
        private void RefreshBackgroundWidth()
        {
            if (_background == null)
            {
                return;
            }

            float textWidth = _contentLabel.GetPreferredValues(_contentLabel.text, float.MaxValue, float.MaxValue).x;
            _background.sizeDelta = new Vector2(textWidth + _horizontalPadding, _background.sizeDelta.y);
        }
    }
}

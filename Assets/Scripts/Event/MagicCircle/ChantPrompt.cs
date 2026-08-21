using TMPro;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 吟唱提示框 — 挂在言出法随事件提示 Text 框 Prefab 的根节点上
    /// 对外仅暴露 SetText，由事件逻辑在实例化后设置提示内容；自身不含任何事件逻辑
    /// </summary>
    public class ChantPrompt : MonoBehaviour
    {
        [Tooltip("显示提示文字的 TMP 组件（UGUI 或 3D TextMeshPro 均可）")]
        [SerializeField] private TMP_Text _text;

        /// <summary>
        /// 设置提示文字内容
        /// </summary>
        /// <param name="content">提示文字</param>
        public void SetText(string content)
        {
            if (_text != null)
            {
                _text.text = content;
            }
        }
    }
}

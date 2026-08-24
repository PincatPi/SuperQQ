using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 【临时测试】Tips 触发器 — 挂在场景中任意 GameObject 上
    /// 支持两种触发方式：点击绑定的按钮（Inspector 指派），或按下指定按键
    /// 触发后调用 PopupManager.ShowTips 弹出一条 Tips，文本与时长可在 Inspector 配置
    /// 仅用于测试，正式版本删除本脚本及场景中的挂载对象
    /// </summary>
    public class TipsTestTrigger : MonoBehaviour
    {
        [SerializeField] private KeyCode _triggerKey = KeyCode.T;
        [SerializeField] private TipsType _tipsType = TipsType.Common;
        [SerializeField] private string _content = "这是一条测试 Tips";
        [SerializeField] private float _duration = -1f;

        [Tooltip("点击后弹出 Tips 的按钮；留空则仅响应按键")]
        [SerializeField] private Button _testButton;

        private void Awake()
        {
            if (_testButton != null)
            {
                _testButton.onClick.AddListener(ShowTips);
            }
        }

        private void OnDestroy()
        {
            if (_testButton != null)
            {
                _testButton.onClick.RemoveListener(ShowTips);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_triggerKey))
            {
                ShowTips();
            }
        }

        private void ShowTips()
        {
            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[TipsTestTrigger] PopupManager 不存在。");
                return;
            }
            PopupManager.Instance.ShowTips(_tipsType, _content, _duration);
        }
    }
}

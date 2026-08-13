using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI.PlacementTest
{
    /// <summary>
    /// 道具槽位视图 — 开发期测试专用
    /// 挂在道具面板中每个槽位按钮上：点击时通知控制器选中对应道具进入摆放
    /// （也可以不用本组件，直接把 Button.onClick 绑定到控制器的 SelectItem）
    /// </summary>
    public class ItemPlacementTestSlotView : MonoBehaviour
    {
        [Tooltip("场景中的测试控制器")]
        [SerializeField] private ItemPlacementTestController controller;
        [Tooltip("对应控制器道具清单的下标（从 0 开始）")]
        [SerializeField] private int slotIndex;
        [Tooltip("槽位按钮；留空则取本物体上的 Button")]
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }
            if (button != null)
            {
                button.onClick.AddListener(OnClick);
            }
        }

        private void OnClick()
        {
            if (controller != null)
            {
                controller.SelectItem(slotIndex);
            }
        }
    }
}

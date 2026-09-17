using System.Collections.Generic;
using SuperQQ.Item;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 道具描述面板（PropSelectionPanelNew / ItemDescription 节点的逻辑脚本）。
    /// 本地玩家点击道具格时展示该道具的名称 / 类型标签 / 描述，纯本地表现，
    /// 与演示视频气泡（SlotIntroVideoPlayer）相互独立、可同时存在；
    /// 已选定道具后仍可点击其它道具查看信息。
    ///
    /// 按子物体命名自动接线：ItemName / ItemDescriptionText / ItemTag（标签底图）/ ItemTagText（标签文字）
    /// </summary>
    public class ItemDescriptionPanel : MonoBehaviour
    {
        [System.Serializable]
        public class ItemTypeColor
        {
            public ItemType type;
            public Color color = Color.white;
        }

        [Header("类型标签颜色（按道具类型在此配置，无需改道具）")]
        [SerializeField] private List<ItemTypeColor> typeColors = new List<ItemTypeColor>
        {
            new ItemTypeColor { type = ItemType.Bridge, color = new Color(0.35f, 0.75f, 0.95f) },
            new ItemTypeColor { type = ItemType.Damage, color = new Color(0.95f, 0.35f, 0.30f) },
            new ItemTypeColor { type = ItemType.Remove, color = new Color(0.60f, 0.60f, 0.65f) },
            new ItemTypeColor { type = ItemType.Score,  color = new Color(0.98f, 0.80f, 0.25f) },
            new ItemTypeColor { type = ItemType.Control, color = new Color(0.70f, 0.50f, 0.95f) },
        };

        [Header("接线（留空则按子物体名自动查找）")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private Image tagBackground;
        [SerializeField] private TextMeshProUGUI tagText;
        [Tooltip("确认选择按钮（美术节点 ConfirmSelectionBtn）；点击后向 Director 发起认领确认")]
        [SerializeField] private Button confirmButton;

        /// <summary>确认选择按钮点击回调（由 PropSelectionDirector 注册，执行认领确认逻辑）</summary>
        public event System.Action ConfirmClicked;

        /// <summary>是否已接线确认选择按钮</summary>
        public bool HasConfirmButton => confirmButton != null;

        private static ItemDescriptionPanel instance;

        public static ItemDescriptionPanel Instance => instance;

        private void Awake()
        {
            instance = this;
            AutoWireRefs();
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }
        }

        /// <summary>确认选择按钮点击：转发给 Director（未注册时忽略）</summary>
        private void HandleConfirmClicked()
        {
            ConfirmClicked?.Invoke();
        }

        /// <summary>设置确认选择按钮显示/隐藏（仅对待确认的道具显示）</summary>
        public void SetConfirmVisible(bool bVisible)
        {
            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(bVisible);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>展示指定道具的信息（点击道具时调用）</summary>
        public static void Show(ItemBase item)
        {
            if (instance == null || item == null)
            {
                return;
            }
            instance.ShowInternal(item);
        }

        /// <summary>隐藏描述面板</summary>
        public static void Hide()
        {
            if (instance != null)
            {
                instance.gameObject.SetActive(false);
            }
        }

        private void ShowInternal(ItemBase item)
        {
            gameObject.SetActive(true);

            if (nameText != null)
            {
                nameText.text = item.DisplayName;
            }
            if (descriptionText != null)
            {
                descriptionText.text = item.Description;
            }
            if (tagText != null)
            {
                tagText.text = TypeDisplayName(item.ItemType);
            }
            if (tagBackground != null)
            {
                tagBackground.color = ResolveTypeColor(item.ItemType);
            }
        }

        private static string TypeDisplayName(ItemType type)
        {
            switch (type)
            {
                case ItemType.Bridge: return "搭路";
                case ItemType.Damage: return "伤害";
                case ItemType.Remove: return "消除";
                case ItemType.Score: return "得分";
                case ItemType.Control: return "控制";
                default: return type.ToString();
            }
        }

        private Color ResolveTypeColor(ItemType type)
        {
            foreach (ItemTypeColor entry in typeColors)
            {
                if (entry != null && entry.type == type)
                {
                    return entry.color;
                }
            }
            return Color.white;
        }

        /// <summary>按命名约定自动接线（prefab 内节点：ItemName / ItemDescriptionText / ItemTag / ItemTagText）</summary>
        private void AutoWireRefs()
        {
            if (nameText == null)
            {
                nameText = FindChildComponent<TextMeshProUGUI>("ItemName");
            }
            if (descriptionText == null)
            {
                descriptionText = FindChildComponent<TextMeshProUGUI>("ItemDescriptionText");
            }
            if (tagBackground == null)
            {
                tagBackground = FindChildComponent<Image>("ItemTag");
            }
            if (tagText == null)
            {
                tagText = FindChildComponent<TextMeshProUGUI>("ItemTagText");
            }
            if (confirmButton == null)
            {
                confirmButton = FindChildComponent<Button>("ConfirmSelectionBtn");
            }
        }

        /// <summary>在直接/深层子物体中按名查找组件（含未激活）</summary>
        private T FindChildComponent<T>(string childName) where T : Component
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                child = FindDeepChild(transform, childName);
            }
            return child != null ? child.GetComponent<T>() : null;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                Transform found = FindDeepChild(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}

using SuperQQ.Item;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.Selection.Runtime
{
    /// <summary>
    /// 道具选择槽位视图：展示候选道具的图标，响应点击并向 Director 发起选中请求。
    /// 被认领后显示认领者颜色标记并关闭点击；本地玩家完成认领后全部槽位对本地锁定。
    ///
    /// 无需在 Inspector 拖拽任何引用，Awake 按以下子物体命名约定自动识别（均可选）：
    ///   ItemIcon     （Image，道具图标；缺失时兜底取第一个非根物体上的 Image）
    ///   ClaimMarker  （Image，认领者颜色标记，未认领时自动隐藏）
    ///   SizeBadge    （TextMeshProUGUI，历史遗留的旋转吐司尺寸角标，已停用：
    ///                 固定 3x3 后不再展示格数说明，运行时始终隐藏）
    ///   SelectedIcon （被认领后激活显示的选中图标，未认领时隐藏）
    ///   ClaimerIcon  （Image，认领该槽位玩家的 PlayerIcon，未认领时隐藏）
    /// 点击入口取本物体上的 Button。
    /// </summary>
    public class PropSelectionSlotView : MonoBehaviour
    {
        [Header("认领表现")]
        [Tooltip("槽位被认领后激活显示的图标；留空时按 SelectedIcon 子物体命名约定自动识别")]
        [SerializeField] private GameObject selectedIcon;
        [Tooltip("显示认领该槽位玩家头像的 Image；留空时按 ClaimerIcon 子物体命名约定自动识别")]
        [SerializeField] private Image claimerIconImage;

        private Button button;
        private Image iconImage;
        private Image claimMarker;
        private TMPro.TextMeshProUGUI sizeBadge;

        private PropSelectionDirector owner;
        private int slotIndex = -1;

        /// <summary>本槽位对应的候选下标</summary>
        public int SlotIndex => slotIndex;

        private void Awake()
        {
            AutoWireRefs();

            if (button != null)
            {
                button.onClick.AddListener(HandleClick);
            }
            if (claimMarker != null)
            {
                claimMarker.gameObject.SetActive(false);
            }
            if (sizeBadge != null)
            {
                sizeBadge.gameObject.SetActive(false);
            }
            if (selectedIcon != null)
            {
                selectedIcon.SetActive(false);
            }
            if (claimerIconImage != null)
            {
                claimerIconImage.gameObject.SetActive(false);
            }
        }

        /// <summary>绑定槽位数据与点击归属（由 Director 在生成槽位时调用）；图标直接取自 ItemBase</summary>
        public void Bind(PropSelectionDirector ownerDirector, int index, ItemBase item)
        {
            owner = ownerDirector;
            slotIndex = index;

            Sprite icon = item != null ? item.Icon : null;
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.enabled = icon != null;
                // 单道具图标缩放（风扇等宽幅道具可缩小显示，避免撑满槽位）
                float iconScale = item != null ? item.IconScale : 1f;
                iconImage.rectTransform.localScale = new Vector3(iconScale, iconScale, 1f);
            }

            UpdateSizeBadge(item);
        }

        /// <summary>
        /// 占地格数角标：已停用（吐司固定 3x3 后不再展示格数说明），始终隐藏
        /// </summary>
        private void UpdateSizeBadge(ItemBase item)
        {
            if (sizeBadge != null)
            {
                sizeBadge.gameObject.SetActive(false);
            }
        }

        /// <summary>标记该槽位已被认领：显示认领者颜色标记与选中图标、认领者头像，关闭点击</summary>
        /// <param name="claimerColor">认领者的玩家颜色</param>
        /// <param name="claimerIcon">认领者的 PlayerIcon（选择阶段标识图），可为 null</param>
        public void SetClaimed(Color claimerColor, Sprite claimerIcon = null)
        {
            if (claimMarker != null)
            {
                claimMarker.color = claimerColor;
                claimMarker.gameObject.SetActive(true);
            }
            if (selectedIcon != null)
            {
                selectedIcon.SetActive(true);
            }
            if (claimerIconImage != null)
            {
                claimerIconImage.sprite = claimerIcon;
                claimerIconImage.color = Color.white;   // 头像不染座位色，保留原始配色（claimMarker 色块仍标座位色）
                claimerIconImage.preserveAspect = true;
                claimerIconImage.gameObject.SetActive(true);
            }
            if (button != null)
            {
                button.interactable = false;
            }
        }

        /// <summary>锁定/解锁本地点击（本地玩家完成认领后锁定全部槽位）</summary>
        public void SetLocalInputLocked(bool bLocked)
        {
            if (button != null)
            {
                button.interactable = !bLocked;
            }
        }

        // ==================== 引用自动识别 ====================

        private void AutoWireRefs()
        {
            button = GetComponent<Button>();

            iconImage = FindChildComponent<Image>("ItemIcon");
            if (iconImage == null)
            {
                // 兜底：取第一个非根物体上的 Image（根物体上的通常是槽位背景）
                Image[] images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i].gameObject != gameObject)
                    {
                        iconImage = images[i];
                        break;
                    }
                }
            }

            claimMarker = FindChildComponent<Image>("ClaimMarker");
            sizeBadge = FindChildComponent<TMPro.TextMeshProUGUI>("SizeBadge");

            // SelectedIcon 未手动拖拽时按命名约定兜底识别
            if (selectedIcon == null)
            {
                Transform icon = FindDeepChild(transform, "SelectedIcon");
                if (icon != null)
                {
                    selectedIcon = icon.gameObject;
                }
            }

            // ClaimerIcon 未手动拖拽时按命名约定兜底识别
            if (claimerIconImage == null)
            {
                claimerIconImage = FindChildComponent<Image>("ClaimerIcon");
            }
        }

        /// <summary>在子层级中按名字递归查找物体并取其组件；未找到返回 null</summary>
        private T FindChildComponent<T>(string childName) where T : Component
        {
            Transform child = FindDeepChild(transform, childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindDeepChild(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void HandleClick()
        {
            if (owner != null && slotIndex >= 0)
            {
                owner.TrySelectLocal(slotIndex);
            }
        }
    }
}

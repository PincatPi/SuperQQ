using System.Collections.Generic;
using SuperQQ.Grid;
using SuperQQ.Item;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SuperQQ.UI.PlacementTest
{
    /// <summary>
    /// 道具摆放测试控制器 — 开发期测试专用，接入正式流程后可整体删除本目录
    ///
    /// 交互流程：
    ///   P 键        进入/退出摆放测试模式（进入：弹出道具面板 + 显示网格；退出：取消未确认的摆放）
    ///   点击道具按钮  生成实例并自动跟随鼠标（无需长按左键），吸附网格并显示落点合法性（绿/红虚线框）
    ///   鼠标左键     确认当前道具放置：落点合规则登记占据格子并锁定位置；
    ///               落点不合法则保持摆放状态，调整后再点击
    ///   R / Esc     旋转 90° / 取消本次摆放
    ///
    /// 测试道具数量无限，可重复选中放置；已确认的道具位置固定，不可再被选中/拖动。
    ///
    /// 实现说明：摆放中实例的 PlacementController 组件被禁用（屏蔽其长按拖拽输入），
    /// 跟随鼠标/吸附/合法性提示由本控制器驱动，确认时调用其 CompletePlacement 完成登记。
    /// 衔接摆放：确认后若道具实现了 IChainedPlacement 并返回下一件（如传送门的出口），
    /// 本控制器直接接管其摆放，入口→出口两次摆放不中断
    ///
    /// Editor 搭建步骤：
    ///   1. Level1 场景新建空物体挂载本组件，拖入道具面板 GameObject，配置道具清单
    ///      （拖入挂有 ItemBase 的道具 prefab）
    ///   2. 面板中每个道具按钮挂 ItemPlacementTestSlotView 并填写 slotIndex（对应清单下标），
    ///      或直接把 Button 的 onClick 绑定到本组件的 SelectItem
    ///   3. 左键确认 / R 旋转 / Esc 取消由本控制器处理，无需额外接线；
    ///      清空按钮可绑定 ClearConfirmed
    ///
    /// 注意：拆除类道具（炸弹）确认后立即引爆、清除自身 footprint 覆盖格子内的道具并销毁，
    /// 属其自身预期行为；叠放目标与免登记占据由 DemolitionItemBase 的策略属性自动声明，无需额外配置
    /// </summary>
    public class ItemPlacementTestController : MonoBehaviour
    {
        [Header("道具面板")]
        [Tooltip("P 键切换显隐的道具面板（SetActive）")]
        [SerializeField] private GameObject itemPanel;

        [Header("道具清单（测试数量无限）")]
        [Tooltip("挂有 ItemBase 的道具 prefab")]
        [SerializeField] private List<ItemBase> items = new List<ItemBase>();

        [Header("提示颜色")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.3f, 0.9f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        private bool modeOn;                            // 是否处于摆放测试模式
        private GameObject current;                     // 正在摆放的实例（未确认）
        private PlacementController currentPc;
        private FootprintBoxView currentBox;
        private ItemBase currentItem;
        private int selectedIndex = -1;                 // 当前选中的清单下标（-1=未选中）
        private int selectFrame = -1;                   // 选中发生的帧号（避免点击 UI 按钮同帧误触发确认）
        private readonly List<GameObject> confirmed = new List<GameObject>();   // 已确认的实例（清空用）

        // ==================== 输入 ====================

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                ToggleMode();
            }

            if (current == null)
            {
                return;
            }

            FollowMouse();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelCurrent();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                currentPc.ToggleRotate();
                RefreshValidityHint();   // ToggleRotate 重建虚线框会重置颜色，立即刷回合法性提示色
            }
            // 左键确认：点击 UI 按钮的同帧 / 指针悬停在 UI 上时不触发
            if (Input.GetMouseButtonDown(0)
                && Time.frameCount != selectFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                ConfirmCurrent();
            }
        }

        // ==================== 模式切换 ====================

        /// <summary>进入/退出摆放测试模式（P 键，也可绑按钮）</summary>
        public void ToggleMode()
        {
            modeOn = !modeOn;
            if (itemPanel != null)
            {
                itemPanel.SetActive(modeOn);
            }

            GridManager grid = GridManager.Instance;
            if (modeOn)
            {
                if (grid != null)
                {
                    grid.ShowGrid();
                }
                Debug.Log("[ItemPlacementTest] 进入摆放测试模式");
            }
            else
            {
                CancelCurrent();
                if (grid != null)
                {
                    grid.HideGrid();
                }
                Debug.Log("[ItemPlacementTest] 退出摆放测试模式");
            }
        }

        // ==================== 选中 / 确认 / 取消 ====================

        /// <summary>选中清单中的道具开始摆放（槽位按钮点击入口，可被 onClick 直绑）</summary>
        public void SelectItem(int index)
        {
            if (!modeOn || index < 0 || index >= items.Count)
            {
                return;
            }
            if (index == selectedIndex && current != null)
            {
                return;   // 重复选中同一道具，无视
            }
            ItemBase prefab = items[index];
            if (prefab == null)
            {
                Debug.LogWarning($"[ItemPlacementTest] 清单第 {index} 项未配置道具预制体");
                return;
            }

            CancelCurrent();   // 有未确认的实例先取消

            if (GridManager.Instance == null)
            {
                Debug.LogError("[ItemPlacementTest] 场景中缺少 GridManager，无法摆放");
                return;
            }

            // 生成在鼠标当前位置并接管其摆放，随后由 FollowMouse 驱动跟随与吸附
            AdoptForPlacement(Instantiate(prefab.gameObject, MouseWorldPos(), Quaternion.identity));

            selectedIndex = index;
            selectFrame = Time.frameCount;
            Debug.Log($"[ItemPlacementTest] 选中 {prefab.name}，进入摆放");
        }

        /// <summary>
        /// 接管一个待摆放实例：禁用其 PlacementController 的长按拖拽输入，
        /// 跟随/确认/旋转由本控制器驱动；确认前该组件保持禁用，道具位置自然锁定不可再动
        /// </summary>
        private void AdoptForPlacement(GameObject go)
        {
            current = go;
            currentPc = go.GetComponent<PlacementController>();
            if (currentPc == null)
            {
                // 拆除类等未挂吸附组件的 prefab 运行时补挂（RequireComponent 自动补齐 FootprintBoxView/Collider2D）
                currentPc = go.AddComponent<PlacementController>();
            }
            currentPc.enabled = false;
            // 关闭自带调试热键：P 键与本测试模式的开关键冲突，
            // 若不关闭，组件一旦被重新启用，按 P 会触发 EnterDraggableState 把已确认道具重新虚化
            currentPc.DebugHotkeys = false;
            currentPc.GhostOn();

            currentBox = go.GetComponent<FootprintBoxView>();
            if (currentBox != null)
            {
                currentBox.Init(currentBox.Footprint, false);
                currentBox.Show();
            }
            currentItem = go.GetComponent<ItemBase>();
        }

        // ==================== 跟随鼠标 ====================

        /// <summary>道具跟随鼠标并吸附到最近格子，刷新落点合法性提示色</summary>
        private void FollowMouse()
        {
            current.transform.position = MouseWorldPos();
            currentPc.SnapToNearestCell();
            RefreshValidityHint();
        }

        /// <summary>按当前位置计算左下角锚点格子并校验合法性，刷新虚线框颜色</summary>
        private void RefreshValidityHint()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || currentBox == null)
            {
                return;
            }

            // 与 PlacementController.AnchorFromRootPos 相同的换算：
            // 根节点（框中心）世界坐标 -> 左下角锚点格子
            bool rotated = currentPc.IsRotated;
            Vector2Int footprint = currentBox.Footprint;
            Vector2Int size = rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;
            Vector2 local = ((Vector2)current.transform.position - grid.PublicOrigin) / grid.PublicCellSize;
            Vector2Int anchor = new Vector2Int(
                Mathf.RoundToInt(local.x - size.x * 0.5f),
                Mathf.RoundToInt(local.y - size.y * 0.5f));

            // 叠放许可由道具自身声明（拆除类允许落在被占据格子上）
            bool allowOverlap = currentItem != null && currentItem.AllowsOccupiedOverlap;
            bool valid = grid.CanOccupy(anchor, footprint, rotated, allowOverlap);
            currentBox.SetColor(valid ? validColor : invalidColor);
        }

        /// <summary>鼠标当前的世界坐标（2D 平面）</summary>
        private static Vector2 MouseWorldPos()
        {
            Camera cam = Camera.main;
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x, Input.mousePosition.y, -cam.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        /// <summary>确认当前道具放置（鼠标左键）：合规则锁定位置，不合规则继续摆放</summary>
        public void ConfirmCurrent()
        {
            if (current == null || currentPc == null)
            {
                return;
            }

            currentPc.CompletePlacement();
            if (!currentPc.IsPlacementValid)
            {
                // CompletePlacement 会关闭虚化，确认失败时恢复，保持"摆放中"的视觉提示
                currentPc.GhostOn();
                Debug.LogWarning("[ItemPlacementTest] 当前落点不合法，请移动到合法位置后再确认");
                return;
            }

            // PlacementController 自生成起已禁用，确认后位置即固定，不可再被选中/拖动
            confirmed.Add(current);
            Debug.Log($"[ItemPlacementTest] {items[selectedIndex].name} 已确认放置");

            // 衔接摆放：实现 IChainedPlacement 的道具（如传送门出口）直接接管，摆放流程不中断
            GameObject chained = current.TryGetComponent(out IChainedPlacement chainProvider)
                ? chainProvider.SpawnChainedItem()
                : null;
            if (chained != null)
            {
                AdoptForPlacement(chained);
                selectFrame = Time.frameCount;   // 确认本次的点击不连带确认衔接道具
                Debug.Log("[ItemPlacementTest] 衔接摆放下一件道具");
                return;
            }

            current = null;
            currentPc = null;
            currentBox = null;
            currentItem = null;
            selectedIndex = -1;
        }

        /// <summary>清空本次测试已确认的全部道具（不动关卡初始物体），可绑"清空"按钮</summary>
        public void ClearConfirmed()
        {
            GridManager grid = GridManager.Instance;
            foreach (GameObject go in confirmed)
            {
                if (go == null)
                {
                    continue;
                }
                PlacedItem placed = go.GetComponent<PlacedItem>();
                // 仅当占据表中登记的确实是该实例时才走 RemoveAt 释放格子
                // （拆除类不登记自身占据，按锚点直接 RemoveAt 会误删同格的其它道具）
                if (placed != null && grid != null && grid.GetItemAt(placed.AnchorCell) == placed)
                {
                    grid.RemoveAt(placed.AnchorCell);   // 释放格子并销毁
                }
                else
                {
                    Destroy(go);
                }
            }
            confirmed.Clear();
            Debug.Log("[ItemPlacementTest] 已清空本次测试放置的道具");
        }

        /// <summary>取消当前未确认的摆放：已登记占据的走 RemoveAt 释放格子，未登记直接销毁</summary>
        private void CancelCurrent()
        {
            if (current == null)
            {
                return;
            }
            PlacedItem placed = current.GetComponent<PlacedItem>();
            GridManager grid = GridManager.Instance;
            // 仅当占据表中登记的确实是本实例时才走 RemoveAt 释放格子（原因同 ClearConfirmed）
            if (placed != null && grid != null && grid.GetItemAt(placed.AnchorCell) == placed)
            {
                grid.RemoveAt(placed.AnchorCell);
            }
            else
            {
                Destroy(current);
            }
            current = null;
            currentPc = null;
            currentBox = null;
            currentItem = null;
            selectedIndex = -1;

            // 配对强制约束：取消出口摆放后，落单的入口一并清除（对已配对的传送门无影响）
            Portal.DestroyAllUnpaired();
        }
    }
}

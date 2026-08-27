using System;
using System.Collections.Generic;
using SuperQQ.Grid;
using SuperQQ.Item;
using UnityEngine;

namespace SuperQQ.Placement.Core
{
    /// <summary>
    /// 单名玩家的放置会话（纯 C#，由场景层每帧驱动）。
    /// 每名玩家每轮仅持有一件待放置道具：进入阶段时由外部 <see cref="Deal"/> 发放，
    /// <see cref="BeginPlace"/> 取出后开始跟随指针摆放，确认落点合法后彻底锁定；
    /// 取消（Esc）会把道具退回待放置状态，由外部重新取出。
    /// 不读取任何输入、不持有 Unity 生命周期，指针位置由外部通过 <see cref="UpdatePointer"/> 喂入。
    /// </summary>
    public class PlacementSession
    {
        private const string LOG_TAG = "[PropPlacement]";

        private readonly string playerKey;
        private readonly Color validColor;
        private readonly Color invalidColor;

        private ItemBase pendingPrefab;         // 待放置道具（已发放、未取出摆放）
        private ItemBase currentPrefab;         // 摆放中道具的来源 prefab（取消时退回 pendingPrefab）

        // 当前未确认的摆放实例及其组件缓存
        private GameObject current;
        private PlacementController currentPc;
        private FootprintBoxView currentBox;
        private string currentItemId = string.Empty;

        /// <summary>一次放置确认成功时触发</summary>
        public event Action<PlacementResult> OnPlacementConfirmed;

        /// <summary>确认放置被拒绝（落点非法）时触发；由场景层订阅并给出玩家提示，本类不感知 UI</summary>
        public event Action OnPlacementRejected;

        /// <summary>
        /// 构造放置会话。
        /// </summary>
        /// <param name="playerKey">放置者标识</param>
        /// <param name="validColor">落点合法时的包围盒颜色</param>
        /// <param name="invalidColor">落点非法时的包围盒颜色</param>
        public PlacementSession(string playerKey, Color validColor, Color invalidColor)
        {
            this.playerKey = playerKey;
            this.validColor = validColor;
            this.invalidColor = invalidColor;
        }

        /// <summary>当前是否有未确认的摆放实例</summary>
        public bool BIsPlacing => current != null;

        /// <summary>当前是否持有待放置道具</summary>
        public bool BHasPendingItem => pendingPrefab != null;

        /// <summary>是否已放置完毕（无待放置道具且当前没有待确认的摆放）</summary>
        public bool BIsFinished => !BIsPlacing && pendingPrefab == null;

        /// <summary>摆放中道具的ID（prefab 名）；未摆放时为空串</summary>
        public string CurrentItemId => currentItemId;

        /// <summary>摆放中道具的 PlacementController；未摆放时为 null</summary>
        public PlacementController CurrentPlacementController => currentPc;

        /// <summary>摆放中道具是否旋转（非 0° 档）；未摆放时为 false</summary>
        public bool CurrentRotated => BIsPlacing && currentPc.IsRotated;

        /// <summary>摆放中道具的旋转档（0=0° 1=顺时针90° 2=180° 3=270°）；未摆放时为 0</summary>
        public int CurrentRotation => BIsPlacing ? currentPc.RotationSteps : 0;

        /// <summary>摆放中道具的镜像状态（无镜像语义为 false）；未摆放时为 false</summary>
        public bool CurrentMirrored => BIsPlacing && currentPc != null
            && currentPc.GetComponent<SuperQQ.Item.ItemBase>() is { } item && item.Mirrored;

        /// <summary>摆放中道具根节点的世界坐标；未摆放时为 null</summary>
        public Vector2? CurrentPosition => BIsPlacing ? (Vector2?)current.transform.position : null;

        /// <summary>摆放中道具按当前位置/朝向的锚点格子（footprint 左下角）；未摆放时为 null</summary>
        public Vector2Int? CurrentAnchorCell =>
            BIsPlacing ? (Vector2Int?)currentPc.GetAnchorCellAt(current.transform.position) : null;

        /// <summary>摆放中道具按当前位置/朝向占据的全部格子（联机确认时上报服务器仲裁）；未摆放时为 null</summary>
        public List<Vector2Int> CurrentOccupiedCells()
        {
            if (!BIsPlacing || GridManager.Instance == null)
            {
                return null;
            }

            Vector2Int anchor = currentPc.GetAnchorCellAt(current.transform.position);
            FootprintBoxView box = current.GetComponent<FootprintBoxView>();
            Vector2Int footprint = box != null ? box.Footprint : Vector2Int.one;
            Vector2Int size = GridManager.GetRotatedSize(footprint, currentPc.RotationSteps);

            var cells = new List<Vector2Int>(size.x * size.y);
            for (int dx = 0; dx < size.x; dx++)
            {
                for (int dy = 0; dy < size.y; dy++)
                {
                    cells.Add(new Vector2Int(anchor.x + dx, anchor.y + dy));
                }
            }
            return cells;
        }

        // ==================== 放置流程 ====================

        /// <summary>发放本轮待放置道具（进入阶段时调用；道具选择阶段实现后改由 SetPendingItem 推入）</summary>
        public void Deal(ItemBase prefab)
        {
            if (prefab != null)
            {
                pendingPrefab = prefab;
            }
        }

        /// <summary>
        /// 取出待放置道具并开始摆放：生成实例并跟随指针、吸附网格。
        /// </summary>
        /// <param name="worldPos">生成位置（指针世界坐标）</param>
        /// <returns>开始摆放返回 true；无待放置道具或正在摆放时返回 false</returns>
        public bool BeginPlace(Vector2 worldPos)
        {
            if (GridManager.Instance == null)
            {
                Debug.LogError($"{LOG_TAG} 场景中缺少 GridManager，无法摆放道具。");
                return false;
            }
            if (pendingPrefab == null || BIsPlacing)
            {
                return false;
            }

            currentPrefab = pendingPrefab;
            pendingPrefab = null;
            // 优先用 ItemCatalog 的数字 itemId（与服务器发牌代号一致），未配置时回退 prefab 名
            currentItemId = ItemCatalog.Instance != null
                ? ItemCatalog.Instance.GetItemId(currentPrefab) ?? currentPrefab.name
                : currentPrefab.name;
            Adopt(UnityEngine.Object.Instantiate(currentPrefab.gameObject, worldPos, Quaternion.identity));
            UpdatePointer(worldPos);
            return true;
        }

        /// <summary>
        /// 更新指针位置：道具跟随指针、吸附最近格子并刷新落点合法性提示。
        /// </summary>
        public void UpdatePointer(Vector2 worldPos)
        {
            if (!BIsPlacing)
            {
                return;
            }

            current.transform.position = worldPos;
            currentPc.SnapToNearestCell();
            RefreshValidityHint();
            UpdateDemolishPreview();
        }

        /// <summary>顺时针旋转当前摆放中的道具一档（0°→90°→180°→270°→0°；不可旋转的道具为空操作）</summary>
        public void Rotate()
        {
            if (!BIsPlacing)
            {
                return;
            }

            if (!currentPc.ToggleRotate())
            {
                // 不可旋转的道具：旋转键退化为切换镜像（樱桃发射器/流星锤等有方向语义的道具）
                currentPc.GetComponent<SuperQQ.Item.ItemBase>()?.ToggleMirror();
                return;
            }

            // ToggleRotate 会重建虚线框并重置颜色，立即刷回合法性提示色
            RefreshValidityHint();
            UpdateDemolishPreview();
        }

        /// <summary>
        /// 确认当前摆放：落点合法则登记占据并彻底锁定位置；非法则保持摆放状态等待调整。
        /// </summary>
        /// <returns>确认成功返回 true</returns>
        public bool Confirm()
        {
            if (!BIsPlacing)
            {
                return false;
            }

            currentPc.CompletePlacement();
            if (!currentPc.IsPlacementValid)
            {
                // CompletePlacement 会关闭虚化，确认失败时恢复，保持“摆放中”的视觉提示
                currentPc.GhostOn();
                Debug.LogWarning($"{LOG_TAG} 当前落点不合法，请移动到合法位置后再确认。");
                OnPlacementRejected?.Invoke();
                return false;
            }

            // 位置锁定：占据已登记、PlacedItem 已补挂，销毁摆放组件后道具不可能再被移动/虚化
            // （仅 enabled = false 的软锁定会被场景级调试热键等外部激活绕过）
            PlacedItem placed = current.GetComponent<PlacedItem>();
            // 写入放置者归属：陷阱击杀计分（RecordTrapKill）按此归属计分
            placed?.SetOwnerKey(playerKey);
            int rotation = currentPc.RotationSteps;
            bool mirrored = currentPc.GetComponent<SuperQQ.Item.ItemBase>() is { } itemBase && itemBase.Mirrored;
            UnityEngine.Object.Destroy(currentPc);

            OnPlacementConfirmed?.Invoke(new PlacementResult(
                playerKey,
                currentItemId,
                placed != null ? placed.AnchorCell : Vector2Int.zero,
                rotation,
                mirrored));

            // 衔接摆放：实现 IChainedPlacement 的道具（如传送门出口）直接接管，摆放流程不中断
            GameObject chained = current.TryGetComponent(out IChainedPlacement chainProvider)
                ? chainProvider.SpawnChainedItem()
                : null;
            if (chained != null)
            {
                // 保持 currentItemId 不变（链生段与首段同一道具 id；
                // 实例名带 "(Clone)" 后缀，覆盖后服务器/远端/快照都无法解析 prefab）
                Adopt(chained);
                return true;
            }

            currentPrefab = null;
            ClearCurrentRefs();
            return true;
        }

        /// <summary>取消当前摆放：道具退回待放置状态，由外部重新取出摆放</summary>
        public void Cancel()
        {
            ReleaseCurrent();
            if (currentPrefab != null)
            {
                pendingPrefab = currentPrefab;
                currentPrefab = null;
            }
        }

        /// <summary>丢弃当前未确认的摆放（阶段结束时调用，不退回待放置）</summary>
        public void DiscardUnconfirmed()
        {
            ReleaseCurrent();
            currentPrefab = null;
            pendingPrefab = null;
        }

        // ==================== 内部实现 ====================

        /// <summary>
        /// 接管一个待摆放实例：屏蔽其自带的拖拽输入与调试热键，跟随/旋转/确认改由本会话驱动。
        /// </summary>
        private void Adopt(GameObject instance)
        {
            current = instance;
            currentPc = instance.GetComponent<PlacementController>();
            if (currentPc == null)
            {
                // PlacementController 要求根物体带 Collider2D（RequireComponent）。
                // 部分道具（如流星锤）碰撞体在子物体、根物体没有，AddComponent 会失败返回 null，
                // 导致后续 BeginPlace/UpdatePointer 空引用。先确保根物体有碰撞体再补挂。
                if (instance.GetComponent<Collider2D>() == null)
                {
                    instance.AddComponent<BoxCollider2D>();
                }
                currentPc = instance.AddComponent<PlacementController>();
                if (currentPc == null)
                {
                    Debug.LogError($"[PlacementSession] 道具 {instance.name} 补挂 PlacementController 失败（根物体缺碰撞体且无法补齐）");
                    return;
                }
            }

            currentPc.enabled = false;      // 屏蔽长按拖拽轮询
            currentPc.DebugHotkeys = false; // 屏蔽 P/R/Enter 调试热键，避免与放置阶段输入冲突
            currentPc.GhostOn();

            currentBox = instance.GetComponent<FootprintBoxView>();
            if (currentBox != null)
            {
                currentBox.Init(currentBox.Footprint, currentPc.RotationSteps);
                currentBox.Show();
            }
        }

        /// <summary>按当前位置校验落点并刷新虚线框颜色</summary>
        private void RefreshValidityHint()
        {
            if (currentBox == null)
            {
                return;
            }

            bool bValid = currentPc.CanPlaceAt(current.transform.position);
            currentBox.SetColor(bValid ? validColor : invalidColor);
        }

        // ==================== 消除预览 ====================

        // 被标红的渲染器 -> 原始颜色（移出爆破范围/结束摆放时恢复）
        private readonly Dictionary<SpriteRenderer, Color> demolishPreviewColors = new();

        /// <summary>
        /// 消除类道具拖拽预览：爆破范围（自身 footprint）内可被消除的道具标红。
        /// 判据与消除执行同一口径——只有道具（有 ItemBase 的占据物）才可被消除，
        /// Map 关卡物体（船/平台等）不会被标红；范围移开后恢复原色
        /// </summary>
        private void UpdateDemolishPreview()
        {
            ClearDemolishPreview();

            if (current == null || current.GetComponent<DemolitionItemBase>() == null
                || currentBox == null || currentPc == null)
            {
                return;
            }
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                return;
            }

            Vector2Int anchor = currentPc.GetAnchorCellAt(current.transform.position);
            foreach (Vector2Int cell in grid.GetFootprintCells(anchor, currentBox.Footprint, currentPc.RotationSteps))
            {
                PlacedItem occupant = grid.GetItemAt(cell);
                if (occupant == null || occupant.GetComponent<ItemBase>() == null)
                {
                    continue;
                }
                foreach (SpriteRenderer r in occupant.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (r == null || demolishPreviewColors.ContainsKey(r))
                    {
                        continue; // 同一道具跨多格去重
                    }
                    demolishPreviewColors[r] = r.color;
                    Color c = r.color;
                    r.color = new Color(c.r, c.g * 0.35f, c.b * 0.35f, c.a);
                }
            }
        }

        /// <summary>恢复全部标红渲染器的原始颜色（幂等）</summary>
        private void ClearDemolishPreview()
        {
            if (demolishPreviewColors.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<SpriteRenderer, Color> pair in demolishPreviewColors)
            {
                if (pair.Key != null)
                {
                    pair.Key.color = pair.Value;
                }
            }
            demolishPreviewColors.Clear();
        }

        /// <summary>
        /// 释放当前未确认实例：已登记占据的走 RemoveAt 释放格子，未登记的直接销毁。
        /// </summary>
        private void ReleaseCurrent()
        {
            if (BIsPlacing)
            {
                PlacedItem placed = current.GetComponent<PlacedItem>();
                GridManager grid = GridManager.Instance;
                // 仅当占据表中登记的确实是本实例时才 RemoveAt
                // （拆除类道具不登记自身占据，按锚点直接 RemoveAt 会误删同格的其它道具）
                if (placed != null && grid != null && grid.GetItemAt(placed.AnchorCell) == placed)
                {
                    grid.RemoveAt(placed.AnchorCell);
                }
                else
                {
                    UnityEngine.Object.Destroy(current);
                }

                ClearCurrentRefs();
            }

            // 配对强制约束：取消/丢弃出口摆放后，落单的入口一并清除（对已配对的传送门无影响）
            Portal.DestroyAllUnpaired();
        }

        private void ClearCurrentRefs()
        {
            ClearDemolishPreview();
            current = null;
            currentPc = null;
            currentBox = null;
            currentItemId = string.Empty;
        }
    }
}

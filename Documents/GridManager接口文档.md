# GridManager 接口文档

命名空间：`SuperQQ.Grid`
类型：关卡场景单例（`GridManager.Instance`），挂载于场景中的 `GridSystem` 物体

职责：世界坐标与格子坐标的双向换算、格子占据表管理（放置/移除/合法性检测）、Scene 视图网格可视化。

核心约定：

- 网格 = **原点（gridOrigin）+ 格子尺寸（cellSize）**，坐标换算对任意世界坐标成立
- `placeableBounds` 是可摆放区域（格子坐标矩形），是"摆放规则"而非网格的属性
- **锚点 = footprint 左下角的格子**；物体放置在 footprint 的几何中心
- 本地操作与网络回放统一走 `Place` / `RemoveAt` 入口，保证各端状态一致
- 跑动阶段的移动与碰撞不依赖网格

---

## 一、静态属性

| 成员 | 类型 | 说明 |
|---|---|---|
| `Instance` | `GridManager` | 当前场景实例（场景内唯一） |

## 二、只读属性

| 成员 | 类型 | 说明 |
|---|---|---|
| `PublicCellSize` | `float` | 格子边长（米），来自 GridConfig |
| `PublicOrigin` | `Vector2` | 格子 (0,0) 中心的世界坐标 |
| `PlaceableBounds` | `RectInt` | 可摆放区域（格子坐标），x/y 为左下角，width/height 为格数 |

## 三、坐标换算

### `Vector2Int WorldToCell(Vector2 worldPos)`

世界坐标 → 所在格子坐标（向下取整）。

### `Vector2 CellToWorld(Vector2Int cell)`

格子坐标 → 该格子**中心**的世界坐标。

### `List<Vector2Int> GetFootprintCells(Vector2Int anchorCell, Vector2Int footprint, bool rotated)`

计算 footprint 覆盖的所有格子。锚点为左下角；`rotated = true` 时宽高互换。

### `Vector2 GetPlacementWorldPos(Vector2Int anchorCell, Vector2Int footprint, bool rotated)`

锚点 + footprint → **物体中心**的世界坐标，即实例化摆放应使用的位置。

## 四、查询

### `bool IsInBounds(Vector2Int cell)`

格子是否在可摆放区域内。

### `PlacedItem GetItemAt(Vector2Int cell)`

获取占据某格子的物体；空闲返回 `null`。

### `Vector2Int ResolveFootprint(PlacableItemDef def)`

解析道具实际占位。**优先读 prefab 上 `FootprintBoxView` 组件的 `Footprint`**；无组件时回退到资产里的 `footprint` 字段。所有放置判定内部都走此方法。

### `bool CanPlace(PlacableItemDef def, Vector2Int anchorCell, bool rotated)`

检测能否在指定锚点放置。判定规则：footprint 覆盖的所有格子均在 `placeableBounds` 内 且 均未被占据。

## 五、放置 / 移除（本地与网络回放共用入口）

### `PlacedItem Place(PlacableItemDef def, Vector2Int anchorCell, bool rotated, int ownerPlayerId)`

放置物体。内部流程：

1. 校验 `def` 与 `CanPlace`，不合法返回 `null`
2. 在 footprint 中心实例化 prefab（旋转时绕 Z 轴转 90°）
3. 附加/初始化 `PlacedItem`（锚点、旋转、放置者）
4. 若 prefab 挂了 `ItemBase`：注入 `Placed` + `Facing` 并调用 `OnPlaced()`
5. 登记 footprint 覆盖的全部格子到占据表

`ownerPlayerId`：放置者玩家 ID；关卡初始物体传 `-1`。

### `bool RemoveAt(Vector2Int cell)`

移除占据某格子的物体：先触发 `ItemBase.OnRemoved()` 钩子，再释放其 footprint 覆盖的全部格子，最后销毁物体。无物体时返回 `false`。

## 六、Inspector 配置

| 字段 | 默认 | 说明 |
|---|---|---|
| `config` | — | GridConfig 资产（全局格子尺寸） |
| `gridOrigin` | — | 原点 Transform，建议放空物体在关卡左下角，随关卡移动 |
| `placeableBounds` | (-20, 0, 40, 15) | 可摆放区域（格子坐标） |
| `drawGrid` | true | 是否绘制 Scene 视图网格 |
| `gridLineColor` | 白 15% | 网格线颜色 |
| `boundsColor` | 青 60% | 可摆放区域外框颜色 |
| `occupiedColor` | 红 35% | 运行时被占据格子的高亮颜色 |

## 七、典型用法

```csharp
// 幽灵体吸附（PlacementController 每帧）
Vector2Int anchor = GridManager.Instance.WorldToCell(pointerWorld);
ghost.position = GridManager.Instance.GetPlacementWorldPos(anchor, footprint, rotated);
bool canPlace = GridManager.Instance.CanPlace(def, anchor, rotated);

// 确认放置（本地）→ 网络层广播后，远端用完全相同的调用回放
PlacedItem item = GridManager.Instance.Place(def, anchor, rotated, playerId);

// 拾回 / 拆除
GridManager.Instance.RemoveAt(cell);
```

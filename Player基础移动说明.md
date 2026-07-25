# Player 基础移动说明

> 本文档介绍 Player 模块的基础移动系统与地图边界组件的代码架构、功能逻辑与使用方式。

---

## 1. 文件结构

```
Assets/Scripts/
├── Player/
│   ├── PlayerController.cs          # 玩家控制器 — 状态机持有者
│   └── PlayerStateMachine/
│       ├── IPlayerState.cs           # 玩家状态接口
│       ├── PlayerAliveState.cs       # 存活状态实现
│       └── PlayerGhostState.cs       # 幽灵状态实现
└── Map/
    └── MapBoundary.cs                # 地图边界组件
```

---

## 2. 架构概览

```
┌─────────────────────────────────────────────────┐
│                 PlayerController                 │
│  职责：组件缓存、输入读取、状态切换、参数配置      │
│                                                 │
│  ┌──────────┐    TransitionTo()    ┌──────────┐ │
│  │  Alive   │ ◄──────────────────► │  Ghost   │ │
│  │  State   │      PlayerDie()     │  State   │ │
│  │          │      Revive()        │          │ │
│  └──────────┘                      └──────────┘ │
│        │                                 │       │
│        └──────────┬──────────────────────┘       │
│                   ▼                              │
│            MapBoundary（外部引用）                │
│            提供边界约束方法                       │
└─────────────────────────────────────────────────┘
```

**设计原则**：

- **状态机模式**：存活与幽灵为两个独立状态类，通过 `IPlayerState` 接口统一调用，消除布尔门控
- **数据归位**：每个状态的运行时数据（土狼计时、跳跃保持计时等）归状态类私有，不暴露给 Controller
- **边界解耦**：地图边界由独立的 `MapBoundary` 组件管理，PlayerController 通过引用访问，不持有边界数据

---

## 3. PlayerController

### 3.1 职责

- 缓存 `Rigidbody2D`、`SpriteRenderer`、`Collider2D` 组件引用
- 在 `Update` 中读取输入，在 `FixedUpdate` 中委托给当前状态
- 管理状态切换（`TransitionTo`、`PlayerDie`、`Revive`）
- 通过属性暴露配置参数，供状态类读取

### 3.2 Inspector 配置
见源码

### 3.3 公开方法

| 方法 | 说明 |
|------|------|
| `PlayerDie()` | 进入幽灵状态（已死亡时调用无效） |
| `Revive()` | 回到存活状态并传送至复活点（已存活时调用无效） |
| `TransitionTo(IPlayerState)` | 切换到指定状态（先 Exit 旧状态，再 Enter 新状态） |

### 3.4 公开状态查询

| 属性 | 类型 | 说明 |
|------|------|------|
| `BIsGrounded` | bool | 是否在地面上 |
| `BIsJumping` | bool | 是否正在跳跃 |
| `BIsDead` | bool | 是否处于幽灵状态 |
| `HorizontalVelocity` | float | 当前水平速度 |

### 3.5 调试功能

- **屏幕调试信息**（`OnGUI`）：左上角显示当前状态、接地、跳跃、坐标
- **K 键**：立即击杀，进入幽灵状态
- **R 键**：立即复活，回到存活状态
- **Scene 视图**：选中 Player 时 `groundCheck` 位置显示绿/红检测圈

---

## 4. IPlayerState 接口

```csharp
public interface IPlayerState
{
    void Enter();                               // 进入状态时调用
    void Exit();                                // 退出状态时调用
    void Update();                              // 每帧更新
    void FixedUpdate();                         // 物理帧更新
    bool BIsGrounded { get; }                   // 是否在地面
    bool BIsJumping { get; }                    // 是否在跳跃
    float HorizontalVelocity { get; }           // 当前水平速度
}
```

所有状态必须实现此接口。Controller 通过 `_currentState.Update()` / `_currentState.FixedUpdate()` 委托调用，无需判断当前处于哪个状态。

---

## 5. PlayerAliveState（存活状态）

### 5.1 功能

- 左右移动（平滑加减速）
- 可变高度跳跃（轻触短跳，长按更高）
- 下落手感优化（快速下落、短跳截断）
- 地面检测 + 土狼时间
- 左右边界约束、掉落死亡

### 5.2 运行时私有数据

| 字段 | 类型 | 说明 |
|------|------|------|
| `_bIsGrounded` | bool | 当前帧是否在地面 |
| `_bIsJumping` | bool | 当前是否在跳跃中 |
| `_jumpHoldTimer` | float | 长按跳跃累计时间 |
| `_coyoteTimer` | float | 土狼时间剩余 |

### 5.3 核心逻辑流程

```
Update:
  CheckGround()       → OverlapCircle 检测地面，维护土狼计时器
  HandleJumpStart()   → 按下跳跃键 + (接地或土狼时间) → 起跳
  HandleJumpCut()     → 跳跃中松手 → 削减竖直速度（短跳）

FixedUpdate:
  ApplyHorizontalMovement()    → MoveTowards 平滑加减速
  ApplyVariableJumpHeight()    → 长按追加向上速度
  ApplyBetterFallGravity()     → 下落加重力 / 松手补重力 / 限速
  ClampToMapBoundary()         → 左右夹紧 + 掉落死亡判定
```

### 5.4 边界行为

| 边界 | 行为 |
|------|------|
| 左 | Clamp 夹紧，不允许超出 |
| 右 | Clamp 夹紧，不允许超出 |
| 上 | 不约束（允许跳越地图上方） |
| 下 | 超出 `mapMinY` 时调用 `PlayerDie()` |

---

## 6. PlayerGhostState（幽灵状态）

### 6.1 功能

- 四向平移（WASD，水平竖直相同速度/加速度）
- 无重力、无碰撞体
- Sprite 半透明
- 四周边界约束
- 进入时传送至 `ghostSpawnPosition`

### 6.2 运行时私有数据

| 字段 | 类型 | 说明 |
|------|------|------|
| `_currentHorizontalVelocity` | float | 当前水平速度 |
| `_currentVerticalVelocity` | float | 当前竖直速度 |
| `_savedGravityScale` | float | 保存的重力倍率（Exit 时恢复） |
| `_savedColor` | Color | 保存的 Sprite 颜色（Exit 时恢复） |

### 6.3 Enter/Exit 对称操作

| 操作 | Enter（进入幽灵） | Exit（退出幽灵） |
|------|-------------------|------------------|
| 碰撞体 | `Collider.enabled = false` | `Collider.enabled = true` |
| 重力 | 保存并置 0 | 恢复保存值 |
| 速度 | 清零 | — |
| 位置 | 传送至 `ghostSpawnPosition` | — |
| 透明度 | 保存并设为 `ghostAlpha` | 恢复保存值 |

### 6.4 边界行为

| 边界 | 行为 |
|------|------|
| 左 | Clamp 夹紧 |
| 右 | Clamp 夹紧 |
| 上 | Clamp 夹紧 |
| 下 | Clamp 夹紧 |

---

## 7. MapBoundary（地图边界组件）

### 7.1 职责

定义地图可活动区域的矩形范围，提供坐标约束方法。作为独立 MonoBehaviour 挂载到场景中的任意 GameObject，由 PlayerController 通过 Inspector 引用。

### 7.2 Inspector 配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| mapMinX | float | -10 | 地图左边界 X |
| mapMaxX | float | 10 | 地图右边界 X |
| mapMinY | float | -10 | 地图下边界 Y（掉落死亡线） |
| mapMaxY | float | 10 | 地图上边界 Y |

### 7.3 公开方法

| 方法 | 参数 | 返回值 | 说明 |
|------|------|--------|------|
| `ClampHorizontal` | Vector2 position | Vector2 | 将 X 坐标夹紧到 [mapMinX, mapMaxX] |
| `ClampAll` | Vector2 position | Vector2 | 将 X/Y 坐标均夹紧到边界内 |
| `IsBelowBoundary` | float y | bool | 判断 Y 是否低于 mapMinY |

### 7.4 Scene 视图可视化

选中挂载 `MapBoundary` 的 GameObject 时，Scene 视图绘制青色矩形边框。

---

## 8. 场景配置步骤

1. 创建 Player GameObject，挂载 `PlayerController`、`Rigidbody2D`、`Collider2D`、`SpriteRenderer`
2. 在 Player 子级创建空 GameObject 作为 `groundCheck`，拖入 Inspector
3. 创建空 GameObject，挂载 `MapBoundary`，设置四边边界值
4. 将 `MapBoundary` 对象拖入 PlayerController 的 `mapBoundary` 引用
5. 设置 `groundLayer` 为地面碰撞体所在 Layer

---

## 9. 状态切换示意

```
         PlayerDie()
  Alive ─────────────► Ghost
    ▲                    │
    │      Revive()      │
    └────────────────────┘

触发条件：
  • PlayerDie() — K 键调试 / 掉落出下边界 / 受伤（后续接入）
  • Revive()    — R 键调试 / 结算续轮（后续接入）
```

# 道具 Prefab 制作规范

适用范围：所有可在建造阶段摆放到网格上的道具（平台、陷阱、控制装置等）。
配套脚本：`Scripts/Grid/`（网格系统）、`Scripts/Item/`（道具基类与行为组件）。

---

## 一、prefab 层级结构

```
ItemName（根物体：ItemBase + FootprintBoxView，不挂碰撞体）
├── Visual            ← 美术表现：SpriteRenderer（道具本体贴图）
├── Colliders         ← 物理碰撞体（站立面、实体阻挡，非 Trigger）
├── HitZones          ← 判定触发器（伤害区、感应区，Is Trigger）
└── Fx                ← 特效/动画（可选，无则省略）
```

层级约定：

- **Colliders**：参与物理，玩家能站上去/被挡住（平台表面、剪刀轴座）
- **HitZones**：Trigger，只产生判定事件（钳口伤害区、闹钟感应半径、磁铁吸引范围）
- 行为组件的查找约定：`ContactHazard` 等只监听 `HitZones` 下的触发器
- 周期攻击的判定开关（如剪刀只在闭合窗判定）由行为组件控制 `HitZones` 下对应物体的 `SetActive`
- **简单道具可从简**：整体即单一判定的道具（如玻璃球），允许省略 Colliders/HitZones 分层，碰撞体直接挂根物体
- **禁止**手动创建 `FootprintBox` 子物体——虚线包围盒由 `FootprintBoxView` 运行时自动生成

复合道具示例（大剪刀，钳口致命、轴座安全）：

```
Scissors_3x2（根）
├── Visual
├── Colliders
│   └── BaseCollider      （轴座，可站立，安全）
├── HitZones
│   └── JawTrigger        （前向钳口 72×56，致命）
└── Fx
```

## 二、根物体挂载组件

| 组件 | 必需 | 作用 | 关键配置 |
|---|---|---|---|
| `ItemBase` 子类（如 `GlassBall`） | 必需 | 道具身份与生命周期钩子（类别、放置回调、阶段启停） | 重写 `Category` 属性 |
| `FootprintBoxView` | 必需 | **定义占位格子数**（宽×高），编辑期/摆放时绘制虚线包围盒 | `Footprint` 填格子数，如平台 2×1 |
| `Rigidbody2D` | 按需 | 道具本身需要物理时添加 | 静态平台类不需要 |
| 行为组件（`ContactHazard` / `PeriodicAttack` / `SurfaceModifier` 等） | 按需 | 道具的具体功能，可挂多个组合 | 见行为组件表 |

碰撞体**不挂根物体**（简单道具除外），放入 `Colliders` / `HitZones` 子层，见第一节。

**禁止**在 prefab 上挂 `PlacedItem`——它由 `GridManager.Place` 放置时自动附加。

## 三、子物体约定

### Visual
- 只放 `SpriteRenderer` 与贴图
- 摆放预览（幽灵体）生成时，此层会被改为半透明；受击/触发反馈（变色、缩放）也只动此层

### Colliders
- 只放非 Trigger 的物理碰撞体（站立面、实体阻挡）
- 尺寸 = 对应部位素材的实际大小，不要求填满 footprint

### HitZones
- 只放 Is Trigger 的判定区（伤害、感应、力场范围）
- 每个判定区独立子物体并起语义化名字（如 `JawTrigger`、`SenseRadius`），便于行为组件按名引用和开关

### Fx（可选）
- 粒子、动画器等纯表现内容
- 幽灵体生成时此层整体禁用

## 四、美术素材规范

| 项目 | 规范 |
|---|---|
| PPU | 全项目统一 **100**（100 像素 = 1 米） |
| 1 格像素 | **50 × 50 px**（cellSize = 0.5 米） |
| 素材尺寸 | 宽高 = 设计格数 × 50px（如 2×1 平台 = 100×50 px） |
| Pivot | **Center**（放置按 footprint 中心对齐） |
| 画布 | 不留多余透明边（会撑大包围盒导致视觉中心偏移） |
| 非整数格道具 | 占位格子数向上取整填 `Footprint`，素材按实际尺寸画（如肥皂设计 1.5×0.5 格 → Footprint 填 2×1，素材 75×25 px） |

## 五、占位（Footprint）规则

- `Footprint` 填**整数格**，锚点 = footprint 左下角格子
- 网格占位只决定"占几个槽位"；伤害判定范围、素材大小由碰撞体与贴图决定，**不走网格**
- 可旋转道具在 `PlacableItemDef` 资产上配置 `Facing Steps`（0=不可转，2=翻面，4=四向）；旋转 90° 时 footprint 宽高自动互换

## 六、行为组件速查（按策划道具表）

| 行为组件 | 功能 | 参数示例 | 适用道具 |
|---|---|---|---|
| `ContactHazard` | 接触即死 | 死亡表现 | 玻璃球、滑动齿轮 |
| `PeriodicAttack` | 周期攻击（前摇→判定窗→冷却） | 周期 2.0s、前摇 0.5s | 大剪刀、电击枪、奶龙、羽毛球拍、流星锤 |
| `SurfaceModifier` | 改变站立面属性 | 减速 0.5 / 无摩擦 | 黄油块、肥皂 |
| `Mover` | 路径移动 | 轨迹、速度、触发方式 | 气球、磁带、跳舞平台、滑动齿轮 |
| `ForceField` | 持续力场 | 方向、强度、范围 | 吹风机、磁铁 |
| `Launcher` | 弹射玩家 | 角度 45°、距离 8 格 | 大炮 |
| `ProximityEffect` | 接近触发效果 | 半径 1.5 格、震屏 3s | 闹钟 |
| `PortalPair` | 传送配对 | 出口引用 | 传送门 |
| `DemolitionCharge` | 延迟爆炸拆道具 | 延迟、半径 | 摔炮、黑炸弹、原子弹（独立流程） |

组合示例：`电击枪 = ItemBase + PeriodicAttack(周期2.5s, 前摇0.65s) + SectorHitZone(45°, 3格)`

## 七、配套资产

每个道具还需创建一份 `PlacableItemDef` 资产（Create → SuperQQ → PlacableItemDef）：

| 字段 | 说明 |
|---|---|
| `Item Id` | 全局唯一（网络传输用），如 `spike_1x1` |
| `Prefab` | 指向本 prefab |
| `Ghost Prefab` | 留空（运行时自动由 prefab 生成半透明预览体） |
| `Category` | 搭路/伤害/控制/拆除（道具栏分组） |
| `Facing Steps` | 朝向档位数 |
| `Icon` | 道具栏图标 |

## 八、自检清单（提交前）

- [ ] 根物体挂 `ItemBase` 子类 + `FootprintBoxView`，层级符合第一节
- [ ] Scene 视图虚线框恰好套住素材，与网格线对齐
- [ ] 素材 PPU=100、尺寸为 50 的倍数、pivot 居中
- [ ] 碰撞体尺寸与素材一致；伤害类为 Trigger
- [ ] 已创建 `PlacableItemDef` 且 `Item Id` 唯一
- [ ] prefab 上无 `PlacedItem`、无手动创建的 `FootprintBox`

## 九、完整示例：1×1 地刺

```
Spike_1x1（根）
├─ Spike : ItemBase          （Category = Hazard）
├─ FootprintBoxView          （Footprint = 1×1）
├─ BoxCollider2D             （size 0.5×0.5，Is Trigger ✓）
├─ ContactHazard             （行为：接触致死）
└─ Visual
   └─ SpriteRenderer         （spike.png，50×50px，PPU 100，pivot Center）
```

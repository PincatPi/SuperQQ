# Round Results UI

小局结算覆盖层，读取现有 `PlayerScoreManager`，也支持程序传入自定义数据。

## 直接调用

```csharp
using SuperQQ.UI.RoundResults;
using UnityEngine;

public sealed class RoundEndExample : MonoBehaviour
{
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RoundResultsPanel panelPrefab;

    private RoundResultsPanel _panel;

    public void OpenRoundResults()
    {
        if (_panel == null)
        {
            _panel = Instantiate(panelPrefab, targetCanvas.transform, false);
        }

        _panel.ShowCurrentRound(OnContinue);
    }

    private void OnContinue()
    {
        // 进入选道具、放置或下一轮流程。
    }
}
```

Prefab 根节点默认关闭，`Show`/`ShowCurrentRound` 会自动启用并播放动画。

## 积分条揭示动画

积分条按固定顺序从左到右增长：先显示 `PreviousTotal` 历史累计分，再依照
`Segments` 列表顺序逐段显示本轮新增积分。每一段完成增长后才会开始下一段，
不会同时弹出。

- 单段时长：`RoundResultsPanel._scoreStageDuration`，默认 `0.18` 秒。
- 单行最短时长：`RoundResultsPanel._rowRevealDuration`，默认 `0.42` 秒。
- 实际单行时长：两者取较大值，即 `max(单行最短时长, 单段时长 × 阶段数)`。
- 阶段数：历史累计分 1 段，加上有效的 `Segments` 数量。
- 演示场景中按 `R` 可重新播放完整顺序。

程序只需按期望的视觉顺序排列 `RoundResultPlayerData.Segments`；不要直接操作
积分块的 `RectTransform` 或 `localScale`。

## 动态名次变化

结算开始时按 `PreviousTotal`（本轮开始前累计分）从高到低排列。每个积分段增长时，
`RoundResultRowView.RevealedScore` 会同步增加；当玩家的可见积分**严格超过**上一名时，
`RoundResultsPanel` 会立即交换两行的目标槽位、更新名次数字，并播放平滑上移与轻微
缩放强调。相同积分不会反复换位。

- 换位时长：`RoundResultsPanel._rankSwapDuration`，默认 `0.28` 秒。
- `Show(...)`：播放积分增长和动态超越动画。
- `ShowImmediate(...)`：不播放过程，直接按最终累计分排列。
- 超过多名时会逐级向上交换，最终行层级会提交为当前排名，避免后续 Layout 重建复位。
- 排名比较使用正在显示的累计分；动画结束时数字会精确落到 `CumulativeTotal`。

演示场景中 `PAPER FOX` 以 `55` 分开始第一，`TURBO TURTLE` 以 `48` 分开始第二；
后者本轮获得 `45` 分并以 `93` 分超过前者的 `87` 分。Play Mode 中按 `R` 可重播。

## 手绘积分条样式

积分轨道尺寸保持 `510 x 64`，`FillContent` 四边内缩 `8px`，避免积分块紧贴
外框。历史分与新增分统一使用以下层级：

`FillContent` 必须保留居中 Pivot `(0.5, 0.5)`；不要在调用四边 Stretch/Inset
之后把它改成左侧 Pivot，否则左边距会变成 `0px`、右边距会变成 `16px`。
逐段增长使用的是内部积分块自己的左侧 Pivot，不依赖 `FillContent` 的 Pivot。

内部积分块的 `pixelsPerUnitMultiplier` 为 `2`，使内层圆角半径小于外框；这是
保持四边视觉间距一致所必需的，不要只把内外层设置成相同圆角半径。

1. 浅纸色圆角底。
2. 与积分类型同色的手绘斜线纹理。
3. 同色系细描边。
4. `Mask` 裁切，保证纹理不会溢出圆角。

运行时新增分段由 `RoundResultRowView.CreateHatchedSegment` 自动构建，程序员无需
手工添加纹理层。斜线源资产为
`Assets/Art/UI/RoundResults/ui_hand_drawn_hatch.png`，由 Prefab 构建器生成。

## Host 调用

把 `RoundResultsPanelHost` 加到流程管理对象，配置 Canvas 和 Panel Prefab，程序只需调用：

```csharp
roundResultsHost.ShowCurrentRound(OnContinue);
```

如果勾选 Host 的 `Notify Game Flow On Continue`，点击 CONTINUE 时会调用
`GamePhaseManager.NotifyCurrentPhaseEvent()`。已有其他表现层负责通知时不要勾选，避免重复推进。

## 正式计分入口

正式游戏推荐将得分事件写入 `PlayerScoreManager`，回合结束时统一结算，再让 UI 读取结果。不要直接修改积分条的 RectTransform。

```csharp
PlayerScoreManager scoreManager = PlayerScoreManager.Instance;

// 游戏过程中由对应系统记录得分事件。
scoreManager.RecordTrapKill(playerName);
scoreManager.RecordBonusScore(playerName, 5);
scoreManager.RecordBossQuiet(playerName);

// 通关分、第一名和独行分根据玩家的 Finished 状态自动计算。
// 正式游玩阶段结束时只结算一次。
scoreManager.SettleCurrentRound();

// 读取刚结算的数据并播放积分条动画。
roundResultsHost.ShowCurrentRound(OnContinue);
```

公开入口：

- `PlayerScoreManager.RecordTrapKill(playerName)`：记录陷阱有效击杀，可重复调用。
- `PlayerScoreManager.RecordBonusScore(playerName, points)`：记录金币或得分道具，可重复累加。
- `PlayerScoreManager.RecordBossQuiet(playerName)`：记录特殊效果达标。
- `PlayerScoreManager.SettleCurrentRound()`：汇总本轮数据、写入历史记录并触发 `OnRoundScored`。
- `PlayerScoreManager.AdvanceToNextRound()`：开始下一轮并清空本轮临时数据。
- `RoundResultsPanelHost.ShowCurrentRound(onContinue)`：从当前 `PlayerScoreManager` 自动生成并显示积分条。

通关、第一名和独行积分不是手动写入接口。结算时会读取 `LevelPlayerRegistry` 中处于 `PlayerStateType.Finished` 的玩家并由 `ScoreCalculator` 计算。

## 监听结算事件并自动显示

```csharp
using System.Collections.Generic;
using SuperQQ.Score;
using SuperQQ.UI.RoundResults;
using UnityEngine;

public sealed class RoundResultsPresenter : MonoBehaviour
{
    [SerializeField] private RoundResultsPanelHost roundResultsHost;

    private void OnEnable()
    {
        if (PlayerScoreManager.Instance != null)
        {
            PlayerScoreManager.Instance.OnRoundScored += HandleRoundScored;
        }
    }

    private void OnDisable()
    {
        if (PlayerScoreManager.Instance != null)
        {
            PlayerScoreManager.Instance.OnRoundScored -= HandleRoundScored;
        }
    }

    private void HandleRoundScored(Dictionary<string, RoundScoreData> results)
    {
        roundResultsHost.ShowCurrentRound(OnContinue);
    }

    private void OnContinue()
    {
        // 进入选道具、放置或下一轮流程。
    }
}
```

`OnRoundScored` 在得分记录已经写入后触发，因此可以在回调中立即调用 `ShowCurrentRound`。

## 积分条字段映射

| 字段 | UI 表现 |
| --- | --- |
| `PreviousTotal` | 左侧历史累计积分 |
| `Segments[].Points` | 本轮新增的彩色积分段长度 |
| `Segments[].ScoreType` | 新增积分段的类型、颜色和图例 |
| `RoundTotal` | 玩家行右侧的 `+N` 文本 |
| `CumulativeTotal` | `当前分 / 胜利分` 文本、最终填充位置以及 WINNER 判定 |
| `victoryScore` | 积分条最大值与 WINNER 门槛，默认 100 |
| `IsRoundWinner` | 保留的本轮最高分数据标记；不控制 WINNER 徽章 |

`WINNER` 徽章只由当前显示的累计积分判断：当积分增长到
`RevealedScore >= victoryScore` 时才出现。默认目标为 `100`，因此 `93 / 100`
不会显示；`ShowImmediate(...)` 则会直接按最终 `CumulativeTotal` 判断。

自动读取路径：

```text
PlayerScoreManager
  -> RoundScoreData
  -> RoundResultsDataAdapter.BuildCurrentRound
  -> RoundResultsPanelHost.ShowCurrentRound
  -> RoundResultRowView.Populate
```

## 外部数据直接驱动

服务器、网络同步、回放或测试工具可以绕过 `PlayerScoreManager`，直接构造 UI 数据：

```csharp
using System.Collections.Generic;
using SuperQQ.Score;
using SuperQQ.UI.RoundResults;
using UnityEngine;

RoundResultPlayerData player = new RoundResultPlayerData
{
    PlayerName = "Player 1",
    PlayerColor = Color.cyan,
    PreviousTotal = 40,
    RoundTotal = 15,
    CumulativeTotal = 55,
    IsRoundWinner = true, // 本轮最高分标记，不会绕过 victoryScore 显示 WINNER。
    Segments = new List<RoundResultScoreSegment>
    {
        new RoundResultScoreSegment
        {
            ScoreType = ScoreType.Completion,
            Label = "FINISH",
            Points = 10,
            Color = RoundResultsDataAdapter.GetSegmentColor(ScoreType.Completion)
        },
        new RoundResultScoreSegment
        {
            ScoreType = ScoreType.ScoreItem,
            Label = "ITEM",
            Points = 5,
            Color = RoundResultsDataAdapter.GetSegmentColor(ScoreType.ScoreItem)
        }
    }
};

roundResultsHost.Show(
    new[] { player },
    roundIndex: 3,
    victoryScore: 100,
    onContinue: OnContinue);
```

外部数据必须保持以下关系，否则右侧数字与彩色积分段长度可能不一致：

```text
RoundTotal = 所有 Segments.Points 之和
CumulativeTotal = PreviousTotal + RoundTotal
```

当前没有写入持久计分记录的通用 `AddScore(player, ScoreType, points)` 接口。正式玩法应使用上述事件接口；只有服务器同步、回放或特殊测试场景才建议直接调用 `Show(...)`。

相关源码：

- `Assets/Scripts/Score/PlayerScoreManager.cs`
- `Assets/Scripts/Score/RoundScoreData.cs`
- `Assets/Scripts/Score/ScoreCalculator.cs`
- `Assets/Scripts/UI/RoundResults/RoundResultsDataAdapter.cs`
- `Assets/Scripts/UI/RoundResults/RoundResultsPanelHost.cs`
- `Assets/Scripts/UI/RoundResults/RoundResultRowView.cs`

## 资产

- `RoundResultsPanel.prefab`：完整覆盖层。
- `RoundResultRow.prefab`：独立玩家行。
- `Assets/Art/UI/RoundResults/round_results_preview.png`：1280×720 离屏验证图。
- `Assets/Art/UI/RoundResults/round_results_sequential_growth.png`：加高轨道与逐段增长的运行态验证图。
- `Assets/Art/UI/RoundResults/round_results_rank_overtake.png`：第二名积分超过第一名后的运行态验证图。

重新生成 Prefab：`Tools > SuperQQ > UI > Build Round Results Prefabs`。

## 演示关卡

打开 `Assets/Scenes/RoundResultsDemo.unity` 即可看到四名模拟玩家的小局结算。
进入 Play Mode 后会自动播放揭示动画；点击 `CONTINUE` 会隐藏面板，按 `R` 可重新播放。

- 场景：`Assets/Scenes/RoundResultsDemo.unity`
- 演示控制器：`Assets/Scripts/UI/RoundResults/RoundResultsDemoController.cs`
- Game View 验证图：`Assets/Art/UI/RoundResults/round_results_demo_scene.png`

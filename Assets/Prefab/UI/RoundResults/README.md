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
| `CumulativeTotal` | `当前分 / 胜利分` 文本与最终填充位置 |
| `victoryScore` | 积分条最大值，默认 100 |

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
    IsRoundWinner = true,
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

重新生成 Prefab：`Tools > SuperQQ > UI > Build Round Results Prefabs`。

## 演示关卡

打开 `Assets/Scenes/RoundResultsDemo.unity` 即可看到四名模拟玩家的小局结算。
进入 Play Mode 后会自动播放揭示动画；点击 `CONTINUE` 会隐藏面板，按 `R` 可重新播放。

- 场景：`Assets/Scenes/RoundResultsDemo.unity`
- 演示控制器：`Assets/Scripts/UI/RoundResults/RoundResultsDemoController.cs`
- Game View 验证图：`Assets/Art/UI/RoundResults/round_results_demo_scene.png`

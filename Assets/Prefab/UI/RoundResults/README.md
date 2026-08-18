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

## 自定义数据

```csharp
panel.Show(entries, roundIndex: 2, victoryScore: 100, onContinue: OnContinue);
```

`entries` 类型为 `IReadOnlyList<RoundResultPlayerData>`。每个玩家可提供旧累计分、本轮各得分段、最终累计分、玩家颜色与赢家标记。

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

# Active Plan

## Acceptance criteria

- Reusable Prefab under an existing Canvas.
- Reads current round from `PlayerScoreManager` and accepts custom data.
- Shows ranking, player identity, previous cumulative score, colored current-round segments, total, winner and victory line.
- Animated reveal and continue callback.
- No modifications to the reference ExportedProject or dirty Level1 scene.
- Standalone demo scene is saved and visible in the Game View without discarding the pre-existing dirty Settlement scene.

## Checklist

- [x] Identify and pin the SuperQQ Unity MCP instance.
- [x] Inspect existing score and settlement APIs.
- [x] Implement adapter, views, animation and host.
- [x] Generate panel and row Prefabs.
- [x] Render and visually inspect a 1280×720 preview.
- [x] Verify Unity compilation and Console.
- [x] Create `RoundResultsDemo.unity` with Camera, Light, Canvas, EventSystem and live demo controller.
- [x] Save the scene, render the Game View and leave the demo scene active.
- [x] Reproduce the Play Mode row overlap, force layout before animation caching, and verify four distinct row positions.

## Stop condition

Complete when assets compile, Prefab references are valid, the standalone demo scene is readable in Game View, and programmer-facing usage is documented.

# Known Issues

- The bundled cartoon TMP font is Latin-only. The Prefab therefore uses English UI labels; player names containing unsupported CJK glyphs require a Chinese TMP font fallback.
- Full game-flow integration was not enabled automatically because the existing `SettlementController` may already call `NotifyCurrentPhaseEvent`. Enable the Host notification toggle only after choosing a single owner.
- Fixed: dynamic rows previously cached `(0,0)` before `VerticalLayoutGroup` rebuilt, so the reveal animation stacked all rows at the upper-left. `RoundResultsPanel.PrepareView` now forces the layout and each row captures its final position before animation.
- Fixed: dynamically created score segments previously had no Sprite and rendered as sharp rectangles. They now copy the historical fill's rounded Sprite, Sliced mode and pixels-per-unit multiplier.
- The standalone demo scene has a runtime controller and `R` replay shortcut. Play Mode validation now covers the animated populated state and reports zero errors/warnings.
- `RoundResultsDemo` and the dirty `Settlement` scene remain loaded together in the current Editor session. `RoundResultsDemo` is the active scene and its Canvas uses sorting order 1000, so the Game View remains readable without discarding the other scene.

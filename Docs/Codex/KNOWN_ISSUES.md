# Known Issues

- The bundled cartoon TMP font is Latin-only. The Prefab therefore uses English UI labels; player names containing unsupported CJK glyphs require a Chinese TMP font fallback.
- Full game-flow integration was not enabled automatically because the existing `SettlementController` may already call `NotifyCurrentPhaseEvent`. Enable the Host notification toggle only after choosing a single owner.
- The reveal coroutine compiled successfully. The standalone demo scene has a runtime controller and `R` replay shortcut; final automated visual QA used its camera-rendered edit-time state rather than entering Play Mode, because the separately loaded `Settlement` scene was already dirty.
- `RoundResultsDemo` and the dirty `Settlement` scene remain loaded together in the current Editor session. `RoundResultsDemo` is the active scene and its Canvas uses sorting order 1000, so the Game View remains readable without discarding the other scene.

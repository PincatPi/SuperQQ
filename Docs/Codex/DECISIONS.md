# Decisions

## 2026-08-18 — Additive UI instead of replacing settlement

The existing `SettlementController` builds a world-space pillar presentation and participates in game-flow notification. The new request is implemented as a separate screen-space Prefab so teams can adopt it incrementally without destabilizing current round flow.

## 2026-08-18 — Existing score model is the source of truth

`RoundResultsDataAdapter` reads `PlayerScoreManager`, `RoundScoreData`, `ScoreType` and `PlayerSessionManager`. It does not duplicate scoring rules.

## 2026-08-18 — Deterministic UI art

The Prefab uses generated rounded-rectangle/circle sprites and existing cartoon TMP font. No external generative image was necessary, reducing import and licensing dependencies.

## 2026-08-18 — Additive demo-scene creation

`RoundResultsDemo.unity` was created additively because the already-open `Settlement` scene was dirty. This preserved that unsaved scene while allowing the new demo scene to be saved, selected and rendered independently.

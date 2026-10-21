# Lane battle (the battler layer)

Additive: the old game is untouched. A continuous battle in PHASES: one body per soldier walks down lanes between two bases on the Tracer navmesh and fights.
Constants (`LaneRules`): 10 ticks/s, phase 30 s = 300 ticks (also `LaneSetup.PhaseTicks`). The sim steps one phase, then PAUSES on the same tick for both players;
plays are applied only at a pause. Same setup + seed + ordered plays = bit-identical state; compare `StateHash()` at each pause.

API (`Doctrine.Lane`): `await LaneBattle.CreateAsync(setup, seed, tracerContext?)` (async: Tracer bakes and paths asynchronously), `ApplyPlays(plays)` (throws mid-phase),
`await StepAsync(maxTicks?)` (to the next pause), `RunToAsync(tick)`, `Snapshot()`, `StateHash()`, `Replay`, `Layout`, `Status`, `Stats`, `RosterCount/RosterTotal`.
Plays: `SpawnPlay(side, lane, kind, count)`, `OrderPlay(side, lane|-1, FieldOrderKind)` (Advance, Hold, FallBack, Hunt, Guard; Flank refused).
Setup: 1 to 3 lanes (neck / pillars / double neck, walls between, cover, mirrored map), base hp in soldier-lives, hard tick cap (draw), live-body cap per side.

Rules carried over: `UnitRules` stats, counter ring (x3/2), cover, Holy vs Cursed, dug-in (Set) bonus. A strike lands once a second. Not carried: army orders (Rotate, Flip,
Age, Merge, Aura, Cull), cohorts, siege clock, loss triggers.

Pipeline (EvalApp): `Lane.Think` -> `Lane.Move` (Tracer multi-agent paths, at most 24 requests a tick, one shared goal per lane/order so Tracer shares one field) ->
`Lane.Engage` (Tracer engagement service, deterministic resolver, line of sight on the baked mesh) -> `Lane.Resolve` (damage lands at once) -> `Lane.Judge`.
Pleroma: one roster store per side and lane (kind counts; a store reads back at most 8 items). Body position and hit points are host records (a Pleroma holds counts, not floats).

Fairness: ids interleave the two sides and strike/think stagger comes from the side-local spawn number, so the two players' plays at one pause COMMUTE (proof). Residual lean
in mirror duels (15 v 15, 200 seeds): Spearmen 98-102, Archers 108-92 (not settled, see `-- lean`).

Replay = plays + seed + a hash per pause (`LaneReplayCodec`): ~1.3 KB for a 240-body 8-phase match against 4.5 MB for per-tick snapshots (3,400x) and 11 KB for per-pause
snapshots (too sparse to animate). Playback re-simulates and checks every hash (`LaneReplayPlayer`).

Proof: `Showroom\tools\DoctrineLaneProof` (`dotnet run -c Release`: 270 checks, 18 mutants; `-- cost`, `-- hashes`, `-- find-draw`, `-- lean`). Fast tests: `Doctrine.Tests\Battle\LaneBattleTests.cs`.
Hashes equal on 16, 2 and 1 logical cores. Not yet run in a browser/wasm.

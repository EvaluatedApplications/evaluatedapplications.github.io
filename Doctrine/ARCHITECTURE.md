# Doctrine: how EvalApp carries the game

The game is an EvalApp application, not a loop with EvalApp around it. State reaches a step through the CONTEXT, work moves as TASK
DATA, and pipelines run other pipelines.

**Context.** `DoctrineContext : GlobalContext` holds the services (`IArmyService`, `IBattleEngine`, `IGeneral`, `IPlayerInput`), the
Tracer extension's own context object (`TracerContext`), and three `PipelineRef`s: `Round`, `BattleRun`, `BattleTick`. Rules live in the
`DoctrineDomain : DomainContext` (rounds, tick limit, enemy gold). Every step is a `ContextSideEffectStep<DoctrineContext, DoctrineDomain, T>`
added with `AddStep<TStep>(name)`; it calls a seam and returns the next task data. No step names an implementation.

**Pipelines (one domain, three tasks).**
- `Round` (task data `RoundState`): `Army.Draft` -> `General.Suggest` -> `Player.Choose` -> `General.Imitate` -> `Army.Orders` ->
  `Battle.Setup` -> `Battle.Run` -> `Army.Losses` -> `Army.Spoils` -> `General.Outcome` -> `Round.Close` -> `If(!Done)`
  { `Round.Advance`, `Round.Next` = this pipeline by `PipelineRef` }. A round feeds the next by RECURSION; history rides in the data.
- `Battle` (task data `BattleState`): `Battle.Begin` -> `Battle.Ticks` -> `Battle.Finish`. `Battle.Ticks` loops the tick pipeline
  (documented "layers" pattern, `ct` passed down) until a win, loss or the tick limit.
- `BattleTick`: `Battle.FieldOrders` -> `Move` -> `Engage` -> `ApplyLosses` -> `Record` -> `Judge`.
The stage names are constants in `Stages` and appear in `pipeline.Report()`; `EndToEndTests` pins that every slot exists.

**Extensions used and why.** `Eval.App().WithContext(ours)`; `DefineDomain(name, ours)`; `DefineTask`; `AddStep<TStep>`; `If`; `Run(PipelineRef)`;
`AddStep(name, PipelineRef)` (recursion); `Report()`; `WithSingleThread`. NOT used: `WithTuning`/`WithBayesianTuning` (they persist tuner
state on disk), `WithResource` gates (nothing contends), `ForEach`/`AddParallelGroup` (the game has no data-parallel stage; on one thread
they run inline anyway). Package extensions (`WithTracer`, `WithEngagement`, ...) each call `WithContext` with their own object, and
`WithContext` takes ONE: using two fights over the slot. So the builder's `WithTracer(world, ...)` builds the same `TracerContext` and
HOLDS it in `DoctrineContext.Tracer` (EvalApp PACKAGE.md section 20, snippet 4). `WithBattle(ctx => ...)` hands the finished context to
the engine factory.

**Builder.** `DoctrineBuilder.New().WithArmy().WithBattle().WithGeneral().WithPlayer().WithRules().WithTracer().WithSingleThread().Build()`
returns a `DoctrineGame` (three compiled pipelines). Any seam left unset gets its stub. `game.PlayAsync(seed)` plays headless and returns
a `GameResult` (every `RoundRecord` with its `BattleReplay`, plus the player's army code).

**One thread, no disk.** Nothing here starts a thread or touches a file. EvalApp detects a browser (`ExecutionHost.IsSingleThreaded`, reason
"browser (no worker threads)") and yields to the host about every 12 ms between steps (3-4 yields for an 8-round game). The loop
inside `Battle.Ticks` is one step, but each tick runs the nested `BattleTick` pipeline, whose entry and steps are yield points on the same
process-wide slice clock, so a long battle does not freeze the page. A single long step inside a section does not yield. MEASURED: the full 8-round game, trimmed
AOT browser-wasm under Node, plays the same game as desktop (identical digest) in about 4 ms per round on a loaded machine. See `Doctrine.Tests`
and the probe notes in the hand-off report.

**Limits.** A pipeline nested deeper than 256 fails with `PipelineRecursionException`, so `MaxRounds` is capped at 200 (`WithRules` refuses
more). The per-tick loop uses a loop in a step because recursion per tick would hit that same guard. Task data holds live objects (armies,
the battle session): a `RoundState` is a handle to a game in progress, not a serialisable save; the save is the army code.

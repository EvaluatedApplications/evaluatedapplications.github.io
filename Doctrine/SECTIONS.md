# Doctrine: section guide

You build ONE section. The skeleton runs the whole game headless today; you replace one stub with the real thing and the rest of the
game neither notices nor changes. Read `CONTRACT.md` (your seam) and `ARCHITECTURE.md` (how EvalApp carries it) first.

## Who owns what

| Section | Owner | Source folder | Test folder | Stub to replace | Contract tests to pass |
|---|---|---|---|---|---|
| Army (all game state in Pleroma stores) | phasor-owner | `Doctrine\Army\` | `Doctrine.Tests\Army\` | `StubArmyService`, `StubArmy` | `ArmyContractTests` |
| Battle (Tracer arenas, cohorts, engagement, recording) | tracer-owner | `Doctrine\Battle\` | `Doctrine.Tests\Battle\` | `StubBattleEngine`, `StubBattleSession` | `BattleContractTests`, `ReplayContractTests` |
| General (the tiny HoloFormer) | algformer-owner | `Doctrine\General\` | `Doctrine.Tests\General\` | `TableLookupGeneral` | `GeneralContractTests` |
| The page (input, drawing, replay playback) | showroom-owner | the Showroom tool (not here) | | `ScriptedPlayer` (`Doctrine\Player\`) | `IPlayerInput` contract (CONTRACT.md f) |

The shared files (`Core\`, `Runtime\`, CONTRACT/ARCHITECTURE) belong to the skeleton: do not edit them. If a record, token or invariant
must change, say what and why in your report; it is a contract amendment that all sections see.

## How to replace your stub

1. Write your implementation in YOUR folder. Name the types as you like; keep the stub files until yours pass, then delete them.
2. In `Doctrine.Tests\<YourFolder>\` add a derived class beside the stub's binding. Army example:
   `public sealed class PleromaArmyContract : ArmyContractTests { protected override IArmyService Create() => new PleromaArmyService(); }`
   (Battle: derive BOTH `BattleContractTests` and `ReplayContractTests`. General: derive `GeneralContractTests`; override `Examples`
   if your model needs more than the default 40 identical examples.) Run `dotnet test C:\Users\dongy\AboutUs\Doctrine.Tests`.
3. Plug it in with the builder, in one line, nowhere else:
   `DoctrineBuilder.New().WithArmy(new PleromaArmyService())` / `.WithBattle(new TracerBattleEngine(...))` / `.WithGeneral(...)`.
   Tracer: `.WithTracer(world)` holds the Tracer extension's own `TracerContext` in the game context; `.WithBattle(ctx => new
   TracerBattleEngine(ctx.Tracer!))` receives the finished context so the engine can read it.
4. `EndToEndTests` must stay green with your section in place (swap it in `ReplacementTests`-style, or change the default in a test of your own).
5. Add contract cases for any real invariant you discover (in the shared `*Contract.cs`, with the CONTRACT.md line). Never weaken a case.
   A new case needs a mutant in `Mutation\` that makes it fail (see `MutationProofs`); a case nobody has watched fail is only an assertion.

## Rules

- Published packages only. Add a package with `dotnet add package` from `Doctrine\` (latest published; say which in your report). No
  `ProjectReference` into MonoRepo, ever. Already referenced: EvalApp, Phasor, Tracer, AlgFormer.
- Browser first: no threads (`Task.Run`, `Parallel`, locks that wait), no filesystem, no reflection-based serialisation, nothing that needs
  a disk tuner. Pass the `ct` you are given down to every awaited call. Single-threaded code must yield (EvalApp's pipeline yields between
  steps; a very long step in your section does not, so slice it).
- A gap in your own package (a missing operation, a service that needs a disk, a call that blocks): file it under YOUR package's `todo\`
  (for example `Phasor\todo\`) with the real workload, and report it. NEVER work around it host-side in the game (no arithmetic on counts
  outside Pleroma operations, no hand-rolled pathfinding, no fake model).
- A gap in EvalApp (the runtime): file it for evalapp-owner (`EvalApp\todo\doctrine-requests.md`) with numbers.
- Nothing is committed or versioned by you. Do not touch `<Version>`, do not `git commit`. Leave the work in the tree and report; the
  coordinator commits and bumps.
- Nothing under Showroom, website or website-data is touched from here.

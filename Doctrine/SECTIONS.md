# Doctrine: section guide

You build ONE section. The skeleton runs the whole game headless today; you add the real thing beside the reference stub and the rest of
the game neither notices nor changes. Read `CONTRACT.md` (your seam) and `ARCHITECTURE.md` (how EvalApp carries it) first.

## Who owns what

| Section | Owner | Source folder | Test folder | Reference stub (STAYS) | Contract tests to pass |
|---|---|---|---|---|---|
| Army (all game state in Pleroma stores) | phasor-owner | `Doctrine\Army\` | `Doctrine.Tests\Army\` | `StubArmyService`, `StubArmy` | `ArmyContractTests` |
| Battle (Tracer arenas, cohorts, engagement, recording) | tracer-owner | `Doctrine\Battle\` | `Doctrine.Tests\Battle\` | `StubBattleEngine`, `StubBattleSession` | `BattleContractTests`, `ReplayContractTests` |
| General (the tiny HoloFormer) | algformer-owner | `Doctrine\General\` | `Doctrine.Tests\General\` | `TableLookupGeneral` | `GeneralContractTests` |
| The page (input, drawing, replay playback) | showroom-owner | the Showroom tool (not here) | | `ScriptedPlayer` (`Doctrine\Player\`) | `IPlayerInput` contract (CONTRACT.md f) |

The shared files (`Core\`, `Runtime\`, CONTRACT/ARCHITECTURE/this guide, `Doctrine.Tests\Mutation\`) belong to the skeleton: do not edit them.
If a record, token, invariant or tool must change, say what and why in your report; it is a contract amendment that all sections see.

## How to add your section

1. Write your implementation in YOUR folder. Name the types as you like. **Do NOT delete the stub.** It is the reference: the shared mutants
   are built on it, `MutationProofs` proves the clean stub passes every case, and it is what an unset seam falls back to.
2. In `Doctrine.Tests\<YourFolder>\` add a derived class beside the stub's binding. Army example:
   `public sealed class PleromaArmyContract : ArmyContractTests { protected override IArmyService Create() => new PleromaArmyService(); }`
   (Battle: derive BOTH `BattleContractTests` and `ReplayContractTests`. General: derive `GeneralContractTests`; override `Examples`
   if your model needs more than the default 40 identical examples.) Run `dotnet test C:\Users\dongy\AboutUs\Doctrine.Tests`.
3. Make it the game's section in ONE place, the composition root. `Build()` takes each seam from, in order: your own `With*` call, the
   `WithDefaults` root, the reference stub. A real section that needs a world (Battle needs Tracer's) is built in the root, from the finished
   context:
   ```csharp
   var game = DoctrineBuilder.New()
       .WithDefaults(b => b
           .WithArmy(new PleromaArmyService())
           .WithTracer(world)                                           // holds Tracer's own context object in ours
           .WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!))      // gets the finished context, so it can read the world
           .WithGeneral(theGeneral))                                    // the General section, once it exists
       .WithRules(new DoctrineDomain { MaxRounds = 8 })
       .Build();
   ```
   An explicit `With*` on the same builder always beats the root (tests swap one seam that way). The page (or the app's startup) owns the root;
   a section never calls `WithDefaults` itself. `CompositionRootTests` shows the three orders of precedence.
4. `EndToEndTests` must stay green with your section in place (compose it through a root of your own in a test, as `PleromaArmyProofs` does).
5. Add contract cases for any real invariant you discover (in the shared `*Contract.cs`, with the CONTRACT.md line). Never weaken a case.
   **A new or changed case needs a mutant that makes it fail** (see "Mutants" below); a case nobody has watched fail is only an assertion.

## Mutants: the shared tooling (`Doctrine.Tests\Mutation\Mutants.cs`)

Every mutant wraps ANY section, not only the stub, so a real section's owner runs the same mutants through the same tooling:

| Seam | Named mutants (`enum`, one invariant each) | Free-form rewriter | Over a real section |
|---|---|---|---|
| Army | `ArmyMut`: `ClaimsEverythingLegal`, `StaleCode`, `LossesAppliedTwice`, `NondeterministicCreate`, `LegalityOnce`, `LossSlotMatters`, `DraftRefusalNoReason`, `DraftUsesSeed`, `ScoreFallsWithGold`, `VeterancyGrows` | `ReadMutantArmyService(inner, rewrite ReadModel)` | `new MutantArmyService(new PleromaArmyService(), ArmyMut.LegalityOnce)` |
| Battle | `BattleMut`: `FrameTickOffByOne`, `LossesReportedTwice`, `TickLimitIgnored`, `Nondeterministic`, `SoldiersReappear` | `FrameMutantEngine(inner, rewrite ReplayFrame)` | `new MutantOver(TracerWorlds.Engine(), BattleMut.TickLimitIgnored)` |
| General | `GeneralMut`: `IgnoresMask`, `PreTrained`, `OverConfident`, `Forgetful`, `SharedState` | `OutputMutantGeneral(inner, rewrite (input, output))` (also how `SlotConfidence` is mutated) | `new MutantGeneralOver(myGeneral, GeneralMut.OverConfident)`; for `SharedState` hand every wrapper the SAME inner |

`MutantArmyService(m)`, `MutantBattleEngine(m)` and `MutantGeneral(m)` are the one-argument forms over the stubs. To prove a case you added:
add a value to the enum (or use a rewriter), apply it over the stub AND over your section, and assert the named case is in the failures
(`SectionMutationProofs` has the pattern: `Caught(await ContractRunner.RunAllAsync(Cases, () => mutant), "case-name")`). Section-specific
mutants (an engagement service that never hits, a gate that never falls) stay in your own test folder, written against your own seam.

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

## Replay: per-agent sample (proposal for the Showroom owner; NOT in the format)

The recording draws a battle as cohort blobs (centroid, strength) plus `WorldFlags` (the gate). The Tracer section simulates up to 300 agents
(`TracerBattleOptions.MaxAgents`, 24 per cohort); a page that wants to draw crowds, not blobs, would need them in the recording. I did not add
it: it is not cheap. Estimate (arithmetic, not measured): 300 agents x 120 frames x about 2 bytes of delta-coded x/y each is about 72 KB,
over the 64 KB budget the ladder shelf needs (`recording-is-small`). Two shapes that fit, both a version-3 addition with a count of 0 meaning
"none" (1 byte per frame): (a) a KEY SAMPLE, every 4th frame, up to 96 agents, as (blob id, dx, dy) varints, about 96 x 30 x 3 B = 9 KB, with the
page interpolating between key frames as it already does between blobs; (b) a CAP, up to 48 agents per frame every frame, about 48 x 120 x 2 B = 12 KB. The
Battle section decides which agents (stable per blob, first living ones) so playback never recomputes. Until the page proves it needs more than
blobs and the gate state, keep the format as it is: the Showroom owner should try drawing crowds as a per-blob scatter seeded from the blob's
id and strength (cosmetic, derived at draw time, not simulation) and come back with a byte budget if that is not enough.

## LESSONS: what a skeleton must define before owners start

Learned from the first two sections (Army, Battle). Each cost an owner a guess or a contract amendment; a skeleton for the next game should settle
them in its CONTRACT before the first owner begins.

1. **Fix the timing of every rule that has "before" and "after".** "Legal means it applies" was ambiguous inside one doctrine (judge once up
   front, or per token against the changing state?). The real army did one, the stub the other, and the contract case was too weak to see it.
   Say WHEN a rule is evaluated and write a case with a two-step input where the answer differs.
2. **Define the score scale and the power formula's invariants (not the formula).** Both sections invented their own scale (stub raw, Pleroma 100 per
   gold) and nothing said what a score is FOR. State the minimum that any scale must satisfy (pure, monotone in the things it measures, strict where a
   thing has real value) and that scores are comparable only within a section.
3. **Put the world state in the replay on day one.** A recording of cohorts alone cannot show terrain or a changing arena; the gate had to be added
   later as a format version. Ask "what changes during a battle that the units do not show?" and give it a field (the world flags), and decide who
   draws static terrain (the layout, not the recording).
4. **Name the composition root and the default policy.** "Delete the stub" (guide) and "unset seams get the stub" (Build) contradicted each other, and a
   real engine needs a world the builder does not have. Decide: the reference stubs stay, a `WithDefaults` root names the real sections once, an
   explicit choice beats it.
5. **Ship the mutant tooling for real sections with the contract.** Mutants hard-wired to the stubs meant the first real section had to write its own
   (`MutantOver`, `FrameMutantEngine`). Build every mutant as a wrapper over any implementation, plus a free-form rewriter per seam, from the start.
6. **Every field in a shared record needs a reader and a check, or it goes.** `CohortLoss.Slot` and `DraftRequest.Round/.Seed` were used by no
   section. Each now has a stated meaning and a case (a loss applies by kind; a draft ignores its labels). If a field has no reader, drop it at the
   first MAJOR; carrying it costs every owner a question.
7. **Make impossible refusals impossible.** A "command span" refusal was in the contract but the roster size equalled the key capacity, so no input
   could ever trigger it. Check each refusal reason has an input that reaches it, or remove it from the contract.
8. **A limit case must be forced, not hoped for.** "A battle ends inside the limit" only bit when the sample fight reached tick 40, and on the real
   engine it never did. Cases about limits need an input constructed to hit the limit (here: both sides Hold).
9. **A model with a cycle states the cycle.** The veterancy ring wraps in the real army and grew without bound in the stub. A bounded or cyclic
   quantity needs its bound written in the contract and a long-run case.
10. **State where chance enters.** The design said drafts roll with `SampleKey` but the seam took explicit picks. Say once which call is allowed
   to consume randomness (here only `Create`) and test that no other call depends on the seed.
11. **Cost figures belong to a section, not to the skeleton.** "About 4 ms a round" was measured on stub battles; with the Tracer battle it is about
   100 ms. Quote per-section costs next to the section that has them, and say which are measured.
12. **A host needs to SEE what the pipeline decides, not rebuild the loop.** The first page ran one round per run to read the closed round and
   copied the end-of-game rule. Give the player's hands the facts at the seams: the closed round whole (`RoundClosedAsync`, with `Final`), the
   draft report (`DraftedAsync`), the army's orders report (`RoundRecord.Orders`). Ask of every pipeline decision "can the host see the result
   without re-running or re-deriving it?".
13. **Progress is a context hook, not a wrapper.** The page wrapped the engine (and the session) to count ticks, which hid the engine's other
   capabilities. `IRoundObserver` (stage names, battle tick N of limit) is optional, allocation-free and set on the context.
14. **State cancel semantics in the contract.** What throws (`PlayAsync` throws `OperationCanceledException`; a raw pipeline run returns a failure
   carrying it), when it lands (the next step or tick), and what it leaves behind (a half-played round has already changed the army: snapshot
   `ToCode()` first). A host that ships a Cancel button needs all three on day one.
15. **A type must not share its namespace's name.** `Doctrine.Doctrine` forced `using Plan = Doctrine.Doctrine` in every consumer and a class
   named `DoctrinePage`; it is `DoctrineSet` now. Name the namespace and the headline type differently before the first consumer.
16. **Say WHO judges a rule and AT WHAT TIME.** The contract said "a token illegal at the start but legal after an earlier one applies", the
   pipeline dropped every token not in the start mask before the army saw it, so the sentence was unreachable. One rule now: the start mask
   binds the general; the player's army orders go to the army, which judges per token in slot order; the pipeline removes only impossible ids
   and forbidden field orders. When two layers can judge the same thing, name one and write a pipeline-level case with a mutant.
17. **Define what a number MEANS, in words.** `Confidence` is the whole-suggestion exact-match rate, not "how sure of each token"; a host puts
   it on screen, so a wrong gloss becomes a wrong claim. Reserve the next field (`SlotConfidence`) with its range case before anyone needs it.
18. **Optional capabilities are interfaces, and wrappers hide them.** `IArenaLayouts` lets a host draw arenas without naming the Tracer engine; a
   decorator that implements only `IBattleEngine` hides it (by design), so hosts attach hooks to the context instead of decorating sections.
19. **Every clause of an end rule needs an input that reaches it.** The game also ends when the army has no soldiers and under 1 gold, but each
   round's spoils award at least 2 gold before the close, so that clause cannot fire today. Either change the spoils or drop the clause.

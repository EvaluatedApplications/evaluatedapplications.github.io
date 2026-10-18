# Doctrine: the seams (CONTRACT)

One page per seam: what it receives, what it returns, who owns it, what must always be true. The interfaces are in
`Core\Seams.cs`, the shared data in `Core\Records.cs` and `Core\Vocabulary.cs`. The invariants below are enforced by the
contract cases in `Doctrine.Tests` (`ArmyContract`, `BattleContract`, `ReplayContract`, `GeneralContract`). A stub and a real
section run the SAME cases. Changing an invariant means changing this file and the case together, never one alone.

Rules for every seam: integers and strings on the wire (no floats in any record); async-shaped (`ValueTask`) but never blocking a
thread; no filesystem, no threads, no reflection-based serialisation; deterministic for fixed inputs; a refusal is data
(a report or `false` plus a reason), never an exception; the game never names an implementation type.

## (a) ARMY: owner phasor-owner, folder `Doctrine\Army\`, stub `StubArmyService`/`StubArmy`

All game state lives in Pleroma stores and changes only through Pleroma operations. A missing operation is a Phasor gap to file,
never a host-side computation.

| Call | Receives | Returns |
|---|---|---|
| `IArmyService.Create(side, seed)` | a side and a seed | a fresh `IArmy` |
| `IArmyService.TryParse(code, out army, out error)` | an army code | true + army, or false + reason |
| `IArmy.Read()` | nothing | `ArmyReadModel`: cohorts (`CohortRead`), gold, round, `LegalMask` |
| `IArmy.Score()` | nothing | a non-negative int on the section's own scale (see below) |
| `DraftAsync(DraftRequest)` | picks (kind, count); `Round` and `Seed` are labels | `DraftReport` (accepted, refused with reason, gold spent) |
| `ApplyArmyOrdersAsync(Doctrine)` | a doctrine | `ArmyOrdersReport` (applied, skipped) |
| `ApplyLossesAsync(CohortLoss[])` | losses by kind (`Slot` is a record, not a condition) | soldiers actually removed |
| `ApplySpoilsAsync(Spoils)` / `EndRoundAsync()` | spoils / nothing | nothing |
| `ToCode()` | nothing | a short URL-safe string |

Invariants (each is a named case in `ArmyContract`):
- `Create(side, seed)` is deterministic; two armies never share state. CHANCE ENTERS AN ARMY ONCE, IN `Create`: the real army pays for its
  opening levy with the Pleroma's own `SampleKey` on the game's `GameRng` (seeded from side and seed). Nothing after that rolls: a draft is
  decided by its picks alone (`DraftRequest.Round` and `.Seed` are labels for logs and displays; the result must not depend on them), and
  every other call is a function of the army and its arguments.
- A fresh army: round 0, gold >= 0, soldiers > 0, at most 8 kinds (`Roster.Size`), one cohort per kind, every cohort count > 0, slot 0..2,
  veterancy on the ring, power > 0, `Legal` always allows End and has no bit outside the vocabulary.
- `Read()` and `Score()` are pure.
- THE LEGAL LIST IS THE TRUTH: for every army-order token, legal means `ApplyArmyOrdersAsync` applies it; not legal means it is
  skipped, reported in `Skipped`, and changes nothing. The list must restrict (some legal, some not). Field-order tokens are
  ignored by the army (they belong to the battle).
- LEGALITY IS JUDGED PER TOKEN, IN SLOT ORDER, AGAINST THE ARMY AS THE EARLIER TOKENS LEFT IT, not once before the doctrine. So a token that
  was legal at the start but is not after an earlier token is skipped (Merge 0 twice: the second finds nothing to merge), and a token that
  was illegal at the start but is legal after an earlier token applies (Flip a trait nobody holds, then its Aura). A section that evaluates
  the list once before the doctrine fails `orders-see-the-state-earlier-orders-left`.
- Orders keep the army valid (counts > 0, slots 0..2, gold >= 0).
- Draft spends exactly price x count; a refused pick (unknown kind, count <= 0, too little gold) is reported with a non-empty reason and
  changes nothing. There is no separate "command span" refusal: the span IS the roster (`Roster.Size` = 8 = the Pleroma's key capacity), so a
  ninth kind cannot exist. (A section may keep a guard for it; the contract cannot exercise it.)
- Losses are exact and clamped: no count goes below zero, unknown kinds and negative losses remove nothing, the return value is what
  was really removed, and a loss record applies once. An army has one cohort per kind, so a loss is applied by KIND; `CohortLoss.Slot` is
  the slot the cohort fought in, carried for the battle's records, and a loss naming another slot still applies. Losses never raise the score.
- Spoils add gold (never subtract). `EndRoundAsync` advances the round by one.
- VETERANCY IS A POSITION ON A RING of at most 64 places (`ArmyContract.RingBound`): ageing and spoils experience move it round and it wraps
  to 0 after the last place, so it stays in `0 .. period-1` however long the war lasts and a cohort's `PowerPct` stays positive at every
  place. The real army's ring has 9 places, the stub's too. A counter that only grows, or one that clamps at a ceiling, fails
  `veterancy-lives-on-a-ring`.
- SCORE AND POWER: the scale and the formulas are the section's own and are NOT frozen (the stub counts price x power / 100 plus gold, the
  Pleroma army counts price x power plus 100 per gold), so compare scores only from the same section. What must hold on any scale
  (`score-follows-soldiers-and-gold`): the score is a function of the army alone (pure, equal armies equal score, preserved by the army
  code); winning gold strictly raises it; losing soldiers never raises it, one soldier at a time, and an army with no soldiers is worth
  strictly less than the same army with them. `PowerPct` is likewise the section's own number: positive, a function of the cohort's
  veterancy, traits and the active auras; no monotone claim is made (the ring wraps).
- Army code: at most 600 bytes, characters from `A-Z a-z 0-9 - _ . ~ : ; , + =`; `TryParse(ToCode())` reads back with an identical
  `Read().Digest()`, `Score()` and code; the restored army then behaves identically (hidden state is in the code); `TryParse`
  never throws on any input (truncated, doubled, huge, control characters).

## (b) BATTLE: owner tracer-owner, folder `Doctrine\Battle\`, stub `StubBattleEngine`/`StubBattleSession`

Arenas are baked navmeshes; each cohort is a group of agents; field orders pick the behaviour per group; the engagement service
resolves hits. The session owns all simulation state. The pipeline calls, per tick, in this order: `FieldOrdersAsync`,
`MoveAsync`, `EngageAsync`, `ApplyLossesAsync`, `Record()`; it asks `Status` after each tick and stops at the tick limit.

| Call | Receives | Returns |
|---|---|---|
| `IBattleEngine.Arenas` | nothing | the arena catalogue (`ArenaInfo`: id, name, size) |
| `StartAsync(BattleSetup)` | arena id, seed, tick limit, two `BattleSide`s (cohorts + `FieldOrder`s) | an `IBattleSession` |
| `Snapshot()` | nothing | the current `ReplayFrame` (used for frame 0) |
| `Record()` | nothing | ticks +1, then the frame for the new tick including this tick's events and the arena's `WorldFlags` |
| `Finish(tickLimitReached)` | whether the limit stopped it | `BattleResult` (outcome, ticks, losses per side) |

Invariants (`BattleContract`, through the game's own battle pipeline):
- At least one arena; ids unique; every arena runs a battle.
- A finished battle's outcome is never Running; it ends within the limit; `Result.Ticks == session.Tick`. When the limit stops a
  running battle the side with more soldiers left wins, equal is a Draw. A side with no soldiers has already lost (zero ticks).
  The sample fight may end before its limit, so the limit is also forced: with BOTH sides on Hold orders and a limit of 10 ticks, nobody can
  be wiped out, so on every arena the battle runs exactly 10 ticks (11 frames) and is decided by soldiers left, never Running
  (`a-battle-nobody-can-win-in-time-runs-to-the-limit-and-is-decided`).
- Frames: tick 0 is the start (no events), then one frame per completed tick, consecutive. `Frames.Count == Ticks + 1`.
- Blobs: ids unique and stable in every frame (a dead cohort stays with strength 0), strength never negative and never rises; the
  start frame equals the setup's cohorts (kind, slot, soldiers).
- Result losses equal the soldiers that vanish between the first and last frame, per side, and no cohort loses more than it brought.
- Events are between opposing sides, name known blobs, and deal damage > 0; fighting armies produce events.
- The same setup gives the same recording on the same device (`ReplayCodec.Digest` equal). Overwhelming force wins.
- A battle is simulated ONCE by one authority and played back everywhere else. Cross-device equality is NOT required (floats); it is
  measured, not promised.

## (c) GENERAL: owner algformer-owner, folder `Doctrine\General\`, stub `TableLookupGeneral`

A tiny model per player: token summary in, doctrine tokens out, masked by the legal list. It never touches a battle. Its weights are not
game state and nothing pre-trained ships.

| Call | Receives | Returns |
|---|---|---|
| `SuggestAsync(GeneralInput)` | round, summary tokens (`GeneralSummary`, 14 ids in 0..15), `LegalMask` | `GeneralOutput`: a `Doctrine`, confidence 0..1000 |
| `LearnAsync(GeneralExample)` | input, the doctrine the player set, whether it overrode the ghost | nothing (imitation) |
| `LearnOutcomeAsync(GeneralOutcome)` | input, doctrine, result, whether the player won | nothing (success-weighted) |
| `ExamplesSeen` | | number of `Learn*` calls |

Invariants (`GeneralContract`):
- A new general has seen nothing, suggests no orders, confidence 0 (nothing pre-trained).
- EVERY suggested non-End token is allowed by the input's mask, for any mask; a mask allowing nothing gives an empty doctrine.
  The mask is a rule applied to the output, not a hint to the model.
- Confidence is in 0..1000. After `Examples` (default 40, raise it in the derived test class if the model needs more, never above ~60)
  identical examples it suggests that doctrine with confidence above the untrained value.
- Outcome learning favours what won: choice B that kept winning beats choice A that kept losing.
- `Suggest` is a pure read; two generals taught identically answer identically; generals share no state; odd input (empty summary,
  wrong length, huge round) never throws and still answers legally.
- Float inference never runs inside a battle two devices must agree on: the general decides in its owner's session and its choices are
  recorded as plain doctrine tokens.

## (d) DOCTRINE: shared vocabulary, `Core\Vocabulary.cs`, changed only by amendment here

54 tokens (id 0 = End/pad): Rotate1-2, Flip0-7, Age, Merge0-7, Aura0-7, Cull0-7 (army orders), then six field orders x three groups
(Advance, Hold, Flank, FallBack, Guard, Hunt at group 0..2). Token ids are wire data; never renumber. A doctrine is four slots
(`Doctrine` struct); `LegalMask` is one ulong over the token ids; `Roster` is the eight kinds with traits and prices;
`DoctrineVocabulary.FieldOrders/ArmyOrders` split a doctrine. The vocabulary is at most 64 so a mask is one word.

## (e) REPLAY: record format, `Core\Replay.cs`, owned by the skeleton; produced by Battle, played by the page

`BattleReplay` = version, arena, seed, tick limit, frames (blobs with quantised integer x/y at 1/16 unit, strengths, events, WORLD FLAGS),
result. `ReplayCodec` is a hand-written varint binary format (magic `DRP`, `CurrentVersion` 2); `TryDecode` never throws and reads
versions 1 and 2 (a version-1 recording has flags 0 and is written back bit for bit in the version-1 layout); `ReplayPlayer` hands out
the recorded frames, nothing is recomputed. `ReplayContract` checks: version and header, ticks from 0 and consecutive, bounded positions,
world flags (below), final strengths agree with the result, codec round trip is bit for bit and deterministic, a recording is at most
64 KB, damaged data is refused with a reason, the player yields the recorded frames themselves. Breaking changes to the format bump
`CurrentVersion` and keep reading the versions before it.

WORLD FLAGS (`ReplayFrame.WorldFlags`, an int bitset, `WorldFlag` holds the names): the part of the arena that CHANGES during a battle,
which the cohorts cannot show. Bit 0 `GateFallen`: the arena's gate has fallen (set from the first frame recorded after it fell, so the page
swaps the gate's picture on that frame). Rules: only defined bits (`WorldFlag.Defined`) are used; the start frame is 0; a bit never clears
once set; the digest and the codec carry the flags. Terrain itself is NOT in the recording: it is static per arena and the page draws it from
the arena layout (the Tracer section's `LayoutOf(arenaId)`); the flags say which state of that layout each frame is in. Per-agent positions
are not in the format (proposal and byte budget in SECTIONS.md, "Replay: per-agent sample").

## (f) PLAYER INPUT: owner showroom-owner (the page), stub `ScriptedPlayer`

`IPlayerInput`: `DraftAsync` returns the picks, `ChooseAsync` sees the ghost and returns a `PlayerChoice` (accept or override). The
round pipeline enforces the legal mask on whatever it returns (illegal tokens are dropped and counted in `RoundRecord.IllegalDropped`).

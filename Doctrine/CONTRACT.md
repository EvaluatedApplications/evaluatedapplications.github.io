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
| `ApplyArmyOrdersAsync(DoctrineSet)` | a doctrine | `ArmyOrdersReport` (applied, skipped; the round record keeps it) |
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
  the list once before the doctrine fails `orders-see-the-state-earlier-orders-left`. This is a property of the ARMY, and the pipeline lets
  it show: the player's army orders reach the army whole (see "Who judges a token" below, proved end to end).
- Orders keep the army valid (counts > 0, slots 0..2, gold >= 0).
- A FLIP IS PAID FOR (2026-10-19, `flip-is-paid-for-and-legal-only-when-affordable`). A Flip relabels one trait bit on every cohort; each soldier whose
  new trait set is worth more costs the difference in gold (`Roster.RelabelCost`, worth = `Roster.TraitPrice` = ceil(2 x damage x (100 + reach) / (taken x 100))
  from the battle's stat tables, reach = `UnitRules.ReachPct`, the free fire a longer weapon gets while a melee unit closes: 20 percent for range 7,
  32 for range 10), a soldier made worse is free. The legal list offers the flip only while the army can pay the sum, and
  the order takes exactly that gold. Before this a Flip cost nothing: one "Flip Machine" token took a Spearmen levy from 0.53 to 0.91 wins
  (todo\difficulty-2026-10-19.md). Since the third round the draft price IS the worth (see DRAFT PRICES below).
- AN AGE IS PAID FOR WHEN THE RULE IS ON (`ArmyRules.PriceAge`; OFF in the game since the third round, 2026-10-19: priced Age made a mass of cheap Spearmen unbeatable; the case runs on `ArmyContract.PricedRules`, `age-is-paid-for-and-legal-only-when-affordable`). In the game an Age is free and always legal. When ON: The army pays ceil(sum over its soldiers of worth x the power
  points one more veterancy step gives, `Roster.AgeWeight`) / 100 gold; the step that wraps the ring back to 0 loses power and costs nothing. Legal only while the
  army can pay. The sum is a Pleroma read (InnerProduct of the keys-only projection with a weight per kind), not a host multiplication.
- AN AURA IS RENTED WHEN THE RULE IS ON (`ArmyRules.PriceAura`, OFF in the game, case `aura-is-rented-and-legal-only-when-affordable` on `PricedRules`; free in the game). When ON: An aura lasts the round, so it costs the sum over soldiers of worth x the power points
  it adds (`Roster.AuraWeight`: the new bonus is the soldiers carrying the trait, capped at 50, never below an aura already active) divided by the rounds a
  bought body would still serve (`Roster.AuraRentalDivisor` = 8 - round, at least 1), rounded up. Legal only while the army can pay.
- BANKED GOLD PAYS INTEREST (`banked-gold-pays-interest-when-a-round-closes`). When a round closes the purse pays `Roster.Interest` = gold / 7 (floor): the rate at
  which banking one round at the start of an 8-round campaign breaks even in soldier-rounds. Done by `Pleroma.MapCounts` on the purse (no new Pleroma operation).
- DRAFT PRICES ARE WORTH (third round, `ArmyRules.DerivedDraftPrices`, ON in the game): a draft costs the kind's worth (`Roster.TraitPrice`, reach in): Spearmen 2, Shieldwall 3, Riders 2, Archers 2,
  Priests 2, Wights 3, Wolves 3, Catapults 5. `Roster.PriceOf(kind)` is that price; the old typed table (1, 2, 2, 2, 3, 2, 2, 4) is `Roster.TypedPrice`. The scripted enemy and the scripted player size
  their picks with it, so the enemy follows the same rule. The army charges exactly the price its rules name (`An_army_charges_the_draft_price_its_rules_name`).
  The rules are switches of `ArmyRules` (default = the game): a measurement uses them to price one lever at a time, a mutant turns one off.
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
  running battle the side with more POWER left wins, equal is a Draw (third round: not soldiers, so a standoff is not won by whoever bought the most bodies). Power = the sum over the side's\n  cohorts of soldiers x power% x damage% x (100 + reach) / taken% (`UnitRules.Rating`), the quantity a kind's worth rounds up to gold; of one kind it is the soldier count. A side with no soldiers has already lost (zero ticks).
  The sample fight may end before its limit, so the limit is also forced: with BOTH sides on Hold orders and a limit of 10 ticks, nobody can
  be wiped out, so on every arena the battle runs exactly 10 ticks (11 frames) and is decided by power left, never Running
  (`a-battle-nobody-can-win-in-time-runs-to-the-limit-and-is-decided`).
- Frames: tick 0 is the start (no events), then one frame per completed tick, consecutive. `Frames.Count == Ticks + 1`.
- Blobs: ids unique and stable in every frame (a dead cohort stays with strength 0), strength never negative and never rises; the
  start frame equals the setup's cohorts (kind, slot, soldiers).
- Result losses equal the soldiers that vanish between the first and last frame, per side, and no cohort loses more than it brought.
- Events are between opposing sides, name known blobs, and deal damage > 0; fighting armies produce events.
- A side's LOSS TRIGGER (BattleSide.Trigger, vocabulary section d) fires at the start of the first tick at which the side has lost N quarters or more of the soldiers
  it brought, never before anything was lost, from the side's own losses only, and stays fired; from then on its cohorts follow the Then orders (a group not named
  advances). A side with no trigger, or one that never fires, plays exactly as before. Proved in TracerTriggerTests with three broken variants watched red
  (never fires, fires at the first tick, reads the enemy's losses: TracerBattleMutationProofs).
- THE COUNTER CYCLE (Tracer section, third round, `TracerBattleOptions.Counters`, ON; the stub has no stat tables and no cycle). A kind's class is read from the stat tables (`UnitRules.ClassOf`: the largest of speed,
  reach and armour edges over the plain soldier; none = Plain): Spearmen, Wights Plain; Riders, Wolves Swift; Archers, Priests, Catapults Ranged; Shieldwall Armoured. Each class hits the next class half again as hard
  (the holy-against-cursed factor, `UnitRules.CounterNum/CounterDen`): Plain over Swift, Swift over Ranged, Ranged over Armoured, Armoured over Plain. It stacks with cover and the holy rule and changes no price. Proved in
  `CounterAndLimitTests` (the class table, the ring, the factor, every edge of the ring clearer than with the option off) with the cycle off and the cycle read backwards watched red.
- The same setup gives the same recording on the same device (`ReplayCodec.Digest` equal). Overwhelming force wins.
- A battle is simulated ONCE by one authority and played back everywhere else. Cross-device equality is NOT required (floats); it is
  measured, not promised.

## (c) GENERAL: owner algformer-owner, folder `Doctrine\General\`, stub `TableLookupGeneral`

A tiny model per player: token summary in, doctrine tokens out, masked by the legal list. It never touches a battle. Its weights are not
game state and nothing pre-trained ships.

| Call | Receives | Returns |
|---|---|---|
| `SuggestAsync(GeneralInput)` | round, summary tokens (`GeneralSummary`, 14 ids in 0..15), `LegalMask` | `GeneralOutput`: a `DoctrineSet`, confidence 0..1000, and an optional `SlotConfidence` |
| `LearnAsync(GeneralExample)` | input, the doctrine the player set, whether it overrode the ghost | nothing (imitation) |
| `LearnOutcomeAsync(GeneralOutcome)` | input, doctrine, result, whether the player won | nothing (success-weighted) |
| `ExamplesSeen` | | number of `Learn*` calls |

Invariants (`GeneralContract`):
- A new general has seen nothing, suggests no orders, confidence 0 (nothing pre-trained).
- EVERY suggested non-End token is allowed by the input's mask, for any mask; a mask allowing nothing gives an empty doctrine.
  The mask is a rule applied to the output, not a hint to the model.
- CONFIDENCE MEANS ONE THING: `ConfidencePermille` is about the WHOLE suggestion, the general's running rate of exact whole-doctrine matches
  (its answer equalled the doctrine the player then set), not a per-slot or per-situation figure. The contract fixes only its range and that it
  rises with consistent evidence; a host must say "how often its whole suggestion has matched yours", never "how sure it is of this token".
- `SlotConfidence` is optional (null by default, the pipeline carries it untouched, no digest sees it): when a general fills it
  it is a `SlotPermille`, four readings (one per doctrine slot) each in 0..1000 (`slot-confidence-is-absent-or-four-permille`). `TinyHoloGeneral`
  fills it as a per-slot MATCH RECORD (not a probability): a slot counts in an imitation example when the doctrine it would have shown or the
  player's is not End there, a hit when the two tokens are equal; slot k reads `1000 * hits_k / (counted_k + 2)` (integer division, so never above
  the measured rate, 0 for a new general or a slot never used, 5 straight matches read 714), over the same recent window as the confidence. Computed
  from observed choices only, deterministic.
- THE MASK IS THE START MASK: the input's `Legal` is the army's list at the START of the round, and the general only suggests tokens it allows,
  so it never suggests an order that becomes legal only after another (the player may; see "Who judges a token"). The pipeline enforces this on
  the ghost again, so a broken general is cleaned and counted, never trusted.
- Confidence is in 0..1000. After `Examples` (default 40, raise it in the derived test class if the model needs more, never above ~60)
  identical examples it suggests that doctrine with confidence above the untrained value.
- Outcome learning favours what won: choice B that kept winning beats choice A that kept losing.
- `Suggest` is a pure read; two generals taught identically answer identically; generals share no state; odd input (empty summary,
  wrong length, huge round) never throws and still answers legally.
- Float inference never runs inside a battle two devices must agree on: the general decides in its owner's session and its choices are
  recorded as plain doctrine tokens.

## (d) DOCTRINE: shared vocabulary, `Core\Vocabulary.cs`, changed only by amendment here

57 tokens (id 0 = End/pad; VERSION 2 since 2026-10-19): Rotate1-2, Flip0-7, Age, Merge0-7, Aura0-7, Cull0-7 (army orders), then six field orders x three groups
(Advance, Hold, Flank, FallBack, Guard, Hunt at group 0..2), then the loss triggers `WhenLost25/50/75` (ids 54..56, family `Trigger`, kind `TriggerKind` = 100).
Version 1 was ids 0..53 and they did not move. Token ids are wire data; never renumber. A doctrine is four slots
(`DoctrineSet` struct, named so it does not shadow the namespace `Doctrine`); `LegalMask` is one ulong over the token ids; `Roster` is the eight kinds with traits and prices;
`DoctrineVocabulary.FieldOrders/ArmyOrders/Trigger` split a doctrine. The vocabulary is at most 64 so a mask is one word (57 used).

THE LOSS TRIGGER. The FIRST trigger token splits a doctrine's field orders in two: those before it are the side's OPENING orders (last word per group wins, as
without a trigger), those after it (last word per group) are the "then" orders, and a group not named after it advances. When the side has lost N quarters or
more of the soldiers it brought (integers: lost x 4 against N x brought, read at the start of each tick, latched), its cohorts take their "then" orders for good.
It reads only the side's own losses. A doctrine with no trigger token reads exactly as in version 1. The trigger token is not an army order (the army ignores it, like
a field order) and is offered by the legal list whenever a line holds a cohort. The setup carries it as `BattleSide.Trigger` (a `LossTrigger`); the replay format is
unchanged (it records cohorts, and the switch is seen as the movement it causes).

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

`IPlayerInput`: `DraftAsync` returns the picks, `ChooseAsync` sees the ghost and returns a `PlayerChoice` (accept or override). Two optional
hooks tell the player what happened (default interface methods that do nothing, so a player that ignores them is unchanged):
- `DraftedAsync(DraftReport, ct)`: the army's own verdict on the picks (accepted, refused with a reason, gold spent), called after the army
  applied them and before the enemy is raised.
- `RoundClosedAsync(RoundRecord, ArmyReadModel after, ct)`: called by the close stage once per round with the whole record (doctrine, `Orders`
  report, replay, result, spoils, `Final` = this round ended the game) and the player's army as the next round finds it. A host plays the whole
  game in ONE run and shows each round from this call; it never needs to copy the end-of-game rule, `Final` is the pipeline's answer.

Order inside a round for a player: `DraftAsync`, `DraftedAsync`, `ChooseAsync`, (battle), `RoundClosedAsync`.

## Who judges a token (one rule, proved end to end in `WhoJudgesATokenTests`)

- The START mask (the army's `Legal` when the round begins) binds the GENERAL. Its suggestions must be legal at the start (section c), and
  the pipeline removes any token of the ghost the start mask forbids and counts it in `IllegalDropped`, so the player never sees it.
- The PLAYER's doctrine is not masked by the start. Its ARMY orders go to the army whole; the army judges each one per token, in slot order,
  against the army as the earlier tokens left it (section a). A token illegal at the start may therefore apply by its turn (Flip a trait nobody
  holds, then its Aura); a token legal at the start may be skipped (Merge 0 twice). What the army skips is counted in `RoundRecord.Orders`
  (`Skipped`) and the record's `PlayerDoctrine` keeps it: the record shows what the player asked, `Orders` shows what the army did.
- The pipeline itself removes from the player's doctrine, and counts in `IllegalDropped`, only (1) ids outside the vocabulary and (2) FIELD
  and TRIGGER orders the start mask forbids (the army never judges a field order, and a group with no cohort has nobody to order).
- `IllegalDropped` therefore counts what the PIPELINE removed (the general's forbidden tokens, the player's impossible ids and field orders),
  never what the army skipped.

## (g) HOST HOOKS: optional, none changes a game

- `IRoundObserver` (`WithObserver(..)` or `DoctrineContext.Observer`, settable between runs): `StageEntered(stage, round)` for every round
  stage by its `Stages` name, `BattleStarted(setup)`, `BattleTick(tick, tickLimit)` after each recorded tick, `BattleFinished(result)`. Every
  method is a no-op by default and takes only ints, constant strings and records the pipeline already holds, so it allocates nothing; with no
  observer a call costs one null check. An observer must be quick and must not throw. A host needs no pass-through engine wrapper.
- `IArenaLayouts : IBattleEngine` (the Tracer engine has it) and `DoctrineContext.LayoutOf(arenaId)`: how to draw an arena, null when the engine
  does not say or the id is unknown. A wrapper that implements only `IBattleEngine` hides the capability by design. `ArenaLayout`: `Cells` are
  rows of text, a cell is `CellSize` ARENA UNITS wide and tall; `G` cells are walkable until the gate falls and BLOCKED from the frame that
  first carries `WorldFlag.GateFallen`; gate numbers are at 1/16 unit like positions.
- CANCELLATION: every seam call receives the run's `ct`. Cancelling it (from an input, an observer, `RoundClosedAsync`) makes `PlayAsync` throw an
  `OperationCanceledException`; a host that runs the pipeline itself gets a failed result carrying it. The cancel lands at the next step or tick,
  so a round cancelled mid-way has already changed the player's army (the draft, the orders): snapshot `ToCode()` before the round and restore
  from it, as the page does. Rounds already closed were delivered.

## The offers (fourth round 2026-10-19: the roguelike round)
Every round the player chooses from what the game OFFERS, drawn from the game seed (`SeededOffers`; same seed, round, purse and legal list give the same offer; a reroll has its own stream).
- **Units, as the round opens** (`Offer.Units`): up to 6 packs `UnitPack(Kind, Count, Cost)`, one per kind (distinct), drawn from the kinds the host's `OfferRules.KindPool` allows. `Count` is a multiple (1, 2, 3 or 5) of the kind's BASE pack, `max(1, purse / (6 x price))` soldiers, so the offer grows with the purse; `Cost = Count x Roster.PriceOf(Kind)`; no pack costs more than the purse (a flagged `Affordable = false` teaser only when `AllowUnaffordable`, off in the game); 6 packs, or one for every kind the purse can buy at all.
- **Orders, after the draft** (`Offer.Orders`): 8 distinct tokens, sorted, all legal at that moment (the draft changes gold, lines and kinds), from the host's `OrderPool(round, token)`. Guarantees (each `OfferRules` switch): at least 3 field orders touching at least 2 lines, at least 1 army order, each only as far as the legal list allows. `Rerolls`: the player may ask (`IPlayerInput.RerollAsync`) up to `MaxRerolls` (2) times, each paying `RerollCost` (1) gold to the army (`IArmy.PayAsync`: true and the gold gone, or false and nothing changed); the new offer is drawn from the legal list as the payment left it. A reroll the purse cannot pay is refused and counted.
- **A pick is** a whole offered pack (`UnitOffer.Pick(pack)`, kind AND count exactly; any subset, each pack once; the ARMY still judges gold) and up to four offered tokens, each once. The pipeline refuses the rest: a pick outside the offer never reaches the army and comes back in `DraftReport.Refused` with the reason `"not on offer"`; a token outside the offer or picked twice is removed from the doctrine. Both are counted in `RoundRecord.OfferRefused` (not in `IllegalDropped`, which stays "outside the vocabulary or a forbidden field/trigger order").
- **The general** is masked to the offer AND the legal list (only the mask changes; the net's input encoding is unchanged), so its action space is "pick up to 4 of 8". What it offers outside is removed and counted in `IllegalDropped`.
- `RoundRecord` carries `UnitOffer`, `OrderOffer` (as it stood when the player chose), `Rerolls`, `OfferRefused`. Replay and army codes are unchanged; the game digest ignores the offers.
- `DoctrineDomain.Offers = OfferRules.Off` is the old free choice: nothing is offered, nothing refused, an `Active = false` offer is what the player receives. A `IPlayerInput` written before the offers keeps working under `Off`; under the default its free picks are refused.
- **Placement** (`ArmyRules.BalancedPlacement`, on): a NEW cohort joins the rank holding the fewest soldiers of its class (`UnitRules.ClassOf` of its live traits), ties to the rank with fewest soldiers, then to the lowest rank, so ranks are mixed on purpose; a kind already standing keeps its rank; `Rotate` (three configurations) is the only formation move. Off = the old kind mod 3.
Contract cases: `OfferContract` (any `IOfferSource`), `OfferTests` (pipeline), `ArmyContract` (`a-fee-is-paid...`, `a-new-cohort-joins...`, `a-kind-keeps-its-rank...`); mutants `MutantOfferSource`, `ArmyMut.PayIsFree/PayAlwaysYes`, rules switches `Enforce`, `Guarantees`, `BalancedPlacement`.
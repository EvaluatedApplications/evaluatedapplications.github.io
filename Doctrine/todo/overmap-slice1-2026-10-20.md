# Overmap, slice 1 (Doctrine library side), 2026-10-20

Design: `design-queue-2026-10-19.md` items 9, 10, 10b, 11. Code: `Doctrine\Campaign\` (`CampaignMap.cs` types and the seeded generator, `CampaignStore.cs` the Pleroma stores,
`CampaignGame.cs` the epoch loop, briefing and `DoctrineGame.NewCampaignAsync`). Tests: `Doctrine.Tests\Campaign\CampaignTests.cs`. PvP stays paused; the sealed-pick structure is kept
(`ILaneOpponent`). The fixed 3/5/8-round games are untouched (digest pins unchanged).

## Pre-registered: what must hold (each is a named case; each mutant is a probe that must turn its case red)
1. DETERMINISM. The map is a pure function of (seed, arena catalogue size, rules). Mutant `MapNotDeterministic`.
2. SYMMETRY under a seat swap (a fairness check though PvP is paused): the two ends of every lane share arena, feature and yield and are held by opposite sides; the two capitals match.
   Mutant `MapNotSymmetric`.
3. SEALED PICK. The enemy's lane depends on (seed, epoch) only, never on the player's pick; `ILaneOpponent` is handed a `LaneTurn(seed, epoch)` and nothing else; the briefing carries no
   enemy pick. Mutant `EnemyPeeks` (the enemy reads the pick).
4. STANDARDS arithmetic. Three at the start; a lost battle costs one; every fifth battle WON restores one, never above three; zero ends the run. Model-based over 40 random sequences.
   Mutants `RestoreEveryFourth`, `RestoreIgnoresMax`, `LossCostsNothing`.
5. CONCEDE costs ground, never a standard, and fights no battle. Mutant `ConcedeCostsStandard`.
6. UNLOCK ON HOLD, exact: the offer pool at the start of every epoch is the base kinds plus the kinds of the features the player holds at that moment (stated independently in the test),
   and a province lost removes its unlock from the NEXT offer. Mutant `UnlockKeptAfterLoss`.
7. BRIEFING = BATTLE. For every lane: the arena, the besieger, the recording's besieger, the enemy at the choose point and the enemy's soldiers in the setup equal the briefing's
   (forced battles on the stub, real battles on the Tracer engine). Mutant `BriefingArenaWrong`.
8. PERSISTENCE. A lane army's wealth at its next battle is at most (wealth before - what the fight took + the heal rate + interest slack): a beaten army returns weaker.
   Mutant `EnemyRecreated` (a full levy each time, the old problem).
9. OFFERS OFF-switch parity. With `Offers = OfferRules.Off` a campaign battle is the open market (unlocks widen nothing). A `FieldScenario` equal to a normal round leaves the same
   recording, digests and spoils as the normal round (Tracer engine); the fixed-game digest pins are unchanged.
10. Yield: the gold paid at epoch close equals the sum of the held provinces' counts, and the army's gold equals before + interest + paid.

Run: 35 cases, all green; Doctrine.Tests 681 green (was 646). Each mutant case was watched to PASS only because its rule failed under the probe (it asserts the check is not null).

## The rules as built
- MAP. Two capitals; three lanes of three provinces, index 0 next to the player's capital. Start: index 0 the player's, index 1 neutral, index 2 the enemy's. Each province has an
  arena (an index into the engine's catalogue; siege or open is the arena's own rule), a feature and a yield. The three middle provinces carry Barracks, Stables, Shrine in a seeded
  order (one per lane); the two ends of a lane are mirrored (Granary or plain, same arena); capitals share an arena. Yield: plain 1, Barracks/Stables/Shrine 2, Granary 3, capital 1.
- PLEROMA. Province = item (key lane x 12 + index, capitals 36 and 37), feature and arena = flags (bits 0..3 and 4..6), YIELD = the count; OWNER = which of three stores holds the item
  (player, neutral, enemy); taking a province is two Adds. Gold paid = `Total()` of the player's store. A held feature = `CountWhere` on the player's store. Standards = the count of a
  complete one-key store (a restore is `MapCounts` with a clamp). "Every fifth win" = a ring of five places (`Tick`, a wrap to 0), the veterancy idea. Fields won and the epoch are counts.
  No Phasor gap met. Host-side: the choice of which operation to run (the lane's next province is read by key order) and the display `Score` (fields won + provinces held).
- EPOCH. The player's sealed pick is (lane, push or concede). The enemy's lane is `ScriptedLanes` (seeded by seed and epoch). Same lane and a push: ONE battle through the round pipeline
  (`RoundState.Field` = a `FieldScenario`: arena, besieger, the lane's persistent enemy army, the epoch's offer rules; the pipeline ends after the battle, no comeback muster). The battle
  is fought at the player's TARGET in that lane (first province outward he does not hold; the enemy capital when the lane is all his). Win: he takes it. Loss: the enemy takes ITS target
  (first province from its side it does not hold; our capital when the lane is all its own) and the player loses one standard. Draw: nothing moves, nothing is lost. Different lanes: both take
  their targets unopposed (a race). Concede: no push, no battle; the enemy takes its target in its lane unopposed. A capital taken ends the war (both in one epoch = a draw);
  out of standards or the epoch limit (24 in a fixed war) ends it too. Besieger = `RoundRules.BesiegerFor(rules, arena, epoch)`, the existing parity swap on the epoch number.
- CLOSE. After the battle (or the pass) the army's round ends (interest), then the held provinces pay their yield as spoils, then the enemy's lane armies muster.
- UNLOCKS. Base kinds with nothing held: Archers, Wights, Catapults. Barracks adds Spearmen and Shieldwall, Stables Riders and Wolves, Shrine Priests and opens the Aura (banner) order.
  Granary opens no kind (it pays 3 gold instead of 1). Applied through the host pool hooks `OfferRules.KindPool` and `OrderPool`, built per epoch from the unlocks held when it opens, so
  they widen the choice and add no power; the soldiers already in the army stay. The offers must be ON for an unlock to mean anything.
- STANDARDS, SCORE. Score = fields won + provinces held (capitals not counted).
- ENDLESS (`CampaignRules.Endless`). No enemy capital. When the player holds a whole lane a fresh enemy province appears beyond it (seeded, up to 12 per lane) so a lane never runs
  out. The enemy's scheduled strength grows by `EnemyEpochGold` per epoch with no cap. The player's capital can still fall.
- BRIEFING (`CampaignGame.BriefingAsync`). Per lane: the target province, `EndsWar`, `ArenaBrief` (the existing type: arena, besieger, layout), `EnemyBrief` (the existing type, read from
  the lane's very army), the feature it unlocks, the kinds that are new, whether it opens the Aura order; and standards, wins to the next restore, score, held unlocks, the kinds the offer draws from.
- ENEMY ARMIES (item 7, chosen simply). Three persistent armies, one per lane, made once. Nothing is re-levied. At the end of every epoch each musters back TOWARDS its schedule
  (`EnemyBaseGold + EnemyStepGold x depth`, plus `EnemyEpochGold x epoch` when endless, read on the wealth scale of `Roster.ExpectedEnemyWealth`), by at most `EnemyHealGold` (10) of
  wealth, then the scripted opponent drafts with the purse. A beaten army therefore returns weaker and heals at that rate; a lane nobody fights stays at its schedule (no snowball).
  Depth = the target's index from the player's capital (the capital: 3), so the army gets stronger as the player pushes towards the enemy capital.

## Decided where the brief was open
- Battle province = the player's target (the border moves one province either way); the besieger keeps the existing even/odd parity rule on the epoch.
- Concede is a pass on that lane (not a second advance for the enemy); the lane number picks nothing else. A draw is neither a win nor a loss.
- Feature layout: one of Barracks, Stables, Shrine per lane in the neutral middle (so each lane is worth fighting for), Granary or plain at the homes; the start pool is the three
  kinds no feature owns.
- No draft in a pass or a race epoch (the draft is a stage of the battle round); gold banks.
- Unlocks are read when the epoch opens: a province won or lost in an epoch changes the next offer, never this one.

## Findings for the coordinator
- SCRIPTED ENEMY GOLD IS NOT SPENDABLE ABOVE 12. `RaiseEnemyAsync` creates the enemy with `IArmyService.Create` (a purse of `StartGold` = 12) and drafts the scripted picks sized to the round's
  enemy gold (14 + 4 x round); the army refuses a pick the purse cannot pay ("gold"). So in the fixed games the enemy of later rounds is the same levy plus at most 12 gold of picks:
  the curve in the briefing (`EnemyBrief.Gold`) is the figure the picks were SIZED to, not what was bought. (From reading the code; not measured. It is the same "full levy every round"
  cause. The campaign avoids it: lane armies are topped up with spoils before they draft.)

## Limits
- No save or restore code for a campaign yet (the stores serialise; not wired). A cancel mid-battle leaves the map untouched and the army changed (CONTRACT.md g).
- A lane has at most 12 provinces in endless; at the end of a cleared 12-lane a push there is refused ("nothing left to assault").
- Enemy `Score` is not tracked; the lane armies are scripted by `IOpponent` (picks only), the enemy's army orders are the scripted doctrine.
- Fighting two lanes at once, scouting, captain/boss provinces and the per-feature order pool beyond the Aura are not built.
- The map depends on the engine's arena count (3 stub, 4 Tracer): a seed gives the same map only on the same catalogue.

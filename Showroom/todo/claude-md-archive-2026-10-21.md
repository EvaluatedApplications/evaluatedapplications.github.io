# CLAUDE.md overflow, 2026-10-21 (moved verbatim from the Doctrine tool paragraph to keep CLAUDE.md inside its budget)

  - **OFFERS ON** (`DoctrineSession.GameRules` = `OfferRules.Default` + the ladder as pools): THE MUSTER (`.dc-pack`, a tap buys a company whole,
    each shows the line it would join, asked on COPIES; the game has NO company reroll: a one-line note says so) -> `IPlayerInput.RerollAsync`
    parks (`#dc-reroll`/`#dc-keep`, not asked when the purse cannot pay) -> THE COUNCIL (`.dc-chip` = `OrderOffer`; `PlaceOrder`/`PickFormation`/
    `MoveSlot` are the guards; arrows and drag) with THE THREE FORMATIONS (copies of the host, Rotate 1 and 2) and the general's pick (`.gen`).
    **GROUND BRIEFING**: `DoctrineSession.Ground` = `DoctrineGame.BriefingAsync(seed, round, 0).Arena` asked at each draft (never copied), shown
    as `.dc-groundline`; the proof compares it with a hand-written draw.
  - **GENERAL'S WORDS** (it now ranks the OFFER): habit table (exact tokens in a situation) + taste table (card features) + the net (d=16, L=1,
    S=d); the net takes a slot at `GeneralOptions.Margin` more right guesses than the better table. Match record: per slot = how often the card
    ranked there was the card taken there; whole = share of rounds the whole pick was exactly the doctrine set; both over about
    `DoctrineWords.RecordWindow` rounds (private to the library, so the proof PROBES the real general); NEVER confidence, probability, accuracy.
    `GeneralHow/GeneralBuilt(options)` read Margin, Mode, History. The unit reroll is NOT in Doctrine (do not build it).
  - **FIELD RULES** read from `UnitRules`/`SiegeRules`/`ArenaInfo`, never typed: DUG IN (`SetTicks` unhit and in place, then `SetBonusPct(traits)`
    harder until hit or moved), SIEGE (Chokepoint, Falling Gate: a besieger with no blow traded by `SiegeClock` loses its stance; roles swap each
    round, you besiege rounds 1, 3, 5, 7), TICK LIMIT (`UnitRules.Rating`: POWER left wins, not soldiers). Replay v3 = `Besieger` +
    `WorldFlag.ClockFell` (replay chip, recap lines).
  - **CHRONICLE** (`BattleChronicle.cs`, a pure function of the recording; every line carries a CITE that `ChronicleCheck` in the proof
    re-checks with its own code): Start, Siege, Contact, Gate, Clock, Flank, Holy, DugIn, Blow, Run, Break, Stand, Lull, End, plus the Why. DugIn
    only when provable: a state machine on the recorded places with `DugInMargin` (1.5 sixteenths; a step too close to the reach is not provable
    until a strike), first cohort of each side, traits known. ENGINE ORACLE: the same set-up re-run by hand (`TracerBattleSession.Stats`):
    every dug-in line at `SetFirstTick`, every clock line at `ClockFiredAtTick + 1`.

  - **THE WAR / OVERMAP** (2026-10-20, UNSEEN IN A BROWSER OR ON A PHONE; PvP paused, no duel screens): start screen section `#dc-war` (`The war`
    / `Endless` radios `.dc-mode`, `#dc-campaign`; the seed field is shared; the fixed games and the three `.dc-opt` are untouched). Session:
    `NewCampaign(seed, endless, out err, maxEpochs)` -> stages `Map` and `Epoch` beside the old ones; `PickLane(lane, hold)` runs ONE
    `CampaignGame.PlayEpochAsync` and the round pipeline's hands park on the same Draft/Doctrine/Watch screens (Ground = the card's arena);
    `ContinueCampaign`, `Leave` (ends the war from Map/Epoch), `End` (`CampaignEnd`), `Briefing`, `Provinces` snapshots. A cancelled battle
    leaves the map as it was and the army as the battle left it (library limit). Level is the ladder's top in a war (the pools are the
    campaign's). Pure, UI-free: `CampaignWords.cs` (roads, province names, cards, summary, end; no digit in a string, scanned), `OvermapView.cs`
    (SVG string, injected as MarkupString: scoped CSS needs `::deep`, an SVG `<text>` cannot be Razor). Map = vertical, enemy capital top, three
    road columns, at most four rows (endless window), boxes coloured by holder class `dc-p-*`, dashed target = what a push would assault. The
    lane cards are the Roads panel (column 3 on desktop; a sheet shut on entry on a phone, dock button `#dc-roads-btn`). Proof: the war
    section of `DoctrineUiProof` (`-- --war` runs only it, ~10 s): scenarios mixed war (race, battle, hold-back, battle), sweep (WarWon),
    triple (WarLost, WarDrawn), short (EpochsUp), endless refill, a won battle; hand-written rules (lane rule, pool, parity, ordinals);
    mutants V1-V12 in `mutants.ps1` (key `Args`). OutOfStandards is checked by its words only (three lost battles are not scripted).

## Header history moved out of CLAUDE.md (2026-10-21, online lane battle pass)
- 2026-10-21 (earlier): Doctrine = the lane battle, old game removed, LaneUiProof 38, DuelProof 438.
- 2026-10-21 (earlier still): LANE BATTLE playable dev page: Release 0/0, DoctrineLaneProof ALL PASS 446, LaneUiProof ALL PASS 31, lane-view-smoke PASS; NOT seen in a browser, not published.
- 2026-10-20: Doctrine overmap and war: DoctrineUiProof ALL PASS 689, mutants V1-V12 caught.

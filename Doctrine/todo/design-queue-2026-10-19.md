# Doctrine design decisions and queue (coordinator as game designer, 2026-10-19)

The user handed the design to the coordinator inside his constraints (our stack; every rule through Pleroma, Tracer, EvalApp,
AlgFormer; desktop primary, mobile compatible; every line true of the code; mechanics become story, see
Showroom\todo\doctrine-story-from-mechanics.md). Decisions below are made; each is built only when dispatched.

## Decided

1. **Plain play UI.** Lord-based names and descriptions that say what an order does; no Pleroma terms in play screens (a 'How it
   is built' expander may keep them). Numbers in text are read from the code. (showroom-owner, in progress.)
2. **Roguelike offers.** Each round: 8 order tokens offered (pick up to 4), 6 unit packs offered (random multiples of a base pack,
   priced, scaled to the budget), reroll for gold. Offers come from the seed, are masked to the legal list, and the general's
   action space becomes 'pick up to 4 of 8'. (tracer-owner, in progress.)
3. **Mixed ranks, three configurations.** Every rank holds a mix of kinds, every kind works in either row (row changes stats),
   and Reform the ranks (the 3-cycle) chooses one of three configurations. New cohorts join the rank by a derived balance rule.
   No per-pack line picker. Measure whether the three configurations actually differ in outcome.
4. **Set and sally (siege play).** A cohort that holds without being engaged for some ticks becomes SET: a bonus to its attack
   derived from its stats. A sally by fast troops that reaches a set line before it acts is the punishment, which the counter ring
   already gives (Swift beats Ranged). Siege arenas: two or three in the rotation, the rest open field; the defender may bide, the
   attacker has a siege clock (an attacker that has not engaged by tick N loses the stance or the round); roles swap each round so
   it stays fair. Mirror and fairness checks as for the other arenas. (Next Tracer-side round after offers.)
5. **Command layer (general commands soldiers).** A command seam in the battle: at decision ticks a commander sees an integer
   view per line and returns a field order per line; every order is recorded in the replay as an event (versioned replay format)
   and shows in the chronicle. A heuristic commander is the baseline and quiet fallback; the general learns from dense short-horizon
   rewards (damage swing) with a baseline; the player's opening orders stay. 20 to 25 decisions per battle. Later: pause-and-command
   for the player.
6. **Pre-battle briefing.** The arena (map, walls, cover, gate tick), the enemy army by line, and the road ahead (next rounds'
   arenas and enemy strength), from one Doctrine function (ArenaFor(seed, round)) the page reads. Enemy opening orders visible in
   the first game, hidden afterwards until scouted (scouting later with the map). Rehearsals only with limited charges (a full
   rehearsal is the real result).
7. **Beginner path.** First game: limited control and an unlock ladder (widening the offer pool), starter advice labelled as the
   game's own, recap after each battle, chronicle credits the player's orders, no timer.
8. **Chronicle.** Built (live): a log of the fight from the recording, every line cited to a frame event.

9. **Endless single player (run mode).** The run lasts until the player loses. Lose = out of STANDARDS: the player starts with 3,
   a lost field costs one, every 5th field won restores one (max 3), the run ends at zero; score = fields won (best run kept
   only in the page session unless the user approves saving). Enemy gold grows every round without a cap; every 5th field is a
   captain's field (stronger enemy, and a free pick from a small offer as the reward). The fixed 3/5/8-round games stay as
   'Campaign' options. Technical: Rounds = endless in the options; the pipeline run is CHUNKED (EvalApp recursion depth is a
   limit, so the host runs a few rounds per pipeline run and carries RoundState); RoundRecord history is capped (last N); the
   'no soldiers and no gold' clause stays but standards are the real end; needs the comeback term so a loss streak is not
   instant. Mechanics as story: a lost field costs the host a standard.

10. **The overworld map is the spine of the run (original idea, moved up).** A seeded map of provinces (a Pleroma store: province
   = item, features = flags such as Barracks, Stables, Shrine, Granary, Pass, Watchtower, garrison/yield = counts, borders = the
   arrangement). Each epoch the enemy threatens 2 or 3 fronts (each with its arena, terrain and a briefing of the enemy). The
   player CHOOSES which front to fight and which to concede (conceding costs ground, a standard is never lost by conceding); a
   held province yields gold and unlocks the kinds and orders of its feature while it is held (they widen the offer pool, never
   raw power); a lost province takes its unlock with it. Endless mode (9) is this map continuing: the front line advances and
   new provinces appear; captain fields are boss provinces. First slice (v0): a small fixed-size seeded map, one front chosen
   per epoch out of 2 or 3, provinces that yield gold, three features that unlock (Barracks, Stables, Shrine), concede and
   hold, briefing per front, standards, no splitting the army across fronts (fighting two fronts at once is a later stage).
   Combat for fronts nobody fights is NOT simulated: the conceded front just falls; no host-side arithmetic.

10b. **REVISION (user: keep it restrained so multiplayer is still possible).** The map is deliberately small and SYMMETRIC so
   the same rules serve solo and PvP: two capitals and three lanes of three provinces each (the same graph for both sides, seeded
   terrain and features mirrored). Each epoch both sides pick a LANE to push (sealed, revealed together). Same lane: a battle
   there, the winner takes the next province in the lane. Different lanes: both advance unopposed (a race). A held province yields
   its gold and unlocks its feature (Barracks, Stables, Shrine) while held; a capital taken ends the war. Solo = the enemy picks
   its lane by a scripted rule (seeded); endless solo = lanes refill as the enemy advances, with standards as the loss rule.
   PvP = the other seat chooses; the armies are built to the same gold budget; offers come from the shared seed. Nothing in the
   map needs per-player randomness or hidden state beyond the sealed pick. Splitting the army across lanes stays out of scope.

## Queued (designed, not dispatched)

- **Campaign layer.** Seeded map of provinces (a Pleroma store), player-chosen number of epochs, win the war not the battle
  (abstain or concede ground), provinces yield gold and unlock kinds and orders while held (unlocks widen choice, not raw power;
  they end when the province is lost). Needs counters and offers first.
- **PvP tabletop.** Same budget, mirrored deployment, seeded mission and event deck revealed to both, sealed orders, one device
  simulates and the other plays the recording, async play with a general standing in when away; per-turn timer, the general plays
  on a timeout, never counted against a beginner.
- **Sprites.** Kenney CC0 Medieval RTS pack (terrain, structures; downloaded to research-scratch, not in a repo); unit art from
  Kenney Tiny Dungeon or Roguelike Characters after checking the zip licence; credit Kenney.nl.
- **Fortify.** Needs Phasor TickKey (committed, unreleased as 2.21.0: needs editorial and user approval of the count wording
  8 of 41, 16 per term, 91 members) and the Doctrine Phasor pin bump.
- **Playstyle matrix.** Re-run with offers, mixed ranks, set and sally, the siege clock and a comeback term; target: no dominant
  style at novice and expert, a crossover where an economy or timing style gains from skill.
- **Known structural gaps:** snowball (round 0 decides the game), free Age, the win curve is a cliff (front-width cap?), banker
  cannot survive a round.

## Honest limits

Nothing here has been seen in a real browser or on a phone. The live site runs the snapshot rules (priced Flip only), not the
counters, derived prices or tick-limit-by-power in the main tree.

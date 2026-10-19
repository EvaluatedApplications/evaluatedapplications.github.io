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

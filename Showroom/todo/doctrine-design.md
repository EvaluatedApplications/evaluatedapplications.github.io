# Doctrine: design v0 (2026-10-18)

Working title. Replaces the card design for new work. The live "The Plenum" (cards, lobby, duel) stays up unchanged until
this game can take its place; nothing here retires it yet. Status: DESIGN. Decisions below were made by the coordinator
from the user's brief ("army sim, players add modifiers like an auto battler, Tracer, a tiny HoloFormer per player to play
within its own rule set, Pleroma on top, Tracer on the bottom"). Anything unproven is marked UNVERIFIED.

## One line

You raise a general. It learns how you command an army, and it fights your battles on its own while you are away.

## The three tiers (hard architecture)

| Tier | What | Rules |
|---|---|---|
| Top: Pleroma | The army, the economy and the legal list. Army-wide moves (doctrine). Scoring reads. | ALL game state lives here and changes only through Pleroma operations (Phasor gaps are filed, never patched host-side). |
| Middle: the general (tiny HoloFormer) | Reads a short token summary of Pleroma reads, outputs the next doctrine tokens, masked by the Pleroma's legal list. | Per player, starts EMPTY (nothing pre-trained ships), trained only by that player's play. Its weights are not game state. It never touches a battle: its choices are recorded as plain discrete orders. |
| Bottom: Tracer | Moves cohorts and resolves engagements on a baked navmesh (user decision 2026-10-18: "use Tracer's battling and mesh etc"). | Use Tracer's real services: NavMeshBaker (arena meshes with obstacles), the multi-agent / adaptive services (cohorts as formation groups), behaviours (Seek, Flee, Patrol, Wander), the engagement service (hit and damage resolution, spatial hash), line of sight, fog of war, influence maps, dynamic obstacles, congestion. The integer GridTactics lane stays as an optional deterministic mode. |

## Runtime: EvalApp (user decision 2026-10-18: "EvalApp as the runtime")

The game is EvalApp-native, not an imperative loop with an EvalApp skeleton around it. The round loop and the battle tick
loop are EvalApp pipelines. The context carries the Pleroma army stores, the Tracer world (TracerContext via
`WithTracer`/`WithEngagement`/`WithAdaptive`/`WithSimulation`) and the general. Task data is the doctrine for the round
and the arena. Recursion (a round that feeds the next) and circular references are fine. In the browser it runs on one
thread with no filesystem and no background tuner. Every package is used through its OWN published EvalApp extension
methods exactly as documented (user decision: "use the NuGet EvalApp extensions correctly"); no hand-rolled contexts where
an extension exists. What degrades in a browser is measured in S0a (tracer-owner probe), and any
EvalApp gap is filed with evalapp-owner, never patched in the game. After S1, evalapp-owner grades the game against its
native rubric.

## The army (Pleroma)

- A cohort is an item: Key = unit kind (8 kinds readable at once: KeyCapacity 8, the "command span"), Flags = 8 traits,
  Arrangement = formation slot (S_3: front, middle, back), Time = veterancy ring. The count is the number of soldiers.
- Traits (8 bits): Heavy, Swift, Ranged, Holy, Cursed, Beast, Machine, Shielded. Kinds are named combinations (a roster of
  8 to start). A kind's traits are a function of its key (one key, one trait set), as the Pleroma requires.
- Signed counts are real rules: debts to mercenaries, curses that cancel blessings, resurrection as undoing a death record.
- Capacity refusal is a rule: a ninth kind makes the army unreadable until a kind is dismissed (command span).

## Doctrine (what the player edits)

A doctrine is up to 4 slots per round. Two families of token:
- **Army orders (Pleroma, one pass over the whole army):** Rotate the line (ShiftKeys / formation relabel), Flip a trait
  (RelabelFlags), Age the veterans (Tick), Merge two cohorts (Bind: pairs, keys add, traits combine), Aura (a Boolean
  condition over traits via FlagPredicate; effect scales with CountWhere), Cull (Restrict where the shape allows).
- **Field orders (Tracer behaviour per cohort group):** Advance, Hold, Flank, Fall back, Guard (stay near an ally), Hunt
  (seek NearestInRange).
A weighted doctrine splits an army exactly (integer weights, no dice): "3 parts hold, 1 part flanks".

## Battle (auto)

An arena is a baked navmesh (a few handmade layouts with walls, chokepoints, cover zones and a dynamic obstacle or two). Each
cohort is a formation group of agents on the mesh; its strength is its count and the number of agents shown is a scaled
sample of it. Per tick: Tracer paths each group by its field order (Seek, Flee, Patrol...), the engagement service resolves
who hits whom within radius with line of sight, fog of war limits what each side knows, an influence map scores territory.
Losses come back as counts and are applied as Pleroma subtracts. Output: both armies after, plus a REPLAY: the recorded
per-tick positions and engagement results (not a recipe to recompute).
DETERMINISM POLICY (changed with the Tracer decision): the navmesh services use floats, so a battle is simulated ONCE by one
authority (the player who starts it), and other devices PLAY BACK the recording instead of recomputing it. Ladder results are
therefore reported by the attacker (casual trust, stated plainly). S0a measures whether two runs on one device, and wasm vs
desktop, give the same recording; if they do, verified replays become possible later. The integer GridTactics arena stays as an
optional mode where every device can recompute and compare.
Combat arithmetic on counts is the open Phasor question: use `MapCounts(f)` (a Pleroma operation) and report whether it is
enough, rather than doing the sums host-side.

## The general (tiny HoloFormer)

- Input tokens: round, own army summary (counts by trait bucket), scouted enemy summary, last result. Output: doctrine
  tokens. A legal mask from the Pleroma zeroes impossible choices before the pick.
- Training signal 1 (imitation): every doctrine the player sets by hand is an example. Training signal 2 (outcome): choices
  that preceded wins are replayed more (success-weighted replay).
- Before a battle the player sees the general's ghost suggestion and a confidence glow, and accepts or overrides. An override
  is a training example.
- Honest bar: the claim is "plays like the person who raised it", not "plays well". The decision space is tiny and
  discrete (a few dozen tokens) on purpose, because fresh in-browser learning has been weak on small data.
- Offline play: the general decides during the owner's own session and its choices are baked into the army code as discrete
  orders; a defender's reactions are a precomputed table over a small situation set. Float inference never runs inside a
  battle that two devices must agree on. (UNVERIFIED: size of the situation table, and whether it plays well.)
- Sizing is an AlgFormer question: the smallest model (vocab about 64, d, layers) that learns a toy doctrine from about 30
  examples. See the pre-registered probe in the AlgFormer todo.

## Modes

1. **Training ground:** you vs scripted opponents with visible hidden rules (an affine rule over traits you can deduce:
   `Eliminate` confirms your guess exactly).
2. **Async ladder:** fight someone's saved army and general-table. An army is a few hundred bytes, a code or link; the relay
   (wss://79-72-78-90.sslip.io/, namespace plenum/v1/ only) can hold a retained "army shelf". Anyone who knows the room can
   read it; say so plainly.
3. Campaign and co-op: later, not designed.

## Interesting mechanics, kept

Aura formulas the player designs (including parity auras); weighted doctrines; merging armies; command span; anti-units;
self-healing formations that misrepair a double hit (Hamming syndrome); scouting a rule by deduction; seasons on the time
ring; sharing an army as a code; interrogating a captured general on test positions.

## Phone first

Portrait 360 to 430 px. 44 px targets, no hover-only, no sideways scroll, safe-area insets. The arena is a small grid drawn
large; the army panel collapses; the doctrine bar is a row of slots. Cohort blobs, not individual soldiers.

## Build order (each step ships only when proven)

- S0a (Showroom): prove the Tracer services we need run in the Blazor WebAssembly build (trimmed, AOT, single thread): bake a
  mesh, MultiAgent/Adaptive tick, the engagement service, line of sight, fog of war, influence map. Tracer is built on the
  EvalApp pipeline (async, parallel, tuner state on disk by default): find out what each needs in a browser (no filesystem,
  no threads) and report gaps to tracer-owner. Then the Pleroma army shape + cohort kinds + one battle sim with a recorded
  replay, and the determinism measurements above.
- S0b (AlgFormer): the smallest-general probe (pre-registered, see the AlgFormer todo).
- S1: manual play: draft, doctrine, battle, spoils, army code and replay. Phone layout. No general yet.
- S2: the general (imitation, ghost suggestion, override).
- S3: async ladder on the relay.
- S4: training ground rules with deducible opponents.

## Not verified

Tracer in WebAssembly; combat arithmetic through MapCounts; whether the tiny model learns a doctrine from about 30
examples; whether any of this is fun. The first slice exists to answer the first two.

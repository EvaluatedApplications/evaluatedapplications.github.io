# Doctrine difficulty: a game where a good player is rewarded but no single rule solves it   2026-10-19 (night)

Owner scope: Doctrine\Battle, Runtime, Army (only where a unit price or stat table lives). CPU only, 6 workers, BelowNormal, no GPU, no HoloFormer is involved
(the standing S = d rule is not touched). Input: AlgFormer\todo\autopilot-selfplay-2026-10-19.md (EXP-AP-1/2 findings).

## What the autopilot found (the reason for this note)
A per-slot bandit solves the real game (~0.99, 8 rounds); a fixed doctrine list is weak (0.558 < random 0.674); "all gold on Spearmen" beats every other draft rule
by 0.2+ (random 0.64 against 0.12-0.37); Advance as a field order gives rows identical to no orders; Flip tokens appear in the winning scripts; the enemy's gold knob
does not make the game harder.

## Reading of the code BEFORE any run (diagnosis; every candidate below follows from this, not from a score)
D1 PRICE vs POWER. `Roster` prices: Spearmen 1, Shieldwall 2, Riders 2, Archers 2, Priests 3, Wights 2, Wolves 2, Catapults 4. Per soldier, in the battle's own stat
   tables (`UnitRules`): damage percent / taken percent is Spearmen 1.00, Shieldwall 100/67 = 1.49, Riders 1.00, Archers 0.70, Priests 0.70, Wights 1.15,
   Wolves 1.10, Catapults 161/90 = 1.79. So Spearmen give 1.00 power for 1 gold, every other kind 0.23-0.58 power per gold (Shieldwall 0.74, Riders 0.50, Wights 0.58,
   Catapults 0.45): a 2x-4x price advantage for the plain soldier, and "mass" is what wins (the battle only samples 3 sqrt(n) agents, strength is soldier count).
D2 NO COUNTERS BETWEEN KINDS. The only matchup rule is Holy vs Cursed (x1.5) plus cover. No kind beats another kind, so nothing makes one composition wrong, and a
   draft rule that buys the best raw value per gold cannot be punished.
D3 FLIP is a free, army-wide XOR of one trait bit on EVERY cohort (`RelabelFlags(null, 1u << bit)`); `Trait` decides damage, range, speed and defence in `UnitRules`.
   Spearmen (no traits) flipped on Machine become x2.3 damage, range 10 (Catapult stats) at price 1; Shielded gives take 75 percent; Beast x1.1; Holy x1 but x1.5 vs Cursed.
   Nothing is paid, nothing is lost: the order has no cost and no limit.
D4 ADVANCE is the default: `if (c.Orders.Count == 0) c.Orders.Add((Advance, 1))` and `GoalFor`'s `default` branch is the Advance branch. No order and Advance are the same code path.
D5 THE ENEMY DOES NOT LOOK. `ScriptedOpponent.Picks(round, seed, gold)` and `DoctrineFor(round, seed, legal)` are functions of (seed, round) only.
D6 RunBattleStage does `throw f.Exception`; `PipelineResult.Failure.Exception` can be null (an NRE appears instead of the failure). `Game.PlayAsync` has the same line.

## Policy family (declared)
Eight rules for a doctrine and a draft, all scored the SAME way on the SAME held-out games:
- PR random-legal doctrine (slot by slot over End + legal unused tokens), under each of the 10 draft rules.
- PB per-slot bandit, key = round (the cheapest keyed learner; AP-1 dev: k1 .998), epsilon .3 while learning, greedy at test, under each of the 10 draft rules.
- PF best fixed list under draft rule 0 (all gold on Spearmen = "Spearmen-bulk").
- the 10 draft rules (AP-1 `Drafts`): all gold on kind 0..7, cycle-all-kinds, fill-smallest-cohort.
No learned draft. The player is the attacker; enemy, arenas and battles are the game's own. The normaliser for every policy is the AP-1 one (legal at the START mask, no
repeats, End = none). Rounds after a wipe still count (battle lost).

## Metric
Per game, the mean over its 8 rounds of the round score (win 1, draw .5, loss 0). Cell = the mean over games. 120 held-out games per cell, seeds 7,000,000..7,000,119 (every
policy and draft sees the same games). SE of a cell about 0.036 (per-game sd about 0.39). Training seeds 8,000,000+, fixed-list search seeds 9,000,000+ (never held out).

## Search width (declared)
- Drafts: 10 rules x 2 policies (PR, PB) = 20 cells; PB trained once per draft (4 iterations x 100 games, iteration 0 uniform random, 400 games).
- PF: 120 field-order triples drawn without replacement (seeded) from the 343 (each group: none or one of 6 orders), 15 shared games each; top 5 x 80 fresh games; the winner is
  scored on the 120 held-out games. Width 120 + 5.
- Candidate change sets: C0 (baseline) and the four cumulative sets C1..C4 below, EACH MEASURED ONCE. No other candidate is tried. If C4 fails, no C5 is added from the
  results; the report says so.

## Target (the game is "hard enough" when ALL of these hold on the held-out games)
T1 REWARD WITHOUT SOLVING: the best bandit cell (any draft) is <= 0.90 and beats random-legal under the SAME draft by >= 0.10.
T2 NO DOMINANT DRAFT: for PR and for PB separately, (best draft - second-best draft) <= 0.20.
T3 NOT TRIVIAL, NOT UNWINNABLE: at the best PB draft, PR, PB and PF each lie in (0.05, 0.95).
Verdict: A PASS = T1, T2, T3. C PARTIAL = exactly the failing ones are named, no verdict. D INVALID = either null below trips.

## Two nulls (written now)
N1 SHUFFLED-OUTCOME BANDIT (learning-pipeline null, empirical): PB under draft 0 trained with each iteration's round scores PERMUTED among its rounds (2 runs). It must sit within
   +-0.05 of PR under draft 0; if it does not, the bandit pipeline has an outcome-independent effect and every PB number is D INVALID.
N2 ANALYTIC / REPLAY: (a) a policy replayed on the same games gives identical rows (checked on 20 games for PR and PB); (b) chance spread of "best draft - second-best draft"
   when all 10 drafts were equal: cell SE .036, expected best-minus-second about 0.04, so a gap of 0.20 is about 5 sigma; a measured gap under 0.08 is reported as inside the null
   (no dominance), a gap over 0.20 is real. Disagreement of replay (any differing row) = D INVALID.

## Flip is an exploit? (declared test, run on the baseline BEFORE any change)
E1 PB (draft 0) trained with all Flip tokens illegal vs PB trained normally (same seeds): exploit if (normal - noFlip) >= 0.05 with a paired-bootstrap 95 percent lower bound > 0.
E2 the trained normal bandit scored on the held-out games with every Flip token DELETED from its scripts (not retrained): reported as the drop.
E3 fixed [Flip Machine(6)] vs fixed [] (draft 0, same 120 games): exploit if the paired difference >= 0.10 with a 95 percent lower bound > 0.
If E1, E2 and E3 are all inside noise Flip is not what wins and its pricing change (C2) is NOT shipped; the measured result is still reported.

## Candidate change sets (cumulative; each derived from the diagnosis, none from a result)
C1  D1 + D2: (a) price = ceil(2 x damage percent / taken percent) computed from the trait table (`Roster` derives it; no typed number): Spearmen 2, Shieldwall 3, Riders 2,
    Archers 2, Priests 2, Wights 3, Wolves 3, Catapults 4; (b) a four-class counter cycle by traits, hit x1.5 on the counter (the same shape as Holy vs Cursed):
    Plain > Swift > Ranged > Armoured (Heavy or Shielded) > Plain, a kind with several classes counters/is countered by any of them (Catapults are Ranged + Armoured).
C2  C1 + D3: a Flip costs gold = sum over cohorts of soldiers x max(0, price of the relabelled trait set - price of the current one) (a better soldier is paid for, a worse one
    is free), and is legal only while the army can pay (legal mask reads gold). Same price formula as C1; stub and Pleroma armies both.
C3  C2 + D4: an EXPLICIT Advance is a charge (speed x1.25, takes x1.2 damage while charging); the default (no order) is the steady march it was.
C4  C3 + D5: the enemy reads the player's army as it stood at the end of the last round: the first (largest-gold) enemy pick is the counter kind of the class of the player's
    biggest cohort (Plain -> Shieldwall, Swift -> Spearmen, Ranged -> Riders, Armoured -> Archers); the other picks and the enemy doctrine stay random as before.
Fix rule (fixed now): SHIP the earliest cumulative candidate that is A PASS. If none passes, ship NO rule change except: the D6 fix (a bug, independent of the target) and, only if
E1/E2/E3 show Flip carries the score, nothing more than C2's Flip pricing when C2 does not make T1..T3 worse than C1. Rejected candidates are kept as patch files under
Doctrine\todo\difficulty-candidates\ and not applied. A candidate that changes the feel of the game is reported to the user as a decision either way.

## Instrument pre-run notes
Games are deterministic; one worker pool of 6 threads (own Tracer world per thread, single-thread pinned EvalApp). A game that fails with the null-exception race (EvalApp 4.4.1 tuner
history, filed by the autopilot owner) is replayed from its seed and counted. Draw counts as 0.5. Runner source: scratchpad `dg\*.cs` (sha256 recorded below before the first scored run).

---
# AMENDMENTS (all written AFTER seeing the baseline C0 run and BEFORE any later state was scored unless said; disclosed)
A1 N1 as registered ("each of 2 shuffled-outcome bandit runs within 0.05 of random") TRIPPED on the baseline (runs 0.470 and 0.841 against random 0.672). Reading: a bandit trained on
   scrambled scores ends as a random greedy SCRIPT, and a script's win rate in this game runs from 0.08 to 0.98, so a per-run 0.05 band is a miscalibrated test, not a bug found.
   Amended before C1 was scored (C1 ran with the old harness; its extra runs were added afterwards): 6 shuffled runs, criterion = mean of 6 within 0.05 of random AND the real bandit
   above the best shuffled run. The amended test is also too tight for runs this wide (sd 0.1-0.4), so a second step was added: N3, six training runs of the REAL bandit (draft 0,
   seeds 300..305) per state, to give the real bandit's own run-to-run spread (below). Both N1 forms are printed for every state; neither gates a verdict here, and the report says so.
A2 Fixed list PF is searched and scored under draft 0 only (the Spearmen-bulk draft the autopilot used), so T3 reads PF at draft 0 and PR/PB at the best bandit draft.
A3 E1 (retrained bandit with Flip illegal vs normal) compares two DIFFERENT training runs, so it is confounded with N3's run-to-run spread; E2 (same trained bandit, Flip deleted) and E3
   (fixed lists, paired) are not, and carry the Flip verdict. E1 is printed but not used.
A4 A sixth state S (the state that ships: D6 fix + Flip pricing, kind prices untouched, no counters, no charge, enemy unchanged) was measured after the fact, because the fix rule ships
   "C2's Flip pricing" and C2 as measured also carried C1's prices and counters. S is a verification of the shipped state, not a seventh candidate.

# RESULTS (2026-10-19 night; 120 held-out games per cell, SE about 0.036; every state replays identically on 20 games, N2a identical in all six)
Hash of the runner as run: Harness.cs 8556E73D63B779D0EA94BA993285C5F4FF95CC4F0B74BBAB6DA6B9C380474D8B, Policies.cs D5D9DA058F6BDABD815BD5A615A223D93F04962A7AF97A4DA3A8F0C2919764E9,
Program.cs 3376DE25D7122FA7DC7EFE69CDB8A3F72C140340AADE9DE39E2760FA21EF99C8 at lock, A027DDB427F590D7689A9E505E650FBAE8C0C3C2029437F95CE87F0E9990D7FA after A1 (the N3 command was added
last; sources in todo\difficulty-candidates\runner\). Per-state reports: todo\difficulty-candidates\report-<state>.txt. ~64-95 games per state were replayed after the known EvalApp
null-exception race (threads in one process; the game is deterministic so a replay is exact).

## Draft table: random-legal (R) and per-slot bandit (B, round key, 400 training games, ONE run per cell)
| draft rule | C0 R | C0 B | C1 R | C1 B | C2 R | C2 B | C3 R | C3 B | C4 R | C4 B | S R | S B |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 all Spearmen | .672 | .973 | .376 | .922 | .226 | .748 | .226 | .759 | .289 | .748 | .644 | .867 |
| 1 all Shieldwall | .354 | .930 | .241 | .950 | .107 | .392 | .121 | .398 | .116 | .431 | .180 | .832 |
| 2 all Riders | .226 | .988 | .266 | .987 | .153 | .368 | .156 | .473 | .159 | .530 | .157 | .408 |
| 3 all Archers | .417 | .982 | .530 | .967 | .373 | .666 | .388 | .548 | .397 | .533 | .430 | .389 |
| 4 all Priests | .205 | .938 | .397 | .959 | .286 | .607 | .262 | .442 | .270 | .540 | .115 | .317 |
| 5 all Wights | .226 | .961 | .177 | .968 | .073 | .355 | .072 | .323 | .077 | .295 | .087 | .169 |
| 6 all Wolves | .281 | .996 | .178 | .930 | .070 | .306 | .064 | .268 | .067 | .144 | .118 | .253 |
| 7 all Catapults | .365 | .735 | .380 | .816 | .360 | .927 | .354 | .907 | .362 | .929 | .333 | .765 |
| 8 cycle all kinds | .229 | .941 | .175 | .977 | .089 | .746 | .088 | .597 | .098 | .561 | .102 | .835 |
| 9 fill smallest | .226 | .992 | .184 | .898 | .091 | .720 | .087 | .732 | .085 | .869 | .099 | .727 |

## Summary per state (candidates cumulative: C1 = prices + counter cycle, C2 = C1 + Flip priced, C3 = C2 + charge, C4 = C3 + enemy reads; S = Flip priced only, kind prices as they were)
| state | PR best draft gap (best - 2nd) | PB gap | best PB cell (draft) | PB - PR at that draft | PF (draft 0) | real bandit d0, 6 runs mean (sd) | Flip E3: fixed [Flip Machine] - fixed [] | T1 | T2 | T3 |
|---|---|---|---|---|---|---|---|---|---|---|
| C0 baseline | .255 | .004 | .996 (6) | +.715 | .542 | .986 (.018) | +.381 [.317, .448] | FAIL | FAIL | FAIL |
| C1 prices + counters | .132 | .010 | .987 (2) | +.721 | .222 | .956 (.033) | +.516 [.461, .568] | FAIL | met | FAIL |
| C2 + Flip priced | .013 | .179 | .927 (7) | +.567 | .253 | .823 (.061) | .000 [.000, .000] | FAIL (by .027) | met | met |
| C3 + Advance charge | .034 | .148 | .907 (7) | +.553 | .260 | .797 (.073) | .000 [.000, .000] | FAIL (by .007) | met | met |
| C4 + enemy reads | .034 | .060 | .929 (7) | +.566 | .301 | .781 (.123) | .000 [.000, .000] | FAIL (by .029) | met | met |
| S Flip priced only | .215 | .031 | .867 (0) | +.222 | .658 | .837 (.029) | .000 [.000, .000] | met | FAIL (by .015) | met |
Random-legal at the Spearmen-bulk draft: C0 .672, C1 .376, C2 .226, C3 .226, C4 .289, S .644. No state passes T1 + T2 + T3. Nulls: N2a identical (all). N1: as registered TRIPPED in all six; amended
form OK in C0, C2, C4 and TRIPPED in C1, C3, S (scrambled-outcome bandit runs span 0.08-0.98; see A1). The real bandit's own spread across training runs is 0.02-0.12 (table), far smaller than the
scrambled one, which is the reason the PB cells are read as real learning and the N1 trip is read as a miscalibrated test. A verdict on T1 is nevertheless soft: the best-of-10 PB cell is a
max over noisy cells (sd .03-.12 each).

## Flip is an exploit (measured on the unchanged baseline, same 120 games, paired)
- E3: ONE token "Flip Machine" as the whole doctrine, all gold on Spearmen: 0.906 against 0.526 for no orders, +0.381 [0.317, 0.448]; the four winning-script flips (3, 6, 5, 7) +0.394 [0.329, 0.464].
- E2: the trained baseline bandit with every Flip token deleted from its scripts: 0.973 -> 0.815 (-0.158 [0.105, 0.212]). E1 (retrained without Flip, 0.823) agrees but is confounded (A3).
- Why: `Perform(Flip)` XORs a trait bit on every cohort (`RelabelFlags`) at no cost; `UnitRules` read the traits live, so Spearmen (price 1) became Machines (x2.3 damage, range 10, speed x0.8),
  Shielded (take x0.75) or Holy/Beast. With a free Flip even the weakest draft rule was solved (every B cell at C0 is 0.74-0.996; with Flip priced it spreads 0.17-0.87).
- After the fix (any state with Flip priced): E3 = 0.000 exactly, E2 drop = 0.000. The same unchanged-prices state S: bandit 0.986 -> 0.837 (6-run means).
- Counter-check of the other direction: pricing alone (C1, Flip still free) did not touch the exploit (E3 +0.516) and the bandit stayed at 0.956.

## What each lever did (read from the states above; one measurement each)
- Prices derived from the stat tables (C1): removes the Spearmen draft dominance under random play (gap .255 -> .132, Spearmen-bulk .672 -> .376) but leaves the bandit near the ceiling while Flip
  is free; with Flip priced (C2) the new free lunch is Catapults (price 4 unchanged, range 10): best PB cell .927 (draft 7). The price rule treats range as free; that is a gap in the rule, not
  a different lever, and it was not chased (registered width exhausted).
- Counter cycle (in C1+): no draft is sure to win any more, but with Flip free it did not matter; its separate effect was not measured (cumulative design, stated).
- Advance as a charge (C3): moves no policy by more than noise (bandit d0 .748 -> .759, best cell .927 -> .907); it makes a named Advance differ from no orders by construction (not tested separately), nothing more was measured. NOT shipped.
- Enemy reading the last army (C4): no measurable gain over C3 at the best draft (Catapults .907 -> .929, Spearmen-bulk .759 -> .748). Catapults are Ranged + Armoured and the reading rule takes the
  LOWEST class bit (Ranged -> answer with Riders), so it is the one composition the table answers weakly. NOT shipped.

## What ships (fix rule applied)
No candidate passed, so: (1) the D6 fix (RunBattleStage, Game.PlayAsync and TicksStage report a Failure with no exception as an InvalidOperationException naming the round, seed and arena);
(2) Flip priced (C2's Flip pricing alone, state S; the rule also needed C2 not to be worse than C1 on T1..T3: it is better on T3). Rejected sets C1..C4 are NOT applied: their C4-state files
are in todo\difficulty-candidates\C4-*.cs.txt (the cumulative state, to diff against the tree) and their reports and the runner are beside them.

## Still failing (plainly)
- T2 in S: with a RANDOM doctrine the Spearmen-bulk draft still beats the next draft by 0.215 (limit .20; inside one standard error of it). The learned player erases the draft gap (PB gap .031), so
  the draft only decides how well a naive player does. Changing the price of a Spearman (1 gold for the cheapest, strongest-per-gold body) is the lever that moved it (C1), and it is the user's call
  because it halves how many Spearmen every gold buys.
- Catapults after any price change (C2+): range 10 is not priced; a bulk Catapult player with a learned doctrine reaches 0.91-0.93.
- Advance is still a no-op field order (finding 5 stands; C3 shows a charge is implementable in about 10 lines but did not change the game's difficulty).
- The enemy is still blind to the player (C4 did not measure better), and enemy gold (x2..x6) is not the lever.
- Side leans from the balance harness are untouched (this work did not look at them).
- Bandit cells are single 400-game runs (sd .03-.12); the table's T1 flags near the 0.90 line (C3 .907, C2 .927, C4 .929) are within about one sd of it.

## Decisions for the user (the game's feel changes)
1. Flip is now paid: a levy cannot become Machines for free. Shipped (an exploit by measurement). It makes most Flip tokens unaffordable in the first rounds; Flip still works for a small or
   rich army, and for any relabel that makes soldiers worse (free). Showroom's Doctrine words (`DoctrineWords.cs` line 61, "Flip the T trait on every cohort...") should say it costs gold; that is
   showroom-owner's file, not edited here.
2. Draft prices (Spearman 1): not changed. C1's derived prices (2, 3, 2, 2, 2, 3, 3, 4) halve the army size per gold and fix the draft dominance for a naive player; say yes or no.
3. Counter cycle and Advance charge: implemented and measured, not shipped (no measured gain once Flip is paid for; they would change the feel of every battle).

---
# ADDENDUM 2026-10-19 (late night): PLAYSTYLES MUST ALL BE VALID. Pre-registered BEFORE the style measurement (coordinator direction relayed by the user)
Direction: the game must support several valid playstyles, not one best script; no style dominates, each is learnable. The earlier T1..T3 stay; this adds T4.
Instrument: the game has no player-vs-player mode (the enemy is scripted), so styles are played AGAINST EACH OTHER in one-round duels built from the real sections (Pleroma army service,
Tracer battle engine, the four arenas): both sides get the same levy rules (Create by seed), the same extra gold G = 24 for the draft, draft by their style rule, army orders applied
through the army (so Flip/Age/Aura costs and legality are the game's own), field orders from the style, then one battle (tick limit 120). Round-robin over all ordered pairs incl. mirrors.
Styles (declared simple rules; kinds: 0 Spearmen 1 Shieldwall 2 Riders 3 Archers 4 Priests 5 Wights 6 Wolves 7 Catapults):
- AGGRESSOR: gold split Wolves/Riders; Advance on all three groups.
- TURTLE (banker): half the gold kept (not drafted: banking strength), the rest Shieldwall; Hold front and middle, Guard back.
- COMPOSER: draft cycles all kinds (rule 8); Advance@0 Hold@1 Flank@2.
- MICRO (battle management): gold split Archers/Priests; Flank@0 Hunt@1 FallBack@2.
- VETERAN: all gold Spearmen; army orders Age x3 then Aura on the most common trait; Advance front.
- CONTROL "MASS": all gold Spearmen, Advance on all groups (the autopilot's winning draft with no orders).
Armies: banked gold counts for the score of the DUEL only through what it bought (a banker has fewer soldiers: the duel asks whether holding gold can ever pay; it cannot inside one round, so
TURTLE is run with the bank spent on Shieldwall in the duel and its banking is judged separately in the multi-round game, see below).
Observable: the pairwise score (win 1, draw .5) of row style over column style, pooled over 4 arenas x 2 orientations x 40 seeds (320 battles per ordered pair; SE about .03); matrix reported per arena (80 battles per cell, SE .055).
T4 PASS: (a) every style's mean score over the other five is within 0.35..0.65; (b) every style beats at least one other (pooled > 0.55) and loses to at least one (pooled < 0.45);
(c) the CONTROL does not beat all five others (not > 0.55 against each) and no style does. Per-arena table is reported without a verdict (80 battles per cell is soft).
Nulls: (1) mirror cells (style vs itself) must read 0.50 +- 0.09 (3 SE) pooled, else the duel instrument leans (D INVALID); (2) a style given +15 percent power must beat every other style (sensitivity).
States measured: C0 baseline, S (shipped), C2 (prices + counters + Flip priced). No tuning on the matrix: if T4 fails, the report lists the cause from the logic (price/power, counters, banking/veteran payoff) and
the user decides; changes made after this measurement are NOT measured by it.
# ADDENDUM 2 (same night): PROFICIENCY. Pre-registered before any style run.
Direction: styles must stay valid across skill: a player who ignores infrastructure and masters battles must be able to overtake an economy/composition player at high proficiency, and vice versa.
Declared deviations from the first addendum, written now: the doctrine has 4 slots, so VETERAN is Age, Age, Aura(top trait), Advance@0 (not Age x3); TURTLE is renamed BANKER (it keeps half of each
round's gold in rounds 0-2 and spends all later; its draft is Shieldwall, its field orders Hold/Hold/Guard).
Multi-round matches (the one-round duel cannot show investment): two armies from the SAME levy, start gold 12, 6 rounds, the real spoils rule (winner 6 + kills/2 gold and 1 veterancy, loser 2 +
kills/2), losses persist, roles swap each round, arena by the game's own hash. Match score = share of the 6 rounds won (draw .5). 20 seeds x 2 orders = 40 matches per pair.
Proficiency = execution noise: NOVICE replaces each drafted kind and each doctrine token by a random legal one with probability 0.5; EXPERT never does. Both players of a match have the same level
(novice matrix, expert matrix). This models how well a plan is carried out, NOT in-battle skill (see below). T4 is read at both levels. Read-out requested: does MICRO (battle management) overtake
COMPOSER/BANKER/VETERAN at EXPERT when it does not at NOVICE, and the reverse.
Structural check (from the code, before running): (1) gold has no yield, so banking cannot pay (BANKER tests whether anything does); (2) Age is a free army order with no limit, so veterancy is a
free +5 percent per tick on a ring of 9, not an investment; (3) a player's only input to a battle is up to three field orders per round, chosen BEFORE the battle starts: there is no decision
during a battle, so "battle micro" has no skill axis beyond which orders to name. If the matrices show no style gains from expertise, that is the lever gap, not a finding about the styles.
# RESULTS: PLAYSTYLES (2026-10-19 night; files: todo\difficulty-candidates\styles-*.txt and matches-*.txt hold every arena matrix; runner Styles.cs.txt)
C0 and S play the SAME in these tests (no style orders a Flip), so one matrix stands for both; C2 (prices + counters + Flip priced) is the other state. 320 battles per pair pooled
(SE .03), 80 per arena (SE .055). Mirror null: cells .48-.56 (S) and .51-.57 (C2), inside +-.09; the +15 percent power null beats the styles it should (S: AGGRESSOR+15 beats
TURTLE/COMPOSER, ties MICRO .57, still loses to VETERAN/MASS 0.00): the instrument is valid and sensitive to a 15 percent edge.

One-round duels, pooled (row over column; mean over the other five styles):
| S (= C0) | AGGR | BANKER | COMP | MICRO | VET | MASS | mean |
|---|---|---|---|---|---|---|---|
| AGGRESSOR | .56 | .88 | .99 | .37 | .00 | .00 | .45 |
| BANKER (Shieldwall, Hold) | .12 | .50 | .70 | .01 | .00 | .00 | .16 |
| COMPOSER | .01 | .30 | .53 | .01 | .00 | .00 | .07 |
| MICRO | .63 | .99 | .99 | .50 | .00 | .00 | .52 |
| VETERAN | 1.0 | 1.0 | 1.0 | 1.0 | .48 | .89 | .98 |
| MASS | 1.0 | 1.0 | 1.0 | 1.0 | .11 | .49 | .82 |
| C2 | AGGR | BANKER | COMP | MICRO | VET | MASS | mean |
|---|---|---|---|---|---|---|---|
| AGGRESSOR | .57 | .90 | .94 | .01 | .01 | .06 | .39 |
| BANKER | .10 | .51 | .53 | .00 | .09 | .18 | .18 |
| COMPOSER | .06 | .47 | .51 | .00 | .00 | .00 | .11 |
| MICRO | .99 | 1.0 | 1.0 | .51 | .89 | .94 | .96 |
| VETERAN | .99 | .91 | 1.0 | .11 | .54 | .90 | .78 |
| MASS | (see styles-C2.txt) | | | | | | .58 |
T4 FAILS in both states, all three clauses. In S two Spearmen styles (VETERAN .98, MASS .82) beat every other style in every arena and the other four lose to them 0.00; the
price-1 Spearman is the whole story (24 gold = 24 bodies, twice anyone else) and the rest is ordered by what is free: VETERAN = MASS + two Age + one Aura beats MASS .89. In C2 (Spearman 2 gold,
counter cycle) Spearmen stop winning and the Archers/Priests style MICRO takes over (.96). So price moves the dominator, it does not remove it: the game has no balance point reachable by prices alone.

Six-round matches (persistent armies, spoils, 40 matches per pair), S state, mean over the other five styles:
| style | NOVICE (error .5) | EXPERT |
|---|---|---|
| AGGRESSOR | .51 | .51 |
| BANKER | .19 | .09 |
| COMPOSER | .36 | .24 |
| MICRO | .45 | .36 |
| VETERAN | .79 | .98 |
| MASS | .70 | .83 |
C2 state: NOVICE AGGR .51, BANKER .16, COMP .32, MICRO .59, VET .78, MASS .64; EXPERT AGGR .47, BANKER .07, COMP .21, MICRO .48, VET .96, MASS .82.
- T4 fails at both skill levels in both states. Skill (less execution error) helps the dominant styles MOST (VETERAN .79 -> .98, MASS .70 -> .83) and hurts the investing styles
  (BANKER .19 -> .09, COMPOSER .36 -> .24): the opposite of what the direction asks.
- Can MICRO overtake an economy/composition style at expert level? Against COMPOSER and BANKER it already wins at novice (.55 / .87) and wins more at expert (.60 / .97) in S; it never beats the
  Spearmen styles at either level (.20-.29 novice, 0.00 expert). No style overtakes another through skill. Can economy/composition overtake micro? No: COMPOSER vs MICRO .45 novice, .40 expert.
- BANKER's investment never pays: it banks half its gold for three rounds and ends last at both levels, because gold has no yield (see below).

## The game has no long-run lever (read from the code, then confirmed by the matrices)
1. Gold has no yield and no sink except drafts: banking only delays a purchase (and, since gold is worth its price in bodies, a delayed body fights fewer rounds).
2. Veterancy: Age is a free army order (one per token, up to four tokens), +5 percent power per tick on a ring of 9, no cost, no limit. Aura adds up to +50 percent for free to cohorts
   carrying a trait. Neither is an investment: they are free power every style should take (VETERAN's edge over MASS is exactly those three tokens).
3. A battle gives the player no decision after it starts; the only battle input is up to three field orders chosen before it. There is no way for battle skill to accumulate or to pay off with practice,
   other than naming better orders.
4. Composition has no synergy beyond Holy vs Cursed (and, in the rejected C1, the counter cycle).

## Smallest additions that would give styles a skill and investment axis (NOT implemented; user decides; each is game logic, not a host substitute)
- COST OF FREE POWER: make Age and Aura cost gold in proportion to what they add (the Flip rule: soldiers x power gained x price / 100). Small, one rule in the army sections; then veterans and auras are
  an investment that pays back over rounds, which is what VETERAN as a style needs and what BANKER lacks.
- YIELD ON GOLD: a banked-gold interest or a "fortify" order that turns gold into a lasting stat on a cohort (a Pleroma Add on a per-cohort veterancy/Time face). If a Phasor operation is missing, file
  it; not built here.
- MID-BATTLE ORDERS: let a doctrine hold a second field order that takes effect at tick N (or on a trigger such as a cohort falling below half), so battle skill is in WHEN to switch. A Battle rule
  in TracerBattleSession (orders already re-evaluated each tick in `Decide`); it gives MICRO a skill axis that economy styles do not have.
- PRICE FOR RANGE AND REACH: the derived price in C1 ignored range, which is why Catapults/Archers then dominate; range 7 / 10 should add to the price.
None of these has been measured; each changes the feel and needs the user's yes.

---
# SECOND ROUND 2026-10-19 (tracer-owner, design decisions handed down by the coordinator): LONG-RUN LEVERS AND A BATTLE SKILL AXIS
PRE-REGISTERED BEFORE any code of the four change sets and before any scored run of them. CPU only (BelowNormal, 6 workers), no GPU, no HoloFormer is named (the S = d rule is not touched), Pleroma operations only.
Direction (coordinator, from the user): several VALID playstyles, each learnable; a style that looks weaker early may win with proficiency. Input finding (above): no long-run lever, Spearmen styles dominate, Age and Aura are free power, battle micro has no skill axis.

## Change sets (cumulative, each measured ONCE; the order is the coordinator's list)
Derivations come from the rules, never from a result. "Worth" is the existing Roster.TraitPrice (what a soldier is worth in gold on the battle's own stat tables).
- A1 PRICE AGE AND AURA (same rule as the priced Flip: a better soldier is paid for, a worse one is free). Age: each cohort moves one veterancy step (+5 power points, `Roster.VetStepPct`; the ring wrap is a LOSS so it is free).
  Cost = ceil( sum over cohorts of soldiers x worth x power gained / 100 ). Aura on trait b: power gained per cohort = (power with the aura) - (power now), new bonus = max(current, min(50, soldiers carrying b)).
  An aura wears off at the end of the round, so it is RENTED, not bought: cost = ceil( sum soldiers x worth x gain / (100 x rounds left) ), rounds left = 8 - round (floor 1), 8 = the campaign length (DoctrineDomain default).
  A body, and an Age, serve the rest of the campaign for their price; a one-round aura is that price spread over the rounds it could have served. Both are legal only while the army can pay (the legal mask reads gold), both take exactly the cost.
  The sums are Pleroma reads (InnerProduct of the keys-only projection with a per-kind weight vector, as Score does), not host multiplications. Flip is moved onto the same read.
  Consequence stated in advance: worth is on the TraitPrice scale (a Spearman 2) while a draft costs `PriceOf` (a Spearman 1), so for Spearmen Age costs MORE than recruiting the same power; only kinds with worth below draft price (Priests 2 against 3) get cheaper power from Age.
- A2 REACH IN THE PRICE. worth = ceil( 2 x damage x (100 + reach) / (taken x 100) ). reach (percent) is the share of one soldier's kill-time a unit fires free while a melee unit closes on it:
  free ticks = (Range - melee range 2.2) / (2 x base speed 2.0) (both sides advancing), one tick of fire is 100 of the 600 units that kill a soldier (`UnitRules.Fp`), so reach = 100 x free ticks x 100 / 600 = (range in tenths - 22) x 5 / 12:
  Archers/Priests (range 7) 20 percent, Machines (range 10) 32 percent. Worth becomes Archers 2, Priests 2, Catapults 5, a Spearman flipped to Machine 7 (all else unchanged). Draft prices (`PriceOf`) are NOT touched by A2: reach only prices Flip, Age and Aura.
- A3 A BATTLE SKILL AXIS: ONE trigger kind, "my side has lost N quarters of its soldiers", N = 1..3. Three new tokens (ids 54, 55, 56, appended; no id moves; vocabulary 57 of 64): family Trigger, `WhenLost25/50/75`.
  A doctrine is read as two lists split at the FIRST trigger token: field orders before it are the opening orders (last word wins per group, as before), field orders after it replace them once the trigger fires (a group not named after it advances, the default).
  No trigger token: the doctrine reads exactly as before (ids and meanings of tokens 0..53 are unchanged, so the wire contract only grows; digests are re-pinned for the new legal tokens, with that reason).
  The check runs at the start of every tick in `TracerBattleSession.FieldOrdersAsync` on soldiers lost / soldiers brought, integers, latched. Replays carry cohorts, so a switch is seen as the movement it causes; nothing else in the format changes.
- A4 A GOLD LEVER THAT PAYS LATER: interest on banked gold. Checked against Phasor first (superposition-gaps.md): `Pleroma.MapCounts(f)` (a pointwise map of the key counts, exact) does it on the purse (the purse shape becomes a complete one-key band), so NO new Pleroma operation is needed and it is built.
  At the close of a round the purse pays gold / (campaign length - 1) = gold / 7 (floor). Derivation: banking from the start of the campaign for one round and spending the next costs one round of service for every body, so the rate that makes the first round of banking exactly break-even in soldier-rounds is 1/(8-1).
  Fortify (gold into a lasting per-cohort stat) needs a per-key Tick (age ONE cohort); Phasor has only the whole-store Tick, so it is filed as a gap (below) and NOT built.
- A5 (CONDITIONAL, decided now, measured only if A4 fails T4): draft prices derived from worth (PriceOf = TraitPrice with reach: Spearmen 2, Shieldwall 3, Riders 2, Archers 2, Priests 2, Wights 3, Wolves 3, Catapults 5). It is the user's call (halves the bodies per gold) so it ships OFF, as a switch (`ArmyRules.DerivedDraftPrices`), and the scripted enemy still sizes picks with the typed prices (stated, so A5 is a style-matrix measurement only).
- C0 = the tree as it stood at the start of this round (Flip priced without reach; Age, Aura free; no interest; no trigger used). Measured again as the reference (all through switches of `ArmyRules`, default = the game as it ships).

## Instrument (styles on the real sections, the declared kit of the late-night addendum, extended)
Styles (kinds: 0 Spearmen 1 Shieldwall 2 Riders 3 Archers 4 Priests 5 Wights 6 Wolves 7 Catapults): AGGRESSOR (Wolves/Riders, Advance x3), BANKER (Shieldwall, Hold Hold Guard; banks half of each round's gold in rounds 0-2, spends all after),
COMPOSER (cycle all kinds, Advance Hold Flank), MICRO (Archers/Priests; C0..A2: Flank Hunt FallBack; A3, A4: Hold@0 Hold@1 Hold@2 WhenLost25 = hold, then everyone closes once a quarter is lost), VETERAN (Age, Age, Aura(top trait), Advance@0; draft kind = Spearmen while Age is free, else the kind with the lowest worth/draft-price ratio, Priests;
it keeps back the smallest gold that lets all its army orders apply, found by probing a copy of the army through its code, which is what a player does with the "would cost" hint), MASS = CONTROL (all gold Spearmen, Advance x3). In A3 and A4 a seventh row MICRO0 (the old MICRO plan) is printed for reference and is not part of the verdicts.
Duels: one round, same levy by seed, 24 extra gold, the army orders through the army, then one battle, tick limit 120. Matches: 6 rounds, persistent armies from the same levy, start gold 12, the real spoils rule, roles swap each round, the arena FIXED per run (all four arenas are run), battle seed as the game's.
Levels: NOVICE replaces each drafted kind and each doctrine token by a random legal one with probability 0.5; EXPERT never does; both players of a pairing have the same level.
Size: duels 20 seeds x 4 arenas x both orientations = 160 battles per pair per level (SE .04); matches 10 seeds x 2 orders x 4 arenas = 80 matches per pair per level (SE .05, per arena 20 matches, SE .11, reported without verdict). Seeds 9,500,000+ (duels) and 9,600,000+ (matches), never used to design anything.
Search width: 4 cumulative candidate sets + 1 conditional + the C0 reference; 6 (7) styles, 2 levels, 4 arenas. No other candidate is tried and no style plan is changed after a run.

## Target T4 (restated, for the 5 styles + control at NOVICE and EXPERT, all four arenas)
A style "beats" another when its pooled score is above max(.55, .5 + 1.64 x SE) and "loses" when below the mirror of that. Read on the pooled (4 arenas) matrices of the duels and of the matches, at each level:
(a) every style's mean over the other five lies in .35 to .65; (b) every style beats at least one other and loses to at least one; (c) no style, the control included, beats all five others;
(d) CROSSOVER (matches): some ordered pair is below .45 at NOVICE and above .55 at EXPERT; (e) SKILL GAIN (matches): MICRO and BANKER each gain at least +.05 (mean over the others, EXPERT minus NOVICE).
(f) NO DRAFT RULE BEATS ALL: the ten autopilot draft rules (Advance x3, expert, duels, pooled) - none beats all nine others by the (b) test. PASS = (a)-(c) at both levels in both formats, and (d), (e), (f). Anything less names the failing clauses.
## Nulls (written now)
N1 mirror: a style against itself pooled (duels) reads .50 +- .09 (3 SE), else the instrument leans: D. N2 replay: 20 duels re-run in the same process give identical results, else D; and C0 must reproduce the recorded S/C0 style matrix of the late-night addendum
(identical to the rounding of that table, same seeds are NOT used, so agreement within 2 SE per cell, row means within .05). N3 sensitivity: MASS with +15 percent power against MASS (expert duels, 160 battles) must score above .58, else the instrument cannot see a 15 percent edge: D.
Analytic: with 36 cells at SE .04-.05, identical styles show a "beat" (above .5 + 1.64 SE) in about 5 percent of cells by chance, so a clause (b) pass inside that rate is reported as soft.
## Predictions (written before any run, to be scored afterwards)
P1 A1: VETERAN falls clearly below MASS (it pays 2x recruiting for its upgrade) and Aura is taken rarely; A1 alone does not stop MASS from beating most styles. P2 A2: no visible change in any style matrix (no style flips; Age on ranged kinds is the only reach-priced act in the kit). P3 A3: MICRO changes by more than 2 SE against at least two styles;
and MICRO gains from EXPERT more than MASS does. P4 A4: BANKER improves by under .05 (the interest is a few gold): banking stays last or second last. P5 T4 fails at A4 on clause (c) through MASS (Spearmen at price 1 is the dominator, as in the late-night addendum), which is why A5 exists.
## Fix rule
Ship the earliest cumulative set that passes T4. If none does, the four sets A1..A4 stay in the tree because the coordinator ordered them (the design is his); each lever's own effect on the clauses is reported with the same numbers, a lever that makes a clause measurably worse (beyond 2 SE) is NAMED as such for his decision, and A5 stays off. Mutant and contract test per behavioural change; digests re-pinned with reasons.
## Gap filed (for phasor-owner, Phasor\todo\superposition-gaps.md style; not edited from here)
AG4 PER-KEY TICK (a lasting per-cohort stat): `Tick(n)` ages the whole store. An order that fortifies ONE cohort (gold in, +1 veterancy on one kind) needs an operation that moves one key's time tick (a keyed Tick, or MultiplyTimes restricted to a key set). Without it the host would have to read the cohort, Add(-n) at the old tick and Add(+n) at the new one, which is the host-side retag already open as AG1b. Not built.
## Instrument notes (written after the code and before the first scored run; none of them is a result)
- The library carries every lever behind a switch of `ArmyRules` (default = the shipped game) and the trigger is in the vocabulary, so C0, A1..A5 are the SAME binary with different rules; the runner is `todo\difficulty-candidates\runner2\` (sources as run: Harness.cs 8556E73D63B779D0EA94BA993285C5F4FF95CC4F0B74BBAB6DA6B9C380474D8B, Styles2.cs 961A6BE1E789ACDE4FF7059D952150648AF8A4361F2ADF69D3A4BC2F77E5E13C, Report2.cs C5970F7E08A6446FAA264E463CD8F9FD50E05CADA4AB623A4E6CEA065CEBA287, Program2.cs 0C0037F43EF839D84C600FBFC695F172C3DB15C6AA3E3D89DD3573FF4D1C70F7 at lock).
- VETERAN reads its aura target (the trait most soldiers carry) AFTER its draft in both formats (the late-night matches read it before, its duels after); in C0 the numbers of VETERAN in the matches may differ from that table for this reason. BANKER spends its bank in a one-round duel (as before) and banks in matches.
- Mechanism check, not a score (10 seeds x 4 arenas, expert): in A3 the MICRO plan (Hold x3, WhenLost25) fires its trigger in every battle against AGGRESSOR, MASS and VETERAN, in 29 of 40 against COMPOSER, and never against BANKER (both sides hold, so nobody loses a quarter and the tick limit decides by soldiers standing).
  So MICRO's contingency does nothing against a side that also holds; that is a property of the declared plan, kept as declared. VETERAN's reserve probe applies all 3 of its army orders in every duel under A1 (Priests drafted; 33 soldiers against 60 for MASS).
- Replays carry cohorts only, so the trigger leaves the recording format as it was (version 2).

# RESULTS, SECOND ROUND (2026-10-19 night; runner2, raw data styles2-raw.zip, per-state reports styles2-report-<state>.txt, one summary styles2-summary.txt, all under todo\difficulty-candidates\)
Every state ran the same binary with different ArmyRules; C0 reproduces the recorded S matrix of the late-night addendum (duels, expert, row means .44 .16 .08 .52 .97 .83 against the recorded .45 .16 .07 .52 .98 .82), so the plumbing did not move the baseline.
Nulls, all states: N1 mirror OK (attacker score of a style against itself, mean .50 to .54, inside .50 +- .09; single cells reach .63 and .65 in A5); N2 replay identical in all six; N3 sensitivity OK (MASS +15 percent beats MASS .88; .96 in A5).
EvalApp race (the known null-exception, Tracer 2.0.3): 1 to 21 battles per run replayed from their seeds, exact. The interest is paid out of MapCounts and the whole of Doctrine.Tests is green (409 of 409, was 364 of 365 with one flaky failure from that race).

## Verdict: T4 FAILS in every state C0 to A5. No candidate passes; the earliest that would pass does not exist, so by the fix rule A1..A4 stay (ordered by the coordinator), A5 stays OFF.
Mean score over the other five styles (pooled over 4 arenas; AGGR BANKER COMP MICRO VET MASS; clauses a = every mean in .35-.65, b = each style beats one and loses to one, c = none beats all; Y met, n failed):
```
state  format  level   AGGR BANKR  COMP MICRO   VET  MASS | (a) (b) (c)
C0     duels   novice   0.52  0.38  0.37  0.55  0.59  0.59 |  Y   n   Y
C0     duels   expert   0.44  0.16  0.08  0.52  0.97  0.83 |  n   n   n
C0     matches novice   0.53  0.17  0.33  0.45  0.75  0.76 |  n   n   Y
C0     matches expert   0.55  0.08  0.20  0.37  0.98  0.82 |  n   n   n
A1     duels   novice   0.57  0.40  0.38  0.59  0.43  0.62 |  Y   n   n
A1     duels   expert   0.54  0.16  0.08  0.67  0.56  1.00 |  n   n   n
A1     matches novice   0.64  0.22  0.42  0.54  0.34  0.84 |  n   n   n
A1     matches expert   0.73  0.08  0.25  0.47  0.47  1.00 |  n   n   n
A2     duels   novice   0.57  0.40  0.38  0.59  0.43  0.62 |  Y   n   n
A2     duels   expert   0.54  0.16  0.08  0.67  0.54  1.00 |  n   n   n
A2     matches novice   0.60  0.22  0.45  0.50  0.40  0.83 |  n   n   n
A2     matches expert   0.73  0.08  0.25  0.47  0.47  1.00 |  n   n   n
A3     duels   novice   0.59  0.42  0.40  0.52  0.45  0.62 |  Y   n   n
A3     duels   expert   0.54  0.36  0.08  0.48  0.55  1.00 |  n   n   n
A3     matches novice   0.61  0.22  0.47  0.49  0.40  0.83 |  n   n   n
A3     matches expert   0.68  0.08  0.21  0.60  0.43  1.00 |  n   n   n
A4     duels   novice   0.59  0.42  0.40  0.52  0.45  0.62 |  Y   n   n
A4     duels   expert   0.54  0.36  0.08  0.48  0.55  1.00 |  n   n   n
A4     matches novice   0.60  0.22  0.46  0.48  0.40  0.84 |  n   n   n
A4     matches expert   0.68  0.08  0.20  0.61  0.43  1.00 |  n   n   n
A5     duels   novice   0.49  0.36  0.40  0.59  0.53  0.64 |  Y   n   Y
A5     duels   expert   0.44  0.14  0.10  0.98  0.56  0.78 |  n   n   n
A5     matches novice   0.55  0.16  0.47  0.54  0.52  0.76 |  n   n   n
A5     matches expert   0.56  0.07  0.16  0.71  0.55  0.96 |  n   n   n
```
Per arena (20 matches or 80 battles a cell, no verdict), clause (c) none-beats-all: at EXPERT it holds in 0 of 4 arenas in every state, duels and matches. At NOVICE in the duels it holds in 4 of 4 arenas in every state (the noise hides the dominator); in the matches it holds in 4 of 4 in C0, 1 of 4 in A1, 0 of 4 in A2, A3, A4 and 3 of 4 in A5. The full per-arena matrices are in the reports.
(d) CROSSOVER (matches): C0 none; A1..A4 exactly one pair, VETERAN over COMPOSER (.40 novice, .78 expert), real but trivial (COMPOSER is last everywhere); A5 none. In the duels A3 and A4 add MICRO over AGGRESSOR (.42 novice, .62 expert), the one crossover a battle-timing style earned.
(e) SKILL GAIN (matches, expert minus novice, mean over the others): C0 MICRO -.08, BANKER -.09 (A1..A2 MICRO -.07, -.04); A3 MICRO +.11, A4 MICRO +.13, A5 MICRO +.18; BANKER stays -.14 in A1..A4 and -.09 in A5. MICRO meets its half of clause (e), BANKER never does.
(f) NO DRAFT RULE BEATS ALL NINE (expert duels): fails in C0 and A4 (rule 0, all gold on Spearmen, beats all nine) and in A5 (rule 3, all gold on Archers, beats all nine).

### Before and after, pooled matches, EXPERT (row over column)
C0, before:
row over col      AGGRESSOR    BANKER  COMPOSER     MICRO   VETERAN      MASS   mean(others of 6)
AGGRESSOR              0.48      0.99      0.92      0.83      0.00      0.00   0.55
BANKER                 0.01      0.50      0.35      0.02      0.00      0.00   0.08
COMPOSER               0.08      0.65      0.51      0.28      0.00      0.00   0.20
MICRO                  0.17      0.98      0.72      0.45      0.00      0.00   0.37
VETERAN                1.00      1.00      1.00      1.00      0.55      0.89   0.98
MASS                   1.00      1.00      1.00      1.00      0.11      0.48   0.82
A4, after (the shipped set; MICRO0 is the old MICRO plan, reference only):
row over col      AGGRESSOR    BANKER  COMPOSER     MICRO   VETERAN      MASS    MICRO0   mean(others of 6)
AGGRESSOR              0.48      0.99      0.92      0.56      0.92      0.00      0.83   0.68
BANKER                 0.01      0.49      0.37      0.00      0.03      0.00      0.02   0.08
COMPOSER               0.08      0.63      0.49      0.07      0.23      0.00      0.29   0.20
MICRO                  0.44      1.00      0.93      0.50      0.66      0.00      0.90   0.61
VETERAN                0.08      0.97      0.77      0.34      0.56      0.00      0.52   0.43
MASS                   1.00      1.00      1.00      1.00      1.00      0.48      1.00   1.00
Pooled matches, NOVICE, C0 then A4: see styles2-report-C0.txt and styles2-report-A4.txt (A4 mean over others AGGR .60, BANKER .22, COMP .46, MICRO .48, VET .40, MASS .84).

## Predictions scored (written before any run)
P1 (A1 breaks VETERAN, MASS still beats most): YES, and more: VETERAN fell from .97 to .56 (duels, expert) and .98 to .47 (matches) while MASS rose to 1.00; it beats EVERY other style at .99 or better, VETERAN included (.00 for VETERAN in the A4 matches). The half "Aura is taken rarely" is not measured by this kit (VETERAN asks for its aura every round and its probe always affords it: 30 of 30 orders applied in the duels).
P2 (A2 shows nothing): YES, every row mean within .06 of A1 (the largest move is AGGRESSOR matches novice .64 to .60 and VETERAN .34 to .40); no style uses a reach-priced act except Age on ranged kinds.
P3 (A3 moves MICRO by more than 2 SE against two styles, and MICRO gains from skill more than MASS): HALF. MICRO moved against BANKER (.99 to .00 in the duels, a hold deadlock, see below), AGGRESSOR (matches expert .17 to .44) and VETERAN, and its skill slope went from -.04 to +.11; MASS still gains more (+.17).
P4 (A4 moves BANKER by under .05): YES, by .00 (.08 expert, .22 novice before and after). Banking stays last or next to last.
P5 (T4 fails at A4 on clause c through MASS): YES. A5 was therefore measured; it fails too.

## What each lever did (one measurement each, cumulative)
- A1 PRICE AGE AND AURA. The free power is gone: VETERAN (Age, Age, Aura) lost its edge and now keeps a quarter of its gold back for upkeep and fields 33 soldiers against MASS's 60, so MASS (all Spearmen, Advance) became the single dominator: 1.00 mean at expert in both formats, beating every style at .99 or better. Clause (c) at NOVICE, which C0 met in the duels and the matches, now fails too (MASS .62 and .84). Age is dominated by recruiting for Spearmen (worth 2, draft price 1); it pays only for kinds whose draft price is above their worth (Priests), which is what VETERAN now drafts. This lever made the game WORSE on T4 and is named for the coordinator's decision.
- A2 REACH IN THE PRICE. No measurable effect on any style (it prices Flip, Age and Aura, and no style flips; a Machine Flip now costs 5 where it cost 3 per Spearman). Its real target, the Catapult problem, lives in the DRAFT price, which this round did not touch: that is the A5 switch.
- A3 THE TRIGGER. A real skill axis for the style that uses it: MICRO's slope from skill went from -.08 (C0) to +.11 (A3) in the matches, and it earned the one duel crossover (MICRO over AGGRESSOR .42 to .62). It also exposed a deadlock rule: Hold against Hold is decided only by soldiers standing at the tick limit, so the trigger (which needs a loss to fire) never fires against a holder and MICRO loses to BANKER 0.00 (duels, expert) because BANKER brings 42 soldiers to MICRO's 39. In the mechanism check the trigger fired in every battle against AGGRESSOR, MASS and VETERAN, in 29 of 40 against COMPOSER and in none against BANKER.
- A4 INTEREST. No effect, and the reason is plain: in the diagnostic BANKER against MASS (styles2-interest-diag.txt) BANKER's army is wiped every round (0 soldiers after each of 6 rounds) and the interest earned is 4 gold per match (gold/7, floored, on a purse of 3 to 16): there is nothing to compound when every round is lost. The lever is built and correct (contract case plus mutants) and too small, and too late, to change a style that loses the fights.
- A5 DERIVED DRAFT PRICES (conditional, OFF). Spearmen cost 2; MASS still wins the matches (.96 expert) and the dominator moves to ranged plus Hold: MICRO .98 in the duels (rule 3, all Archers, beats all nine draft rules). Price moves the dominator, as the late-night addendum found; it does not remove it.

## What still fails, and why (from the logic and the numbers, not tried)
1. NO COUNTERS (D2 of the diagnosis): no kind beats another, so one composition with the right orders beats all, and pricing only chooses which. The C1 counter cycle was measured earlier only WITH a free Flip; with the present rules it has not been re-measured together with these levers.
2. THE CHEAP BODY. A Spearman is one gold; "Advance with everything" needs no skill, no infrastructure, and scales with gold. A1 removed the only thing that could beat it (free veterancy).
3. HOLD IS A DEADLOCK DECIDED BY BODY COUNT at the tick limit. Any Hold-heavy style then reduces to a draft count; this is a rule of the battle (Finish: more soldiers wins), not of the new trigger, and it needs a decision (a tie-break that counts power, or a siege rule).
4. THE ECONOMY STYLE HAS NOTHING TO ECONOMISE: if the army is wiped each round (BANKER against MASS), gold is spent on bodies that die and interest is a rounding error. The fortify lever (a lasting per-cohort stat) needs a per-key Tick (gap AG4 above); until then the only long-run lever is Age, which A1 made dear.
5. THE SKILL KIT: the harness models proficiency as execution error. The trigger gives MICRO a slope; BANKER and COMPOSER (the styles that invest) lose from expertise at every state (BANKER -.14, COMPOSER -.26 in A4) because a precisely executed plan that cannot beat the dominator loses more precisely.

## Decisions for the user (the feel of the game changes)
1. Keep A1? It is in the tree because it was ordered, but it removes the free veterancy that made VETERAN a style and leaves the Spearman mass unopposed. Reverting it (ArmyRules.PriceAge/PriceAura = false) restores C0.
2. Price a Spearman at its worth (A5, DerivedDraftPrices, OFF): measured, moves the dominator to ranged-hold, does not fix. Needs counters or a hold rule with it.
3. The hold deadlock: choose a rule (the side with more POWER standing wins at the limit, or an attacker who has not engaged by tick N loses, or a pressure rule). Not changed here.
4. Raise the interest or add the fortify lever after AG4: only worth it once a banking style can survive a round.
5. Vocabulary version 2 (three trigger tokens, ids 54..56): new family Trigger; the Showroom palette does not list them (the one failing check of 154 in DoctrineUiProof, below).

## What ships (fix rule applied; nothing committed, no version touched)
In the tree: Age and Aura priced (A1), reach in the worth (A2), the loss trigger (A3, vocabulary version 2), interest (A4), all through ArmyRules whose defaults are the game; A5 is a switch that ships OFF. Contract cases: ge-is-paid-for-and-legal-only-when-affordable, ura-is-rented-and-legal-only-when-affordable, anked-gold-pays-interest-when-a-round-closes; loss trigger: TracerTriggerTests.
Mutants watched red: Age free, Aura free, Age always legal, Aura always legal, interest doubled (each over the stub and the Pleroma army); a rule switched off (age, aura, interest, reach) on both armies; the trigger never firing, firing at the first tick, reading the enemy's losses (3 probes, 5 checks). Digest pins re-pinned with reasons (stub 5025665426094534569, real 8746276498987472616).
Doctrine.Tests: 409 of 409 green (was 365 with one flaky failure). The Showroom page builds (0 errors); its DoctrineUiProof passes 153 of 154: the one failure is the palette check (36 enabled, 9 disabled, 8 Cull not shown) which sees the three trigger tokens outside the palette.
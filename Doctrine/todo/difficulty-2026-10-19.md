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
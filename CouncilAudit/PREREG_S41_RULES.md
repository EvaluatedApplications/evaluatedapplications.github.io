# Pre-registration: two rules from the Session 40 findings (Session 41, 2026-10-04)

Written and committed BEFORE the rules are coded or run. Both rules are built from what I saw in Wokingham in Session 40, so
every Wokingham result below is IN-SAMPLE and labelled so; the out-of-sample evidence is the other 20 councils.

## Rule 1: NSquaredListing (Schedule A, multi-payee transactions)
Definition. In a multi-payee transaction (3 or more payees, one Gross repeated), take each payee's rows R. The transaction has the
"n-squared" shape when for EVERY payee R = n^2 for an integer n >= 1 and every distinct line (Net, Gross, Description, Service,
Cost Centre, Pay Date text) of that payee is printed a multiple of n times, and at least one payee has n >= 2. The de-duplicated
sum is the sum of each distinct line's count / n times its Net.
- Reconciles: de-duplicated sum equals the stated Gross within 0.01 (net or VAT-uplifted). Then class `NSquaredListing`
  (a publication defect: the report prints a payee's n lines n times); it leaves the Unreconciled pile like DoubleListing does.
- Shape holds but does not reconcile: the row stays Unreconciled; the detail gives the de-duplicated sum and the gap to Gross.
- Schedule B groups whose transaction has the shape get a reading (flag, class stays Unclear, count unchanged) that states the
  de-duplicated value, so a value is never quoted from the inflated line total.
Predictions written now: (P1) Wokingham: all 9 academy runs in Schedule A have the shape (the 10th multi-payee row, 3815610,
does not); (P2) exactly 1 of them reconciles (3804026, April 2023, 253,570.88); (P3) no multi-payee transaction in any of the other
20 councils has the shape (null: a fire elsewhere is read by hand and, if it is a real defect, reported, if not, the rule is
too loose and is tightened with the change labelled post hoc).

## Rule 2: BlockContractCatchUp (Schedule B reading, a flag)
Definition. For an open Schedule B group of supplier S, net N dated in month M: the BAND STREAM is every non-credit positive
transaction of S whose Net is within 5% of N (not equal to N: a block contract's invoice moves by pence and by rate change). The
stream qualifies when it has at least 8 active months, they fill at least 60% of the months from first to last, at least 70% of
active months carry exactly one such transaction. The group is read as a block-contract catch-up when
payments(M) <= 1 + empty(M), where payments(M) = band transactions in M and empty(M) = consecutive empty months just before M
(at most 6). A reading, never a classification: the group stays Unclear and in the open count (owner rule, Session 34).
It applies only to groups with no earlier reading.
Predictions written now:
- H1 (in-sample): the six Optalis months from Session 40 (Jun 2022, Jul 2023, Nov 2023, May 2025, Oct 2025, Dec 2025) are read
  as block catch-ups. Refuted if fewer than 5 of the 6 groups are read. (Dec 2025 is stated because the sixth is a pair; if the
  band stream has fewer than 8 months in the file at that supplier, that group cannot be read and I say which.)
- H2 (null A, placebo month): for every group in a qualifying stream, put the same group size at a random month of the same
  stream (seeded, 200 draws per group) and apply the same test. The real read rate must be at least twice the placebo read rate,
  else the reading says nothing about the copy (the Session 37 result for the young-series rule).
- H3 (null B, size of the move): the reading may touch at most 10% of the open Schedule B groups in each of the 21 councils; a
  council above 25% means the rule is not a method and it is not shipped. (Session 33 lesson: a rule moving 40-80% of the open
  pile is not a method.)
- H4 (hand check): a seeded sample of 30 read groups outside Wokingham is opened against its own stream; the claim is that in
  every one payments(M) <= 1 + empty(M) holds as printed (arithmetic, not truth), and I list any whose stream I would not call
  a block contract.
What would show I got this wrong: H1 fails, H2 fails, or H3 fails. Any of these means the rule is withdrawn from the export and
the finding stays text only.

## Amendment 1 (written after the first Wokingham export, before any further run or any placebo number)
What I had seen: the first export of Rule 1 and Rule 2 as pre-registered on Wokingham only. Rule 1: P1 holds (9 of 9 academy runs have
the shape), P2 holds (1 of 9 reconciles: 3804026, 253,570.88), 2 Schedule B groups get the reading. Rule 2 as defined: H1 FAILS,
0 of the six Optalis months are read (Wokingham reads 46 other groups, small care-home and school lines). I then checked why, on
the slim transaction file: the 537,730-band stream has 27 active months of 60 between Apr 2020 and Mar 2025 (24 single), so
it fails the 60% density gate (45%). The gate is what excludes a block contract that is paid when invoiced.
Change (post hoc, labelled; the pre-registered result stays reported as 0 of 6): the density gate is replaced by a norm test on the
stream itself: the stream's own months with two or more payments must number at least 2 and EVERY one of them must satisfy
payments <= 1 + empty (empty counted as before, at most 6). A stream with a real extra payment in any month fails. All other
gates (8 active months, 70% single months, band 5%, only open ungrouped, flag only) are unchanged. Net 'reads' definition for the
group is unchanged.
H1 is re-run with the amended definition and reported as post hoc, in-sample. H2 is made exact: placebo = remove the group's own
copies from its stream, put the same number k of payments at each other month of the stream's span (counting the payments already
there), apply payments <= 1 + empty; the placebo rate is the share of those (group, month) pairs that read. H3 and H4 stand.
If H2 or H3 fails the reading is withdrawn from the export even though H1 now holds.

## Amendment 2 (written after the Amendment 1 Wokingham run, before any other council is run)
Result of Amendment 1 on Wokingham: 32 groups sit in a qualifying stream and all 32 read (by construction: a group of two or more
is one of its stream's multi-payment months, and the norm test requires every such month to read). Placebo: 1,069 of 1,591
(group, month) pairs read, 67.2%. Real/placebo = 1.49 < 2, so H2 FAILS: with "no more than" a gappy stream reads almost any
placement, which is the Session 37 lesson again. (S40's observation was stronger: payments EQUAL to 1 + empty in 6 of 6.)
Change (post hoc, labelled): the test becomes equality. A stream qualifies when it has at least 3 multi-payment months and in
EVERY one payments == 1 + empty (empty counted as before, at most 6; the lookback cap makes a month with more than 6 empty months
fail); the group reads when its own month is also equal. All other gates unchanged. The placebo is the same with equality (k
copies at another month equals 1 + empty there). Decision rule written now: ship the reading only if the placebo rate is at most
half the real rate AND H3 holds in all 21 councils; if H2 fails again, remove the hook and keep the finding as text and as the
test case. The three Amendment 1 and Amendment 2 results all go in the close-out.

## What the exports are allowed to move
Class counts in Schedule A: only the multi-payee rows with the n-squared shape and exact reconciliation (expected 1 row).
Schedule B: no classification changes (readings only), so T_B, the frozen stage files and `budgettest` must be byte-identical.
Every other export file must be byte-identical except the ExplainedBy/ExplainedMeaning columns of exceptions.csv and the
phone slices that carry them.

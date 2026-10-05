# Pre-registration, Session 43: Surrey and the extract-timestamp blind spot

Written BEFORE the fix is coded or re-run. Date 2026-10-04.

Observation (already seen): `export\file_duplication.csv` gives Surrey 2018-04_06 an exact-repeat count of 0 of 54,248 rows (rate 0.0000), where
every other Surrey file has 4.6% to 17.5%. That file's first published column, "extractdate", holds a per-row timestamp
(20180724135050.0726188+01:00), and the engine keeps every unmapped column in OtherColumns, which the exact-repeat key includes. Every other
Surrey file with that column holds one value for the whole file.

Claim 1: the zero is the timestamp, not the data. After the key ignores the "Extract Date" cell, 2018-04_06 has a repeat RATE between 0.04 and 0.16
(the range of its neighbours' rates, 0.046 to 0.175). REFUTED if the rate is outside that range (then the cause is something else).
Null 1: in the 36 other Surrey files the repeat COUNT must be unchanged by the strip (their timestamp is one value per file). Any change refutes
the idea that the strip only removes a per-row timestamp.
Claim 2: the 21 other councils' rows in file_duplication.csv are byte-identical before and after (none has an "Extract Date" column).
Refuted by any difference.
What this does not show: that the repeats are errors. Surrey publishes no transaction number, and per-resident care and school lines repeat
by design; the rate is compared with its own neighbours, as for every other council.

## Amendment 1 (written before any measurement of other councils; 2026-10-04)

Observation while reading Surrey's most frequent payee labels: besides "Redacted Personal Data" (628,557 lines) the file spells the same
placeholder wrongly in 2,254 lines: "Redated Personal Data" 1,722 and "Redcated Personal Data" 532. The engine's redaction test is "contains REDACT" (or RED...ACTED), so these
are counted as a real payee and grouped in Schedule B. Also 99 lines have the payee "Null" (a missing name written out).
Rule under test: a payee is a redaction placeholder also when it contains "PERSONAL DATA" (any case), or when the whole name is "NULL" (any case).
A1: the rule matches at least 2,000 Surrey lines that the old test did not, and in each of the other councils' exports fewer than 100 lines in all;
refuted if the other councils together match more than 100 lines (then the rule is too broad and I read the names before keeping it).
A2: with the rule, Surrey's retained + redacted rows still equal its mapped rows (2,398,241), and its signed total is unchanged.
What it does not show: that the old test's grouping of these lines produced a false finding. It shows only that an unrelated-payee placeholder was treated as one payee.

## Results (written after the runs; 2026-10-04)

Claim 1 HELD: 2018-04_06 now has 6,026 exact-repeat rows of 54,248 (rate 0.1111), inside the pre-registered 0.04 to 0.16 (it was 0 of 54,248).
Null 1 REFUTED as written: 35 of the other 36 Surrey files have an identical repeat count, but 2018-01_03 gains 60 (6,601 to 6,661). Cause, read from the raw file: that
file carries two extract times a minute apart (12/04/2018 15:51 on 43,155 rows and 15:50 on 34,128 rows), so 60 lines repeated across the two batches were hidden as well.
Claim 2 HELD: every one of the other 21 councils' rows in file_duplication.csv is byte-identical before and after (5 rows changed, all Surrey: three only in the
neighbour median).

Amendment 1: A1 HELD. The rule matches 2,374 Surrey lines the old test missed (Redated Personal Data 1,722, Redcated Personal Data 532, Null 99, Dedacted Personal Data 12, 9 more
in other spellings) and 45 lines in all other councils together (Cornwall 37, Wirral 8; Wirral's are "REDACETED PERSONAL DATA" 7 and "REEDACTED PERSONAL DATA" 1, real
misspellings of the placeholder; Cornwall's are "Personal Data" 12 and firm names with "- personal data" appended 25). A2 HELD: Surrey retained 1,764,397 + redacted
633,844 = 2,398,241 mapped, signed total GBP 15,643,217,474.79 unchanged. Effect: Surrey Schedule B 138,743 to 138,536 groups and 432,754 to 431,772 member rows;
Wirral's and Cornwall's Schedule B and D row counts are unchanged (the 45 lines were in no group).

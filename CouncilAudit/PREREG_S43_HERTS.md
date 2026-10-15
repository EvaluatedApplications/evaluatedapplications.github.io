# Pre-registration, Session 43: Hertfordshire

Written before the Hertfordshire export is read for findings and before the fix below is coded. 2026-10-04.

State: 31 CSVs (11 quarterly April 2022 to December 2024, 20 monthly January 2025 to August 2026), 963,276 rows, signed total GBP 6,939,472,722.64, a real
transaction number, a beneficiary id, threshold GBP 250 until March 2025 and GBP 500 from April 2025. `rawcheck` 300 of 300. The engine excludes 347,334 rows (36.1%) as redacted.

Observation: reading the redacted names one by one, 345,253 are "Identity Redacted" in six capitalisations and "Idenity Redacted" (caught by the plain test), and 2 are
"REDACTIVE EVENTS LTD" and "Redactive Events Limited": a real events company whose name contains the letters REDACT. The engine's test is "contains REDACT", so it drops them.
Rule under test: a name is a redaction placeholder only when it contains REDACT and does not contain REDACTIVE.
R1: the rule changes exactly 2 Hertfordshire rows and fewer than 20 rows in all the other councils together (any council over 20 is named and read).
R2: nothing else moves: the 22 other councils' retained + redacted still equal their mapped rows.
What it does not show: that any payment to Redactive Events is right or wrong; it shows a company was treated as a withheld name.

## Results (written after the runs; 2026-10-04)

Correction to the text above: the placeholder rows were mis-summed there. "Identity Redacted" in its five capitalisations plus one "Idenity Redacted" is 347,332 rows (36.1%, GBP 521.9m), not 345,253;
347,332 + the 2 Redactive rows = 347,334, which is exactly what the engine had excluded.
R1 REFUTED as written: the rule moves 270 rows, not "2 in Hertfordshire and fewer than 20 elsewhere". The false positive is a real supplier group (Redactive Media Group: Redactive Publishing,
Redactive Events, Redactive Media Sales) present in 22 of the 24 councils: Hertfordshire 2, and elsewhere 268 (Merton 46, Sheffield 31, Leicester 27, Leeds 22, Kirklees 21, Wokingham 12, Essex 18, and 15 more councils
under 20 each). The rule is kept: these are real company names. The visible effect is small: Schedule B gains 6 member rows in Merton (3 pairs of REDACTIVE MEDIA SALES lines of GBP 897.75, 750 and 600),
4 in Kirklees, 2 in Coventry and 2 in Leicester; in no other council does a Schedule B row move.
R2 HELD: for every council retained + redacted = mapped after the change (Essex 2,277,760 + 1,436,202; Surrey 1,764,404 + 633,837; Hertfordshire 615,944 + 347,332).
Frozen budget-test files (Stage 1 to 2c freeze files and the pooled results) are byte-identical; the unfrozen budget_units, budget_reconciliation and budget_test rows for four councils moved
(T_B of Merton 2021-22 by GBP 1,648, Coventry 2022-23 by GBP 595, Kirklees 2019-20 and 2021-22 by GBP 950 and 995; the permutation threshold of one arm by 0.0001). All six arms still INCONCLUSIVE.

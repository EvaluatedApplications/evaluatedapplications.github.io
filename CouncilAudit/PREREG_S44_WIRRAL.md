# Pre-registration, Session 44: Wirral December 2022 (legacy .xls) through XlsReader

Written BEFORE `prep wirral` is run on the .xls. Nothing about the December file has been read except its size (1,434,112 bytes) and that it starts with the .xls signature.

Neighbours (physical lines of the prepared files): October 2022 8,002, November 2022 8,414, January 2023 8,431.

Claims, each able to fail:
- P1 (size): December 2022 holds 6,500 to 10,500 data rows (a December is thin in some councils; I allow a wide band around 8,000).
- P2 (tag): the payment dates, not the name, put the sheet in 2022-12, and at least 95% of its dated rows fall in December 2022.
- P3 (reader): XlsReader reads every date cell as a date (no row with an unparseable date) and every Paid Amount as a number to the penny.
- P4 (no overlap): no row of the December file equals a row of November 2022 or January 2023 by supplier, amount, paid date and description (`filedup` / `crossfile`: 0 cross-file repeats involving 2022-12 beyond what the council's own neighbouring months show).
- P5 (the other 47 months): re-running `prep wirral` rewrites all 47 existing `wirral_*.csv`; each must be byte-identical (SHA-256 of the before list in `scratch/s44_wirral_before.txt`). Null: any differing file means the prep change is not neutral and I stop.
- P6 (hand check): `rawcheck wirral` 300 of 300 re-read against the raw files, December included.
- P7 (the loop): the 47 old months' exceptions are unchanged except through cross-month rules (a December row can join a group that spans Nov-Jan); I will diff Schedule B keys and say what moved.

Failure of P1 or P2 is reported as failed, not re-tuned.

## Addendum A, before the loop (written after prep, rawcheck and export, before filedup/txntwin/crossfile/budgettest/reconcile)
Results so far, written here so they cannot be re-tuned: P1 FAILED (12,603 rows, band was 6,500 to 10,500; neighbours 8,413 and 8,430). P2 held (12,603 of 12,603 dated 12/2022). P5 held (50 existing prepared files byte-identical; I wrote 47 in P5 by miscount, the right number is 50). P6 held (rawcheck wirral 300/300). Total signed value GBP 36,087,454.92 against GBP 35.94m and 38.42m either side, so the extra rows are not extra money.
Cause of P1 on a first look (to be graded, not asserted): 1,964 negative lines (15.6%; Nov 364, Jan 230); 1,446 of them are dated 20/12/2022, 66 transactions, 59 suppliers, GBP -3,131,072.03, in adult social care and energy.
New predictions, before the loop:
- P8: the 20 December credit lines are in the file as published, not an artefact of the .xls reader: the raw .xls re-read independently (ExcelDataReader, a different path from the prep step: count of negative Paid Amount cells on that date) gives the same 1,446.
- P9: the budget test (`reconcile all`, `budgettest`) arms all remain INCONCLUSIVE. The frozen Stage 2c pool file was frozen without Wirral December 2022; I leave it as committed and report any difference as new data, not as a re-run of the frozen statistic.
- P10: no other council's cross-council row changes (cross_file_repeats, file_duplication, txn twins) except rows involving Wirral; checked by git diff on the tracked cross-council CSVs.

## Results (written after the loop; refuted claims stay refuted)
- P1 FAILED (12,603 rows). Cause graded afterwards, not asserted: POTENSIAL LIMITED T/A POTENS has 2,593 lines in December (63, 82 and 66 in Oct, Nov, Jan), 2,570 of them in four transactions (2224330, 2224331, 2224332, 2224334) all paid 20/12/2022; without that supplier the month is 10,010 rows, inside the band. The same date holds 1,446 negative lines (GBP -3,131,072.03, adult social care and energy); the four Potensial transactions each net positive (GBP 28.7k to 142.0k). A published itemised run, not a reader artefact: credits have equal-value counterparts in the council's own January file (read by the .xlsx reader).
- P2 held (100.0%). P5 held (50 of 50 existing prepared files byte-identical). P6 held (300/300).
- P3 held in the sense that no date failed to parse and the written total re-reads to the penny (raw rows 341,208 = written 341,203 + 5 blank).
- P4 held for the file-level test (file_duplication row for 2022-12: 11,806 rows, exact-repeat rate 30.09% against the neighbour 75th percentile 21.78%, Flagged False; 0 file repeats). It is NOT a clean zero for content: one new transaction-twin pair, below.
- P8 NOT DONE as written: there is no second .xls reader on this machine (no Excel, no Python; ExcelDataReader's net10 build will not load under Windows PowerShell 5.1). The weaker evidence above (credits with counterparts in the .xlsx January file) is what I have.
- P9 held: all six arms INCONCLUSIVE after `reconcile all` and `budgettest`; the frozen stage files are untouched; Wirral is not a pool unit. `budget_units.csv` and `budget_reconciliation.csv` (unfrozen) changed only through Wirral's 2022-12 row and the neighbour-rate column that moves with it.
- P10 REFUTED as written: two cross-council files changed in rows that are not Wirral's: `debt_sink.csv` (19 non-Wirral lines) and `payment_misfits.csv` (7), all of them rows that involve Hertfordshire CC. The Session 43 committed copies held 0 Hertfordshire rows (now 20 and 7): they were stale for the 24th council. Fixed by this commit.
- New read, not a finding (a payments file cannot show wrongdoing): TOTAL GAS AND POWER LIMITED, transactions 2217604 (28/11/2022) and 2218986 (02/12/2022), 130 lines each, GBP 146,231.08 each, identical on cost centre, description and amount line for line (only the number formatting and the transaction number differ). The supplier's other large energy transactions differ in line count and value (95, 83, 93, 106, 109 lines). No credit of that size follows in December 2022 to March 2023. It is the largest of Wirral's 30 transaction-twin pairs by lines.

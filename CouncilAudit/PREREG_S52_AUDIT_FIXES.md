# Pre-registration, Session 52: fixes for the independent audit of 2026-10-04

Written and committed BEFORE any export, scan or statistic is run with the changed code. The "before" figures below were
measured on the committed export (Session 51 state) with read-only commands: `rawrecon all` (new, run against the old export,
log `scratch/s52_rawrecon_before.log`) and `dropscan` (new, read-only, `scratch/s52_dropscan.csv`), plus direct reads of
`export/*.csv`. Each fix states what it should change, what must NOT change (the nulls), and what would refute it.

## Fix 1 (audit E1): no silent drop between a council's file and the published rows

Rule change. `AuditEngine.MapRows` used to skip every row whose transaction-number cell is blank, without a word. Now a row is
dropped only when it holds no payee AND no payment date (a footer, sub-total or note line), and every dropped row is written to
`export/<slug>/dropped.csv` with its reason. A row with a payee or a date but no number is a payment and gets its own placeholder
"(no number) N". `WestBerkshireFixups.AddSyntheticRowId` puts the row number in the RowRef column (it was appended after the row's
own last cell, so a short row had it under the payee column) and leaves a fully blank row blank. `RbwmFixups.LineKey` ignores a
placeholder number. A file `export` cannot read is reported with its reason (`ExportInputs.KnownExcluded`).

Before (rawrecon on the Session 51 export: records in the table the mapping receives vs published rows; 148 files not accounted):

| Council | Records | Published | Unaccounted rows | Unaccounted Net | What the rows are (dropscan) |
|---|---|---|---|---|---|
| Reading | 512,489 | 505,832 | 6,657 | 230,323,074.18 | 2,621 unnumbered payments (206,026,279.04); 36 "#N/A" lines (0.00); 4,000 rows of 2021-05, a whole file skipped (24,296,795.14) |
| West Berkshire | 92,897 | 91,784 | 1,113 | 0.00 | blank trailing rows that got a row number under the payee column |
| RBWM | 70,754 | 68,618 | 2,136 | 56,026,288.66 | 84 unnumbered payments (40,040.61; 48 with a redacted payee); 405 footer/blank-like lines (36,301,071.64); 1,647 already published in an earlier file |
| Leeds | 2,701,281 | 2,701,271 | 10 | 93,408,532.37 | 9 metadata lines and one total line |
| Bristol | 469,723 | 469,694 | 29 | 1,906,884,014.60 | one amount-only total line per file |
| Wakefield | 746,573 | 746,555 | 18 | 1,133,501,484.56 | amount-only footer lines |
| Coventry | 1,099,019 | 1,099,011 | 8 | 107,669,204.00 | 5 note lines, 3 amount-only lines |
| Nottingham | 336,170 | 336,169 | 1 | 0.00 | one "REDACTED GENERAL SUPPLIER" line, no number, no date, 0.00 |
| Newcastle | 647,515 | 647,513 | 2 | 62,139,328.17 | "Grand Total" lines |
| the other 19 | | | 0 | 0.00 | |

Reading's 2,621 unnumbered payments: 2,034 rows / GBP 69,352,398.38 from July 2021 (2,021 rows in the 61 monthly CSVs, as the
audit found, plus 13 in the 2021-09 workbook) and 587 rows / GBP 136,673,880.66 in the files before July 2021 (2019-Q3 to 2021-06:
inter-authority loans such as GBP 5,009,567.12 to the City & County of Swansea, CHAPS to Brighter Futures for Children, HMRC
remittances). 109 of the 2,621 carry the payee "Redacted". August 2025: 33 rows, GBP 5,783,038.88, including Gateley PLC
GBP 3,704,464.99 on 12/08/2025. FY2023-24 (April 2023 to March 2024 files): 454 rows, GBP 8,434,327.20.

Predictions (all must hold):
1. Reading published rows 505,832 -> 508,453 (+2,621), published Net +206,026,279.04; redacted rows 1,118 -> 1,227 (+109).
   August 2025 published 3,521 -> 3,554, and the Gateley PLC line is in it. FY2023-24 F in `budget_units.csv` rises by 8,434,327.20.
2. RBWM published rows rise by at most 84; the 2025-05 copies of the 2025-04 unnumbered lines are dropped as already published,
   so the rise is between 28 and 84. 405 lines are dropped with the footer reason and 1,647 (plus any unnumbered copies) with the
   already-published reason.
3. Nottingham published rows +1 (the redacted line), redacted rows +1. West Berkshire published rows unchanged (91,784).
4. Leeds, Bristol, Wakefield, Coventry, Newcastle: published rows unchanged; their dropped lines appear in `dropped.csv` with the
   footer reason.
5. `rawrecon all` after the re-export: 0 rows unaccounted in every council; Reading 2021-05 reported as an excluded file with its
   reason (4,000 rows).

Nulls (written before the run; either failing refutes the claim that the change is confined):
- N1a: every `*.transactions.csv` of the 23 councils other than Reading, RBWM, Nottingham (and West Berkshire, Birmingham and
  Sheffield, which use the changed row-number fixup) is byte-identical to the committed file.
- N1b: West Berkshire, Birmingham and Sheffield `*.transactions.csv` are byte-identical too (the fixup change only touches rows
  shorter or longer than the header, and none of those reached the export as a payment). If any differs, the row is reported.

## Fix 2 (audit E2): Schedule A difference is the VAT-adjusted gap

Rule change. `ScheduleARow.Difference` is the stated Gross minus the VAT-type-uplifted expected Gross (the gap the classifier
already used). It was Gross minus Net, which is the VAT. A new class `SmallGap` takes a gap of under GBP 1.00 that is above the 5p
rounding band; `Unreconciled` keeps gaps of a pound or more.

Before (Wokingham, 7,867 Schedule A rows): Unreconciled 1,856, of which 482 have a VAT-adjusted gap under GBP 1 and 0 at or under
5p; for 1,759 the shown Difference differs from the VAT-adjusted gap by more than GBP 1. VatRoundingNoise 371.

Predictions: Wokingham Unreconciled 1,856 -> 1,374, SmallGap 482, VatRoundingNoise 371 (unchanged), every other class unchanged;
Matrix SCM Ltd 3763717 shows Difference 0.37 and class SmallGap. Other councils' SmallGap counts are reported, not predicted.
Null N2: no Schedule A row appears or disappears in any council except through Fix 1's new Reading/RBWM/Nottingham rows (the set of
A row keys is otherwise identical); only the Difference column, the class and the multi-payee detail text may change.

## Fix 3 (audit E3, E4, E5): transaction twins, the rule the page states

Rule change (`TxnTwinScan`). Both first pay dates must be published. The hidden "signature in at most 3 transactions" condition
is replaced by a stated one: in a run of four or more identical transactions a pair is listed only when it is closer than the run's
own median spacing between distinct first pay dates. Three columns appended: GroupId, GroupSize, ExtraCopyValue (each transaction
after a group's first is credited once; the sum over a list is the value of the extra copies, signed).

Before: 640 pairs in 15 councils, 3 with DaysApart -1 (Wokingham Cambian 3644308/3605427, Derwen College 3639051/3661298,
Phoenix Learning & Care 3644265/3657017); sum of |TotalValue| over pairs 40,224,556.62.

Predictions: the 3 undated pairs are gone; every other previously listed pair is still listed (a group of 3 or fewer is judged
exactly as before); the pair count rises (runs of 4+ were wholly excluded before), including The Loddon Foundation 3768856/3768857
(22/11/2022) and 3779302/3779303 (05/01/2023); Wokingham Cambian 3833138/39/40 is one group of 3 with ExtraCopyValue summing to
108,673.34; Reading Oxfordshire CC 4605483/4605499/4605500 one group of 3 summing to 340,417.50.
Null N3: no previously listed dated pair disappears.

## Fix 4 (audit E6): government figures for the seven deferred councils, as a new Stage 2d

Rule change. The ONS codes of Surrey, Essex, Hertfordshire, Stockport, York, Calderdale and Camden are added to
`PublishedOutturn.OnsBySlug`; their units are labelled pool "stage2d" (they fell through to "confirmatory" before, harmless only
because no figure was held). `budgettest` now reads every frozen stage from its freeze file, never from the live
`budget_units.csv`, and reads Stage 2d live until `export/budget_units_stage2d.csv` is committed.

Order, binding: (1) `outturn` for all eight years, (2) `reconcile all` with output to a log I do not read, (3) a script copies the
stage2d rows to `budget_units_stage2d.csv` without printing them, (4) the freeze is committed, (5) only then `budgettest stage2d` and
`budgettest pooledall3` (all five groups, additional, never a verdict).

Predictions: `published_outturn.csv` only gains rows (Surrey 2023-24 RSX running expenses 1,437,753 thousand, Essex 1,763,574,
Hertfordshire 1,335,216, Stockport 453,206, York 252,563, Calderdale 323,189, Camden 566,383); Stage 2d verdict INCONCLUSIVE on all
six arms (as in every earlier stage).
Null N4a: every row already in `published_outturn.csv` is unchanged. Null N4b: `budgettest` for stage1, exploratory, stage2,
pooled, stage2b, pooledall, stage2c and pooledall2, read from the freeze files, reproduces the committed `budget_test*.csv`
byte-for-byte, both before and after the Fix 1 re-export moves Reading's F in the live file.

## Fix 5 (audit E10): debt sink counts undated rows

Rule change. Rows with no published pay date stay in Rows, NetOut, CreditRows and CreditNet; the year series, rising streak and
month test still need a date, and two appended columns UndatedRows / UndatedNet say how much they leave out.
Prediction: Wokingham -> Reading BC: Rows 1,080 -> 1,139, NetOut 75,621,723.57 -> 77,661,331.87, CreditRows 60 -> 62, CreditNet
-4,620,660.13 -> -4,935,360.71, UndatedRows 59. NetOut + CreditNet = 72,725,971.16, the flows panel's figure.
Null N5: for a payer with no undated rows the row changes only through Fix 1's new rows (Reading, RBWM, Nottingham as payers).

## Fix 6 (audit E12, E15, E11): three consistency fixes

- Cross-file repeats compare description and service without letter case (Leeds republished November 2022 card lines in its
  December file with the purpose re-capitalised). Prediction: Leeds 2022-12 6,582 -> 8,556 (the multiset count; 8,568 rows have a
  match of any count). Null N6a: no file's RowsAlsoInEarlierFile falls.
- `phoneexport` joins a spreadsheet-mangled number's exception rows ("~row<n>") to their payment. Prediction: Merton exception rows
  unmatched to a payment 63 -> 0, months.csv 115 rows -> 114, the month count 114.
- A multi-payee transaction's detail counts named lines and states the redacted lines of the same number with their value; no cause
  is stated for a gap. Prediction: Wokingham 3766535 / 3766545 (11/11/2022) say 1 redacted line of 350.00 and a gap of 19,250.00.
  NSquaredListing class count unchanged (1).

## What would make me stop and report instead of shipping

Any null failing; rawrecon showing an unaccounted row after the re-export; a fixed figure not equal to the prediction (it is then
reported as refuted, with the reason found, not adjusted to fit).

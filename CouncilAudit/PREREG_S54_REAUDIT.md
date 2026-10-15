# PREREG Session 54 (2026-10-05): fixes for the re-audit (N2 to N8, cosmetic), N4 first

Written before any run of the changed code. Source: the independent re-audit `council-reaudit-report.md` (read-only, public sources). Bradford is
held off the public page by the owner's coordinator until C14 passes and the owner says so.

## N4 (privacy): redaction in any column

Rule: a row is redacted if ANY column carries a redaction marker (`RowRedaction.MarkerIn`). Then every name-keyed cell of the row (the payee column,
"Supplier Name (alt)", beneficiary and similar columns) is replaced by the marker at mapping time, so no name of a redacted row exists in any export,
slice or cross file. Only a hash of each withheld name is kept (`export/<slug>/withheld_names.csv`). New category C14 "personal data on redacted rows":
every column of every published slice, export file and cross file of all 28 councils; plus every withheld-name hash against every name column of every
published file.

Predictions (measured on the CURRENT export with `redact14`, before the engine change):
- P1. Bradford 2026-08 shows exactly 229 redacted rows with a name still in a name-keyed column. No other Bradford file shows any.
- P2. Councils other than Bradford with the same pattern (marker in one column, a name in the payee column): I predict AT MOST 3 of 27, most likely Merton and
  one or two others whose redaction marker sits in the transaction number (Merton "RED11538ACTED"). NULL (what would refute my guess): zero other councils
  (then the pattern is Bradford only) or more than 5 (then the problem is wider than a one-off file).
- P3. After the fix: Bradford redacted total 17,553 (17,324 + 229); Bradford row total 909,957 unchanged; C14 clean on all 28; planted defect (a name in
  a redacted row's alt column, and a withheld name in a cross file) is caught by the check and not by the clean twin.
- P4. A row that carries a marker in a non-payee column and a company payee also becomes redacted. That changes redacted counts in other councils. I do not
  know how many rows; I measure it (column "marker NOT in payee col") and report each council that moves. NULL: no other council's counts or
  exported rows change (then "all 27 stay byte-identical" is the claim to test with a before/after hash).

## N2: n-squared runs classed Unreconciled

The headline gap of a payment run printed n times each must be computed on the de-duplicated (payee, cost centre, amount) lines. Prediction: the 8 rows
(3758820, 3766535, 3766545, 3774234 and four more) change from about -728,547.95 to small gaps, the 3766535 gap is 19,250.00, and no other Wokingham
A row changes. Null: any other A row changes (then the de-duplication touched more than the n-squared rows).

## N5: same transaction number in two files

Twin pairs with TransactionA = TransactionB are not twins (one transaction in two files). Prediction: 6 pairs leave transaction_twins.csv
(5 Bradford, 1 RBWM); the cross-file check shows them (or already shows them for RBWM). Null: any other twin row changes.

## N6: Bradford cross-file panel reads 0

Prediction of the cause (to be tested, not assumed): the cross-file key uses the payee name and the Bradford July 2025 file (named July, holding July to
September) has the name column empty ("(no name published) N01651") while the later files carry names, or the pay-date format differs (ISO vs dd/MM/yyyy).
Refuted if the key matches on every column in a hand comparison.

## N7, N8, N3, cosmetic

- N7: Wokingham "37,559 rows" counts the header; correct is 37,558.
- N8: Reading 1,227 vs 1,236, Liverpool 48,489 vs 48,498, Cornwall 334,953 vs 334,927: prediction: the differences are rows that contain "redact" in the
  payee but are not redactions (REDACTIVE-like names, "redactive"), and rows the engine redacts that a plain regex does not (RED..ACTED, "personal data"). Each
  difference must be explained row by row, not by a total.
- N3: the export marks "(no number) N" as a placeholder the way "(no number published) N" is, so the page can label it; renaming the placeholder to
  "(no number published) N" is the fix I predict (the page already maps that prefix).
- "rawcheck:" in 7 profiles: plain-language sentence.

Each fix that is a new KIND of error becomes a checklist category: C14 (personal data on redacted rows), C15 (a pair that is one transaction), C16 (a headline
figure computed from a repeated listing), C17 (a repeat check that reads 0 where a content comparison finds repeats).

## Amendment 1 (2026-10-05, after the baseline `redact14` measurement of the unchanged export, before any run of the changed engine on the other 27 councils)

What the measurement (`scratch/s54_redact14_before.log`, 28 councils, export and slices) showed, against the predictions above:

- P1 held: Bradford 2026-08, exactly 229 redacted rows with a name still in a name-keyed column; no other Bradford file.
- P2 (at most 3 other councils, most likely Merton): REFUTED in its guess. Merton's 169,242 marker rows all have the marker in the payee column too (no name shown). Read as "a marker in a NAME column other than the payee column while the payee is named", no other council has the pattern. Read as the literal "any column", seven others do: marker in a purpose, description, text or identifier column on rows whose payee is a named company: Wakefield 567,113 rows ("Supplier ID" reads Redacted on every row, the company registration number is published beside it), Kirklees 71,682 (school taxi firms, Purpose of Spend = REDACTED PERSONAL DATA), Surrey 163,483, Hertfordshire 15,917 (Beneficiary ID), Camden about 5,600 (Description), Nottingham about 480, Cornwall about 19, RBWM about 4.
- My first C14 draft also produced thousands of false positives because it treated "Body Name", "Organisation Name" and "Entity Name" (the PAYER) as payee-name columns; fixed in `RowRedaction.IsNameHeader` (supplier, payee, vendor, beneficiary, creditor, claimant, recipient, client words only; not numbers, ids, codes, groups).

Decision (mine, to be confirmed by the owner): the engine rule is "a row is redacted when a payee-NAME column carries a marker" (the payee column or any second name column). The literal any-column rule would withhold a named company on 800,000+ rows whose council deliberately published the company beside a redacted purpose or identifier, wrecking Wakefield, Kirklees and Surrey, with no privacy gain I can show for a company. It is available as `COUNCILAUDIT_REDACT_ANYCOLUMN=1` and C14 follows it. P4 is therefore restated: with the name-column scope, no council other than Bradford changes its redacted count; NULL: any other council's redacted row count or exported row differs after the re-export (checked with a before/after hash of every export and web_export file).

Added predictions made before the full re-export:
- P5 (N6 cause): two causes together: the pay date text differs between files (Bradford 2025-07 ISO "2025-08-28", 2025-09 "18/09/2025") and the file named August has no payee names while July has. Fix: compare the pay date as a day; where one row has no name, key on the Supplier Number. Prediction: Bradford 2025-08 repeats about 33,900 of 33,928 rows, 2025-09 about 10,300 of 10,423; every other council's cross_file_repeats row is unchanged except where a council writes the same date two ways in different files (any such change is listed and read, not assumed). NULL: another council's panel moves for a reason I cannot name.
- P6 (N5): 6 same-number pairs leave the twins file (5 Bradford, 1 RBWM); the twin total of every other pair is unchanged.
- P7 (N3): placeholders "(no number) N" and "(no number: X) N" become "(no number published) N" and "(no number published) N X"; the rows of every council are otherwise byte-identical (a before/after hash with the id column normalised).
- P8 (N8): Reading and Liverpool: the 9 rows by which a plain "redact" count exceeds the engine's are payees that contain REDACT in a real name (Redactive Events and similar); Cornwall: the 26 rows by which the engine exceeds the plain count are "personal data" labels and similar. Refuted if the rows differ by another cause.

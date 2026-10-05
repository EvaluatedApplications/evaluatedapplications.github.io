# Audit history

What independent checking found in the engine and in the pages built from it, in date order, and what was done. Counts and
figures are as recorded at the time; later runs may differ. "Auditor" means an independent reviewer of the public council
pages: a separate AI agent that had not built the engine, working only from the councils' original published files. Dates are 2026.

## 4 October: first independent audit of the public council page

Pre-registered before any fix: `PREREG_S52_AUDIT_FIXES.md`.

- **Silent row loss (E1).** The row mapper skipped every row with a blank transaction number, for every council. Fixed: a row with
  a payee or a date is kept with a placeholder number ("(no number published) N"); a row with neither is dropped with a stated
  reason. A reconciliation of raw records against published rows plus dropped-with-reason went from 148 files in 28 councils
  not accounted for to none unaccounted. Example: Reading gained 2,621 rows.
- **Invoice-amount check compared the wrong figures (E2).** The Schedule A difference was the stated gross minus the net (that
  is, the VAT) instead of minus the VAT-adjusted expected gross. Fixed; a new class `SmallGap` (under GBP 1) was added. Wokingham's
  unreconciled count fell from 1,856 to 1,374 with 482 in `SmallGap`.
- **Transaction twins (E3, E4).** Undated pairs removed; group size and extra-copy value columns added. A widening of the
  twin rule to catch one further case (E5) listed about 5,200 pairs, mostly same-day batches; it was refuted as a rule and
  reverted. That one case stays unlisted until a batch test exists. Still open.
- **Debt sink, cross-file key, id joins, text (E6, E10, E11, E12, E15, prose).** Frozen budget stages are now read from their
  freeze files, undated rows are kept in the debt sink, the cross-file key ignores letter case, redacted lines of a
  transaction are stated with their value, and profile text was rewritten in plain words.

## 4 to 5 October: the error-category checklist

Pre-registered before any run: `PREREG_S53_CHECKLIST.md`. Every error category the audit found became a check that runs over
every council and fails the run if it finds the error again, plus a self-test that plants one defect per category and asserts the
check catches it (`CHECKLIST.md`, `check_rules.csv`).

- The first run was compared with the predictions in `PREREG_S53_CHECKLIST.md`. Several predictions were wrong, and several
  first-run failures were bugs in the checker, not in the data. Selected outcomes (category: prediction, first run):
  - C02 computed figures: float noise in a few councils predicted; 4 councils failed, plus 54 Schedule A rows that were the
    checker taking one of two VAT roundings.
  - C04 per-group check: predicted pass in all 28; 2 failed because the checker assumed k(k-1)/2 pairs per group, while a chain of
    copies seven days apart lists k-1. The checker was wrong.
  - C05 rules disclosed: predicted to fail in all 28; 15 failed (the count was wrong, the substance right: six undisclosed
    conditions on the twins list).
  - C06 prose numbers: both findings were false positives of the checker.
  - C07 same fact: up to 3 predicted; 3 failed, all the checker's definition of one budget-table column. No real disagreement was found.
  - C12 private prose: 5 to 20 councils predicted; 22 failed, 7 of them only because the regex flagged backticks (which the page builder strips) or asterisks inside payee data.
  - C01, C03, C10 and C09 (links) passed as predicted.
- Real findings from it: the twins rule text hid six conditions (two lines, GBP 10,000, one payee, redacted, mangled numbers,
  doubled listings), now stated on every row and exported as the rule registry; unnumbered rows were written as a bare 1, 2, 3
  that read like a council transaction number, now "(no number published) N"; a council's own name was counted as another
  body in the debt tables; and spreadsheet cells that store 17 digits made exact money comparisons off by 1e-13 and moved
  some Schedule A and B counts (Wokingham: 14 rows exactly 1p out were flagged and no longer are).

## 5 October: independent re-audit

Pre-registered before any run: `PREREG_S54_REAUDIT.md`.

- **A privacy defect (N4), fixed at the root.** For one Bradford month, 229 childcare-voucher rows carried the redaction marker
  in one payee-name column and a person's name in a second name column. The scanner read the second column, counted the rows as
  not redacted, and the name was published. The engine now treats a row as redacted when any payee-name column carries a
  marker, replaces every name column of that row with the marker at mapping time, and keeps no name. A check (C14) now scans
  every column of every export for this pattern and for any name that was withheld elsewhere. No other council has the pattern.
  Earlier published copies of the old engine carried this defect; they are superseded.
- **Scope decision.** Redacting a row on a marker in any column, taken literally, would also withhold the named company on
  hundreds of thousands of rows whose purpose or identifier column is redacted (Wakefield 567,113 rows, Surrey 163,483,
  Kirklees 71,640, others; CHECKLIST.md gives Kirklees as 71,682; the two figures have not been reconciled). It was measured and not made the default; it is available behind a switch
  (`COUNCILAUDIT_REDACT_ANYCOLUMN=1`).
- N2: runs printed n-squared times and still unexplained are now reported on their distinct lines (a gap of 19,250.00, not about
  728,000).
- N5: a transaction with the same number in two files is merged before pairing (631 twins, not 637); the same number under
  different pay dates is Schedule D, not a twin.
- N6: a cross-file key compared a pay date as text, so a Bradford panel showed 0; now compared as a day, and a supplier
  number stands in where a row has no name.
- N7, N8, N3: a row count that included a header or footer, reconciliation of "redacted" counts (real suppliers named
  Redactive are kept, 11 rows in Cornwall, for example), and the placeholder form of unnumbered rows.
- Wrong first predictions (P2, the P5 count, the size of the same-number list) are left in the pre-registration.

## 5 October: "show us your algorithm"

A member of the public asked to see the algorithm. The first answer was the rules, the checklist and the pre-registrations as
public documents, with private review of the source on request. This repository replaces that: the engine library and its tests
are published under a noncommercial source-available licence, so that "your rule is wrong here" can be checked and a fix tried.
The method pages are not the same as running the code, and this repository is closer to the second.

This release is version 1, a plain C# engine. A rebuild on EvalApp and HoloDb, including a live in-browser mode, is in progress and will replace this code in place.
## Still open at the time of writing

- The Loddon pair (E5) is not on the twins list; it needs a batch test.
- Public text and data for some councils lags the engine until the website builder is re-run and published.
- Several councils cannot be fetched by a plain script (Hull, Kent, Norfolk, Manchester, Hampshire, Lancashire) and are not covered.
- Wokingham, Reading and some other hand-check samples pre-date later rule changes; their counts are as first run.

# Pre-registration, Session 53: the error-category checklist (`checklist all`)

Written and committed BEFORE the checklist has been run once. Nothing below was measured by the new command; the
"known" column comes from the independent audit (2026-10-04) and my Session 52 notes.

Search width: 13 categories x 28 displayed councils (every slug in web_export/index.csv) = 364 cells. The categories are
fixed here; any later audit finding adds a row, it does not change these.

Design: one check per category, run per council, each returns a list of findings (empty = pass). `checklist all` exits
non-zero if any council fails any category that is not on the holdlist (`export/checklist_holds.csv`: slug, category, reason).
A hold that no longer fails is reported as STALE and also fails the run, so a hold cannot outlive its cause.

## Predictions per category (what it should find on the committed Session 52 state)

| Id | Category | Prediction before the first run |
|---|---|---|
| C01 | Silent row loss (raw = published + dropped-with-reason, per file) | PASS 28/28 (Session 52 rawrecon: 0 unaccounted everywhere). Fails only if raw_reconciliation.csv is stale or missing a file. |
| C02 | Computed figures mean their label (Schedule A gap from net/gross/VAT; twin extra-copy value; money to 2dp) | Schedule A: PASS (Wokingham, the only council with A rows; Fix 2). Money formatting: FAIL for Reading at least (the audit saw months.csv Net "22744619.2899999999854253"); I expect 1 to 10 councils to carry float noise in months.csv. |
| C03 | Interval/date claims only where both dates exist | PASS 28/28 (the 3 undated Wokingham pairs were removed in Fix 3). Fails if any twin carries DaysApart -1, an empty date, or DaysApart that disagrees with its two dates. |
| C04 | Counting per group, not per pair | PASS 28/28 (ExtraCopyValue columns added in Fix 3; within-transaction ExtraValue = (copies-1) x net). |
| C05 | Every filtering rule disclosed | FAIL for all 28 at first, because no rules file exists (rules live only in code comments and one "Reading" column on the twins). Passes once `export/check_rules.csv` is written from a registry and every scan that has an output file for the council has a rule entry. |
| C06 | Numbers in profile prose equal the data (counts, percentages, money where derivable) | Counts: PASS 28/28 (profileaudit all: 0 missing, 0 wrong). Money: I expect 0 to 5 near-miss money figures (stated within 1% of a derivable total but not equal), concentrated in Reading and RBWM, whose dropped/unnumbered money moved in Session 52. |
| C07 | The same fact agrees everywhere (index vs months vs slices vs raw_reconciliation vs exceptions vs cross files vs budget F) | PASS for most; I expect at most 3 FAILs, from a stale web_export slice or a cross file not re-run after Session 52's three councils with new rows (Reading, RBWM, Nottingham). |
| C08 | "Not available / none held" claims are true (outturn presence; no-number claims; undated counts) | The outturn claim: PASS 28/28 after Stage 2d (ONS codes). The no-transaction-number claim: FAIL for Sheffield (audit E9: ids 1, 2, 3 are scanner row numbers but not marked as such) and I expect the same for at least one of Birmingham, West Berkshire, Durham (audit "probably"; unverified). |
| C09 | Source links resolve (HTTP, cached 7 days) | PASS 27/28 or 28/28: the audit's dead Wokingham link was replaced in Session 52. West Berkshire answers 403 to scripts: reported as BLOCKED-TO-SCRIPTS (warning, not a pass claim and not a fail). Unknown: links changed since 2026-10-04. |
| C10 | Totals don't silently exclude rows (undated etc.) | PASS 28/28 for months.csv totals and debt sink (Session 52 kept undated rows: series + undated = out + credits). Merton had the one known miss (fixed). |
| C11 | Self-payments not classed as other bodies | FAIL: the audit named Leeds (684 rows to "Leeds CC") and Surrey (GBP 5.09m to "Surrey CC"); the debt_sink head already shows Birmingham to "Birmingham CC" (5 rows). I expect 5 to 15 councils to carry a self-row in debt_sink / crossref_alias_flows / payment_misfits. |
| C12 | No first-person, working-note or jargon text in public profile prose | FAIL for several councils: Session 52 cleaned the audit-named profiles (Wokingham, Reading, RBWM, Bracknell, West Berkshire, Leeds, Camden, York, Calderdale). I expect 5 to 20 of the other councils to still hold at least one sentence the builder would drop (first person, "this session", "seeded", file names). |
| C13 | Hand-check of the restored rows: Reading and RBWM unnumbered raw payments are all in the published files or in dropped.csv with a reason (independent parser) | PASS: Reading 2,621 unnumbered payments (2,034 from 2021-07: 2,021 in the 61 monthly CSVs + 13 in the 2021-09 workbook; 587 in the 2019-21 files), Gateley PLC 3,704,464.99 on 12/08/2025 present, August 2025 33 unnumbered rows 5,783,038.88, FY2023-24 454 rows 8,434,327.20; RBWM 84 unnumbered raw payments, 28 published with a placeholder id and the rest dropped as already published (28 to 84 published, Session 52 prediction). |

Control (a deliberately seeded defect each check must catch): every category has a unit test that plants one defect in a small
synthetic input and asserts the check fails on it, and passes on the clean twin. A check that cannot fail is not a check.

## Nulls, written before the run

- N1: a check that I fix a council for must not change any other council's result in any other category (matrix diff before/after,
  only the predicted cells flip).
- N2: no committed export CSV changes except the files each fix names (hash list `scratch\s53_hash_before.txt` vs after). Fixes
  that regenerate a file I did not predict are reported as unpredicted.
- N3 (what would refute the design): if a category passes 28/28 on its first run AND I cannot construct a seeded defect it fails on,
  the check is vacuous and is rewritten.

## Fix policy

Root cause first (engine, export, profile source). A council that cannot be fixed this session goes on the holdlist with the
reason and the slugs go in the report. The builder reads `export/checklist_holds.csv`; `checklist holdenv` prints the
COUNCIL_HOLD value for the builder's existing environment variable.

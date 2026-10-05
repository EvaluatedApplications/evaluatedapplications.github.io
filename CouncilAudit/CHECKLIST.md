# CHECKLIST: error categories for the council scanner

`CouncilAudit.Cli checklist all` runs one automated check per error category over every council on the public page (every
slug in `web_export/index.csv`) and exits non-zero if any council fails any category that is not on the holdlist.

```
checklist all [--links] [--rawrecon] [--nocache]   every council, every category (exit 1 on any failure)
checklist wokingham,reading                        some councils
checklist selftest                                 plant one defect per category and assert each check catches it
checklist rules                                    write export\check_rules.csv from the registry (CheckRules.cs)
checklist holdenv                                  held slugs with Effect = withhold, for the builder's COUNCIL_HOLD
```

- `--links` re-checks every source link now (otherwise a result is reused for 7 days; `export\link_cache.csv`).
- `--rawrecon` re-runs `rawrecon all` first (reads every raw file; minutes). Without it C01 reads `export\raw_reconciliation.csv`
  and fails if that file disagrees with the current export (a stale reconciliation is a finding, not a pass).
- The first run reads all 7.6 GB of transaction files (about 4 minutes) and caches one summary per council in
  `scratch\checklist_cache\` (keyed on file names, sizes and times); later runs take about a minute.
- Output: a matrix on the console, `export\checklist_results.csv` (council, category, status, findings), every finding in
  `export\checklist_findings.txt`, and `export\checklist_pending.txt` (things only another agent's code can fix, listed, not counted).

## Holdlist: `export\checklist_holds.csv`

Columns `Council,Category,Reason,Effect`. A failing cell that is listed is shown as HELD and does not fail the run. A listed cell
that now PASSES is shown as STALE-HOLD and fails the run, so a hold cannot outlive its cause. `Effect`:

- `withhold`: the builder should not publish the council until the hold is lifted (`checklist holdenv` prints the slugs for the
  builder's existing `COUNCIL_HOLD` variable).
- `note`: the council ships; the issue is tracked and shown.

A hold needs a reason that names the root cause and what would lift it.

## Adding a category

Every new audit finding becomes a row here and a function in `CouncilAudit.Cli/Checklist.cs`:
1. write down, before running it, what the check should find (PREREG file, the three things a pre-registration needs: prediction, null, what would refute it);
2. put the pure rule in `CouncilAudit/ChecklistRules.cs` and a test that plants the defect and asserts the check fails on it, then passes on the clean twin;
3. add the id to `Checklist.Cats` and a `Cxx(slug...)` function; run `checklist all`; fix at the root or hold with a reason.

## The categories

| Id | Guards against | Origin | How it is checked |
|---|---|---|---|
| C01 Silent row loss | A council's raw rows disappearing between its file and the published rows with no word | Audit E1 (Reading's 2,021 payments with no voucher number dropped silently; found by comparing raw and published counts); Session 52 `rawrecon` | For every file `export` reads: raw records = published rows + rows in `dropped.csv` with a reason (+ a whole file named as excluded with its reason). Every listed file must have a row; the published count and net in the reconciliation must equal the current export; `dropped.csv` must agree with the reconciliation. |
| C02 Computed figures mean their label | A number that is not what its label says: the Schedule A "difference" being the VAT, money printed with float noise | Audit E2 (Matrix SCM 37p shown as 21,049.45); audit E15 (months.csv `22744619.2899999999854253`) | Wokingham's Schedule A gap recomputed independently from net, gross and VAT type (own VAT table, both readings, sign flip) and compared with the shown Difference; a class must match the size of its gap; months.csv and debt_sink money must carry no float noise (at most six decimals: a council can publish a sub-penny figure, Reading 6,226.666667; spreadsheet noise has ten or more). Source of the noise fixed at the reader: a workbook amount stored with 17 digits is kept as the 15 the sheet shows. |
| C03 Dates only where both exist | An interval claimed ("N days apart", "within seven days") when one date is missing or the stated gap is wrong | Audit E3 (three undated Wokingham pairs shown as "1 day apart") | Every twin row: both dates parse, `DaysApart` equals the real gap, gap at most seven. |
| C04 Counting per group, not per pair | A group of k identical items counted as k(k-1)/2 pairs of "second copies" | Audit E4 (Cambian 163,010 against 108,673; Oxfordshire 510,626 against 340,418) | Twins: each group's pairs link all its transactions (between k-1 and k(k-1)/2 pairs) and its extra-copy value sums to (k-1) x the value; within-transaction repeats: extra value = (copies-1) x net. |
| C05 Every filtering rule disclosed | A list that silently leaves things out, so a reader takes it for complete | Audit E5 (hidden "at most 3 transactions" condition); this session found five more on the same list | Every rule is in `CheckRules.cs`, exported to `export\check_rules.csv`; a rule stated per row (twins) must carry every condition; what each output contains must satisfy what the rule says (no twin under GBP 10,000, no redacted payee in a schedule, no group over three); a count of row-skipping `continue;` statements in each scan's source is a tripwire for a new filter. Page text and the builder are checked in `checklist_pending.txt`. |
| C06 Prose numbers equal the data | A figure in a profile that does not match the export | Session 50-51 `profileaudit` (Bracknell 29 vs 519, RBWM sum, Kirklees 34.4%); audit E7, E13, E14 | `profileaudit` (counts of 1,000 or more present, "N of M (P%)" arithmetic, redaction percentages, index agreement) fed from the cached census; extended to money: every GBP figure is compared with the amounts the export derives (totals, redacted, unnumbered, dropped by reason, financial years, Schedule B value and extra copies, scan outputs); a figure of a million or more within 0.5% of a derived amount but not equal is a finding. |
| C07 The same fact agrees everywhere | The profile, months.csv, index.csv, the cross files, the reconciliation and the budget table disagreeing | Audit E15 (Merton 114 vs 115 months), E12 (Leeds 6,582 vs 8,568), Session 52 stale-file finds | index vs months vs the export on rows, flagged rows, parts, months; every month's rows and net; no flagged row in a month with no payments; cross_file_repeats and file_duplication rows per file; budget_units F against the export under the budget scan's own year rule. |
| C08 "Not available / none held" claims are true | The page saying nothing exists when it does, or showing a scanner number as the council's | Audit E6 (outturn "none held" for seven councils), E9 (Sheffield's scanner counter shown as a transaction number) | A budget unit that says no Revenue Outturn is held must have no row in `published_outturn.csv`; transaction ids that equal the row's position must be marked unnumbered; a profile that says numbers are synthetic must not publish plain numbers; months.csv undated lines equal the export's. |
| C09 Source links resolve | A "where the council publishes them" link that is dead | Audit E8 (Wokingham 404) | Every URL in a profile is fetched (cached 7 days). 2xx and 3xx pass; 404, DNS and server errors fail; 401/403/429 are listed as "answers a script with bot protection", not claimed to resolve. |
| C10 Totals do not silently exclude rows | A total that leaves out undated rows | Audit E10 (debt sink dropped 59 undated Wokingham rows) | months.csv rows and net equal the export (undated month included); debt_sink year series + undated money = money out + credits. |
| C11 Self-payments are not other bodies | A council's payments to its own name shown as flows to other councils | Audit E16 (Leeds to "Leeds CC", Surrey to "Surrey CC") | No row in debt_sink, crossref_alias_flows or payment_misfits names the paying council itself (normalised name); a debt-ledger counterparty that is the council itself must be class OwnCouncil. |
| C12 No first-person, working-note or jargon text | A public profile written as a notebook | Audit E16 ("I read all 77 workbooks", "Wilson 95% CI", "need a human click-through") | The builder's own patterns (first person, working-file references, working notes) plus protocol jargon, applied to every profile source text and to the exception detail text. The source must be clean so the public text never depends on the builder dropping a sentence. |
| C13 Restored rows hand-checked | The rows Session 52 restored for Reading and RBWM being wrong or incomplete | Owner request after Session 52 | Own decoder and CSV parser (not the engine's): every raw payment with no number in the council's CSV files is in the published file as an unnumbered row (Reading), or published or already in an earlier file (RBWM); named facts recomputed (2,021 Reading payments from 2021-07; August 2025: 33 rows, 5,783,038.88; FY2023-24: 454 rows, 8,434,327.20; Gateley PLC 3,704,464.99; Swansea 5,009,567.12; RBWM 84 = 28 published + 56 already published). The 2017-2021 quarterly workbooks are not re-parsed independently. |
| C14 No personal data on redacted rows | A row the council redacted in one name column still showing a person's name in another, in any export, slice or cross file; a name withheld from one row published on another | Re-audit N4 (Bradford August 2026: "SupplierName" = REDACTED PERSONAL DATA on 229 childcare-voucher rows while "Supplier Name" held the person's name; 229 names published, 0 redacted counted) | `RowRedaction` (engine, at mapping time): a row is redacted when a payee-name column (the payee column or a second name column such as "Supplier Name (alt)") carries a marker; every name column of that row becomes the marker and only a hash of the name is kept (`export/<slug>/withheld_names.csv`, git-ignored). C14 scans every column of every export file, every published slice and exceptions file of the council (`RedactionScan`): a row with a marker in a name column must hold only markers or nothing in every other name column (payee, supplier key, `OtherColumns` entries with a name header); and no name-like withheld name (two to five plain words, no organisation word) may appear in a name column of any published file of any council. The cross files are scanned once and reported under GLOBAL. `COUNCILAUDIT_REDACT_ANYCOLUMN=1` widens the rule to any column (see Limits).|
| C15 One transaction is not two numbers | A transaction published twice under the SAME number, listed as "identical lines under two transaction numbers" | Re-audit N5 (5 Bradford and 1 RBWM pairs with TransactionA = TransactionB) | The twin scan merges entries with the same number before pairing and writes them to `same_number_two_files.csv`. C15: no twin pair has the same number on both sides; each file that holds such transactions must show repeated rows in the cross-file check (at least half of those lines). |
| C16 Headline gaps use distinct lines | A gap computed on a payment run printed n-squared times | Re-audit N2 (8 Wokingham runs showing a gap of about 728,000 where the distinct lines are 19,250 short) | Engine: an unexplained n-squared run reports net, expected gross and gap on its de-duplicated lines. C16: for every Schedule A row classed Unreconciled whose detail says "n-squared", the headline Difference must be the gap the detail states (within a VAT uplift). |
| C17 Repeat check agrees with a content recount | A cross-file panel that reads 0 while the same payments are in two files | Re-audit N6 (Bradford: 0 repeats shown where September 2025 repeats 10,371 July rows and the file named August repeats all of July) | Cross-file key: pay date compared as a day, and the published Supplier Number stands in for the name where one of two rows has none. C17: the census recounts, per file, rows whose real transaction number, net and pay day are already in an earlier file (no payee, description or service in the key); the panel must show at least half of that. |
| C18 Counts leave out the header | A profile row count that includes the header row | Re-audit N7 (Wokingham "37,559 rows" for a sheet of 37,558 data rows) | Every "N rows" in a profile that equals a file's or the council's exported row count plus one fails (unless it says "data rows"). |
| C20 No individual named in public prose | A profile that names a private landlord, a sole trader or a performer's trading name because a council's data does | Session 57 (Reading "[individual's name withheld]", Calderdale "[individual's name withheld]"; sweep also reworded two trading names that may be individuals) | `IndividualNames.Find` over every profile text: a title before a name, a couple ("A & B Surname"), a forename (plus optional initial) and a surname-shaped word, a personal name before a trade word (Electrical, Plumbing, Taxis ...), an initial and surname before a trade, and any 2 to 5 word run whose `NameHash` is in a `withheld_names.csv` (person-like names the export withheld from redacted rows). Reviewed false positives (company names that look like forename and surname) live in `IndividualNames.NotPeople` with the reason. Describe the pattern instead ("two private landlords", "a sole-trader electrician"). Selftest plants the Reading and Calderdale defects in the real profiles and a withheld name. |

## Limits (what this checklist does not prove)

- `checklist selftest` proves each check can fail (one planted defect per category). It does not prove the checks are complete.
- The checklist passes the committed export and profile source. The published copy in `website-data/council-web` is behind it until the
  builder is run; `checklist_pending.txt` says by how much.
- It checks the committed export and the profile source. The live page also depends on the builder and Showroom, which are not in this
  workspace; `checklist_pending.txt` lists what they must do.
- C06's money check can only compare with amounts the export derives. A hand-opened figure (a payment someone read in the raw file) is
  counted as "not derivable", not as verified.
- C13 covers two councils and the CSV files; the other 26 are covered by C01 (every raw record accounted for) but not by an independent reader.
- C14 follows the owner's rule "a row is redacted if any column carries a marker" for NAME columns. The literal any-column reading is available (`COUNCILAUDIT_REDACT_ANYCOLUMN=1`) and was measured before choosing (PREREG_S54_REAUDIT.md Amendment 1): it also withholds the named payee of every row whose purpose or identifier column is redacted (Wakefield 567,113 rows whose "Supplier ID" reads Redacted on every row beside a published company number; Kirklees 71,682 school-taxi and similar firms with a redacted Purpose of Spend; Surrey 163,483; Hertfordshire 15,917; Camden about 5,600; Nottingham about 480). Which reading to publish is the owner's decision; C14 follows whichever the engine used.
- C14's name-withheld test is limited to names that look like a person (an organisation withheld from one row, a nursery or a firm, may be published on other rows).
- C17 compares with a key built on the real transaction number, so it cannot see councils whose files carry no usable number (Sheffield, Birmingham, West Berkshire and others with placeholders): for those the cross-file check by content is the only evidence.
- C05's tripwire counts `continue;` statements in four scan files; a filter written another way is not seen. It forces a look, it does not read code.

---
*Public copy note: a private individual's name that appeared in the original of this file has been replaced by "[individual's name withheld]". The unredacted original is kept privately by the author.*

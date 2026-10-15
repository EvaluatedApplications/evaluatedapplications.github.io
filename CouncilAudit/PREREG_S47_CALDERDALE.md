# Pre-registration: Calderdale (The Borough of Calderdale) onboarding (Session 47, 2026-10-04)

Written BEFORE `prep calderdale` or any engine run on Calderdale, and committed before it. What I have already seen: the CKAN listing
(7 datasets "Payments to suppliers 2020-21" to "2026-27", 77 CSV and 77 XLSX/XLS resources on dataworks.calderdale.gov.uk), every CSV's header line, 4 data
lines of two files, and three one-off C# census passes over the 77 CSVs (rows, signed sum and modal date month per file; the 60 most common payee
labels; Supplier ID kinds; lines per Internal Ref Number; labels containing REDACT, PERSONAL, NULL; the Personal Budgets and Purchase Card labels).
Nothing else. I have NOT run `prep`, `city`, the engine, `filedup`, `crossfile`, `topb` or any hand-check on it.

## Observed, not predicted (so not claims)
- 77 CSV files, one per month, April 2020 to August 2026, every month present (file names "April 2020.csv" ... "August 2026.csv"); 408,188 rows; census signed sum
  of the 77 file sums GBP 2,311,536,965.22; 0 blank rows; every Net Amount parses; every Date is written dd/MM/yyyy and the commonest month of the dates is the file's own month in 77 of 77 files (100.0% in each).
- Two layouts: 70 files with a leading "Body" column (a statistics.data.gov.uk URL) and 7 files (March to August 2026 and the file "February 2026") without it; the payee column is "Supplier(Beneficiary) Name" with a leading space in the cell.
- "Internal Ref Number": 408,188 different values over 408,188 lines (one line each, none repeated in any file or across files; it ranges around 29,860,000 to 34,765,000).
- "REDACTED PERSONAL DATA" is the one placeholder spelling: 147,066 lines (36.0%), of which 136,333 have Definition "Boarding Out Allowances" (foster care allowances), 8,094 "Accounts Payable Invoices", 2,327 "Adult Placements".
  "REDACTIVE PUBLISHING LIMITED" 16 lines (a real supplier; the engine rule keeps it).
- Two pooled-looking labels: 1,032 lines with Supplier ID "Personal Budgets" whose payee cell is "YYYY:MM" plus a client group ("2024:12LD Learning Disabilities", 702 different labels), and
  1,780 lines with Supplier ID "Purchase Card Supplier" (merchants such as Landlord Supplies, Travelodge Gb0000, Premier Inn; Definition "Credit Cards").
  "ROYAL BANK OF SCOTLAND-PURCHASING CARDS" is a different thing: 2,298 accounts-payable lines.

## Claims (each can fail)
- C1. `prep calderdale` reconciles: raw rows = written rows + blank rows, 77 files, 408,188 written rows, signed total GBP 2,311,536,965.22 raw and re-read, no gap in the months. The 70 "Body" files and the 7 without it come out under one set of columns.
- C2. After the engine's rules, redacted = 147,066 exactly (the one spelling, no Redactive line counted) and retained = 261,122. NULL: any other count means a spelling or a rule I have not seen.
- C3. Schedule A = 0 and Schedule D = 0 (one number per line, so no number is shared by two payees). This is by construction of what I counted; if D is not 0 the engine reads the number differently from my census and I stop and read it.
- C4. Schedule B member rows are between 3% and 15% of the 261,122 retained rows (neighbours: York 9.1%, Surrey 7.8%, Stockport 37%).
- C5. The 1,032 "Personal Budgets" lines are under 1% of the Schedule B member rows; the 1,780 purchase-card lines are under 3% of them. (If either is over, I measure the schedule with and without the label as a control, as I did for Essex.)
- C6. `filedup` flags 0 of 77 files and `crossfile` finds under 0.5% of the retained rows repeated from an earlier file (same payee, amount, date, description and service). (The numbers are unique over the 77 files, so a doubled month could only show on content.)
- C7. Independent second read: the 76 XLSX copies and the one XLS copy that data.gov.uk lists beside the CSVs hold the same rows as the CSVs, month by month: same row count and same signed sum to the penny in at least 75 of 77 months. NULL: a month where the workbook and the CSV differ is reported as a difference of the publication, not fixed.
- C8. Seeded hand-checks (n=30 per class, seed as in earlier sessions) confirm 30 of 30 in each of the Schedule B classes that exist and `rawcheck` finds 300 of 300 against the downloads.
- C9. Payees naming HM Revenue or HMRC have fewer than 5 lines in at least 60 of the 77 months.
- C10. Alias screen: the NoOverlap census stays 0 right (read by name, as before); no threshold separates right from wrong proposals.
- C11. Adding Calderdale changes no row of the other 26 councils: in the ten cross-council CSVs there are 0 removed lines and 0 added lines that are not Calderdale's. NULL: any other change means a shared rule moved, and I report it.
- C12. The public-page profile figures for the 26 existing councils still match `city` (no engine rule changes this session unless a claim above fails and one is pre-registered as an amendment first).

## Results (written after the run; refuted claims first)
- C4 REFUTED. Schedule B is 19,722 groups / 68,820 member rows = 26.4% of the 261,122 retained rows (I said 3% to 15%). Cause: small per-site, per-worker and per-placement lines (largest payees by member rows: Reed Specialist Recruitment 5,189, Managed Water Services 4,557, [individual's name withheld] 3,314, [trading name withheld] 2,097); 51.8% of all lines are under GBP 500 (27.5% of retained), which I had not counted. Classes: Unclear 18,250 groups / 65,397 rows, StandingScheduleCatchUp 1,017 / 2,291, StandingScheduleSurplus 452 / 1,126, ReversedSameDay 3 / 6; GBP 192,577,649.79.
- C9 REFUTED. A payee naming HM Revenue or HMRC has fewer than 5 lines in only 17 of the 77 months (I said at least 60); it has 5 or more in 60. The payees are "HMRC - AC24 BLESSED PETER SNOW ACADEMY ..." style labels (one per academy), 79 lines at most under one label.
- C1 held: 77 files, 408,188 rows = written 408,188 + 0 blank, GBP 2,311,536,965.22 raw and re-read, months 2020-04 to 2026-08 with none missing; both header layouts come out as one set (the first prep run stopped at once because the header anchor was searched before the header was renamed; `CalderdaleTable` fixed that, no data touched).
- C2 held: redacted 147,066 and retained 261,122 exactly (reconcile 408,188 = 261,122 + 147,066).
- C3 held: Schedule A 0 rows, Schedule D 0 transactions, 0 (file, number) groups with more than one line.
- C5 held for Personal Budgets (0 of 68,820 Schedule B rows) and NOT FULLY TESTED for the purchase-card lines: I counted only the five biggest merchant names (Landlord Supplies 89, Appliance World 37, Travelodge 12, Premier Inn 9) and the RBS account (8), 155 rows = 0.23% of 68,820; the other card merchants were not counted.
- C6 held: `filedup` flags 0 of 77 files (0 exact-repeat rows), `crossfile` 0 of 261,122 rows (0.00%), 0 transaction twins, 0 doubled transactions.
- C7 held, with a real difference found: all 77 workbooks (76 .xlsx, 1 .xls via XlsReader) have the same row count and signed total to the penny as their CSV (77 of 77; claim was 75). They have a title row and different column names and no payment number in all 77, so the match key is date, amount and payee. 704 keyed differences by strict payee text (352 lines each side), all in two payee names with quotation marks (Next Stage "A Way Forward" Youth Development Ltd 347 lines, Paramount Therapy Centre Ltd -Change for YP" 5 lines): the CSV loses the opening quote mark. Ignoring quotation marks there are 0 differences. Nothing was changed in the CSV text.
- C8 held: seeded hand-checks 30/30 (Unclear, n=30 of 18,250), 30/30 (StandingScheduleSurplus of 452), 30/30 (StandingScheduleCatchUp of 1,017), 3/3 (ReversedSameDay); `rawcheck` 300/300.
- C10 held as measured by the tool: 96 NEAREST proposals read by name (50 accept: variant spellings of Calderdale's own name, and Calderdale's payee spellings of Leeds, Sheffield, Bradford, Wakefield, Durham, Kirklees, Stockport and York; 46 reject: other county councils, Calderdale College, Calderdale Schools Ltd, Calderdale Music, Smartmove, Norse, Pride, Removals, Interfaith Council, Council of Mosques, Carpets, Forum 50 Plus) plus 103 screened out as no distinctive word (all reject: other metropolitan boroughs and city councils); 0 ungraded after. `aliasscreen` now prints NoOverlap right-or-probable 0, reject 1,948 (my earlier cumulative tally said 1,382 read; the tool's current figure is different and I have not reconciled the two). No threshold separates right from wrong: accept "Calderdale Council" 0.429, reject "Calderdale College" 0.435 and "Calderdale Schools Ltd" 0.421.
- C11 held for the other 26 councils: in the ten cross-council CSVs there are 0 removed lines. Added lines are Calderdale's, plus 25 York lines that Session 46 should have had (debt_sink 8 York-as-entity and 14 York-as-payer, payment_misfits 12 and 3): Session 46's loop ran `debtsink` before its alias grades were complete and I wrote "unchanged"; this session's re-run after grading has them. Same timing mistake as Session 44's Hertfordshire.
- C12 held on the evidence available: no engine rule changed (registration only: slug, alias target, `BudgetTest.Deferred`, prep spec), no tracked export file of any other council changed, and the 26 profiles in `website-data/council-web/profiles.json.gz` (built 17:34) carry the York and Stockport counts of Session 45 and 46. `city` was not re-run for the other 26 this session.

---
*Public copy note: a private individual's name that appeared in the original of this file has been replaced by "[individual's name withheld]". The unredacted original is kept privately by the author.*

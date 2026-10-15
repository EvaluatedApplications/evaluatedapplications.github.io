# Pre-registration: London Borough of Camden onboarding (Session 48, 2026-10-04)

Written BEFORE `prep camden` or any engine run on Camden, and committed before it. What I have already seen: the Socrata dataset metadata (opendata.camden.gov.uk,
view 3ixw-qvb8 "Camden Council Spend Over 500 GBP", listed on data.gov.uk), the CSV export (`inbox/camden/camden_spend.csv`, 96,387,900 bytes, downloaded 2026-10-04),
two one-off C# census passes over it (record and row counts, signed sum, per financial year and per month; id uniqueness; the top 40 payee labels; redaction-like
labels; same payee + amount + date groups; starred payee names; HMRC payee lines per month; id prefix against date month; largest lines), and the server-side
aggregates (count, sum, min/max date, and count and sum per month, from the Socrata SoQL API). I have NOT run `prep`, `city`, the engine, `filedup`, `crossfile`,
`topb` or any hand-check on it.

## Observed, not predicted (so not claims)
- ONE export file, 416,000 data rows, 15 columns, 0 rows with a wrong column count, signed sum GBP 5,620,030,075.32; the server's own count(*) and sum(amount_gbp) are 416000 and 5620030075.32.
  The round row count is a coincidence confirmed by the server, not a cut.
- Payment dates 16 September 2019 to 28 August 2026, 84 months (2019-09 first, 2026-08 last, none missing; 2019-09 is a part month, 3,162 lines), every date dd/MM/yyyy and
  parsed; the file's Payment Month, Payment Year and Financial Year columns agree with the date on every row (0 mismatches). The description says "since 2010"; this export holds only 2019-09 on.
- Unique Identifier ("aug-26-6288"): 416,000 different values over 416,000 lines (one line each); its month-year prefix agrees with the payment date on every row.
- 0 lines under GBP 500, 0 negative lines, 0 lines with irrecoverable VAT; the largest are Greater London Authority lines (GBP 31.55m, 7 Sep 2020).
- Redaction labels: "xxxxREDACTEDxxxx" 49,232, "REDACTED" 16,601, "Redacted" 258 = 66,091 lines (15.9%; 14.1% to 18.2% in each financial year); "REDACTIVE EVENTS" 11 lines is a real supplier. One line has an EMPTY payee
  (10/09/2025, Responsive Repairs Costs, Homes and Communities HRA, GBP 17,395.07).
- 43 payee names carry a trailing asterisk marker (****: 4,962 lines, ***: 549, **: 18, *: 1, 15 stars: 9); for 19 of them the same name also appears without stars.
- By my own count (not the engine's): 23,450 groups of 2 or more retained lines with the same payee text, amount and date, holding 74,461 lines = 21.3% of the 349,909 retained.

## Claims (each can fail)
- C1. Split by payment month into `inbox/camden/raw/Camden YYYY-MM.csv` (a pure split, no row changed), the 84 files have the same row count AND signed sum to the penny as the server's per-month aggregates in 84 of 84 months
  (this is the independent read: the server computed them, I did not). NULL: any month that differs is reported as a difference. `prep camden` reconciles: 416,000 rows written + 0 blank, GBP 5,620,030,075.32.
- C2. After the engine's rules, redacted = 66,091 exactly and retained = 349,909. NULL: any other count means a spelling or a rule I have not seen.
- C3. Schedule A = 0 and Schedule D = 0 (one id per line). If D is not 0 the engine reads the id differently from my census and I stop and read it.
- C4. Schedule B member rows are between 15% and 30% of the 349,909 retained rows (my count above is 21.3%; the engine adds a same-transaction rule, so it can be lower).
- C5. For the 19 starred names that also appear without stars, `SupplierKey.Normalize` gives the same key with and without the stars (so no Schedule B group is split by the marker). NULL: a different key for any of the 19 is a rule to fix, pre-registered as an amendment before it is coded.
- C6. The one empty-payee line does not reach any schedule under a blank payee (it is labelled, as Bradford's rows are, or left out of Schedule B).
- C7. `crossfile` finds under 0.5% of retained lines repeated from an earlier month by payee, amount, date, description and service, and `filedup` flags 0 of 84 months (all months come from one export, so a republished month could show only on content).
- C8. Seeded hand-checks (n=30 per class, as before) confirm 30 of 30 in every Schedule B class that exists, and `rawcheck` finds 300 of 300 against the file.
- C9. Alias screen: the NoOverlap census stays 0 right (read by name); no threshold separates right from wrong proposals.
- C10. Adding Camden changes no row of the other 27 councils: in the cross-council CSVs there are 0 removed lines and 0 added lines that are not Camden's. NULL: any other change means a shared rule moved, and I report it.
- C11. The alias gate (Session 48): `debtsink` refuses to run after Camden's export until `aliascrossref` has been run and printed 0 ungraded; if the first `aliascrossref` prints ungraded proposals, `debtsink` refuses until they are graded and `aliascrossref` re-run. NULL: it runs with an ungraded proposal, or refuses with 0 ungraded.
- C12. The public-page profile figures for the 27 existing councils still match `city` (no engine rule change unless a claim above fails and an amendment is committed first).

(Results and Amendment 1 follow below.)

## Amendment 1 (written after the first loop, BEFORE the rule is coded; committed before it)
What I saw: `debtledger` added 262 Camden lines to `export/debt_ledger.csv`. 259 are class Other (211 Cyclescheme Ltd, 36 Capita Travel and Events Ltd, 8 Clarion Housing Association, 1 Sapphire, 3 others), and all carry the council's Purpose label
"Other Debtor Entities and Individuals". `HasDebtEvidence` removes "DEBTORS" (plural) as noise but not the singular, so "DEBTOR" trips the DEBT keyword. 57 of the 262 are marked Decomposed with an invented principal and implied rate
(for example Capita Travel and Events GBP 14,184.67 read as 14,000 principal plus 184.67 interest at 1.32%); that is a number the file does not say, on a label about money owed to the council by sundry debtors, not borrowing.
Rule to code: add the noise phrase "DEBTOR" (covers "DEBTORS" and "LONG TERM DEBTOR", which are already noise).
Claims: A1. Camden's `debt_ledger.csv` lines fall from 262 to under 10, and none of the 57 Decomposed lines stays. A2 (null that can fail). No other council's row in `debt_ledger.csv` changes: 0 removed lines and 0 added lines
outside Camden's, after re-running `debtledger` for all councils. If any other council loses a row I read it and report it.

## Results (written after the run; refuted claims first)
- C5 REFUTED. `SupplierKey.Normalize` does NOT drop the trailing asterisks: the company-suffix strip needs the suffix at the end, so "24HR AQUAFLOW SERVICES LIMITED****" keeps LIMITED and its key differs from the unstarred spelling (the xunit test fails as I wrote it; I changed it to record the behaviour). I measured the effect before deciding: 0 groups of the same payee, amount and date mix a starred and a plain spelling, so no Schedule B group is split. I did NOT change the shared key rule (no measured gain, a shared rule). It would show in a per-payee total.
- A2 (Amendment 1) REFUTED as written: re-running `debtledger` for every council removed 4 Durham lines ("Debtor - Independent Sector Care Providers", canceled repayments of COVID-19 advances: Haswell and District Mencap GBP 856.00, Mencap GBP 920.53 and 926.00, Growing Together Durham GBP 855.00), money owed TO Durham, which the new phrase "DEBTOR" removes as noise; 0 other council lines changed, 0 added outside Camden. I read them, they are the same kind as the "Long Term Debtor" noise already in the rule, and I kept the rule. A1 held in part: Camden's debt-ledger lines fall from 262 to 6, but 2 of the 57 Decomposed lines stay (London Borough of Camden, "Interest Payable on Long Term Borrowing", GBP 116,909.68 = 116,000 + 909.68 at 0.7842% and GBP 47,342.70 = 47,000 + 342.70), which are not debtor lines and are not removed (I said none would stay).
- C1 held: the split's 84 monthly files equal the site's own per-month count and signed sum (SoQL aggregate on the council's query service, computed by the site) in 84 of 84 months, 416,000 rows, GBP 5,620,030,075.32; `prep camden`: 416,000 = 416,000 written + 0 blank, re-read total equal, months 2019-09 to 2026-08, none missing.
- C2 held: redacted 66,091, retained 349,909 (reconcile 416,000 = 349,909 + 66,091).
- C3 held: Schedule A 0 rows, D 0 transactions, 0 of 416,000 (file, number) groups with more than one line.
- C4 held: Schedule B 23,308 groups / 73,906 member rows = 21.1% of retained (my pre-run count of the same payee+amount+date groups said 21.3%), GBP 480,233,010.56; Unclear 20,940 / 68,465, StandingScheduleCatchUp 1,861 / 4,061, StandingScheduleSurplus 507 / 1,380. Extra copies GBP 289.2m.
- C6 held: the one empty-payee line (sep-25-11315, GBP 17,395.07) is the only line of its payee, so it is in no schedule; it is not excluded as redacted (retained 349,909 includes it).
- C7 held: `crossfile` 0 of 349,909 retained lines repeated from an earlier month (0.00%), flagged months 0; `filedup` 0 of 84.
- C8 held: seeded hand-checks 30/30, 30/30 and 30/30 in the three Schedule B classes (Wilson 95% CI [88.6%, 100.0%]); `rawcheck` 300/300 (against the split files, which are derived from the one export; the independent read is C1's site aggregates). `datespread`: every row is inside its file's month (0 rows outside).
- C9 held: `aliasscreen` NoOverlap 0 right of 2,340 read (Session 47 printed 1,948; +392 screened proposals graded this session = 2,340, so the tool's count reconciles; my cumulative hand tally of 1,382 was stale); Contained 378 right/probable vs 5 reject; Partial 132 vs 1,888; AUC 0.876 over 510 right and 4,233 reject.
- C10 held: in the nine cross-council CSVs and the grades table, 0 removed lines and 0 added lines that are not Camden's (except Amendment 1's 4 Durham removals above): cross_file_repeats +84, file_duplication +84, debt_ledger +262 then 6 after Amendment 1, debt_sink +12, payment_misfits +1, crossref_alias_flows +15, supplier_alias_grades +445; budget_units, transaction_twins, within_txn_repeats +0.
- C11 held in a live run: the first `aliascrossref` printed 54 ungraded proposals, `debtsink` REFUSED (exit 3: "the last aliascrossref left 54 ungraded proposal(s)"); after the grades were written (53 distinct rows + 392 screened) `aliascrossref` printed 0 and `debtsink` ran (25,104 flows, 306 payer->entity pairs). Also shown by hand: no stamp, ungraded=2 in the stamp, and a stale fingerprint each refuse.
- C12: no engine rule changed except Amendment 1 (debt ledger only); `city` was not re-run for the 27 older councils; the nine CSVs above show no row of theirs changed.
- Read, not findings: Camden's Greater London Authority "Levies Paid" lines (2 x GBP 20,840,838.00 on 22 June 2021, and again on 17 August 2021; 2 x GBP 15,437,658.00 on 19 January 2022, consecutive identifiers), HMRC Central Payroll (2 x GBP 8,325,361.11 on 20 August 2025) and Veolia (4 x GBP 1,940,998.07 on 1 September 2025) are the biggest Schedule B groups: a payments file cannot tell them from instalments. The seeded read of 30 open groups (seed 20264801): 18 care, support, housing, charity or health payees, 3 recruitment agencies, 2 Ashdale Services groups unclassed by name, 7 others.
- Alias grades: 54 NEAREST proposals read by name (15 accept: Camden's own name in 8 other councils' files, "Camden Council" in Coventry's, Leeds, Durham and three Hertfordshire spellings in Camden's own file; 1 probable: "London Borough of Camden" paid by Camden itself, 3 lines; 37 reject) plus 392 screened out (all reject), 0 ungraded after.
- Coverage limit: the export's description says "since 2010" but holds only 16 September 2019 on; the council's older yearly lists on data.gov.uk have no files and the legacy camden.gov.uk asset links answer 403 to a script (I do not look for a way round it).
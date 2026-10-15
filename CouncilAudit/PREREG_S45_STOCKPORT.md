# Pre-registration, Session 45: Stockport Metropolitan Borough Council

Written before `prep stockport` exists and before any Stockport row is read for findings. 2026-10-04. Nothing outward; no FOI text.

Source: data.gov.uk dataset "All spend" (Stockport MBC), files on the council's own S3 bucket (`live-iag-static-assets.s3...amazonaws.com/pdf/Transparency/`), fetched
by a plain script (no 403) into `inbox/stockport/raw`: 116 files, 224,546,055 bytes (115 CSV listed on the dataset plus Nov 2019 and Oct 2021 as .xlsx only, and July 2018 as both).
Header census so far (first line only): 17 files from April 2025 carry `transaction_id` and `paid_date` and a Directorate; every earlier file has no transaction number;
files have invoice_date only (60 + others), invoice_date and paid_date (about 14), or "Date" (about 12); three files have no date column (March 2018, July 2022, September 2022);
the payee column is "Supplier" or "Supplier Name"; one 2019 file names the Service column "Services to People"; November 2024 is 8.9 MB because of about 1,000,000 trailing `,,,,,` lines.
Amounts are written 1787, 12204.95, " 57,283.20 ", "£3,340.00" and -£51.46.

## Claims, each with the result that would refute it

- C1 size: mapped rows between 1.4 million and 2.1 million (bytes per line 100 to 138 in three sampled files, 214.5 MB of CSV without November 2024's padding).
  Refuted if outside that band.
- C2 coverage: 115 monthly units, February 2017 to August 2026, no gap inside the range after tagging by file name. Refuted by any missing month printed by `prep`.
- C3 tagging: the file-name month must be the tag, because invoice dates precede the payment month. Test: in the files that carry ONLY an invoice date, the modal
  date month differs from the name month in at least half; in files with paid_date or "Date", the modal month equals the name month in at least 90%. Refuted if the
  invoice-only files mostly agree with their names (then dates could have been used).
- C4 transaction number: only the 17 files from April 2025 have a real one; for them no (number, supplier, amount, date) key appears in two files (0 keys). Refuted by any key.
- C5 redaction: payee text `*Redact - Personal Information` (and spellings) is the placeholder; before April 2025 fewer than 1% of rows carry one; from April 2025 between 5% and 40%.
  Refuted outside either band.
- C6 doubled publications: `filedup` and `crossfile` find none (0 doubled months) apart from the July 2018 CSV/xlsx pair, which is two formats of one month by design.
- C7 Schedule B (same payee, amount, date, no number to separate payments) is at least 5% of retained rows for the pre-April-2025 months, as for Surrey and Essex.
  Refuted below 5%.
- C8 `rawcheck` 300 of 300; seeded Schedule B hand-checks all confirmed. Reported as measured.
- C9 other councils: no cross-council row changes except rows involving Stockport (git diff of every tracked cross-council CSV); all six budget arms stay INCONCLUSIVE;
  Stockport goes in `BudgetTest.Deferred` (no per-service outturn mapping done).
- Null for the whole onboarding: a rule that moves Stockport only is allowed; any change to another council's prepared file or export row is a failure and is reported as one.

What none of this shows: that any Stockport payment is right or wrong. A payments file is one side of the ledger.

## Amendment 1 (written after reading the Stockport results above C1 to C9 and before any code for it)

Observation: Stockport's October 2025 file has 163 lines whose payee is the council's own label "*Exclude" (131) or "*Exclude - VAT only" (32). They are not names; one label stands for
unrelated payments, which is why redaction placeholders are left out of every schedule. 24 of Stockport's 495,774 Schedule B member rows carry it (read in exceptions.csv, not yet
re-run), and the ONE Schedule D transaction (104625028, 16 lines, "2 suppliers") is a Bedspace Resource Ltd run plus a "*Exclude - VAT only" line of -19.78.
Rule under test: a payee whose text begins with "*Exclude" is a placeholder (excluded from the schedules, counted in the export), exactly like a redaction placeholder.
- E1: Stockport Schedule D goes from 1 transaction / 16 lines to 0.
- E2: Stockport Schedule B member rows fall by at most 24 (exactly the 24 rows read), groups by at most 8.
- E3: no other council's Schedule B, D, exceptions.csv or export row changes (no payee in any other council starts with "*Exclude").
- E4: Stockport's retained + redacted still equals its mapped rows (1,947,912), with redacted going 616,335 to 616,498 (+163).
Refuted if any holds false; the rule is kept only if E3 holds. What it does not show: that any of those 163 payments is right or wrong.

## Results

Runs: `prep stockport` (115 files, no gap), `city`, `export` and `phoneexport` for all 25 councils, `rawcheck`, `datespread`, then the loop (`filedup`, `txntwin`, `crossfile`, `withintxn`,
`debtledger`, `aliasscan`, `aliascrossref`, `correctionscan`, `debtsink`, `reconcile all`, `budgettest`). A first loop run was stopped after about 15 minutes, when the "*Exclude" label was found,
and everything was re-run after the rule (so no number below comes from the stopped run).

- C1 HELD: 1,947,912 mapped rows (band 1.4 to 2.1 million); 2,971,136 raw lines, of which 1,023,224 are blank (1,023,222 are trailing `,,,,,` lines in November 2024), signed total GBP 6,872,346,948.88.
- C2 HELD: 115 monthly units, February 2017 to August 2026, no gap inside the range. 116 files downloaded (113 CSV, 3 Excel); July 2018's Excel copy holds exactly the CSV's 8,490 rows and is left out.
- C3 REFUTED in its null clause: I said the files with only an invoice date would mostly disagree with their names. They do not: in all 112 dated files the most common date month is the month in the file name
  (14 files that publish both dates: 100% of rows in the file's own month; the other 98: lowest share 67.6%, mean 92.9%, the rest one month earlier). The second clause (paid-date files agree in at least 90%) held (14 of 14 at 100%).
  Names are still used as the tag, because three files have no date at all and dates run up to a month behind the file (91.5% of all rows in the file's own month, 5.6% in the month before).
- C4 HELD for the 17 numbered files (April 2025 on): no key repeats across files. The overlap step lists 164 keys between July and September 2022: both are files with no date and a row-position placeholder,
  and the two files share 164 rows by position out of 18,027 (0.9%), which is chance in two lists sorted by payee; not a finding.
- C5 REFUTED before April 2025: payee text with "Redact" is 30.2% of rows (443,313 of 1,467,504; 40% in 2017 to 2019, 25% in 2022 to 2024), not under 1%. HELD from April 2025: 36.0% (173,035 of 480,408), inside 5 to 40%.
  All redacted rows with the Session 45 rule: 616,498 (31.6%): "*Redact - Personal Information" 544,596, "Redact - Personal Information" 70,284, "*Exclude" and "*Exclude - VAT only" 163, others 1,455.
- C6 REFUTED as written: `filedup` flags no file (exact-repeat rate 8.8% to 59.9%, median 38.6%), but `crossfile` flags one: September 2022, 5,379 of 15,995 retained rows (33.6%) match July 2022 by payee, amount, date, description
  and service. Both files have no date, so the date part of the key is empty and fixed weekly care fees match. Test of the reading: with the date ignored, adjacent months match 29% to 63% (eight pairs), two months apart 43% to 59%;
  September against July is 43.0%. Not a republished month. With those two files out, 1,253 of 1,331,414 rows (0.09%) match an earlier file (0.50% with them).
- C7 HELD: Schedule B is 495,751 member rows, 37.2% of 1,331,414 retained rows (117,912 groups, GBP 942.15m: Unclear 108,888, surplus 2,067, catch-up 2,982, reversed the same day 3,975). A 12-group seeded read by payee:
  10 care-provider groups, one cleaning contract, one fees line of a care and education provider. The largest groups are annual levies paid in monthly instalments under one invoice date
  (Greater Manchester Combined Authority, GBP 1.8m and GBP 2.1m under 2 April 2024 in 11 files in a row), which this payments file cannot show as instalments.
- C8 HELD: `rawcheck` 300 of 300; seeded hand-checks 30 of 30 in each of the four classes (Wilson 95% CI [88.6%, 100.0%] for n=30).
- C9 HELD: after re-running every council, no tracked export or web_export file of the other 24 councils changed (git status), and in the ten cross-council CSVs there are 0 removed lines and 0 added lines that are
  not Stockport's (budget_units 17, cross_file_repeats 115, crossref_alias_flows 24, debt_ledger 6, debt_sink 22, file_duplication 115, payment_misfits 1, supplier_alias_grades 242, transaction_twins 6, within_txn_repeats 524).
  All six budget arms are INCONCLUSIVE; the frozen stage files are untouched; Stockport is in `BudgetTest.Deferred`.
- Alias screen: Stockport is a source and a target. 136 NEAREST proposals read by name (121 unique) plus 121 screened-out ones: 24 accept (other councils' spellings of Stockport, and one council's name for another),
  1 probable (the council's own imprest account "Stockport MBC Client Finance"), 217 reject; 0 ungraded after. The NoOverlap census stands at 0 right of 1,078 read (957 before, 121 now).
- Amendment 1: E1 HELD (Schedule D 1 transaction / 16 lines to 0), E2 HELD (member rows 495,774 to 495,751, 23 fewer; groups 117,920 to 117,912, 8 fewer), E3 HELD (no payee label like it in any other council; the
  other 24 councils' exports unchanged), E4 HELD (616,335 to 616,498, +163; retained 1,331,414 + redacted 616,498 = 1,947,912). The rule as coded is narrower than the sentence above: only the two labels,
  compared after punctuation is removed, not every payee beginning "*Exclude".
- Read, not findings (nothing in a payments file shows wrongdoing): six pairs of transactions with the same lines under two numbers within a week, all in the 17 numbered months: Phoenix Software Ltd 4 lines GBP 138,426.76
  (5100551054 and 5100551056, both 10 Oct 2025), BAE Systems Pension Funds CIF CHAPS 3 lines GBP 233,000.00 (1900871133 and 1900871141, both 28 May 2026), Derbyshire County Council 10 lines GBP 40,500 (4 days apart),
  Totally Local Company Ltd GBP 11,656 (5 days), PINC College GBP 10,070.73 (7 days), Marwood Electrical GBP 25,493.82 (5 days); the first two are identical in every published column except the number.
  Payees starting HMRC have fewer than 5 lines in 10 of 115 months (December 2021, June 2023, October 2023, March 2024, June 2024, December 2024, March 2025, July 2025, March 2026, May 2026) against 24 to 123 in the others.
  Monthly rows double from March 2020 (6,947 to 13,444) and again step up in April 2025 (22,884 to 35,991, when the council says it moved to all spend).
- Mistakes of mine: (1) I first started the long loop before reading the Schedule D transaction, found the "*Exclude" label from it, and had to stop and restart (about 15 minutes lost). (2) Three of my pre-registered clauses (C3, C5, C6) were wrong because I wrote them from file sizes and headers without opening one month of payee labels or dates; the real header split (13 "Date", 66 invoice, 14 both, 17 paid, 3 none, and two Excel months) came from reading every header after that. (3) A PowerShell grouping of 20,000-row
  months printed 36 MB of errors (scope of functions inside the tool); the same test in C# took seconds.

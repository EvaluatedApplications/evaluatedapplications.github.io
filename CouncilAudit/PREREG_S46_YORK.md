# Pre-registration: City of York Council onboarding (Session 46, 2026-10-04)

Written BEFORE `prep york` or any engine run on York. What I have already seen: the 15 file names and sizes, every file's header line,
2 to 3 data lines of 8 files, the `sheffcensus` output (rows, first-date-column month range, signed sum per file), and the top 25 payee labels
of three files (2012/13, 2015/16, 2025/26). Nothing else.

Source: data.gov.uk dataset "All Payments to Suppliers" (City of York Council), 15 annual CSVs on data.yorkopendata.org, 2011/12 to 2025/26.

## Observed, not predicted (so not a claim)
- 15 files, one financial year each, 1,273,453 rows and a signed census total of GBP 3,396,056,245.58 (sum of the 15 census sums).
- Every file's dates fall in April to March of its own year (census ranges), so the tag is the financial year.
- Four layouts: 2011/12 to 2014/15 (8 columns, "Amount"), 2015/16 to 2017/18 (9 columns, "NetAmount_ExcVAT"), 2018/19 (13 columns with GL_Date AND Payment_Date),
  2019/20 on (13 columns, Card_Transaction; in 2022/23 on the Transaction_No and Card_Transaction columns swap places).
- Redaction placeholders seen: "REDACTED - PERSONAL DATA", "REDACTED- PERSONAL DATA", "REDACTED-PERSONAL DATA", "REDACTED PERSONAL DATA". All contain REDACT.

## Claims (each can fail)
- Y1. `prep york` reconciles: raw rows = written rows + blank rows for each of the 15 files, signed total equal to the census to the penny, no file dropped.
  Written rows 1,273,453 minus blank rows (I expect 0 blank rows; band 0 to 50).
- Y2. For 2018/19 the payment date is `Payment_Date` (not `GL_Date`); its month is within one month of the `GL_Date` month for at least 95% of rows.
- Y3. Redaction share (rows with a REDACT payee) is above 10% in 2012/13 (I count 15,780 of 88,575 = 17.8% from the three labels above; band 15% to 25%) and below 6% in 2025/26 (2,320 of 68,369 = 3.4% from one label; band 3% to 6%).
- Y4. Transaction numbers are council-wide unique ("CR0000105270", "201819CR00000001"): Schedule D (one number, several payees) is under 200 transactions in the whole series.
  NULL for Y4: if D is over 1,000 transactions, a label (like Stockport's "*Exclude") or a batch id is shared, and I read the top ones before running the loop.
- Y5. `filedup` and `crossfile` flag no file (no annual file repeats another): 0 of 15 flagged.
  NULL for Y5: 2012/13 holds 88,575 rows against 37,522 in 2011/12 with almost the same total (GBP 150.79m against 150.73m) and August 2016 holds 27,612 rows
  against about 14,000 in its neighbours; if either is a re-published or doubled batch, `filedup`/`crossfile` or the exact-repeat rate will show it. I predict they do NOT:
  the rise in 2012/13 is a change in what is published (lines under GBP 500 or split lines) and August 2016 is one large batch.
- Y6. Schedule B (same payee, amount, date) is mostly per-visit or per-resident care lines: in a seeded 30-group read by payee, at least half are care or home-care providers.
- Y7. Seeded hand-checks of Schedule B/D reach 30/30 per class against the raw files (mechanical check, not a judgement).
- Y8. `datespread` finds the most common date month inside the file's financial year in 15 of 15 files (a trivially true claim from the census; listed so it is checked by the tool, not by me).
- Y9. HM Revenue and Customs / Teachers Pensions lines are present in every file (I have not looked; Surrey's October 2020 quarter lacked them). Claim: at least 13 of 15 years have an HMRC-named payee with 5 or more lines.

## Declared search width
One council, 15 files, one prep rule set, one engine run; every claim above is stated once and scored once. No threshold or rule is tuned on York after the run
without a labelled amendment written before the code it governs.

## Results (written after the run; the pre-registration file above was written before `prep york` ran but, my mistake, was not committed until now, so its timing rests on file times and the order of this session, not on a commit)
- Y1 HELD. 15 files, 1,273,453 raw rows = 1,273,453 written + 0 blank, signed total GBP 3,396,056,245.58 raw and re-read; every file equals its census figure to the penny.
- Y2 HELD. 2018/19: 62,462 of 64,624 rows with both dates (96.7%) have Payment_Date within 31 days of GL_Date; 5,189 are equal.
- Y3 HELD, both bands. 2012/13 15,801 of 88,575 (17.8%); 2025/26 2,320 of 68,369 (3.4%). All 15 years sum to 136,053, equal to the engine's redacted count. Range 0.9% to 26.0%.
- Y4 REFUTED, and the null fired. Schedule D is 46,498 transactions / 350,520 lines (claimed under 200). Two causes, read before anything was called a finding: in 2011/12 to 2017/18 the number is a payment-run reference
  shared by unrelated payees paid the same day (30,578 transactions); in 2024/25 the "202425CRCR" numbers are each used by exactly two lines of different payees (15,920 transactions, 31,840 lines).
  None in 2018/19 to 2023/24 or 2025/26. I had looked at two number formats and not at how many lines share one.
- Y5 HELD. `filedup`: 0 of 15 flagged; `crossfile`: 0 of 1,137,400 retained rows match an earlier file. The null (2012/13 and August 2016 being doubled) did not fire: August 2016 has identical-line extras of 16.3% against
  about 11% in neighbours (a doubled month would give about 50%); the extra rows are home-care lines (Riccall Carers 2,102 against 810 in September).
  Not predicted, found: rows fall from about 14,000 a month to under 5,000 in December 2016, and 2016/17 has the highest exact-repeat rate (20.0%).
- Y6 HELD. Seeded 30-group read (seed 46, Unclear groups): 19 care, support or housing providers, 11 others.
- Y7 HELD. Seeded hand-checks 30/30 in each of the four Schedule B classes (Unclear, StandingScheduleSurplus, StandingScheduleCatchUp, ReversedSameDay) against the raw files. Schedule D is not hand-checked by the tool.
- Y8 HELD (stronger than stated): 1,273,419 of 1,273,453 rows (100.0%) dated inside their own financial year; 34 after it (2018/19 file, up to 11 April 2019); `datespread`.
- Y9 REFUTED. A payee naming HM Revenue or HMRC has 5 or more lines in 5 of 15 years (0 to 19 a year), not 13; Teachers' Pensions 1 line in all. The file is not where pensions and tax are paid.
- Also: `rawcheck` 300/300. Alias screen: 126 NEAREST proposals read by name plus 304 screened out: 36 accept, 10 probable, 80 + 304 reject; the NoOverlap census is now 0 right of 1,382 read.
  Cross-council files: 6 files gained only York rows (budget_units 15, cross_file_repeats 15, debt_ledger 29, file_duplication 15, transaction_twins 7, within_txn_repeats 483), 0 removed; the budget test treats York as deferred (no stage, no statistic).
- 432 tests pass (14 new in Session46Tests.cs, which was 418 before).

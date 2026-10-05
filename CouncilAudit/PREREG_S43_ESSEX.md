# Pre-registration, Session 43: Essex County Council, pooled payee labels

Written BEFORE the Essex files are prepared or run. 2026-10-04.

What exists already (read, not yet run through the engine): 30 workbooks from essex.gov.uk (15 legacy .xls, 15 .xlsx), one sheet a month,
April 2019 to August 2026 (September 2026 is an empty sheet). Columns: Date, Name, Value, Function, Source (Accounts Payable, Mileage &
Expenses, Purchase Card), Spend Description, Merchant Group, Merchant Category. All items, no threshold, no transaction number.
Each sheet starts with a summary of totals by Source; in the census every sheet's rows sum to its own "Total" to the penny (89 of 89
non-empty sheets).

The observation that makes a rule necessary: a dozen payee labels (14 spellings) are not a payer's counterparty but a label standing for many people.
Counts over all 89 sheets (3,713,962 lines with a name): FOSTER CARE PAYMENT 728,434; ECC EMPLOYEE 292,593 (+ "ECC Employee" 4,718);
DIRECT PAYMENT 287,182 (+ "DIRECT PAYMENTS" 6,800); PAYMENT TO INDIVIDUAL 31,005 (+ "PAYMENT TO INDIVIDIUAL" 490); NON EMPLOYEE EXPENSE
25,579; TRAINING BURSARY 21,780; ONE OFF NON INVOICE 10,947; FOSTER CARERS 10,238; ONE OFF UNDER GBP10K 8,518; ONE OFF GRANTS 5,284;
ONE OFF TRAVEL EXPENSES 1,816; ONE OFF CORONERS EXPENSES 818. The council's own field note says "Some names have been removed in line with
Data Protection policy". The engine's treatment of a redacted payee is: leave out of every schedule, count in the export.

The rule under test (`NextFixups.EssexPooledLabel`, applied by `prep essex`, a case-insensitive EXACT match on the 14 spellings above (15 counting the case variant "ECC Employee"), nothing
fuzzy): the name cell becomes "Redacted (pooled label): <published label>", so the engine excludes it as redacted, and the published label stays
readable in the export. No amount, date or other cell changes.

Run order: (1) prep and run the engine with the labels as PUBLISHED (no rule), record the Schedule B numbers; (2) then with the rule.

H1: with the labels as published, pooled-label lines make up at least 50% of the member lines of Schedule B. Refuted if under 50%
(then the rule is not needed to read the schedule and I do not ship it).
H2: the open Schedule B groups of the pooled labels have a modal amount that is a published per-week or per-month rate (foster care): not tested
here, only observed; nothing is claimed.
Null N1 (the rule must be specific): applied to the other 22 councils' exports, the 14 spellings match 0 rows in 21 of them. Any council with
more than 100 matching rows is named, and the rule does not run for it (the rule is only used by `prep essex`).
Null N2 (the rule must not drop money): total signed Value of the prepared Essex files is identical with and without the rule, and the
retained plus redacted row counts equal the prepared row count.

What this does not show: that any pooled-label payment is right or wrong. A pooled label cannot be checked for repeats, because it stands for
different people. After the rule the schedules say nothing about 38.7% (1,436,202 of 3,713,962) of Essex's lines, and the profile says so in KnownQuirks.

## Results (written after the runs; 2026-10-04)

H1 HELD: with the labels as published (control run, `COUNCILAUDIT_ESSEX_NORULE=1`) Schedule B had 283,957 groups and 1,439,391 member rows; the pooled labels were
832,330 member rows (57.8%) and were in 100,316 groups (35.3% of groups). H1 was set on member rows, so it holds on the measure I fixed; on groups the share is lower.
With the rule: 183,641 groups, 607,061 member rows, GBP 811.47m of extra value.
N1 HELD: the 14 spellings match 0 lines in 21 of the other 22 councils' exports; Cornwall has 20 lines of "Direct Payments" (under the 100 I set; the rule only runs for Essex).
N2 HELD: signed total GBP 17,118,041,469.02 with and without the rule, equal to the sum of the council's own sheet totals (90 sheets); retained 2,277,742 + redacted 1,436,220 = 3,713,962
(1,436,220 = the 1,436,202 pooled lines counted in advance + 18 lines whose payee was already blank or redacted).
H2: not tested.

Correction (written after the Hertfordshire run): I wrote above that the 18 rows beyond the 1,436,202 pooled lines were "already blank or redacted". They were 13 lines of "REDACTIVE EVENTS LTD" and 5 of
"REDACTIVE", a real supplier the engine's "contains REDACT" test dropped (PREREG_S43_HERTS.md). With the test corrected Essex has retained 2,277,760 + redacted 1,436,202 = 3,713,962; Schedule B is
unchanged (607,061 member rows).

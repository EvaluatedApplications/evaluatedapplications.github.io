# Pre-registration: Wokingham payments to private social care providers (Session 42, 2026-10-04)

Owner direction: "look with a focus into social care private companies". Scope corrected by the owner mid-session: WOKINGHAM
ONLY. Other councils' files may be used only as a comparison (the same provider elsewhere), never as the subject. Nothing outward,
no FOI drafts. Written and committed BEFORE any per-supplier figure, share, rate or Companies House match is computed.

## What I had seen before writing this
- Label census only: Wokingham `ServiceArea`, `Description` and `CostCentreArea` totals (no per-supplier figures). Adult care sits
  under Description `TPP - WBC Funded Care` (GBP 241.4m, 74,606 rows) with cost centres naming the care type (OP Nursing, LD
  Supported Living, OP Domiciliary Care ...); children's placements sit in cost centres `Children's Homes Purchasing`,
  `Children's External Residential`, `Semi Independent`, `UASC ...`, `Independent Fostering Agency placements`, `18+ Care Leavers
  Placements`, `Staying Close` (two apostrophe spellings). `Agency Staff` is a Description.
- 25 raw rows of OP Nursing, June 2024 (to see what a row is): lines are per-client invoices (Barchester Healthcare 5,314.29 six
  times on 14 Jun 2024). I noticed 5,314.29 = 1,200 x 31 / 7 and 4,428.57 = 1,000 x 31 / 7: a weekly rate times days / 7. That
  observation is the reason for scan S2 below and is in-sample for those two amounts only.
- Session 40 knowledge of Optalis (council-owned; block contract) and the engine's exceptions.csv format.

## Population (fixed now)
Wokingham, all six files (FY2020-21 to Dec 2025), rows of `scratch/s40_wok.csv`. A row is IN the care population when
(a) ServiceArea is Adult Social Care & Health and CostCentreArea matches
`Nursing|Residential|Domiciliary|Domicilary|Supported Living|Shared Lives|Day Care|Respite|Other Care` (care type = that word), or
Description is `TPP - WBC Domicilary Care (to be Recharged to Health)`, `TPP - Self Funded Care` or `TPP - Continuing Care`; or
(b) CostCentreArea matches `Children.s Homes Purchasing|Children.s External Residential|Semi Independent|UASC|Independent
Fostering Agency|18\+ Care Leavers|Staying Close` (children's residential / semi-independent / fostering agency); or
(c) Description is `Agency Staff` and ServiceArea is Adult Social Care & Health or Children's Services (staffing in care).
Control C0: the population total and row count by care type are printed and must equal the label census sums for the same
filters (no row lost by the loader).

Supplier groups: (1) COUNCIL-OWNED: Optalis Limited (comparison group, never in the target ranking); (2) PUBLIC: names matching
NHS, Council, Borough, County Council, Integrated Care, Foundation Trust, Health Authority, CCG, ICB; (3) INDEPENDENT: everything
else, split by Companies House form where matched: for-profit (Private/Public Limited Company, LLP) and not-for-profit form
(limited by guarantee, CIO, CIC, charity, Community Benefit Society); (4) UNMATCHED, no corporate word: may be an individual
(sole trader, carer, personal assistant). Category 4 is reported only in aggregate and never named.

## Scans and their nulls (declared width: these seven, no others ranked)
S1 Concentration (descriptive). Per care type and overall, per financial year: supplier shares, top 10, HHI. "Fastest growing":
absolute and relative change between FY2021-22 and FY2024-25 (the first and last complete years after COVID), only for suppliers
with >= GBP 250k in FY2024-25. Optalis printed alongside as comparison. No hypothesis test; control C0 above.

S2 Weekly-rate decode. A line of net A >= 100 decodes when exactly one (w, d) exists with d in 1..31 days and w a whole pound in
[100, 25,000] such that round(w x d / 7, 2) = A (or the truncation to the penny equals A). Null N2 (placebo): the same lines with
the pence part replaced by seeded uniform pence (seed 42); the real decode rate must be at least 3 times the placebo rate in a care
type, else rates are not reported for that care type. Lines that are whole pounds are counted separately (their d is ambiguous).
Outputs: per provider, care type and FY, the median decoded weekly rate and n lines. Step change: a provider-care type whose median
moves by more than 15% between consecutive FYs with n >= 10 both years. Null N2b: the distribution of the same YoY change across
all provider-care types; a step change is reported as unusual only if it is also above that distribution's 95th percentile.
Innocent readings stated in advance: needs mix (new clients at higher bands), annual fee uplifts (National Living Wage rises of
9.7% Apr 2023, 9.8% Apr 2024, 6.7% Apr 2025), Fair Cost of Care uplifts, a home switching to nursing.

S3 Same provider, other councils (comparison only, exploratory). Where the same provider name appears in another of my 20 council
files, decode its lines there the same way (only if that council's own decode rate passes N2 for the care type) and print median
weekly rates side by side. A ratio is reported only with n >= 20 decoded lines on each side, and always with the reading that
councils buy different needs mixes and the published line does not say the care package.

S4 Excess lines (the copy question for per-client invoicing). For supplier S and exact amount A (>= 500), c(m) = number of
lines at A in month m. Base b(m) = median of c over the six months m-3..m+3 excluding m (needs all six months inside the file).
Excess e(m) = c(m) - b(m) when > 0. Catch-up reading: covered when the shortfall sum of (b - c) over m-3..m-1 is >= e(m).
Uncovered excess = e(m) minus that shortfall. Null N4: the same statistic with the months of each (S, A) series shuffled (seeded,
20 shuffles); a provider is listed only if its uncovered-excess lines are at least 3 times its own shuffled mean and at least 5
lines. Innocent reading stated in advance: a short stay (respite, a client who arrived and left within a month, or a backdated
period paid in one month) produces the same shape; the published file has no client or period column.

S5 Companies House dates (source: Companies House Free Company Data Product snapshot 2026-10-01, live companies; company pages
on find-and-update.company-information.service.gov.uk for names not in the snapshot; every figure labelled with its source).
Match: exact normalised name (Ltd/Limited, punctuation, "The"), then by hand for the top 150 independent suppliers by value; a
match is accepted only with a care-related SIC code (86xxx, 87xxx, 88xxx, 78xxx for staffing) or a registered office/trading name
that fits, else marked uncertain and not used for date tests. Tests: payment dated before incorporation; payment dated after
dissolution (dissolved companies come from company pages). Null N5: the same match-and-test run on a seeded random sample of 150
NON-care Wokingham suppliers of the same size band; the care hit rate is reported beside the non-care rate. Each hit is
hand-checked against raw rows and the company page (previous names: a renamed or re-registered company paid under its trading
name is the first innocent reading; a sale of the business to a new company the second).

S6 New providers taking large sums. Supplier whose first payment in the file is on or after 1 Apr 2021 and whose first 12 months
from first payment total >= GBP 250k. Null N6: the same rule's hit count among non-care suppliers per GBP of spend. For each hit:
incorporation date, previous names, care type. Innocent readings: new home or scheme opened, acquisition or rename (previous-name
field), a contract transferred from another provider (look for a provider that stops when it starts, same care type).

S7 Related companies (shared name stem, registered office or parent). Groups formed by (i) a shared registered-office address in
the Companies House snapshot where that address hosts fewer than 20 companies in the whole snapshot (mass formation and
accountants' addresses excluded by that cap), (ii) a shared corporate parent in the PSC snapshot if I can load it (individual
PSC names are used only as a link key and are never written in any report). Reported: group share of care spend and whether
members take turns (one stops as another starts, a handover) or run side by side in the same care type. Splitting test: same
date, same care type, the group's lines on that date summing to a round figure (multiple of 1,000) while no single line is round.
Null N7: the same test on random pairs of unrelated independent suppliers of the same care type.

## Hand check
For every item in the final ranked list, the raw rows are re-read from `export/wokingham/FY*.transactions.csv` (not the slim
file) and quoted; I state for each whether an ordinary explanation fits. Nothing is called wrong-doing; a payments file shows
only the council's side.

## What would show this is not informative
N2 fails in a care type (no rates there); N4 shows real at or below 3 x shuffled (the excess scan says nothing); N5 care hit rate
not above non-care (date hits are name-matching noise); N6 care rate not above non-care; N7 the related groups split no more than
random pairs. Each failure is reported as a failure.

## Amendment 1 (after control C0, before any per-supplier figure)
C0 printed the population by care type and Description. The cost-centre rule (a) also caught non-care purchases booked to care
cost centres: `Acquisition of Buildings` (GBP 4.30m, 2 rows), `Construction` (3.55m), `Professional Fees`, `Client / Customer
Transport` (5.83m), `Services - ...`. Change: a row in (a) or (b) must also have a Description starting `TPP - ` or equal to
`Individual Service Fund`; (c) is unchanged. Optalis appears under two names (`Optalis Limited`, `Optalis Ltd– Hollies Care
Home`); both are COUNCIL-OWNED. I saw no supplier figure before this change.
## Amendment 2 (after S2 failed N2, before the replacement is run)
Result as pre-registered: S2 FAILS N2 in every care type (decoded share 0.3% to 2.5% of non-whole lines against a placebo of 1.2%
to 2.8%; ratio 0.17 to 1.28). Reason (my design error): when an invoice is w x d / 7 with whole-pound w, 7A is an integer K = w x d,
and K has many divisor pairs in range (5,314.29: K = 37,200 = 1,200 x 31 = 18,600 x 2 = 1,240 x 30 ...), so "exactly one (w, d)"
almost never holds. The pre-registered rates are therefore NOT reported.
Replacement (post hoc, labelled): (a) SEVENTHS signature: a non-whole line is "weekly-priced" when 7A is within 0.035 of an
integer (A is a whole number of sevenths of a pound, to the penny). Null: under uniform pence the chance is 6/99 = 6.1% (pence 14,
29, 43, 57, 71, 86); the care type passes when its rate is at least 3 times that, i.e. >= 18.2%. (b) Weekly rate: for a
weekly-priced line, d is taken as the length of the calendar month BEFORE the pay month (invoices in arrears); the rate is reported
only when K / d is a whole pound; the share of weekly-priced lines for which that holds is printed beside the share for the pay
month itself and for d = 28, as a check that the prior is better than the alternatives. Rates feed the S2 step-change test and S3
unchanged.
## Amendment 3 (before S4 is run; no S4 number seen)
As written, S4 counts every one-off amount (base 0) as excess, so it would measure the number of distinct amounts, not copies.
Change: excess is counted only where the amount is standing around m, b(m) >= 1. Everything else as pre-registered, including the
month-shuffle null N4. I note now, before running, that a month shuffle keeps the multiset of counts, so a one-month spike survives
shuffling; N4 can only show that real excess is MORE covered by earlier shortfalls than chance (catch-up timing), and I expect it
to fail as a copy detector. If it fails, S4 is reported as uninformative.
## Amendment 4 (after S4 was run)
S4 result: real uncovered excess 1,899 lines against a shuffled mean of 327 (ratio 5.8), 55 providers "pass". The pass is an
artefact, not evidence: (1) shuffling destroys the standing base (a shuffled series rarely has b >= 1 around a spike), so the
null is far too low; (2) the top spikes I opened (Jigsaw 7,885.24 counts 3,3,3,6,0,6,0; Voyage Care and Anthony Toby Homes Trust
pairs on the 3rd/4th and 28th-31st of one month) are pay-run bunching: two monthly invoices paid in one calendar month with an
empty month AFTER, which my backward-only catch-up cannot see. S4 is reported as uninformative and no provider is listed from it.
Replacement (post hoc, exploratory, labelled): S4' "count against time" (the Session 26 lesson). For supplier S and amount A
(>= 500, at least 4 months with a payment): L = median of the non-zero monthly counts; span = months from first to last payment;
surplus = payments - L x span - L (one period of timing slack). Series with surplus >= 1 are listed by surplus x A. No null is
claimed; the innocent reading is a second client at the same rate for part of the span (rises over consecutive months), against
a copy (a single month above L with no empty month on either side within 2 months). Each listed item is hand-checked against raw
rows before it is called anything.
## Amendment 5 (population correction, found while hand-checking S4'; independent of any result)
The FY2020-21 file prints some transactions twice (known since Session 29: the KPMG-letter item 2; October 2020 above all). Rows
of a transaction are dropped to one copy when every line key (supplier, net, cost centre, description, date) occurs an even number
of times AND the half-sum equals the stated Gross (net or x1.2, within 1%) or the full sum exceeds 1.21 x Gross. Removed from the
care population: 1,272 rows, GBP 7,757,538.92 (of which Optalis transaction 3558420, 13 Oct 2020, GBP 3.93m). Remaining exact
row twins in care: 96 rows, GBP 89,910.84 (a transaction whose lines sum to its Gross: two clients at one rate, kept). All S1
figures are re-run on the corrected population; the S2-S4' results above were on the uncorrected population and are re-run too.
## Amendment 6 (S5 matching details, after the first live-snapshot match, before any dissolved-company lookup)
- Normaliser refined: apostrophes dropped, II/III to 2/3, and names compared with spaces removed (GABRIEL'S ANGELS and Gabriels
  Angels; RESI- ACTION and Resiaction). Previous names in the snapshot are matched too.
- Pay-before-incorporation on the live snapshot (unique exact matches, before the refinement): care 5 of 438 (1.1%; top 150: 1 of
  108), non-care 4 of 87 (4.6%). Every hit I read is a later company re-using a name (Jasmine Care Limited 16428776, incorporated
  May 2025, against payments to "Jasmine Care Ltd" in 2020-21). N5 FAILS for this test: the care rate is not above non-care, so
  pay-before-incorporation on name matching is noise and is not ranked.
- Payment after dissolution (dissolved companies are not in the snapshot): looked up on the Companies House search page for every
  supplier with a corporate suffix (Ltd/Limited/LLP/PLC), value >= GBP 50k and no live match: care 25, non-care 7 (all of each).
  A dissolved company counts only if its name matches the published name after normalising AND no live company of the same
  group (same registered office) carries the trading name; otherwise it is recorded as "trading name of a live group company".
## Amendment 7 (two more scans, written before they are run)
Results so far, recorded before adding: S1 run; S2 failed as pre-registered, sevenths replacement passes in Nursing (53.9%),
Residential (34.6%), Children residential (24.6%) and Semi-independent (21.6%) but the month prior separates d only in
Nursing/Residential, so rates are reported there only; S3: no other council has 20 or more decodable lines for the same providers
(0 council-provider-years), so no cross-council rate comparison is possible; S4 uninformative; S5 dissolution lookups: care 1 of 25
(Network Healthcare Ltd, a dormant company dissolved 24 May 2022, paid as a label until Jul 2025), non-care 1 of 7 (EDF Energy 1
Limited, dissolved 8 Aug 2017, paid as a label to Sep 2025): N5 FAILS (care not above non-care); S6: care 11.26 hits per GBP 100m
against non-care 7.73 (passes, ratio 1.46); N7: related groups 0 round-sum hits in 96 shared days, random pairs 4 in 5,160.
S8 Isolated repeat (exploratory): independent care supplier S, amount A >= 5,000, exactly two payments of A at S in the six
files under different transaction numbers, at least 60 days apart, and no other payment at S within 5% of A within 45 days of
either. Null N8: the same definition on non-care independent suppliers; rate per 1,000 lines of >= 5,000. Listed only if the care
rate is at least 1.5 times non-care; each listed pair is hand-checked (invoice dates, the supplier's other lines).
S9 Round lump sums (descriptive): lines to independent care suppliers that are whole thousands and >= 20,000. Null N9: the same
share among non-care independent lines >= 20,000. Innocent readings stated now: block or framework payments, grant pass-through
(Infection Control Fund, Workforce funds), fee-uplift lumps, deposits or advance payments.
## Amendment 8 (one more scan, before it is run)
S8 result: care 2.58 isolated pairs per 1,000 lines >= 5k against non-care 15.08 (ratio 0.17): NOT listed. S9: care round lumps
1.5% of lines >= 20k against non-care 5.8%: care is lower. Hand-check notes (outside the scans): Bridges Home Care Ltd two GBP
50,000.00 lines with one invoice date; Resiaction Staffordshire two GBP 43,500.00 lines 14 months apart.
S10 Accounts size against payments (Companies House "accounts category" of the latest filed accounts, live snapshot): an
independent care supplier matched to exactly one company whose latest accounts are MICRO ENTITY, and whose Wokingham payments in
one financial year exceed the micro-entity turnover limit (GBP 632,000; GBP 1,000,000 for FY2025-26). Null N10: the same on the
non-care arm. Innocent reading stated now: a company qualifies as micro if it meets two of three tests (turnover, balance sheet,
employees), so a turnover above the limit does not by itself mean the filing is wrong; the latest accounts may cover a different
year from the payments.
## Close-out (Session 42)
S10: care 2 of 68 micro-entity filers have a year of Wokingham payments above the limit (Zencare Children Services Ltd and Codeko
Homes Limited, GBP 661k each in FY2024-25), non-care 0 of 11: too few to separate from chance; the two-of-three rule fits.
Coded (tested, 10 tests): SocialCare.cs and `socialcare wokingham`; the CLI reproduces the probe's population exactly (88,901 lines,
GBP 347,531,910.29, 1,272 doubled rows removed). One difference, mine: the probe matched "Borough" inside a word, the library on a
word boundary, so 1 more payee counts as independent (629 against 628). Ranked list and readings: WOKINGHAM_SOCIALCARE_2026-10-04.md.Correction (same session): the Companies House matcher first filed each company under one matching name only; fixed after the
Amendment 6 test. Re-run on the fixed matches: pay-before-incorporation care 8 of 463 unique matches (1.7%), non-care 4 of 93 (4.3%).
N5 still fails.
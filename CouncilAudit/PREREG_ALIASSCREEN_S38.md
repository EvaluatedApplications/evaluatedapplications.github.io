# Pre-registration (Session 38): does AliasScreen hold on a new set of proposals?

Written 2026-10-04 BEFORE any of the new proposals is graded. Session 32 built `AliasScreen` after reading all 637 grades (in-sample: Contained 90 right
and 0 rejects, NoOverlap 0 right and 463 rejects, Partial 33 right and 51 rejects) and said the real test is the next new council's proposals. Nine councils
were added in Session 38 (Wakefield, Coventry, Durham, Kirklees, Leicester, Cornwall, Nottingham, Wirral, Newcastle), and `aliascrossref` produced 543
ungraded proposals at NEAREST score >= 0.40 (Contained 116, Partial 427, counted by `aliastiers`, which reads no grade) and screened out 633 as NoOverlap.

Claims under test, each with the result that would refute it:
1. Contained needs no person. Refuted if at least one of the 116 Contained proposals is a REJECT when I grade it by reading the names (I grade all 116).
2. NoOverlap loses nothing right. I grade a seeded sample of 200 of the 633 (seed 20263811, `scratch/alias/screened_sample.csv`). Refuted if at least one is
   right or probable. Zero of 200 would put the 95% upper limit of the miss rate at 1.5% (rule of three); it would not prove zero.
3. Partial is the person's pile: no claim, the counts are reported.
Baseline for comparison: the NEAREST score alone has AUC 0.930 on the old grades and no threshold separates right from wrong.
Grading is by reading the supplier name against the entity (a council or a company), as in Sessions 24 to 32: "right" is the same body, "probable" a
plausible variant or a school or department of it that the entity would pay, "reject" a different body. The proposals graded here are added to
`export/supplier_alias_grades.csv`, so `aliascrossref` reports 0 ungraded afterwards.
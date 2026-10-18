# Showroom CLAUDE.md overflow (moved 2026-10-17)

Detail moved out of `Showroom/CLAUDE.md` to meet the charter size budget. Current-state essentials stayed in
CLAUDE.md; this file holds the longer descriptions and measured numbers. Grep here before re-deriving.

## Dashboard (full description)
`/dashboard` (2026-10-15, UNSEEN IN A BROWSER: built + `tools/DashboardProof` passed, never run live): cross-filter dashboard over an
in-browser HoloDb `orders` table (size picker 100k..5M, default 1M; the 2M/5M ceiling is UNMEASURED). `Services/Dashboard/*.cs` is the pure core (namespace
`OrdersDemo`): `OrdersGenerator` (integer-only splitmix64, chunk-independent; also EMBEDDED and shown as the file a console app copies), `DashboardSql` (schema, plane
DDL, `Plan(filter)` = the ONLY SQL), `DashboardCode` (build listing + the console `MinimalProgram` rendered from those constants). `DashboardEngine` (`Database.Open()`,
chunked `BulkLoad`, `CREATE INDEX .. USING SUMMARY WITH (max_joint_bits = 16)`, `PleromaMode.Always`, `Database.RouteTrace`), `DashboardSession` (two-stage refresh:
plane panels paint before engine scans), `LatencyLog`. Components have NO css of their own: `Pages/Dashboard.razor.css` styles them with `.dash ::deep`. Each chart omits
its own dimension from its WHERE (cross-filter). AVG is declined by the plane (sum/count of one exact answer); top customers / largest orders are engine scans, tagged
`engine`. `Compare` re-runs with `PleromaMode.Off` and checks identical answers.

## Helix (full description)
`/helix` (2026-10-15, UNSEEN IN A BROWSER, built, `tools/HelixProof` ALL PASS, components smoke-rendered with `HtmlRenderer`): HoloGenome 0.2.0 graded on the visitor's
own sequences. `Services/Helix/*.cs` (namespace `HelixDemo`): `HelixAnalysis` (EMBEDDED, shown whole: `Parse` FASTA or `SEQ,value` lines; `Track`; `Plan` = lazy
`Stage`s whose `Run()` does the work, so the page paints "running" first and honours Cancel BETWEEN stages: helical at Olson/7/15 bases per turn, dinucleotide-shuffle
null, free-period `PhasorPeriodModel.Fit` on a train slice, held-out grade, shuffled-fit control), `HelixDemoData` (EMBEDDED; synthetic planted 10.4-period set,
splitmix64, small sets are prefixes of big ones; the page says it is synthetic), `HelixCode` (`Region(name)` cuts the `// >>> name` / `// <<< name` markers of the
embedded source = the snippet beside each result; `MinimalProgram` renders the console app). `HelixSession` sequences stages; `HelixSvg` draws the track and the
r/interval chart (MarkupString); components `Hx*` (CSS in `Pages/Helix.razor.css`, `.hx-panel` is in depth.css's mid tier). Caps: 5,000 rows, 1,000 bases, 2M chars;
fit rows 500..4,000 (<= 200,000 bases), 5,000 iterations (package default; the free period moves <= `PeriodRate` 5e-5 rad per iteration, so ~1,500 iterations cannot get
12 -> 10.5). Measured natively (600 demo rows): controls 7/15 bases r 0.10/0.155, order null 0.125; headline 0.637; permuted measurements r sd 0.043 = 1/sqrt(n-1).

## Council scanner detail
- Data layout (`website-data/council-web`, ~525 MB, ~6,500 files, largest 1.2 MB; Pages cap 1 GB): `index.csv`, per-council `months.csv` + `years.csv`,
  `<slug>/<YYYY-MM>.exceptions.csv.gz`, `<slug>/fy-<YYYY-YY>.exceptions[.N].csv.gz`, month TRANSACTION slices `<slug>/<YYYY-MM>[.N].csv.gz` (only for
  "See the source rows"), `cross/*.csv.gz` (incl. `check_rules`), `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`. Delete a file the run no longer writes.
- Measured (headless Edge, CPU 6x, 4 Mbps, 100 ms RTT): `scripts/council-load-perf.mjs <dist> 6 4096 100 <data> [big|select|cancel|care|new]`, `council-perf.mjs`,
  `gap-perf.mjs`. Compare only interleaved passes (machine drifts 1.5-2 s an hour). Baseline 2026-10-08: Wokingham 2024-25 Load 2.2-2.5 s, Reading 2022-23 1.0-1.5 s,
  York 2015-16 (68,771 lines) 4.5-5.0 s.
- Audit rules detail (2026-10-04): `TxText`/`CheckCannotRun`/`CheckNote`/`CheckCoverage` say where a check cannot or only partly runs; placeholder ids are read PER LINE
  (`CouncilTerms.IsPlaceholder`). Schedule A `Difference` = stated Gross minus Gross expected from the VAT types; the letter quotes facts and that gap, never a class
  or cause. Builder `Clean` drops first-person, working-note and jargon text and PRINTS `HELD LINES` for review.
- Code detail: `CouncilMonth` (`MonthScan`: byte-level, columns by header name), `CouncilGap` (K3 EXCLUDES employee pay), twins count GROUPS (`GroupId`) and sum
  `ExtraCopyValue`; N-squared runs (Wokingham): value is the STATED Gross once. Pattern readings (`RecurringBatchRate`, `CadenceCatchUp`) are not classes.
  Wokingham only: `Care` caption and `SocialCarePanel` (ALWAYS with its source; no individual named). Request tray (`FoiTrayPanel`): the letter states facts and asks
  "Please provide the records you hold for this item, and the reason for it"; caps 300 items / 100 per letter; tick/source buttons are PLAIN markup.

## Shell verification scripts
`scripts/shell-check.mjs <dist> <website-data>` (7 behaviours; ALL PASS 2026-10-15), `shell-metrics.mjs` (bytes/paint/click-to-working; `local` vs `live`), via
`scripts/pages-server.mjs`; full deploy tree `AboutUs/web/scripts/cutover-dry-run.mjs`. Chat/story timing: `chat-turns.mjs <dist> <website-data> 9 6`, `story-times.mjs`.

## Council scanner rules moved out of CLAUDE.md (2026-10-18, verbatim)
- **Audit rules (2026-10-04)**: no raw engine code is ever printed: `Services/CouncilCodes.cs` maps every code to words (unlisted = "Other (not
  described here)"); `CouncilWebBuilder` and `CouncilTermsCheck` FAIL (exit 1) on a code the table lacks. No "fault" label, no "published twice"/"lead"/"actual
  target" wording (twins = "identical lines under two transaction numbers"). A council paying itself (`IsSelf`) is left out of flows/debt/misfits/loan checks
  and counted. Say where a check cannot run (`CheckCannotRun`); the letter quotes facts, never a class or cause. Builder `Clean` PRINTS `HELD LINES` for review.
- **Regression**: `dotnet run -c Release --project CouncilTermsCheck -- <council-web> out.txt [slugs]`; diff against `CouncilTermsCheck/golden.txt` (regenerated
  2026-10-05; explain every diff) and read the builder's REWORDED/HELD BACK/HELD LINES. `BudgetTestPanel`'s stage prose numbers are still TYPED (frozen 49 of 52 / 96;
  Stage 2d 37 units, 26 below, 133 pooled): recompute from `budget_units*.csv` when a stage moves.
- **Load by financial year**: `LoadPicker` = tick-list of April-March years, ONE Load with progress + Cancel. `MonthScan` keeps raw bytes only for
  recent years (`RawBudget` 40 MB, LRU); an evicted year re-inflates (`EnsureRawAsync`) before a list opens.

## The five pre-merge pages (retired 2026-10-18; facts only, for history)
- Creature: own HoloFormer d=384 L=1 `MaxCtx=32` `MinShifts=8` (ShiftsFor(32,384) floors to 1), K from `oracle-stackk.txt`, vocab = 400 cells + 8 moves, eight-way moves, ε-greedy forage,
  advantage-weighted `RefinementLoop.Observe` per step on a channel-fed background trainer, optional per-pass inspector (`Inspector.Capture`). Forecaster: own HoloFormer d=128 L=1
  `CandleContext=128` (ctx 256) vocab 17 (8 time buckets + 9 return buckets, `RetEdges`), lr 0.005, predict/countdown/reveal animation, optional Finnhub top-up when
  `data/finnhub-key.txt` existed (research in `forecaster-live-data-research.md`; the merged tab has NO live data). Both trained a model from scratch in the tab; neither is in the code any more.
- Prism, Stories and Cartographer pages became `PrismChatPanel`, `PrismStoriesPanel`, `PrismMapPanel` (code moved, comments condensed; behaviour and constants unchanged).

## Dashboard gotchas (moved 2026-10-18)
Type `Stmt` clashes with `HoloDb.Stmt` (ours is `DashQuery`); `QueryResult.GetLong` only on a columnar projection; sibling refreshes go through `DashboardSession.Repaint`. Never write "N lines" by hand (`Plural`/`NumLines`). Chat/Stories: `AsciiPunctuation.Fold` runs at every tokenizer entry.

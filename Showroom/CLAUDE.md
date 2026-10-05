# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-05 (r3,970 window fix; council scanner DATA-DRIVEN, 28 councils; virtual-customer sessions 52-54 built in; Bradford back, builder checklist-gated)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public site
(`AboutUs` repo, base href `/tools/`). Every tool runs entirely client-side: no server, no upload. Charter:
`MonoRepo\.claude\AGENT-CHARTER.md` (applies in full, incl. the §3 ~250-line budget). This repo only ever
*consumes* MonoRepo packages via published NuGet, never source.

**Purpose**: each tool is a real, working demo of a published `EvaluatedApplications.*` package's
capability, driven live by the visitor, no smoke and mirrors. Eight tools: **The Analyst** (HoloDb),
**The Creature** (AlgFormer/HoloFormer + Tracer), **The Forecaster** (AlgFormer/HoloFormer),
**Prism** (AlgFormer/HoloFormer, trained-checkpoint chat REPL), **Nano Stories** (same checkpoint,
asked to write), **The Cartographer** (same checkpoint, a 2D map of the path one next-token decision
takes), **Prose** (HoloDb + AlgFormer, grammar-mining corpus generator), **Council Spending Scanner**
(a real analytical tool over England's council spending transparency data, built on the EA
virtual-customer's engine export). Plus one **unlisted** client-preview page.

## Site plumbing
`Program.cs`: standard WASM host; one scoped `HttpClient`; `AddSingleton<HoloKernel.SessionHost>()`
(shared model cache). Nav (`MainLayout.razor`) is
`Home · Tools · NuGet` (lean by design): a new tool needs a `Home.razor` gallery card (`<a class="card tool"
href="/tools/<slug>" style="--cat:...">`, tag, `.ver` pill, `.desc`, `.go-in`), **not** a nav entry, and NO count
of anything typed into its text.
`wwwroot/index.html`: `<base href="/tools/" />`; links `/assets/site.css` (shared design system) + `boot.css`/
`depth.css`; SPA deep-link restore; JS interop `window.analystDownload`, `window.copyText`.
**CSS**: each tool has its own `Pages/<Tool>.razor.css` duplicating a base block (`.room`/`.crumb`/`.lede`/
`.hint`/`.err`/`.go`/`.outro`); copy from `Prism.razor.css`.
**Parallax glow** (`depth.css`): 3 scroll tiers, zero JS, tinted per tool via `[data-cat]` on the outer
`.room`; a new tool's panel class must join the mid-tier selector list and must not carry its own `opacity`.

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 2.16.0 + the `Prism`
package (a `ProjectReference` here is the designed path, not a MonoRepo boundary break). Surface:
- `ModelSpec` — shape + the **S>1 invariant enforced by construction**.
- `HoloSession.Create(spec)` / `.FromCheckpoint(bytes, kPass, serveAlpha)`: **K and alpha are
  mandatory args** (`Iters`/`IterAlphaServe` are NOT persisted by `Serialize()`; a deserialized
  checkpoint reads back `1`/`1`). `.Model` exposes the raw `HoloFormer` (Cartographer uses it directly).
  `.Logits(ctx)` full recompute; `.NewServeCache()`/`.Prime()`/`.StepToken()` O(1)/token KV-cache path,
  bit-identical to `.Logits` only for a MULTI-layer model (Prism/Stories); NOT verified for L=1.
- `PrismCheckpoint` — `SessionKey` (`"prism"`), `ResolveK`, `ReconstructAlpha`: every tool serving
  Prism's checkpoint (Prism, Stories, Cartographer, Analyst, Prose) uses these IDENTICALLY.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool; **ephemeral** (reload
  drops everything). `RefinementLoop.Observe`/`.ObserveSequence` — the live-training loop
  (`NewGrads()`->`IterAccumulate`/`StackIterAccumulateAllPos`->`Step`), Creature/Forecaster only.
- `Decoding` is the **`Prism` package's** `Prism.Inference`: `@using global::Prism.Inference`.
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)`: the ONE choke point for checkpoint loads;
  `CheckpointF32.Unpack` ("PF32") is a **cross-repo byte-exact contract with PrismStudio**: never change the
  wire format here alone. `TokenVoice` (never quantise to a musical scale); `DegenerateTail` trims a loop.
- **Browser contract**: visitors **train**, never **reshape** (`GrowLayers`/`GrowShifts` are
  PrismStudio-only; a better model ships as a new checkpoint).

**Prism/Stories constants** (re-measure on every re-mint): `MaxReplyStepsConst = 160` /
`MaxStorySteps = 448` are PINNED, not `Context/2` (r3,970: ctx=512 K=3, ~1.15 chars/token). **THE WINDOW
RULE: prompt + reply must stay under `Context`, or
every step past it re-Primes the whole window** (~2.9 s PER STEP at 512; a measured 163.5 s chat turn). So
Prism builds history with budget `Context - MaxReplySteps` (352), Stories clamps the prompt to `Context -
MaxStorySteps` (64 tokens, tail kept). `Prism.razor` context is the tagged wire format (`user: Q\nprism: A`
+ STOP per turn); Stories is a plain continuation. `AsciiPunctuation.Fold` runs at every tokenizer entry.
Check: `node scripts/chat-turns.mjs <dist> <website-data> 9 6` (flags any turn over 6 s).

**Checkpoint refresh** (data-only, no publish): write `prism/oracle-brain.bin.gz` (`GZipStream`) +
`oracle-vocab/rounds/stackk/iterwarm.txt` (UTF-8 no BOM) into `C:\Users\dongy\website-data\prism\`,
commit, push website-data only. Verify: `.../website-data/prism/oracle-rounds.txt`.

## Data and runtime live in the website-data repo (2026-10-04)
`C:\Users\dongy\website-data` (GitHub `EvaluatedApplications/website-data`, Pages on main, served at
`/website-data/` on the SAME origin: no CORS; `.gitattributes` `* -text`, byte-exact) holds
`council-web/`, `prism/` and `_framework/` (the Blazor runtime). The site keeps `dist/` (0.7 MB),
`wwwroot/data/forecaster-history.json` (+ refresh workflow), `content.wal`.
- **ONE setting**: `window.EA_DATA_BASE` at the top of `wwwroot/index.html` (`/website-data/`;
  `website-data/` on `localhost`) -> `Services/DataUrl.cs`; every fetch is `DataUrl.For("prism/...")`/
  `("council-web/...")`. Never write a literal `data/...` URL for these files.
- **Local dev**: `scripts/link-data.ps1` once (junction `wwwroot/website-data`, gitignored). **Runtime
  split**: `loadBootResource` returns `EA_FRAMEWORK_BASE + file`; `blazor.webassembly.*.js` stays in the site.
- **Source change = `scripts/publish-site.ps1`** (publish, rebuild `dist/`, copy the runtime to
  `website-data/_framework`). PUSH ORDER: website-data first, then the site; re-run with `-Prune`
  only after the site push is live (it deletes old runtime files the live site still boots from).
- **Check a build** (published `dist`): `scripts/boot-check.mjs <dist> <website-data>`, `chat-turns.mjs`,
  `tool-check.mjs`, `council-*perf.mjs`, `rules-check.mjs`.

## The tools (one paragraph each)
- **Analyst** `/analyst`: in-browser data profiler + SQL REPL over **HoloDb** (`Database.Open(null)`).
  Sniffs CSV/TSV/JSON/JSONL/text, infers types, bulk-loads, profiles, charts, free SQL + CSV export.
  Caps 500k rows / 2M-char entity scan / 64MB. **Novelty scan**
  (opt-in): a text column's surprisal against Prism's checkpoint via `SessionHost` key `"prism"`.
- **Creature** `/creature`: 20x20 grid the visitor draws; a **HoloFormer** learns to forage live.
  `Dim=384, Layers=1`, KPass from `oracle-stackk.txt`, `MaxCtx=32`, `MinShifts=8`. **Tracer**
  `GridTactics.Reachable` BFS = the distance field.
- **Forecaster** `/forecaster`: same substrate on a real hourly AAPL tape. `Dim=128, Layers=1`,
  `CandleContext=128` -> `MaxContext=256`, `Vocab=17`; `wwwroot/data/forecaster-history.json`
  (~3,484 candles); optional Finnhub top-up if `finnhub-key.txt` exists.
- **Prism** `/prism`: chat REPL over a point-in-time copy of the user's PrismStudio checkpoint;
  `ServeCache` O(1)/token, `Gate`/`DegenGuard` confidence-gated decoding, `Prime()` strips
  the trailing newline, `_turns` capped, audible tokens off by default.
- **Nano Stories** `/stories`: SAME weights (not a fine-tune), shared `"prism"` key and `TokenVoice`;
  one-shot continuation, Focused/Balanced/Wild presets, seeded `Gate.Pick`; states the real parameter count
  (~1.0M at r3,970). **Decode gate = TopP, not ResonanceSigma** (degenerates to top-1 here):
  `Floor = FloorMode.TopP` (Prism >= 1.3.0); `P` is checkpoint-specific, re-measure on every re-mint (r3,970:
  chat 0.43; Stories 0.43 / 0.77 / 0.89); `ConfidentThreshold=0.60`. Copy "plays its part three times" = K=3.
- **Cartographer** `/cartographer`: 2D view of ONE next-token decision on Prism's checkpoint (prompt capped
  `min(64, Context)`): the last position's face at every boundary (embed, each (layer,pass), FINAL) as a path,
  the first boundary whose greedy top-1 = the final answer marked; power-iteration PCA (captured variance always
  shown); top-4 attention per boundary; above 24 boundaries it thins lines and labels. **No training loop, ever**;
  `await Task.Yield()` keeps the tab responsive (no `MapAsync`).
- **Prose** `/prose`: paste/drop text; `ProseEngine.MineText` (chunks+yields ~200k chars) then
  recombines into sentences/Q&A/conversations. HoloDb `ProseStore` + AlgFormer plausibility (None /
  Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch), `data-cat=
  "holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines). Cap 64MB.

## Council Spending Scanner: `Pages/CouncilSpending.razor` (`/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`; SPEC_FOR_SHOWROOM.md
items 9-30). 28 councils (the count is computed), no HoloDb/engine in the browser. Public-audience,
mobile-first, not editorial: audit terms, plain prose + counts + GBP, OGL credit, never a cause.
- **A new council needs NO page edit** (Calderdale proved it). After the virtual-customer's `phoneexport <slug>`
  (and every `export`): run the builder, commit + push website-data only, no site publish. The builder
  DISCOVERS councils: an `index.csv` row (first column = slug = folder) + the engine profile whose name matches
  (`NameMatchesSlug`: the name's words run together, or its initials: "rbwm"); order = the engine's, so a new
  council lands last; `ShortName` derived. A council is HELD (rows also dropped from `cross/*.csv`,
  `WithoutHeldCouncils`) when its profile `VerificationNote` starts "Profile text pending", it is in env
  `COUNCIL_HOLD`, or `VirtualCustomer\export\checklist_holds.csv` lists it with Effect `withhold` (the builder
  reads the file; same slugs as `CouncilAudit.Cli checklist holdenv`; empty on 2026-10-05).
  **CHECKLIST GATE**: the builder REFUSES (exit 2, writes nothing) unless `export/checklist_results.csv` exists, every cell is
  PASS or HELD, every shipped council has rows, and the file is newer than every file in `web_export` and `export` (so run
  `CouncilAudit.Cli checklist all` first, in `C:\Users\dongy\VirtualCustomer`). No override, on purpose.
- **`index.csv`** = the export's columns + `Name,Short`: the page's council list, order, count and names
  (`CouncilWebData.Councils`, filled by `IndexAsync`; nothing draws before `_catalogReady`; `CouncilTerms.Words(n)`
  writes the count in prose). `profiles.json` also carries `short` and `facts`; Home's card has no count.
- **Facts** the builder MEASURES (byte pass over every slice + exception file; a `profiles` run reuses them): `noA`
  (no A row, Gross = Net, no VAT), `noD`, `noNumber`, `numberedFirst/Last/Months`, `counterIds` (per-file counter),
  `dByNumbering`, `dYears`, `noBudget` (every `budget_units` note "no published Revenue Outturn", or no unit),
  `redacted`/`pooled`. `CouncilFacts` (set when `ProfileAsync` reads profiles.json) and `CouncilTerms` build
  every statement from them. Say plainly where a check cannot run, never an empty list.
- **`Services/CouncilOverrides.cs`**: ONLY prose that cannot be derived: Hertfordshire (amount column, threshold),
  Stockport (A text, date-only and redaction lines, year notes), York (numbering notes, row counts, redaction
  range), Durham (`DByNumbering`), `NoPublishedNumber` (Sheffield, Birmingham, West Berkshire: the scanner added
  a row number). `ListForEveryCouncil = true`: the "What cannot be checked" list is on EVERY council page.
- **Audit rules (2026-10-04, an independent auditor)**: no raw engine code is ever printed:
  `Services/CouncilCodes.cs` maps classification (incl. `SmallGap`), reading, debt-sink flag, label check, loan status and
  debt-ledger payee class (`OwnCouncil`...) to words (unlisted = "Other (not described here)"), and BOTH `CouncilWebBuilder` (full run) and
  `CouncilTermsCheck` FAIL (exit 1) on a code the table lacks: add it there. No "fault" label, no
  "published twice"/"lead"/"actual target" wording (twins = "identical lines under two transaction numbers"). A council paying itself (`IsSelf`, "Leeds CC") is left out of flows/debt sink/misfits/
  loan checks and counted. `TxText`: a council with no published number never has the scanner's row number
  called its transaction number; `CheckCannotRun`/`CheckNote`/`CheckCoverage` say where a number-based check
  cannot or only partly runs (within-transaction: not run for Wokingham, `WithinTxnNotRun`, mirrors the engine).
  A placeholder id ("(no number published) N") is read PER LINE (`CouncilTerms.IsPlaceholder`): councils that number most
  lines (Reading, RBWM, Nottingham, Leicester, Wakefield) never show it as a transaction number (`TxText`, `SourceBlocks`).
  Schedule A `Difference` = stated Gross minus the Gross expected from the lines' VAT types (engine S52); the letter
  quotes net, gross, transaction, date, payee and that gap, never a class, reading or cause. Builder `Clean` drops
  first-person, working-note and statistical-jargon text and PRINTS `HELD LINES` for review.
- **Regression**: `dotnet run -c Release --project CouncilTermsCheck -- <council-web> out.txt [slugs]`
  prints every council's list, year notes and A/D lines from the SHIPPED profiles + years.csv; diff against
  `CouncilTermsCheck/golden.txt` (regenerated 2026-10-05; explain every diff). Read the builder's REWORDED/HELD
  BACK/HELD LINES output and grep profiles.json for working-file words. `budget_units.csv` Pool `confirmatory` =
  the first test group; a council with units but none eligible gets the "cannot run" line (measured, never typed per
  council). The builder adds `FrozenEligible` (from `export/budget_units_<stage>.csv`): Wirral 2022-23 became
  eligible after Stage 2c froze, so `BudgetPanel` says it is outside the statistic. `BudgetTestPanel`'s stage prose
  numbers are still TYPED (frozen 49 of 52 / 96 vs current 50 of 53 / 97; Stage 2d 37 units, 26 below, 133 pooled):
  recompute from `budget_units*.csv` when a stage or unit moves. The Leeds same-line repeats are DERIVED
  (`LeedsRepeatsAsync`, from `cross/cross_file_repeats.csv`: 2022-23 26,402 rows, 2023-24 8,196). New cross files: the
  builder's `cross/` list + a row in `BudgetTestPanel`'s set list.
- **Load by financial year** (user's choice). `LoadPicker` = tick-list of financial years (April-March; "No
  readable date" last), size line before any fetch, "Choose months" per year, ONE Load with progress + Cancel.
  `YearBlock` per loaded unit: totals, `GapBars`, schedule/bucket lists. `MonthScan` keeps raw bytes only for
  recent years (`RawBudget` 40 MB, LRU); an evicted year re-inflates (`EnsureRawAsync`, `NeedRaw`) before a list opens.
- **Data** (`website-data/council-web`, ~525 MB, 6,5xx files, largest 1.2 MB; Pages cap 1 GB; `AboutUs\CouncilWebBuilder`,
  `dotnet run -c Release --project CouncilWebBuilder`, ~3 min; 2nd arg `profiles` rewrites only profiles.json.gz;
  fails above 50 MB/file): `index.csv`, per-council `months.csv` + `years.csv`, **exception files in ONE slim format** `<slug>/<YYYY-MM>.exceptions.csv.gz`
  and `<slug>/fy-<YYYY-YY>.exceptions[.N].csv.gz` (parts of whole months above 12 MB raw): `@2022-11` marker
  line, a header, slim rows (TransactionGross empty when = Net; Detail + ExplainedMeaning on a group's first line;
  `Gross` kept on A and NSquaredRows rows; `Care` "1" on a Wokingham care B group). Month TRANSACTION slices
  `<slug>/<YYYY-MM>[.N].csv.gz` are fetched only by "See the source rows"; also
  `cross/*.csv.gz` (incl. `check_rules`: the engine's registry of every row filter), `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`.
  Profile text is cleaned in the BUILDER. Delete a file the run no longer writes (Merton's `undated` slices, 2026-10-05).
- **Code**: `Services/CouncilWebData` (fetch, `InflateAsync`, caches), `CouncilLoad`, `CouncilMonth`
  (`MonthScan`: byte-level, columns by header name), `CouncilSource`, `CouncilChecksWeb` (plain loops:
  LINQ over decimals is slow in WASM), `FoiTray`, `CouncilGap` (K3 EXCLUDES employee pay);
  `Components/LoadPicker|YearBlock|GapBars|GapPanel|CheckPanel|BudgetPanel|BudgetTestPanel|FoiTrayPanel|
  SourceBlocks|SocialCarePanel|RulesDisclosure`. Each load logs `CW-PERF` lines. **One rules disclosure per check**
  (`RulesDisclosure`, from `cross/check_rules.csv`): `CheckDef.RuleChecks` names the registry's checks; YearBlock shows
  Schedule A/B/D; BudgetPanel/GapPanel `BudgetComparison`; the council page `RawReconciliation`. Twins: the summary counts
  GROUPS (`GroupId`) and sums `ExtraCopyValue` (signed and absolute), and `CheckDef.RuleOf` shows the row-level RuleText
  (the file's `Reading` column) beside it; its `Plain` states every condition. `CheckDef.SideFile` lists
  `cross/same_number_two_files.csv` (one number in two files: not paired, not a cause) under the twins list. Debt sink shows `UndatedRows`/`UndatedNet`. **N-squared runs** (Wokingham): class
  `NSquaredListing` / reading `NSquaredRows`; the value is the STATED Gross once (`MonthScan` `Value =
  max`), never the inflated row total. `Plural`/`NumLines`: never write "N lines" by hand. Pattern
  readings (`RecurringBatchRate`, `CadenceCatchUp`) are NOT classes: `ExplainedBy` + `ExplainedMeaning`.
- **Wokingham only**: `Care` caption on B groups, and `SocialCarePanel` (closed until tapped; 4 files in
  `wokingham/socialcare/`): HHI/top-10, who is paid by year with a Companies House record (ALWAYS with its
  source; link hidden when uncertain), nursing/residential weekly medians only. No individual named.
- **Request tray** (`FoiTrayPanel`, one letter per council): the letter states facts and asks "Please
  provide the records you hold for this item, and the reason for it." No class meanings/readings/`Detail`.
  Address = the profile's verified `foi` else a placeholder. Caps 300 items / 100 per letter. Tick/source
  buttons are PLAIN markup (a component per row cost ~5 ms). **Source rows**: that month's slice, cap 80 ids, 200 lines.
- **Measured** (headless Edge, CPU 6x, 4 Mbps, 100 ms RTT): `scripts/council-load-perf.mjs <dist> 6 4096
  100 <data> [big|select|cancel|care|new]` (`ONLY=` limits `big` and `new`; `CASES=wokingham:2024-25,
  reading:2022-23` runs exactly those council-years: the biggest years move with the data, check
  `years.csv`), `council-perf.mjs`, `gap-perf.mjs`; `rules-check.mjs <dist>` prints the rules/twins/A/debt/budget
  text (a content check, no timings). Compare only
  interleaved passes of two builds (the machine drifts 1.5-2 s an hour). Load wall / worst stall (2026-10-05):
  Wokingham 2024-25 1.8-2.4 s / 134-190 ms; Reading 2022-23 1.1 s / 0-60 ms; York 2015-16 (68,771 lines) 4.5-5.0 s;
  Essex 7.5 s. Hub twins open 1.6-2.0 s / 480-540 ms. Page open 0.5-2.1 s; list 27-300 ms.
- Gotchas: `--` in csproj XML comments breaks load; BudgetPanel's `<text>` trick fails in code blocks; CSS
  is one scoped file using `.cs ::deep`; lists with stateful children need `@key`.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package demo, NOT in the gallery: a private, link-only client preview (`recycledao-owner`'s repo,
never edit it from here), `noindex,nofollow`. Mint invariant: only `MintForApproval` increases `_totalMinted`.

## Dependencies (exact NuGet versions, `Showroom.csproj`, re-verified 2026-10-03)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.11**: **must equal the installed WASM runtime pack**
  (a skew caused the 2026-09-06 outage: silent death at boot); check `dotnet --list-runtimes`.
- `EvaluatedApplications.AlgFormer` **2.20.1** (same pin in `HoloKernel.csproj`); `SubwordVocab.MaxLen`
  must be >=16 to load a freshly-minted checkpoint. `EvaluatedApplications.Prism` **1.3.3** via
  `HoloKernel.csproj` (`FloorMode.TopP`/`DecodePolicy.P` need >=1.3.0).
- `EvaluatedApplications.HoloDb` **2.1.0** (Analyst, Prose; first with a working `score` and graded `NEAREST`);
  `Tracer` **2.0.0** (Creature); `Prose` **1.3.2**. A bump ripples to every tool in this one `.csproj`
  (NU1605 otherwise): re-`dotnet build` the whole app; versions only via `dotnet add package`.
  `ProjectReference ..\HoloKernel\HoloKernel.csproj` serves every tool but the council scanner.
- `PublishTrimmed=true` + `RunAOTCompilation=true`: **AOT is load-bearing** (the interpreter can't run IL in
  `DecodePolicy`'s static ctor). `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker);
  `WasmEnableThreads=false` (threading regressed a laptop's boot).

## Boundary (hard, from the agent charter)
**NuGet only, never MonoRepo `ProjectReference`** (`HoloKernel`, a sibling NuGet-only RCL, is the one
exception). Never touch `AboutUs/site/*`, nav or the shared design system (`website-owner`'s). Never launch
the app / open a browser for a demo: build-verify only. Checkpoint hand-off is `prismstudio-owner`'s call.

## Standing facts, build
**Shifts must be > 1, always** (S=1 is a pure diagonal); re-derive a floor from `bindRank = shifts·d/2`
per tool. `golden: true` on every `HoloFormer`. No filesystem in WASM; single-threaded: small
live-training shapes, yield cooperatively. `HoloShape` statics `ShiftsFor/BindRank/CleanCapacity`.
Build: `dotnet build Showroom.csproj -c Release` (0/0); `scripts/publish-site.ps1` publishes (deploy
hard-couples to `dist/`, see `AboutUs\CLAUDE.md`). No test project: build + review + `CouncilTermsCheck`;
live behaviour is the user's (`dotnet run`, or the deployed `/tools/`).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O with ABSOLUTE paths (a relative path
  resolves against the shell's cwd), never `Get-Content`/`Set-Content` on source (mojibake).
- `Home.razor` card hrefs are ABSOLUTE (`/tools/<slug>`). `PrismFormer` (not `AlgFormer`) is the namespace of
  `HoloFormer`/`HoloShape`/`SubwordVocab`.
- A `.razor` page whose class name COLLIDES with a package namespace (`Showroom.Pages.Prism`, `.Prose`) needs
  `@using global::<Namespace>` in every page referencing `Prism.*`/`Prose.*` (Prism, Stories, Prose, Analyst).
- Razor reserves the bare `<text>` tag (cannot carry attributes, `RZ1023`): build an SVG `<text>` as a
  `MarkupString`, HTML-encoding interpolated content by hand.

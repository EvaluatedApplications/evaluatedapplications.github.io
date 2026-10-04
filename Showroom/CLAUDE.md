# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-04 (r3,970 window fix; council scanner now DATA-DRIVEN: 27 councils incl. Calderdale, a new council needs no page edit)

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
(shared model cache); `AddSingleton<ContentDbHost>()` (unrelated spike). Nav (`MainLayout.razor`) is
`Home · Tools · NuGet` (lean by design): a new tool needs a `Home.razor` gallery card
(`<a class="card tool" href="/tools/<slug>" style="--cat:...">`, live/soon tag, package `.ver` pill,
`.desc`, `.go-in`), **not** a nav entry, and NO count of anything typed into its text.
`wwwroot/index.html`: `<base href="/tools/" />`; links `/assets/site.css` (shared design system) + `boot.css`/
`depth.css`; SPA deep-link restore; JS interop `window.analystDownload`, `window.copyText`.
**CSS**: each tool has its own `Pages/<Tool>.razor.css` (isolation can't share a partial) duplicating a base
block (`.room`/`.crumb`/`.lede`/`.hint`/`.err`/`.go`/`.outro`); copy from `Prism.razor.css`.
**Parallax glow** (`depth.css`): 3 scroll-driven tiers, zero JS, tinted per tool via `[data-cat]` on the outer
`.room` (a package name or a chord such as `"algformer-tracer"`); a new tool's panel class must join the
mid-tier selector list and must not carry its own `opacity` (the "OPACITY-MULTIPLIER TRAP" comment).

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 2.16.0 + the `Prism`
package (a `ProjectReference` here is the designed path, not a MonoRepo boundary break). Surface:
- `ModelSpec` — shape + the **S>1 invariant enforced by construction**.
- `HoloSession.Create(spec)` / `.FromCheckpoint(bytes, kPass, serveAlpha)`: **K and alpha are
  mandatory args** (`Iters`/`IterAlphaServe` are NOT persisted by `Serialize()`; a deserialized
  checkpoint reads back `1`/`1`). `.Model` exposes the raw `HoloFormer` (`InspectStackIterFaces`/
  `InspectAttention`/`DecodeFace`/`EmbRow`, used directly by The Cartographer). `.Logits(ctx)` full
  recompute; `.NewServeCache()`/`.Prime()`/`.StepToken()` O(1)/token KV-cache path, bit-identical to
  `.Logits` only for a MULTI-layer model (Prism/Stories); NOT verified for L=1 (Creature/Forecaster).
- `PrismCheckpoint` — `SessionKey` (`"prism"`), `ResolveK`, `ReconstructAlpha`: every tool serving
  Prism's checkpoint (Prism, Stories, Cartographer, Analyst, Prose) uses these IDENTICALLY.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool; **ephemeral** (reload
  drops everything). `RefinementLoop.Observe`/`.ObserveSequence` — the live-training loop
  (`NewGrads()`->`IterAccumulate`/`StackIterAccumulateAllPos`->`Step`), Creature/Forecaster only.
- `Decoding` is the **`Prism` package's** `Prism.Inference`: `@using global::Prism.Inference`.
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)`: the ONE choke point for checkpoint
  loads (Pages serves the hand-shipped `.gz` uncompressed); `CheckpointF32.Unpack` ("PF32" fp32 to
  f64) is a **cross-repo byte-exact contract with PrismStudio**: never change the wire format here alone.
- `TokenVoice`/`TokenVoiceControls.razor`: per-token voice (never quantise to a musical scale);
  `DegenerateTail.Start/.SafePrefix` trims a repeating tail (Prism, Stories).
- **Browser contract**: visitors **train**, never **reshape** (`GrowLayers`/`GrowShifts` are
  PrismStudio-only; a better model ships as a new checkpoint).

**Prism/Stories constants** (re-measure on every re-mint): `MaxReplyStepsConst = 160` /
`MaxStorySteps = 448` are PINNED, not `Context/2` (r3,970: V=320 d=64 L=33 S=64 ctx=512 K=3, ~1.15
chars/token; STOP in ~45% of replies). **THE WINDOW RULE: prompt + reply must stay under `Context`, or
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
  split**: `index.html`'s `loadBootResource` returns `EA_FRAMEWORK_BASE + file` for dotnetjs/wasm/
  assemblies/globalization; `blazor.webassembly.*.js` stays in the site.
- **Source change = `scripts/publish-site.ps1`** (publish, rebuild `dist/`, copy the runtime to
  `website-data/_framework`). PUSH ORDER: website-data first, then the site; re-run with `-Prune`
  only after the site push is live (it deletes old runtime files the live site still boots from).
- **Check a build**: `node scripts/boot-check.mjs <dist> <website-data>`, `chat-turns.mjs`,
  `tool-check.mjs` (Stories + Cartographer), `council-perf.mjs <dist> 6 4096 100`,
  `council-load-perf.mjs` (below).

## The tools (one paragraph each)
- **Analyst** `/analyst`: in-browser data profiler + SQL REPL over **HoloDb** (`Database.Open(null)`).
  Sniffs CSV/TSV/JSON/JSONL/text, infers types, bulk-loads (100k-row chunks), profiles, entities,
  chart builder, free SQL + CSV export. Caps 500k rows / 2M-char entity scan / 64MB. **Novelty scan**
  (opt-in): a text column's surprisal against Prism's checkpoint via `SessionHost` key `"prism"`.
- **Creature** `/creature`: 20x20 grid the visitor draws; a **HoloFormer** learns to forage live.
  `Dim=384, Layers=1`, KPass from `oracle-stackk.txt`, `MaxCtx=32`, `MinShifts=8`. **Tracer**
  `GridTactics.Reachable` BFS = the distance field; a bounded `DropOldest` channel keeps simulation off
  training. `ResetBrain` drops the session.
- **Forecaster** `/forecaster`: same substrate on a real hourly AAPL tape. `Dim=128, Layers=1`,
  `CandleContext=128` -> `MaxContext=256`, `Vocab=17`; `wwwroot/data/forecaster-history.json`
  (~3,484 candles); optional Finnhub top-up if `finnhub-key.txt` exists and NYSE is open.
- **Prism** `/prism`: chat REPL over a point-in-time copy of the user's PrismStudio checkpoint;
  `ServeCache` O(1)/token stepping, `Gate`/`DegenGuard` confidence-gated decoding, `Prime()` strips
  the trailing newline, `_turns` capped + render-batched, audible tokens off by default.
- **Nano Stories** `/stories`: SAME weights (not a fine-tune), shared `"prism"` key and `TokenVoice`;
  one-shot continuation, Focused/Balanced/Wild presets, seeded `Gate.Pick`; states the real
  parameter count (~1.0M at r3,970). **Decode gate = TopP, not ResonanceSigma** (it degenerates to
  top-1 here so Temperature would be inert): `Floor = FloorMode.TopP` (Prism >= 1.3.0); `P` is
  checkpoint-specific, re-measure on every re-mint (r3,970 by the cumulative-mass rule: chat 0.43;
  Stories 0.43 / 0.77 / 0.89; never an untested 0.9); `ConfidentThreshold=0.60`. Copy "plays its part
  three times" = K=3; update it if K changes.
- **Cartographer** `/cartographer`: 2D view of ONE next-token decision on Prism's checkpoint (prompt capped
  `min(64, Context)`): the last position's face at every boundary (embed, each (layer,pass), FINAL) as a
  path, the first boundary whose greedy top-1 = the final answer marked; power-iteration PCA (captured
  variance always shown); top-4 attention per boundary; above 24 boundaries (r3,970: 100) it thins lines and
  labels. **No training loop, ever**; `await Task.Yield()` keeps the tab responsive (no `MapAsync`).
- **Prose** `/prose`: paste/drop text; `ProseEngine.MineText` (chunks+yields ~200k chars) then
  recombines into sentences/Q&A/conversations. HoloDb `ProseStore` + AlgFormer plausibility (None /
  Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch), `data-cat=
  "holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines). Cap 64MB.

## Council Spending Scanner: `Pages/CouncilSpending.razor` (`/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`; SPEC_FOR_SHOWROOM.md
items 9-30). 27 councils (the count is computed), no HoloDb/engine in the browser. Public-audience,
mobile-first, not editorial: audit terms, plain prose + counts + GBP, OGL credit, never a cause.
- **A new council needs NO page edit** (2026-10-04, Calderdale was the proof). After the
  virtual-customer's `phoneexport <slug>` (and every `export`): run the builder, commit + push
  website-data only, no site publish. The builder DISCOVERS councils: an `index.csv` row (first column
  = slug = folder) + the engine profile whose name matches it (`NameMatchesSlug`: the name's words run
  together, or its initials: "rbwm"); order = the engine's, so a new council lands last; `ShortName`
  derived. A council whose profile `VerificationNote` starts "Profile text pending" (or in env
  `COUNCIL_HOLD`) is HELD (Camden, 2026-10-04) and its rows are dropped from `cross/*.csv`
  (`WithoutHeldCouncils`): it ships by itself the run after its profile text is written.
- **`index.csv`** = the export's columns + `Name,Short`: the page's council list, order, count and names
  (`CouncilWebData.Councils/NameOf/ShortOf`, filled by `IndexAsync`; the page draws nothing until it has
  run, `_catalogReady`; `CouncilTerms.Words(n)` writes the count in prose). `profiles.json` also carries
  `short` and `facts`; Home's card has no count.
- **Facts** the builder MEASURES (a byte pass over every slice + the exception files; `profiles` reuses them): `noA` (no A row, Gross = Net and no VAT on every row), `noD` (no D
  row), `noNumber`, `numberedFirst/Last/Months` (numbers from one month on), `counterIds` (per-file
  counter: smallest trailing number 1 and nearly all different in >= 80% of months: Leeds, Birmingham,
  West Berkshire, Sheffield, Durham), `dByNumbering` (noD with real numbers), `dYears`, `noBudget` (every
  `budget_units` note "no published Revenue Outturn", or no unit), `redacted`/`pooled` (engine
  `IsRedactedSupplier`). Page: `CouncilFacts` (registered when `ProfileAsync` reads profiles.json),
  `CouncilTerms` (`IsNa`, `NotAvailable`, `CannotCheck`, `CheckCannotRun`, `YearNote`, `NumberingNote`)
  builds every statement from them. Say plainly where a check cannot run, never an empty list.
- **`Services/CouncilOverrides.cs`**: ONLY prose that cannot be derived: Hertfordshire (amount column,
  threshold caption), Stockport (A text, date-only and redaction lines, year notes), York (numbering
  notes, row counts, redaction range), Durham (`DByNumbering`: its ledger-reference counter reads like
  Leeds's, the page always worded it the other way). `CouncilTerms.ListForEveryCouncil = false`: the
  page-top "What cannot be checked" list is built only for `noBudget` councils (the five that always
  had one + Calderdale); the other 21 state the same limits per year. Flip it to list every council.
- **Regression**: `dotnet run -c Release --project CouncilTermsCheck -- <council-web> out.txt [slugs]`
  prints every council's list, year notes and A/D lines from the SHIPPED profiles + years.csv; diff
  against `CouncilTermsCheck/golden.txt` (913 lines for the 26 were byte-identical before/after the
  refactor). Profile prose needs no page code (flagged = quirk opening in capitals), but read the
  builder's REWORDED/HELD BACK output and grep profiles.json for working-file words (`prep`, "Prepare
  step" -> "the scanner" in `Reword`; `XlsxReader`/`XlsReader`/`FileDuplication` sentences dropped).
  `budget_units.csv` Pool `confirmatory` = the first test group; a council with units but none eligible
  gets the "cannot run, no government figure held here" line from `BudgetPanel`. New cross files go in
  the builder's `cross/` list and a row in `BudgetTestPanel`'s set list.
- **Load by financial year** (user: "I preferred it when I could choose what to load"). `LoadPicker` =
  tick-list of financial years (April-March; "No readable date" last), Select all/Clear, size line before
  any fetch, "Choose months" per year, ONE Load with progress + Cancel (keeps finished years).
  `YearBlock` per loaded unit: totals, `GapBars`, schedule/bucket lists; top: per-year totals +
  `GapSummary`. `MonthScan` keeps raw bytes only for recent years (`RawBudget` 40 MB, LRU); an evicted
  year re-inflates via `EnsureRawAsync` (0.04-0.85 s) before a list opens (`NeedRaw`).
- **Data** (`website-data/council-web`, ~515 MB, 6,336 files, largest 1.2 MB; Pages cap 1 GB;
  `AboutUs\CouncilWebBuilder`, `dotnet run -c Release --project CouncilWebBuilder`, ~3 min incl. the
  facts pass; arg 1 / `COUNCIL_WEB_OUT`, 2nd arg `profiles` rewrites only profiles.json.gz; a run only
  overwrites, so delete a stale file BY NAME; fails above 50 MB/file): `index.csv`, per-council
  `months.csv` + `years.csv`, **exception files in ONE slim format** `<slug>/<YYYY-MM>.exceptions.csv.gz`
  and `<slug>/fy-<YYYY-YY>.exceptions[.N].csv.gz` (year bundle, parts of whole months above 12 MB raw).
  Format: `@2022-11` marker line, a header, rows (Council/Year/SupplierKey dropped; TransactionGross empty
  when = Net; Detail + ExplainedMeaning on a group's first line; `Gross` kept on an A row where it differs
  from Net and on NSquaredRows lines; `Care` "1" on a Wokingham B group wholly in care cost centres).
  Month TRANSACTION slices `<slug>/<YYYY-MM>[.N].csv.gz` are fetched only by "See the source rows"; also
  `cross/*.csv.gz`, `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`. Profile text is cleaned in the BUILDER.
- **Code**: `Services/CouncilWebData` (fetch, `InflateAsync`, caches), `CouncilLoad`, `CouncilMonth`
  (`MonthScan`: byte-level, columns by header name), `CouncilSource`, `CouncilChecksWeb` (plain loops:
  LINQ over decimals is slow in WASM), `FoiTray`, `CouncilGap` (K3 EXCLUDES employee pay);
  `Components/LoadPicker|YearBlock|GapBars|GapPanel|CheckPanel|BudgetPanel|BudgetTestPanel|FoiTrayPanel|
  SourceBlocks|SocialCarePanel`. Each load logs `CW-PERF` lines. **N-squared runs** (Wokingham): class
  `NSquaredListing` / reading `NSquaredRows`; the value is the STATED Gross once (`MonthScan` `Value =
  max`), never the inflated row total. `Plural`/`NumLines`: never write "N lines" by hand. Pattern
  readings (`RecurringBatchRate`, `CadenceCatchUp`) are NOT classes: `ExplainedBy` + `ExplainedMeaning`.
- **Wokingham only**: `Care` caption on B groups, and `SocialCarePanel` (closed until tapped; 4 files in
  `wokingham/socialcare/`): HHI/top-10, who is paid by year with a Companies House record (ALWAYS with its
  source; link hidden when uncertain), nursing/residential weekly medians only. No individual named.
- **Request tray** (`FoiTrayPanel`, one letter per council): the letter states facts and asks "Please
  provide the records you hold for this item, and the reason for it." No class meanings/readings/`Detail`.
  Address = the profile's verified `foi` else a placeholder. Caps 300 items / 100 per letter. Tick/source
  buttons are PLAIN markup (a component per row cost ~5 ms). **Source rows**: the tap fetches that month's
  slice (all parts), finds the group's transaction numbers (cap 80), lists up to 200 lines.
- **Measured** (headless Edge, CPU 6x, 4 Mbps, 100 ms RTT): `scripts/council-load-perf.mjs <dist> 6 4096
  100 <data> [big|select|cancel|care|new]` (`ONLY=york,calderdale` limits `big` and `new`; `new` also
  prints the rendered cannot-check list), `council-perf.mjs`, `gap-perf.mjs`. Compare only interleaved
  passes of two builds (the machine drifts 1.5-2 s an hour). Load wall / worst stall: York 2015-16 (its biggest
  year, 576 KB, 68,771 lines) 4.5-5.0 s / 140-175 ms, York 2024-25 5.0 s / 317; Calderdale 2024-25 (140 KB,
  13,473 lines) 1.3 s / 0; Surrey 5.4 s / 145; Essex 7.5 s / 97; Hertfordshire 1.6 s / 0; Cornwall 5.4 s / 207;
  Sheffield 4.3 s / 63; Wokingham 2020-21 0.8 s / 57. Page open York 2.1 s (first open, 0.8 s stall) vs
  Calderdale 0.5 s; the data-driven build is within noise of the old one (2 interleaved passes). Select
  all (Leeds 12 yrs) 17.7 s / 131; open a list 27-300 ms; source rows 1.4-2.4 s.
- Gotchas: `--` in csproj XML comments breaks load; BudgetPanel's `<text>` trick fails in code blocks; CSS
  is one scoped file using `.cs ::deep`; lists with stateful children need `@key`.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo, NOT in the gallery: a private, link-only client preview
(`C:\Users\dongy\RecycleDAO`, `recycledao-owner`'s repo; never edit it from here). Absent from
`Home.razor`/nav, `noindex,nofollow`. Mint invariant: `MintForApproval` is the only method that
increases `_totalMinted`.

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
exception). Checkpoint hand-off is `prismstudio-owner`'s call; Forecaster's live top-up needs a Finnhub
key. Never touch `AboutUs/site/*`, nav or the shared design system (`website-owner`'s). Never launch the
app / open a browser for a demo: build-verify only (the headless-Edge scripts are measurement harnesses
run on a published build).

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
- A `.razor` page whose class name COLLIDES with a package's root namespace (`Showroom.Pages.Prism` vs the
  `Prism` package; `.Prose`) needs `@using global::<Namespace>` in every page referencing `Prism.*`/`Prose.*`
  (`Prism`, `Stories`, `Prose`, `Analyst`).
- Razor reserves the bare `<text>` tag (cannot carry attributes, `RZ1023`): build an SVG `<text>` as a
  `MarkupString`, HTML-encoding interpolated content by hand.

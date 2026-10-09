# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-09 (every /tools page is now a static HTML shell, runtime boots on intent: Blazier 1.0.0; Prism/Stories context-agnostic, ctx=64 checkpoint; council scanner DATA-DRIVEN, 28 councils, builder checklist-gated)

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
`Program.cs`: `builder.UseBlazier(b => b.App<App>())` (see Static shells), `HeadOutlet`, one scoped `HttpClient`,
`AddSingleton<HoloKernel.SessionHost>()` (shared model cache). Nav (`MainLayout.razor`) is
`Home · Tools · NuGet` (lean by design): a new tool needs a `Home.razor` gallery card (`<a class="card tool"
href="/tools/<slug>" style="--cat:...">`, tag, `.ver` pill, `.desc`, `.go-in`), **not** a nav entry, and NO count
of anything typed into its text.
`wwwroot/index.html`: `<base href="/tools/" />`; links `/assets/site.css` (shared design system) + `boot.css`/
`depth.css`; SPA deep-link restore; the start script; JS interop `window.analystDownload`, `window.copyText`.
**CSS**: each tool has its own `Pages/<Tool>.razor.css` duplicating a base block (`.room`/`.crumb`/`.lede`/
`.hint`/`.err`/`.go`/`.outro`); copy from `Prism.razor.css`.
**Parallax glow** (`depth.css`): 3 scroll tiers, zero JS, tinted per tool via `[data-cat]` on the outer
`.room`; a new tool's panel class must join the mid-tier selector list and must not carry its own `opacity`.

## Static shells (Blazier 1.0.0, 2026-10-08)
`EvaluatedApplications.Blazier` 1.0.0 + `<BlazierScaffold>true` in the csproj: the last step of `dotnet publish` renders every `[BlazierPage]`
with `HtmlRenderer` into real HTML (`dist/index.html` = gallery, `dist/<slug>/index.html`) using the published `index.html` as the template. A
visitor reads content at once; the runtime (AOT, trimmed, ~11 MB gzip) downloads only on intent. AOT and the one-SPA design are unchanged. No
lazy assemblies, so the BLZ001 guard has nothing to check.
- **Page recipe**: `@attribute [BlazierPage("<slug>", Title="<=PageTitle>", Layout=typeof(MainLayout), Static=true)]`, `@inject IBuildTime
  Build`, `if (Build.IsPrerender) return;` first in `OnInitialized[Async]`/`OnParametersSetAsync`, and the interactive or fetching region
  swapped for `<ToolStart Tool="..."/>` when `Build.IsPrerender` (header, lede, outro stay as real text). I/O at build time fails the publish
  (BLZ102); `Services/ShowroomPrerender.cs` (`IBlazierPrerenderSetup`) supplies `SessionHost`/`CouncilWebData`/`FoiTray` to that render. A NEW
  tool also needs an entry in `scripts/finish-shells.mjs` `PAGES` (meta description; publish fails without it) and a Home card. `Static=true`
  is deliberate: without it Blazier arms the whole page and a touch or scroll (`touchstart`) would download the runtime.
  `ToolStart.RuntimeSize` ("11 MB") is typed: re-measure when the runtime changes.
- **Intent** = the script at the end of `wwwroot/index.html` (the template of every shell): the `[data-blazier-go]` button ->
  `Blazier.mount('#app','app')` (the routed `App` mounts over the shell; boot terminal in `[data-boot-slot]`); or arrival from another page
  of this site (same-origin referrer, `AUTO_START_FROM_SITE`); or a bounced deep link (`window.__eaBounced`); or `dotnet run` (no
  `blazier-scaffold` meta). Reading, scrolling and links never start it; the gallery has no button (its cards are links). **Never write the
  quoted text `name="blazier-scaffold"` in index.html**: Blazier treats any file containing it as an already generated page.
- **Navigation**: after boot, in-app links are client-side and share the one `SessionHost` (Prism <-> Stories <-> Cartographer reuse the
  loaded model). A HARD navigation (typed URL, reload, a link followed before the app started) is a new page load: another shell, a fresh
  runtime, an empty SessionHost, checkpoint re-fetched (HTTP cache only). Pages 301s `/tools/x` to `/tools/x/`.
- **Deep links**: `/tools/council-spending/{slug}` (not pre-generated: data-driven list, one attribute per component) and unknown paths have
  no file: Pages 404 -> the site's `404.html` -> `/tools/?/path` -> gallery shell, whose restore script sets `__eaBounced` and the app boots
  at once. `finish-shells.mjs` adds description/canonical/Open Graph per shell (noindex for RecycleDAO).
- **Verify a build**: `scripts/shell-check.mjs <dist> <website-data>` (8 behaviours), `shell-metrics.mjs` (bytes/paint/click-to-working, `local`
  vs `live`), plus the older scripts. All serve through `scripts/pages-server.mjs` (Pages-shaped; presses Start for the tool scripts); wait for
  `#app[data-blazier-state="live"]`, never `.room` (the shell has one).

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

**Prism/Stories constants** (re-measure on every re-mint): `MaxReplyStepsConst = 160` / `MaxStorySteps = 448` are CEILINGS pinned on ctx=512 K=3
(~1.15 chars/token); the caps in force come from the LOADED model's `Context` via `HoloKernel.ContextBudget` (`ReplyCap` = min(160, Ctx/2);
`StoryCap`; `PromptBudget` = Ctx - cap; `KeepTail` clamps an over-budget prompt). **THE WINDOW RULE: prompt + reply must stay under `Context`,
or every step past it re-Primes the whole window** (~2.9 s PER STEP at 512/L33; ~32 ms at ctx=64 L9). **Stories SLIDES instead**:
`StoryRollTarget = 400`, `ContextBudget.RollingStoryCap`; when `cache.Filled == Context` it re-Primes on `ContextBudget.RollTail` (half the
window) and carries on with O(1) `StepToken` (ctx=512 never rolls; ctx=64 writes 400 tokens with 11 re-primes, ~0.45 s native).
`Paragraphs()` lays the text out (the model emits no newline): `ParagraphChars = 130`; `.story-body{white-space:pre-line}` is on its own
span. About 1 story in 5 ends early (DegenerateTail/loop cut): the model's doing. Copy states only numbers read off the model (`Stats()`,
`TokenVoice.CodecComponents`, `_stats.KPass`), never typed. `Prism.razor` context is the tagged wire format (`user: Q\nprism: A` + STOP per
turn); Stories is a plain continuation. `AsciiPunctuation.Fold` runs at every tokenizer entry. Test rig (scratch): console app + real
`Renderer` + local HttpListener as website-data, calling the pages' own `Ask`/`TellStory`/`Visualize` by reflection on the built `Showroom.dll`.
Check: `chat-turns.mjs <dist> <website-data> 9 6` (flags any turn over 6 s); `story-times.mjs <dist> <website-data> 3`.

**Checkpoint refresh** (data-only, no publish): write `prism/oracle-brain.bin.gz` (`GZipStream`) +
`oracle-vocab/rounds/stackk/iterwarm.txt` (UTF-8 no BOM) into `C:\Users\dongy\website-data\prism\`,
commit, push website-data only. Verify: `.../website-data/prism/oracle-rounds.txt`.

## Data and runtime live in the website-data repo (2026-10-04)
`C:\Users\dongy\website-data` (GitHub `EvaluatedApplications/website-data`, Pages on main, served at
`/website-data/` on the SAME origin: no CORS; `.gitattributes` `* -text`, byte-exact) holds
`council-web/`, `prism/` and `_framework/` (the Blazor runtime). The site keeps `dist/` (shells, css, `blazor.webassembly.*.js`, 0.8 MB),
`wwwroot/data/forecaster-history.json` (+ refresh workflow), `content.wal`.
- **ONE setting**: `window.EA_DATA_BASE` at the top of `wwwroot/index.html` (`/website-data/`;
  `website-data/` on `localhost`) -> `Services/DataUrl.cs`; every fetch is `DataUrl.For("prism/...")`/
  `("council-web/...")`. Never write a literal `data/...` URL for these files.
- **Local dev**: `scripts/link-data.ps1` once (junction `wwwroot/website-data`, gitignored). **Runtime
  split**: the start script's `loadBootResource` returns `EA_FRAMEWORK_BASE + file`; `blazor.webassembly.*.js` stays in the site.
- **Source change = `scripts/publish-site.ps1`** (publish incl. the shells, `finish-shells.mjs`, rebuild `dist/` without `.br/.gz`, copy
  the runtime to `website-data/_framework`; ~5 min, AOT). PUSH ORDER: website-data first, then the site; re-run with `-Prune` only after
  the site push is live (it deletes old runtime files the live site still boots from).
## The tools (one paragraph each)
- **Analyst** `/analyst`: in-browser data profiler + SQL REPL over **HoloDb** (`Database.Open(null)`). Sniffs CSV/TSV/JSON/JSONL/text,
  infers types, bulk-loads, profiles, charts, free SQL + CSV export. Caps 500k rows / 2M-char entity scan / 64MB. **Novelty scan** (opt-in):
  a text column's surprisal against Prism's checkpoint via `SessionHost` key `"prism"`.
- **Creature** `/creature`: 20x20 grid the visitor draws; a **HoloFormer** learns to forage live. `Dim=384, Layers=1`, KPass from
  `oracle-stackk.txt`, `MaxCtx=32`, `MinShifts=8`. **Tracer** `GridTactics.Reachable` BFS = the distance field.
- **Forecaster** `/forecaster`: same substrate on a real hourly AAPL tape. `Dim=128, Layers=1`, `CandleContext=128` -> `MaxContext=256`,
  `Vocab=17`; `wwwroot/data/forecaster-history.json` (~3,484 candles); optional Finnhub top-up if `finnhub-key.txt` exists.
- **Prism** `/prism`: chat REPL over a point-in-time copy of the user's PrismStudio checkpoint; `ServeCache` O(1)/token, `Gate`/`DegenGuard`
  confidence-gated decoding, `Prime()` strips the trailing newline, `_turns` capped, audible tokens off by default.
- **Nano Stories** `/stories`: SAME weights (not a fine-tune), shared `"prism"` key and `TokenVoice`; one-shot continuation, Focused/
  Balanced/Wild presets, seeded `Gate.Pick`; states the real parameter count. **Decode gate = TopP, not ResonanceSigma** (degenerates to
  top-1 here): `Floor = FloorMode.TopP` (Prism >= 1.3.0); `P` is checkpoint-specific, re-measure on every re-mint (r3,970: chat 0.43; Stories
  0.43 / 0.77 / 0.89); `ConfidentThreshold=0.60`. Copy "plays its part three times" = K=3.
- **Cartographer** `/cartographer`: 2D view of ONE next-token decision on Prism's checkpoint (prompt capped `min(64, Context)`): the last
  position's face at every boundary (embed, each (layer,pass), FINAL) as a path, the first boundary whose greedy top-1 = the final answer
  marked; power-iteration PCA (captured variance always shown); top-4 attention per boundary. **No training loop, ever**; `await Task.Yield()`
  keeps the tab responsive (no `MapAsync`).
- **Prose** `/prose`: paste/drop text; `ProseEngine.MineText` (chunks+yields ~200k chars) then recombines into sentences/Q&A/conversations.
  HoloDb `ProseStore` + AlgFormer plausibility (None / Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch),
  `data-cat="holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines). Cap 64MB.
## Council Spending Scanner: `Pages/CouncilSpending.razor` (`/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`, SPEC_FOR_SHOWROOM.md items 9-30). 28 councils (counted
from `index.csv`), no HoloDb/engine in the browser. Public audience, mobile-first, not editorial: audit terms, plain prose + counts + GBP,
OGL credit, never a cause. Its static shell carries no council count or name (they need `index.csv`); the live hub reads them.
- **A new council needs NO page edit.** After the virtual-customer's `phoneexport <slug>` (and every `export`): run the builder
  (`AboutUs\CouncilWebBuilder`, `dotnet run -c Release --project CouncilWebBuilder`, ~3 min; 2nd arg `profiles` rewrites only
  profiles.json.gz; fails above 50 MB/file), commit + push website-data only. It DISCOVERS councils: an `index.csv` row (slug = folder)
  + the engine profile whose name matches (`NameMatchesSlug`: words run together, or initials "rbwm"); order = the engine's. A council is
  HELD (rows also dropped from `cross/*.csv`) when its profile `VerificationNote` starts "Profile text pending", it is in env
  `COUNCIL_HOLD`, or `VirtualCustomer\export\checklist_holds.csv` lists it `withhold`. **CHECKLIST GATE**: the builder REFUSES (exit 2,
  writes nothing) unless `export/checklist_results.csv` exists, every cell is PASS or HELD, every shipped council has rows, and the file
  is newer than every file in `web_export` and `export` (run `CouncilAudit.Cli checklist all` first, in `C:\Users\dongy\VirtualCustomer`).
- **`index.csv`** = the export's columns + `Name,Short`: list, order, count and names (`CouncilWebData.Councils`, filled by `IndexAsync`; nothing
  draws before `_catalogReady`; `CouncilTerms.Words(n)` writes the count). The builder MEASURES per-council facts (`noA`, `noD`, `noNumber`,
  `numberedFirst/Last/Months`, `counterIds`, `dByNumbering`, `dYears`, `noBudget`, `redacted`/`pooled`) into `profiles.json`; `CouncilFacts` +
  `CouncilTerms` build every statement from them (say plainly where a check cannot run, never an empty list). `Services/CouncilOverrides.cs`
  holds ONLY prose that cannot be derived (Hertfordshire, Stockport, York, Durham, `NoPublishedNumber`); `ListForEveryCouncil = true`.- **Audit rules (2026-10-04, an independent auditor)**: no raw engine code is ever printed: `Services/CouncilCodes.cs` maps classification,
  reading, debt-sink flag, label check, loan status and payee class to words (unlisted = "Other (not described here)"); BOTH
  `CouncilWebBuilder` and `CouncilTermsCheck` FAIL (exit 1) on a code the table lacks. No "fault" label, no "published twice"/"lead"/
  "actual target" wording (twins = "identical lines under two transaction numbers"). A council paying itself (`IsSelf`) is left out of
  flows/debt sink/misfits/loan checks and counted. `TxText`: a council with no published number never has the scanner's row number called
  its transaction number; `CheckCannotRun`/`CheckNote`/`CheckCoverage` say where a number-based check cannot or only partly runs. A
  placeholder id ("(no number published) N") is read PER LINE (`CouncilTerms.IsPlaceholder`). Schedule A `Difference` = stated Gross minus
  the Gross expected from the lines' VAT types (engine S52); the letter quotes net, gross, transaction, date, payee and that gap, never a
  class, reading or cause. Builder `Clean` drops first-person, working-note and jargon text and PRINTS `HELD LINES` for review.- **Regression**: `dotnet run -c Release --project CouncilTermsCheck -- <council-web> out.txt [slugs]` prints every council's list, year
  notes and A/D lines from the SHIPPED profiles + years.csv; diff against `CouncilTermsCheck/golden.txt` (regenerated 2026-10-05; explain
  every diff); also read the builder's REWORDED/HELD BACK/HELD LINES. `FrozenEligible` (from `export/budget_units_<stage>.csv`) makes
  `BudgetPanel` say a unit is outside the statistic. `BudgetTestPanel`'s stage prose numbers are still TYPED (frozen 49 of 52 / 96 vs current
  50 of 53 / 97; Stage 2d 37 units, 26 below, 133 pooled): recompute from `budget_units*.csv` when a stage moves.- **Load by financial year**: `LoadPicker` = tick-list of financial years (April-March; "No readable date" last), size line before any fetch,
  ONE Load with progress + Cancel. `MonthScan` keeps raw bytes only for recent years (`RawBudget` 40 MB, LRU); an evicted year re-inflates
  (`EnsureRawAsync`) before a list opens.- **Data** (`website-data/council-web`, ~525 MB, ~6,500 files, largest 1.2 MB; Pages cap 1 GB): `index.csv`, per-council `months.csv` + `years.csv`,
  exception files in ONE slim format `<slug>/<YYYY-MM>.exceptions.csv.gz` and `<slug>/fy-<YYYY-YY>.exceptions[.N].csv.gz`, month TRANSACTION
  slices `<slug>/<YYYY-MM>[.N].csv.gz` (fetched only by "See the source rows": that month, cap 80 ids, 200 lines), `cross/*.csv.gz` (incl.
  `check_rules`, the engine's registry of every row filter), `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`. Profile text is cleaned in the
  BUILDER. Delete a file the run no longer writes.- **Code**: `Services/CouncilWebData` (fetch, `InflateAsync`, caches), `CouncilLoad`, `CouncilMonth` (`MonthScan`: byte-level, columns by
  header name), `CouncilSource`, `CouncilChecksWeb` (plain loops: LINQ over decimals is slow in WASM), `FoiTray`, `CouncilGap` (K3 EXCLUDES
  employee pay), and `Components/*` (LoadPicker, YearBlock, CheckPanel, BudgetPanel, FoiTrayPanel, SocialCarePanel, RulesDisclosure, ...).
  Each load logs `CW-PERF` lines. One rules disclosure per check (from `cross/check_rules.csv`; `CheckDef.RuleChecks`). Twins count GROUPS
  (`GroupId`) and sum `ExtraCopyValue`. N-squared runs (Wokingham): the value is the STATED Gross once. `Plural`/`NumLines`: never write "N
  lines" by hand. Pattern readings (`RecurringBatchRate`, `CadenceCatchUp`) are not classes (`ExplainedBy`). Wokingham only: `Care` caption
  and `SocialCarePanel` (who is paid by year with a Companies House record, ALWAYS with its source; no individual named). Request tray
  (`FoiTrayPanel`): the letter states facts and asks "Please provide the records you hold for this item, and the reason for it"; caps 300
  items / 100 per letter; tick/source buttons are PLAIN markup (a component per row cost ~5 ms).- **Measured** (headless Edge, CPU 6x, 4 Mbps, 100 ms RTT): `scripts/council-load-perf.mjs <dist> 6 4096 100 <data> [big|select|cancel|care|new]`
  (`CASES=wokingham:2024-25,reading:2022-23` runs exactly those council-years; `ONLY=` limits), `council-perf.mjs`, `gap-perf.mjs`;
  `rules-check.mjs <dist>` prints the rules/twins/A/debt/budget text. Compare only interleaved passes of two builds (the machine drifts
  1.5-2 s an hour). 2026-10-08, shell build vs the pre-Blazier build, 3 interleaved passes: Wokingham 2024-25 Load 2.2-2.5 s vs 2.2-2.7 s,
  Reading 2022-23 1.0-1.5 s vs 1.0-1.4 s: no change. Earlier: York 2015-16 (68,771 lines) 4.5-5.0 s; Essex 7.5 s; page open 0.5-2.1 s.
- Gotchas: `--` in csproj XML comments breaks load; BudgetPanel's `<text>` trick fails in code blocks; CSS is one scoped file using
  `.cs ::deep`; lists with stateful children need `@key`.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package demo, NOT in the gallery: a private, link-only client preview (`recycledao-owner`'s repo,
never edit it from here), `noindex,nofollow` (the page's `HeadContent` and `finish-shells.mjs` both). Mint invariant: only `MintForApproval` increases `_totalMinted`.

## Dependencies (exact NuGet versions, `Showroom.csproj`, re-verified 2026-10-08)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.11**: **must equal the installed WASM runtime pack**
  (a skew caused the 2026-09-06 outage: silent death at boot); check `dotnet --list-runtimes`. Blazier 1.0.0 needs the same 10.0.11.
- `EvaluatedApplications.Blazier` **1.0.0** (static shells, loader; MSBuild runs its tool on Microsoft.AspNetCore.App 10).
- `EvaluatedApplications.AlgFormer` **2.20.1** (same pin in `HoloKernel.csproj`); `SubwordVocab.MaxLen`
  must be >=16 to load a freshly-minted checkpoint. `EvaluatedApplications.Prism` **1.3.3** via
  `HoloKernel.csproj` (`FloorMode.TopP`/`DecodePolicy.P` need >=1.3.0).
- `EvaluatedApplications.HoloDb` **2.1.0** (Analyst, Prose; first with a working `score` and graded `NEAREST`);
  `Tracer` **2.0.0** (Creature); `Prose` **1.3.2**. A bump ripples to every tool in this one `.csproj`
  (NU1605 otherwise): re-`dotnet build` the whole app; versions only via `dotnet add package`.
  `ProjectReference ..\HoloKernel\HoloKernel.csproj` serves every tool but the council scanner.
- `PublishTrimmed=true` + `RunAOTCompilation=true`: **AOT stays (user decision, held 2026-10-08)**. The interpreter hit `NIY` in `DecodePolicy`'s
  static ctor on 2026-09-06; the lazy-Blazor study (`AboutUs/docs/lazy-blazor-study.md`) could not reproduce that on 10.0.11 + Prism 1.3.3
  (interpreter ran the tools 1.3x-5x slower), but the choice was not reopened. `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker);
  `WasmEnableThreads=false` (threading regressed a laptop's boot).
## Boundary (hard, from the agent charter)
**NuGet only, never MonoRepo `ProjectReference`** (`HoloKernel`, a sibling NuGet-only RCL, is the one
exception). Never touch `AboutUs/site/*`, nav or the shared design system (`website-owner`'s). Never launch
the app / open a browser for a demo: build-verify only (the headless scripts above are the user-approved exception, they only drive published builds).
Checkpoint hand-off is `prismstudio-owner`'s call.

## Standing facts, build
**Shifts must be > 1, always** (S=1 is a pure diagonal); re-derive a floor from `bindRank = shifts·d/2`
per tool. `golden: true` on every `HoloFormer`. No filesystem in WASM; single-threaded: small
live-training shapes, yield cooperatively. `HoloShape` statics `ShiftsFor/BindRank/CleanCapacity`.
Build: `dotnet build Showroom.csproj -c Release` (0/0); `scripts/publish-site.ps1` publishes (deploy
hard-couples to `dist/`, see `AboutUs\CLAUDE.md`). No test project: build + review + `CouncilTermsCheck` + `shell-check.mjs`;
live behaviour is the user's (`dotnet run`, or the deployed `/tools/`).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O with ABSOLUTE paths (a relative path
  resolves against the shell's cwd), never `Get-Content`/`Set-Content` on source (mojibake).
- `Home.razor` card hrefs are ABSOLUTE with a trailing slash (`/tools/<slug>/`: a bare folder name costs a Pages 301, ~230 ms of first paint on the phone profile). `PrismFormer` (not `AlgFormer`) is the namespace of
  `HoloFormer`/`HoloShape`/`SubwordVocab`.
- A `.razor` page whose class name COLLIDES with a package namespace (`Showroom.Pages.Prism`, `.Prose`) needs
  `@using global::<Namespace>` in every page referencing `Prism.*`/`Prose.*` (Prism, Stories, Prose, Analyst).
- Razor reserves the bare `<text>` tag (cannot carry attributes, `RZ1023`): build an SVG `<text>` as a
  `MarkupString`, HTML-encoding interpolated content by hand.
- A shell's words come from the page's own markup (one source), but a page's prerender branch must not need data: no council count,
  no model number. Shell copy is indexable HTML, so the licence rule applies: never "free"/"free to use" about price (removed from Analyst
  title/lede/badge and Prose badge 2026-10-09; the privacy claim stays, worded without it).

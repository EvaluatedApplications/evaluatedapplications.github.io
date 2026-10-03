# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-03 (council scanner Session 25/26 sync)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/`). Every tool runs entirely client-side: no server, no
upload, the compute happens in the visitor's own browser tab. Charter:
`MonoRepo\.claude\AGENT-CHARTER.md` (applies here in full, incl. the §3 ~250-line budget, even
though this repo sits outside MonoRepo). This repo only ever *consumes* MonoRepo packages via
published NuGet, never source.

**Purpose**: each tool is a real, working demo of a published `EvaluatedApplications.*` package's
capability, driven live by the visitor — no smoke and mirrors. Eight tools: **The Analyst** (HoloDb),
**The Creature** (AlgFormer/HoloFormer + Tracer), **The Forecaster** (AlgFormer/HoloFormer),
**Prism** (AlgFormer/HoloFormer, trained-checkpoint chat REPL), **Nano Stories** (same checkpoint as
Prism, asked to write instead of chat), **The Cartographer** (same checkpoint again — a 2D map of the
representation-space path one next-token decision takes), **Prose** (HoloDb + AlgFormer, grammar-
mining corpus generator), **Council Spending Scanner** (HoloDb, a real analytical tool over England's
council spending transparency data, engine vendored from the EA virtual-customer agent). Plus one
**unlisted** page (below), a client preview.

## Site plumbing
`Program.cs`: standard WASM host; one scoped `HttpClient`; `AddSingleton<HoloKernel.SessionHost>()`
(shared model cache); `AddSingleton<ContentDbHost>()` (unrelated spike, `Pages/ContentDbSpike.razor`).
Nav (`MainLayout.razor`) is `Home · Tools · NuGet` (3 items, lean by design) — a new tool needs a
`Home.razor` gallery card (`<a class="card tool" href="/tools/<slug>" style="--cat:...">`, a live/
soon tag, a package `.ver` pill, `.desc`, `.go-in`), **not** a nav entry. `wwwroot/index.html`:
`<base href="/tools/" />`; links `/assets/site.css` (shared design system) + Showroom's own
`boot.css`/`depth.css`; GitHub Pages SPA deep-link restore; JS interop: `window.analystDownload
(name, text, mime)` (Blob-URL save), `window.copyText(text)`.
**CSS pattern**: each tool has its own `Pages/<Tool>.razor.css` (Blazor CSS isolation can't share a
partial across components) duplicating a shared base block verbatim (`.room`/`.crumb`/`.room-head
h1`/`.lede`/`.hint`/`.err`/`.go`/`.outro`) before its own classes — copy from `Prism.razor.css`/
`Analyst.razor.css`. `wwwroot/css/voice.css` is the one real global exception (`TokenVoiceControls`
lives in the HoloKernel RCL). **Parallax depth/glow** (`wwwroot/css/depth.css`): 3 scroll-driven
tiers, zero JS, reduced-motion-gated, tinted per tool via `[data-cat]` on the outer `.room` (a
single package name sets `--glow-near`/`--glow-mid`; a multi-package tool uses a chord, e.g.
`"algformer-tracer"` Creature). A new tool's own panel class must be added to the mid-tier
selector list to get the glow — and must not carry its own `opacity` (see the "OPACITY-MULTIPLIER
TRAP" comment in that file).

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 2.16.0 + the `Prism`
package — a `ProjectReference` here is the designed path, not a MonoRepo boundary break. Surface:
- `ModelSpec` — shape + the **S>1 invariant enforced by construction**.
- `HoloSession.Create(spec)` / `.FromCheckpoint(bytes, kPass, serveAlpha)` — **K and alpha are
  mandatory args**: `HoloFormer.Iters`/`.IterAlphaServe` are NOT persisted by `Serialize()`, so a
  deserialized checkpoint always reads back `1`/`1`. `.Model` exposes the raw `HoloFormer` (public
  `Layers`/`Dim`/`Shifts`/`Context`/`Vocab`/`ParamCount`/`EmbRow`/`InspectStackIterFaces`/
  `InspectAttention`/`DecodeFace`, used directly by The Cartographer below rather than through a
  HoloSession wrapper). `.Logits(ctx)` full recompute; `.NewServeCache()`/`.Prime()`/`.StepToken()`
  O(1)/token KV-cache path — bit-identical to `.Logits` only for a MULTI-layer model (Prism/Nano
  Stories); NOT verified for L=1 (Creature/Forecaster) — don't route those on without re-checking.
- `PrismCheckpoint` — `SessionKey` (`"prism"`), `ResolveK`, `ReconstructAlpha` — every tool serving
  Prism's checkpoint (Prism, Nano Stories, The Cartographer, Analyst's novelty scan, Prose's "Score
  with Prism") must use these two formulas IDENTICALLY.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool; every consumer of
  Prism's checkpoint shares one in-memory instance this page load. **Ephemeral** — no persistence,
  reload drops everything.
- `RefinementLoop.Observe`/`.ObserveSequence` — the live-training pattern (`NewGrads()`->
  `IterAccumulate`/`StackIterAccumulateAllPos`->`Step`), used by Creature/Forecaster only.
- `Decoding` comes from the **`Prism` package's** `Prism.Inference` namespace — `@using
  global::Prism.Inference` required on any page referencing it (see Gotchas: namespace-collision
  rule applies repo-wide, not just to the colliding page).
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)` — fetch+decompress a `.gz` sidecar
  (BCL `GZipStream`, no JS interop); GitHub Pages serves files uncompressed, so a raw checkpoint
  needs a hand-shipped `.gz` sibling. The ONE choke point every checkpoint consumer shares (Prism,
  Nano Stories, The Cartographer, Analyst's novelty scan, Prose's "score with Prism"); Creature/
  Forecaster only fetch small metadata sidecars, never checkpoint bytes. `CheckpointF32.Unpack`
  runs inside it, rebuilding a "PF32"-magic fp32 buffer to plain f64 losslessly (`Pack` is the
  inverse, oracle/verify-only) — **cross-repo byte-exact contract with PrismStudio**, don't change
  the wire format here without changing it there.
- `TokenVoice`/`TokenVoiceControls.razor` — Prism's per-token voice (face→tone synth), shared
  byte-for-byte by Prism and Nano Stories; never quantise to a musical scale. `DegenerateTail.
  Start(ids)`/`.SafePrefix(ids)` trims a repeating tail mid-generation (Prism, Nano Stories only).
- **Browser contract**: visitors **train**, never **reshape**. `GrowLayers`/`GrowShifts` are real
  but PrismStudio/server-side only; a better model reaches visitors via a new checkpoint, never a
  runtime shape mutation.

**Prism/Nano Stories operational constants** (re-measure on every checkpoint re-mint):
`MaxReplyStepsConst = 56` (Prism) / `MaxStorySteps = 128` (Stories) are PINNED, not `Context/2` —
that fraction silently broke on a past checkpoint swap. Both caps exist because this checkpoint
has never emitted its own STOP token, so every reply/story runs to its cap and ends mid-sentence —
train it, don't paper over with a sentence-boundary heuristic. `Prism.razor`'s chat context
(`BuildContextTokens`) uses a tagged wire format (`user: Q\nprism: A` + STOP per turn) matching
PrismGym's packed training windows; Nano Stories encodes its prompt as a plain continuation
instead. `HoloKernel/AsciiPunctuation.Fold` (curly quotes/dashes/nbsp/ellipsis → ASCII) runs at
every tokenizer entry point site-wide — `SubwordVocab.Fold` blanks non-ASCII to a bare space
otherwise, which silently mangled phone-autocorrected input before this fix.

**Checkpoint refresh** (Prism's `oracle-brain.bin` + sidecars, also Stories/Cartographer): data-only, no publish: copy into `wwwroot/data`+`dist/data`,
regenerate the `.gz` with `GZipStream`, write sidecars as UTF-8 no BOM, cross-check `-stackk`/`-iterwarm` against PrismStudio.

## The Analyst — `Pages/Analyst.razor` (route `/analyst`)
In-browser data profiler + live SQL REPL over **HoloDb** (`Database.Open(null)`, in-memory). Sniffs
CSV/TSV/JSON/JSONL/plain-text, infers a type per column, bulk-loads (100k-row chunks), profiles every
column via HoloDb aggregates, plus entity extraction, a no-SQL chart builder, click-to-filter, a free
SQL prompt + CSV export. Caps: 500k rows, 2M-char entity scan, 64MB upload. **Novelty scan** (opt-in):
scores a text column's surprisal against Prism's checkpoint via `SessionHost` key `"prism"`.

## The Creature — `Pages/Creature.razor` (route `/creature`)
A 20×20 grid the visitor draws where a **HoloFormer** brain learns to forage live, on **HoloKernel**.
`Dim=384, Layers=1, KPass=` live-read from `data/oracle-stackk.txt`, `MaxCtx=32`, `MinShifts=8`.
**Tracer**'s `GridTactics.Reachable` BFS supplies the distance field; trains toward the
advantage-weighted decisive move. Training is a producer/consumer pipeline (bounded channel,
`DropOldest`) so simulation never blocks on training. `ResetBrain` drops the session.

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same HoloFormer substrate as Creature, on a real hourly AAPL tape. `Dim=128, Layers=1, KPass=` (same
live source), `CandleContext=128` -> `MaxContext=256`, `Vocab=17` (`[TIME_bucket][RETURN_bucket]`).
Data: `wwwroot/data/forecaster-history.json` (~3,484 real hourly candles); optional live Finnhub
top-up if `wwwroot/data/finnhub-key.txt` exists and NYSE is open. `RunOneAnimatedTick()` splits each
tick into PREDICT then REVEAL+TRAIN, paced by a speed slider.

## Prism — `Pages/Prism.razor` (route `/prism`)
A chat REPL over a point-in-time copy of the user's live PrismStudio checkpoint. `ServeCache`-based
O(1)/token stepping, `Prism.Inference.Gate`/`DegenGuard` for confidence-gated decoding, `Prime()`
strips (not appends) the trailing newline. `_turns` capped + render-batched. **Audible tokens** via
`HoloKernel.TokenVoice`/`TokenVoiceControls`, off by default.

## Nano Stories — `Pages/Stories.razor` (route `/stories`)
SAME model as Prism (same weights, not a fine-tune), shared `"prism"` `SessionHost` key and `TokenVoice`. Plain one-shot continuation,
own Focused/Balanced/Wild presets, seeded (`Random(seed)` -> `Gate.Pick`). States the real (~370K) parameter count under the story.

**Decode gate: TopP, not ResonanceSigma** (Prism and Stories): ResonanceSigma degenerates to top-1 on this checkpoint so Temperature is inert.
Both pages build `Floor = FloorMode.TopP` (needs Prism >= 1.3.0); `P` is checkpoint-specific, re-measure on every re-mint (r84,639:
chat P=0.30; Stories Focused 0.30 / Balanced 0.466 / Wild 0.635; never a conventional 0.9). `ConfidentThreshold=0.60`.

## The Cartographer — `Pages/Cartographer.razor` (route `/cartographer`, added 2026-09-24)
A 2D visualiser for **one** next-token decision — not a chat, not a generation loop. Reuses Prism's
exact checkpoint via the shared `"prism"` `SessionHost` key. Encodes the prompt (capped to
`min(24, Stats().Context)` tokens, truncating the front if longer), then calls `HoloFormer.
InspectStackIterFaces`/`InspectAttention`/`DecodeFace`/`EmbRow` directly off `HoloSession.Model` —
no HoloKernel wrapper exists for these (a deliberate gap; read-only inspectors aren't worth
wrapping). **Trajectory**: the LAST position's face at every `[boundary]` (embed, one point per
(layer,pass), then FINAL), joined into a path; crystallisation (first boundary whose greedy top-1
already equals the final answer) marked with a triangle. **Projection**: hand-rolled-power-
iteration PCA fit on the TRAJECTORY points only (vocabulary cloud + attended positions projected
into that same basis, captured-variance fraction always shown); a second embed→final Gram-Schmidt
basis is offered as a cross-check. **Attention**: one row per boundary (excluding FINAL); top-4 by
|weight| draw as lines to `grid[boundary][position]`, decoded via `DecodeFace`. **No training
loop, ever** — `HoloFormer.Map` defaults to `SequentialMap` (deadlock-safe on WASM's single
thread); no `MapAsync` twin exists, so `await Task.Yield()` around the call keeps the tab
responsive (same as `Prism.razor`).

## Prose — `Pages/Prose.razor` (route `/prose`)
Paste/drop text; `ProseEngine.MineText` mines it (page chunks+yields at ~200k chars), then recombines into sentences/Q&A/conversations.
HoloDb `ProseStore` + AlgFormer plausibility (None / Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch), chord`data-cat="holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines a fresh engine). Cap 64MB.

## Council Spending Scanner — `Pages/CouncilSpending.razor` + `.Insights.cs` (route `/council-spending`)
Real analytical tool over England's spending-over-£500 data (not a model demo), in **HoloDb** (`Database.Open(null)`, one
shared `spend` table). Public-audience, mobile-first, NOT editorial: nothing hand-picked, every check is a stated neutral
rule run identically for every council; audit terms only (exception/discrepancy/unreconciled/anomaly); every section has
plain prose (what is highlighted, what it could mean, counts + £), the "at a glance" table has a grand total; OGL credit per
council. Engine `CouncilAudit/` is **vendored verbatim from `VirtualCustomer\src\CouncilAudit`** (re-synced 2026-10-03,
Session 26; header comment on each file; never edit here; DebtLedger/LoanDecoder/HandCheckHelpers are CLI-only, not vendored).
- **Four hosted councils**, data-driven by `Hosted` records (`BuildHosted`): Wokingham, Reading, West Berkshire, RBWM; picker
  grouped by financial year (`FinancialYearOf`) with select-all for lists > 8 periods. Counts/spans are read from profiles.
- **Data pipeline (offline, `CouncilDbBuilder`, run `dotnet run -c Release`; ~3 min)**: Wokingham/Reading from raw files in
  `wwwroot/data/<c>/`; WB/RBWM from `VirtualCustomer\export\<c>\<tag>.transactions.csv` (env `COUNCIL_EXPORT_DIR`), through the
  same `AuditEngine.Run` (verified: reproduces his exports row-for-row). Writes `wwwroot/data/councils/<c>/*.norm.csv.gz`,
  `exceptions.csv.gz` (18 cols; `net` appended, group keys are council-qualified in the page) and `cross/*.csv.gz` (his flows,
  alias grades, debt ledger, debt sink, misfits, file duplication, within-txn repeats, + `flow-rows` source lines, reconciled
  to the flows file). Page loads cross files once (`EnsureCrossAsync`); copy regenerated data into `dist/` on publish.
- **Sections** (`InsightsBlock`): standing payments (B rows `StandingSchedule*`), alias-aware flows (accept only; probable
  separate, excluded), loan-interest decode (InterAuthority; NotApplicable shown as "not a loan"), correction pairs, publication
  faults, file duplication, within-transaction repeats, debt sink + payment misfits. Scope = councils opened, switch for all six.
- **In-browser checks = one EvalApp pipeline** (`Services/CouncilChecks.cs`, `Eval.App(...)` with two `ForEach`: per-file
  faults, per-group `CorrectionPairs.Find`); built once; failure shows as a plain error line, never a silent fallback.
  Publication fault rule: single-month files only for the "named month" share (marked < 9 in 10); overlap = lines already in an
  EARLIER file (transaction, pay date, supplier key, net). Row ids stand in for rows (`id IN (...)` read-back).
- **FOI**: new-section rows are `ExceptionItem.Custom` (own sentence/lines/request, facts only, no offered cause); a letter goes
  to ONE council (`FoiAddressee`, only that council's items); FOI emails from `Hosted`; RBWM has none verified -> placeholder.
- Gotchas: HoloDb >= 2.1.0 (graded NEAREST). `ExceptionItem.IsUnexplained` includes `StandingScheduleSurplus`. Cross-council
  group ids restart per council (hence the prefix). Verified (not live-browser): desktop harness replaying the load path,
  EvalApp pipeline and a full HtmlRenderer render with no exceptions; WASM runtime of the EvalApp pipeline is UNVERIFIED.
- Numbers differing from his spec: exports are RBWM post-dedup, so overlap shows 8/14 not 381/61 (the 1,067/381/61 figures are
  only in his profile quirks text); D groups total 3,196 not 3,234 (1 correction pair in 434 small groups with RBWM+Wokingham).
## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo, NOT in the gallery — a private, link-only client preview
(`C:\Users\dongy\RecycleDAO`, `recycledao-owner`'s repo; never edit it from here). Absent from
`Home.razor`/nav, `noindex,nofollow`. Mint invariant: `MintForApproval` is the only method that
increases `_totalMinted`.

## Dependencies (exact NuGet versions, `Showroom.csproj`)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.11** — **must stay version-equal to the
  installed WASM runtime pack** (a skew caused a real 2026-09-06 outage: the interpreter hit IL it
  didn't recognise from a mismatched native runtime and died silently at boot). Check
  `dotnet --list-runtimes` whenever the SDK moves.
- `EvaluatedApplications.AlgFormer` **2.20.1** (re-verified against `Showroom.csproj`/
  `HoloKernel.csproj` 2026-10-03 — both pin the same version, no skew). `SubwordVocab.MaxLen` must
  be ≥16 to load a freshly-minted checkpoint.
- `EvaluatedApplications.Prism` **1.3.3**, via `HoloKernel.csproj`'s own `PackageReference` (re-
  verified 2026-10-03) — `FloorMode.TopP`/`DecodePolicy.P` don't exist before 1.3.0 (needed for the
  decode-gate retightening below); depends on AlgFormer >=2.15.0, satisfied by this project's 2.20.1.
- `EvaluatedApplications.HoloDb` **2.1.0** (bumped from 1.10.0, 2026-10-03 — load-bearing for
  Council Spending Scanner: 2.1.0 is the first version with a working `score` pseudo-column and
  graded `NEAREST`, holodb-owner's F-04 fix) — Analyst, Prose, Council Spending Scanner.
- `EvaluatedApplications.Tracer` **2.0.0** (re-verified against `Showroom.csproj` 2026-10-03) —
  Creature.
- `EvaluatedApplications.Prose` **1.3.2** (re-verified against `Showroom.csproj` 2026-10-03) —
  Prose. A multi-package tool's version bump ripples to
  every other tool in this one `.csproj` (NU1605 otherwise); re-`dotnet build` the whole app after
  adding/bumping any tool, not just its own page.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism, Stories,
  Analyst, Prose, The Cartographer.
- `PublishTrimmed=true` + `RunAOTCompilation=true` — **AOT is load-bearing, not a perf luxury**: the
  Mono WASM interpreter can't execute IL in `Prism.Inference.DecodePolicy`'s static ctor without it.
  `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker alongside concurrent local training).
  `WasmEnableThreads=false` — real threading regressed a laptop's boot; nothing here dispatches
  across threads, so nothing to gain re-enabling it. **Version bumps only via `dotnet add
  package`** — never hand-edit `<Version>`.

## Boundary (hard, from the agent charter)
**NuGet only, never MonoRepo `ProjectReference`** — verify API assumptions against the actual
published package before wiring new code. `HoloKernel` is the one exception (a sibling in-repo
RCL, itself NuGet-only). Checkpoint hand-off (Prism) is `prismstudio-owner`'s call; Forecaster's
live top-up needs a Finnhub key this agent can't self-register. Never touch `AboutUs/site/*`, nav,
or the shared design system — `website-owner`'s. Never launch the app / open a browser —
build-verify only; demonstrating a tool live is the user's to do.

## Standing technical facts
**Shifts must be > 1, always** (S=1 is a pure diagonal); re-derive a floor from `bindRank = shifts·d/2` per tool, never copy another's.
`golden: true` on every `HoloFormer`. No filesystem in WASM; single-threaded, so keep live-training shapes small and yield cooperatively.
`HoloFormer` ctor: `(vocab, shifts, layers, maxContext, dModel=0, ..., golden=false, ...)`; `HoloShape` statics `ShiftsFor/BindRank/CleanCapacity`.

## Build / verify
`dotnet build Showroom.csproj -c Release` — green (0/0) with all eight tools + HoloKernel wired in.
`dotnet publish` also spot-checked periodically (deploy hard-couples to it — see `AboutUs\CLAUDE.md`'s
"hard coupling" note on `dist/`). No test project — verification is build-green + code review; live
behaviour is the user's to check (`dotnet run`, or the deployed `/tools/` URL).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O, never `Get-Content`/
  `Set-Content` on these files (non-ASCII punctuation throughout -> mojibake risk).
- `Home.razor` card hrefs are ABSOLUTE (`/tools/<slug>`); the nav doesn't list individual tools.
- `EvaluatedApplications.AlgFormer`'s `PrismFormer` namespace (not `AlgFormer`) is where
  `HoloFormer`/`HoloShape`/`SubwordVocab` live.
- Any `.razor` page whose generated class name COLLIDES with a package's bare root namespace
  (`Showroom.Pages.Prism` vs. the `Prism` package; `.Prose` vs. `Prose`) needs `@using
  global::<Namespace>` — **not scoped to the colliding page**: every page in `Showroom.Pages`
  referencing `Prism.*`/`Prose.*` needs the `global::` form once ANY sibling page is named
  `Prism`/`Prose`. Affects: `Prism.razor`, `Stories.razor`, `Prose.razor`, `Analyst.razor`.
- A new tool page gets the parallax/glow treatment free by reusing the house `.room`/`.room-head`/
  panel shape — needs its own `data-cat="<pkg>"` (or chord) on the outer `.room`, and its own main
  panel class added to `wwwroot/css/depth.css`'s mid-tier selector list.
- Razor reserves the bare `<text>` tag for raw-text-without-a-wrapper output — it CANNOT carry
  attributes (`RZ1023`). An SVG `<text x=".." y="..">` must be built as a `MarkupString` from a C#
  string instead, HTML-encoding any interpolated content by hand.
- Multi-package tools force a real dependency-version bump for every tool in this one `.csproj` —
  re-`dotnet build` the whole app after adding/bumping any tool, not just its own page.

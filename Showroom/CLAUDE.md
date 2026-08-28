# Showroom — CLAUDE.md (showroom-owner)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/` — see `wwwroot/index.html`). Every tool runs entirely
client-side: no server, no upload, the interesting compute happens in the visitor's own browser tab.
Charter: `MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — it's a sibling repo
that only ever *consumes* MonoRepo packages via published NuGet, never source.

**Purpose**: each tool = a real, working demo of a published `EvaluatedApplications.*` package's
actual capability, driven live by the visitor — no smoke and mirrors, no mocked output. Four tools in
the public gallery: **The Analyst** (HoloDb), **The Creature** (AlgFormer/HoloFormer + Tracer), **The
Forecaster** (AlgFormer/HoloFormer), **Prism** (AlgFormer/HoloFormer, trained-checkpoint autocomplete
REPL). Plus one **unlisted** page (below), a client preview, not a package-capability demo.

## Site plumbing
- `Program.cs`: standard Blazor WASM host; one scoped `HttpClient` with `BaseAddress =
  HostEnvironment.BaseAddress` (relative fetches like `data/foo.json` resolve under `/tools/`).
- `App.razor` / `_Imports.razor` / `Layout/MainLayout.razor`: router + shared nav/footer chrome.
  `MainLayout.razor`'s `.nav-links`: `Home · HoloDb · Tools · NuGet` (`Tools` links to `Home.razor`'s
  gallery via `href="."`; a new tool needs a `Home.razor` card, not a nav entry).
- `Home.razor` (`@page "/"`): the tool gallery. Each tool is a whole-card `<a class="card tool"
  href="/tools/<slug>">` (absolute path) with a `--cat` accent colour, a `live`/`soon` tag, a
  one-line desc, "Open X →". Mirror this shape for any new tool. `Pages/NotFound.razor`
  (`@page "/not-found"`) is the router fallback.
- `wwwroot/index.html`: `<base href="/tools/" />`, links the SHARED `/assets/site.css` (Showroom
  borrows the one design system) plus Showroom's OWN `wwwroot/css/boot.css` (see Boot screen) and
  Blazor's bundled `Showroom.styles.css`; also carries the GitHub Pages SPA deep-link restore script
  and `window.analystDownload` (Blob download helper for The Analyst's CSV export).
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css`, Blazor-scoped, all four DUPLICATE
  the same base block (`.room`/`.crumb`/`.room-head h1`/`.lede`/`.badges`/`.err`/`.hint`/
  `.cr-controls`/`.cr-stats`/`.cr-curve`/`.cr-log`/`.outro` — established house style, CSS isolation
  can't share a partial file across components); reuse it verbatim for a new tool.

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL in this repo (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 1.5.0 — a
`ProjectReference` to it is the designed consumption path, not a MonoRepo boundary break. All three
live-brain tools are ported onto it (2026-08-28). Surface used: `ModelSpec` (shape + the S>1
invariant; `Validate()` also rejects `Layers>1 && KPass>1` — AlgFormer 1.5.0's weight-tied K-pass is
single-layer only, verified via `NotSupportedException`; irrelevant today since all 3 tools are
`Layers=1`, but a real constraint on ever growing one deeper while keeping K>1), `HoloSession`
(`Create(spec)`/`FromCheckpoint(bytes,kPass,serveAlpha)` — K/alpha mandatory, `Stats()`), `AlphaRamp`
(`Advance`/`Reconstruct`), `RefinementLoop` (`Observe`/`ObserveSequence`, replaces the
`NewGrads→IterAccumulate→Step` triple each tool wrote independently), `Decoding` (`DecodePolicy`/
`Gate`/`DegenGuard`), `InspectorTrace` (`Inspector.Capture`/`Focus` — NEW for Creature/Forecaster,
opt-in "🔍 inspect brain" toggle; Prism's own Inspector UI was REMOVED, own section below).
`ParallelMapping` isn't wired into any tool (inert, not a gap).

**Cadence gotcha**: `RefinementLoop.Observe` advances its `AlphaRamp` on EVERY call, not once per
episode. Forecaster's old per-CLICK ramp already matched this; Creature's old ramp was per-EPISODE,
so its `IterWarmSteps=300` is an **ESTIMATED, unmeasured** conversion — retune live if the ease-in
looks too fast/slow. **Prism's tokenizer swapped to the published `SubwordVocab`** (was a hand-rolled
greedy-longest-match encoder) — verified against `MonoRepo\AlgFormer\SubwordVocab.cs`:
`CharN=>CharVocab.N=96` already special-cases `Symbol(CharVocab.End)=="\n"`, matching Prism's own
quirk; ctor takes ONLY the merges list (base chars handled internally, a gotcha vs. the old version).
**Browser contract (user directive): visitors TRAIN, they never reshape.** "Grow Prism" means
refining a FIXED-shape model's weights via `RefinementLoop.Observe`/`ObserveSequence` — layers/
shifts/dim/context are chosen up front, immutable for the session. `HoloFormer.GrowLayers`/
`.GrowShifts` are real, published, in-place growth methods, but a PrismStudio/server-side operation —
`HoloKernel` deliberately doesn't wrap either; a better model reaches visitors via a new CHECKPOINT,
never runtime shape mutation.

## Boot screen — `wwwroot/index.html` + `wwwroot/css/boot.css`
Retro-terminal boot log, authentically real not decorative: real file names as the WASM host fetches
them (`loadBootResource` hook, pure observation, always returns `undefined` — zero added latency)
plus the framework's own real cumulative-bytes progress (`--blazor-load-percentage`/`-text`, set on
`document.documentElement` by the SDK's own boot script). `autostart="false"` +
`Blazor.start({loadBootResource})` in the next script tag (synchronous order, no `load`-event wait)
installs the hook before the real download starts. `boot.css` is Showroom's OWN file (`site.css`
boundary stays hard) but reuses its global tokens (`--bg`/`--ok`/`--mono`/`--spectrum-*`) for
on-brand styling for free. Prism's checkpoint fetch gets its own step (`Prism.razor`'s `!_loaded`
branch, `LoadStep`/`Begin`/`Finish`), reusing `boot.css`'s classes — pure narration, no extra latency.
`MainLayout.razor`'s brand mark + `index.html`'s favicon carry the site-wide prism-triangle motif.
**Compile-gap fix (2026-08-28, real phone-confirmed stall report)**: `loadBootResource` only fires at
fetch-START, so once AOT's big `dotnet.native.*.wasm` (~8 MB compressed) finishes downloading, the
browser still has to COMPILE it (real, sometimes multi-second CPU work on a slow phone) with nothing
logged and the byte-percentage plateaued near 100% — reads as frozen. Verified by grepping the
shipped `dotnet.*.js` (not guessed): the loader calls `WebAssembly.compileStreaming(response)`
(fallback `.compile(bytes)`) to do exactly this, and nothing else in any shipped file calls either —
so `index.html` wraps them (pure observation like `loadBootResource`: call straight through, self-
remove after the one call) to log a real "compiling…"/"…compiled (Nms)" pair. Paired with `boot.css`:
an always-on blinking terminal cursor after the log (covers any wait, hook or not), `#boot-phase` — a
status line distinct from `--blazor-load-percentage-text`, encoding "downloaded ≠ ready" — and a
striped `#boot-bar.busy` overlay while compiling (fill WIDTH still tracks real bytes). Build-verified
only (0/0), not seen live — user should confirm on the phone that showed the original stall.

## The Analyst — `Pages/Analyst.razor` (route `/analyst`)
In-browser data profiler + live SQL REPL over **HoloDb** (`Database.Open(null)`, in-memory). Sniffs
CSV/TSV/JSON/JSONL/plain-text, infers a type per column, bulk-loads into a real HoloDb table (100k-row
chunks), then profiles every column via HoloDb aggregate queries (COUNT/DISTINCT/GROUP BY/min/max/
mean/3σ-outliers). Also: regex entity extraction, computed insights, click-to-filter drill-down, a
no-SQL chart builder → HoloDb `GROUP BY` → hand-rolled inline SVG chart, a free-form SQL prompt + CSV
export. Row cap 500k; entity scan capped 2M chars; upload cap 64MB. Six built-in live public feeds
(USGS/NYC/Chicago/Seattle/movies) fetched client-side, or paste any CORS-permitting feed URL.
`window.analystDownload` JS interop does the CSV save (Blob URL + synthetic click, since
`<a download>` can be CSP-blocked).

## The Creature — `Pages/Creature.razor` (route `/creature`)
A 20×20 grid the visitor draws (walls/start/apples) where a **HoloFormer** brain learns to forage
live, on **HoloKernel** (see above). Brain shape: `Dim=384, Layers=1, KPass=2`, `MaxCtx=32` (a
focused recent-trajectory window — measured to converge faster than a longer one; dilution of the
decisive last-token signal was the failure mode), `MinShifts=8` (natural `ShiftsFor(32,384)` returns
1 — floored by `ModelSpec`'s own S>1 invariant now). Distance field: **Tracer**'s
`GridTactics.Reachable` BFS to the nearest apple; trains toward the DECISIVE move (advantage-
weighted: best move minus the mean of legal moves) via `RefinementLoop.Observe(ctx,
_actionBase+target)` once per informative step, `LearningRate` set per-call from the advantage
weight. `ResetBrain` drops `_session`/`_ramp`/`_loop` (WASM has no filesystem).

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same **HoloFormer** substrate as The Creature, pointed at a price tape instead of a foraging grid.
Predicts the direction (and coarse magnitude) of the next hourly tick for one bundled real stock
series. **Tokenisation** (ported from `MonoRepo\MarketSim\PriceForecaster.cs`; `STOCK_i` token
DROPPED — single-symbol demo): each candle → `[TIME_bucket][RETURN_bucket]`. `TimeBuckets=8` =
hour-of-day (UTC) mod 8. `RetEdges` ported VERBATIM: `{-0.0020,-0.0009,-0.0004,-0.00003,0.00003,
0.0004,0.0009,0.0020}` → 9 buckets, `FlatBucket=4`. Vocab=`8+9=17`. **Known skew**: edges tuned for
MarketSim's smaller simulated ticks, so ~65% of bundled transitions land in the two outermost
buckets — direction split (what accuracy scores) stays near-balanced; magnitude granularity is
compressed, not direction.

**Model shape**: `Dim=128, Layers=1, KPass=2`, `CandleContext=128` → `MaxContext=256` tokens
(2/candle), `MinShifts=8` (no-op: `ShiftsFor(256,128)=16` already clears it; `CleanCapacity(16,128)=
122` is under the 256-token window — a real v2 tuning knob, not a v1 blocker). **Training loop** on
**HoloKernel**: per tick, predict via `_session.Logits(ctx)`/`Inspector.Capture` (opt-in) →
`RefinementLoop.Observe(ctx, PriceBase+trueBucket)` (trains + advances the α-ramp) → append the TRUE
token to the tape. `IterWarm=40` clicks maps straight onto `AlphaRamp` (old loop already advanced
once per click, matching `Observe`'s cadence exactly — unlike Creature). `Lr=0.005` reasoned (between
MarketSim's `0.02` and Creature's `0.0025-0.004`), not yet watched live. **Data**:
`wwwroot/data/forecaster-sample.json` — 450 REAL hourly AAPL closes (~3 months), fetched once from
Yahoo Finance's public chart JSON endpoint; training cursor wraps the finite series when it runs out.
**Queued**: live CORS-open feed; a symbol picker; tuning `Lr`/`IterWarm` against a real run.

## Prism — `Pages/Prism.razor` (route `/prism`)
An autocomplete REPL over a real, point-in-time COPY of the user's own live `prism-holo.bin`
HoloFormer checkpoint from PrismStudio, with a full per-character Inspector trace — same spirit as
PrismStudio's own Inspect tab. Renamed from "The Oracle" to match the product line's naming; the
underlying checkpoint asset filenames (`oracle-brain.bin`/`-vocab.txt`/`-rounds.txt`/`-stackk.txt`/
`-iterwarm.txt`) were deliberately kept as-is — internal names, no need to track the public tool
name. Each submission is an INDEPENDENT prompt-in/continuation-out pass — no chat memory, no
speaker-labelled bubbles (`_history: List<RunEntry(Prompt, Continuation, TrailedOff)>`, rendered as
prompt-text immediately followed by continuation-text, Copilot-suggestion style).
**On HoloKernel** (see that section): `HoloSession.FromCheckpoint(bytes, kPass, serveAlpha)` +
`Gate.Pick` replace the old hand-rolled `GateInfo`/`PickToken`/`TopFaces`/`TopAttn`. `FromCheckpoint`
REQUIRES K/alpha as ctor args (kernel-enforced — `HoloFormer.Iters`/`.IterAlphaServe` are **NOT
persisted** by `Serialize()`, verified by round-tripping the real checkpoint, always read back `1`/
`1`). `_stats` (`HoloSession.Stats()`) replaces the old hand-rolled `EquivCompute`/`InvisibleX`
properties; real `ParamCount` vs. `_stats.EquivCompute` (`12·d²·L·K`) is explicitly framed as
**compute-equivalence**, not a claim of real stored parameters. **K is FIXED, not a control**: a
structural fact about the trained checkpoint ("k is not a parameter, its fixed to the model count" —
user), not a visitor-exploreable knob — no slider exists. `_k` sourced live from `oracle-stackk.txt`/
`oracle-iterwarm.txt` (never hardcoded — verified live in `HoloEngine.cs`: `OneShotStackK=8`,
`OneShotIterWarm=20000`). `_trainedAlpha` reconstructed via `AlphaRamp.Reconstruct(rounds, addRound:0,
iterWarm)` — single-layer checkpoints only, falls back to 1.0 otherwise; current snapshot
(rounds=24,360 > iterWarm=20,000) reconstructs to exactly 1.0.
**Generation loop**: one `_session.Logits(ctx)` call per character, `Gate.Pick` draws the token.
Confidence gate = `DecodePolicy.Default` (`ConfidentThreshold=0.60`/`Temperature=0.80`/`FloorK=3.0`/
`DegenRepeat=4`, ported verbatim from PrismStudio's `HoloEngine`) + `DegenGuard` — **verified
necessary**: dry-running against an earlier checkpoint (round ~21,720) produced a 100%-confidence
GREEDY space-repeat on every short prompt, a real repetition-collapse. Shipped snapshot (round
24,360): `Dim=1536,Layers=1,Shifts=16,ParamCount=381,056`, matching live `OneShot*` shape exactly.
**Inspector REMOVED 2026-08-28, for speed**: per-character `Inspector.Capture` recompute (pricier
than `LogitsFor` alone) was the real generation-speed cost — removed entirely (panel markup, call
sites, dead CSS); Creature/Forecaster's own "🔍 inspect brain" toggles are unrelated, untouched.
**Cold-start + pacing fixes (2026-08-28)**, build-verified only, **neither confirmed live** — treat
as unproven if a first-click stutter or non-realtime streaming persists: (1) first Continue click
used to render the whole continuation at once instead of streaming, fixed with a throwaway
`_session.Logits(...)` warm-up + a throwaway render warm-up in `OnInitializedAsync`; (2)
`await Task.Delay(60)` used to sit INSIDE the compute loop, serializing inference to typing speed —
`Ask()` is now a producer/consumer split over `Channel<int>`, decoupling compute from reveal cadence.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo and NOT in the public gallery — a private, share-by-link-only client
preview for the RecycleDAO PoC (`C:\Users\dongy\RecycleDAO`, separate repo, owned by
`recycledao-owner`; NEVER edit that repo from here). Absent from `Home.razor`'s gallery and
`MainLayout.razor`'s nav; carries `<meta name="robots" content="noindex,nofollow">` via
`<HeadContent>` (same pattern as `AboutUs\site\recycledao-preview.html`, website-owner's).
**A full eBay-classifieds marketplace**: RCYT is EARNED by verified recycling and SPENT claiming
material other participants rescued — a real token sink, not just a reward wallet. 21 screens off one
`Screen` enum + `Nav` record-struct stack (real Back button); one `<section class="app-page">`
renders at a time. **Mint invariant (must never regress)**: `MintForApproval` is the ONLY method that
appends to `_mintLog`/increases `_totalMinted`/`_lifetimeMinted`, reachable from exactly two call
sites (verifier queue's `ApproveSubmission`, and seeding) — all other money movement only *moves*
RCYT between balances. Tier table verbatim from `RecycleDAO/docs/demo-mechanics-spec.md` §2
(`Paper/Cardboard=3, Plastic=5, Glass=5, Metal/Aluminum=8, Electronics/E-waste=15`).
**Chrome honesty**: header/search/category/filters genuinely live; only the top utility strip + footer
link columns (+ photo-upload, notification toggles) stay inert, tagged `.mk-tag` "mockup". **Hard
boundaries** (recycledao-owner's charter): testnet-only banners; verification = manual human review,
not fraud-proofing; NO referral/share-to-earn, fiat/cash-out, wallet-connect, or governance screen;
sim counterparty actions only fire from a labelled demo control, never a timer. **Verified gotcha**:
Blazor scoped CSS DOES apply the `b-*` scope attribute inside `RenderFragment<T>` templates in the
same `.razor` file — a shared `ListingCard`/`ListingRow` helper styles correctly, beats duplication.

## Dependencies (exact NuGet versions, `Showroom.csproj`)
- `Microsoft.AspNetCore.Components.WebAssembly` 10.0.8 (+ `.DevServer` 10.0.8, dev-only)
- `EvaluatedApplications.HoloDb` 1.4.0 — The Analyst
- `EvaluatedApplications.AlgFormer` **1.5.0** — The Creature, The Forecaster, Prism (`PrismFormer`
  namespace: `HoloFormer`, `HoloShape`, `CharVocab`, `SubwordVocab`) — needs `InspectStackIter`/
  `InspectAttention`/`DecodeFace`/`EquivCompute`/`InvisibleMultiplier`, none published before 1.5.0.
- `EvaluatedApplications.Tracer` 1.1.0 — The Creature (`Tracer.Helpers.GridTactics`). `EvalApp` comes
  in transitively (AlgFormer's own dependency); Showroom never references it directly.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism (see HoloKernel
  section). A sibling in-repo RCL, not a MonoRepo reference; itself NuGet-only against AlgFormer.
- `TargetFramework=net10.0`, `PublishTrimmed=true` + `RunAOTCompilation=true` (landed 2026-08-28, an
  EXPERIMENT testing whether AOT is worth it for this app's numeric hot loops; `dotnet publish` only,
  not `dotnet run`/dev server — trimming was off over an "EvalApp reflection" worry, now believed
  stale, not independently re-verified). Cost: `dotnet.native.*.wasm` becomes one large AOT module
  (~8 MB compressed) — see Boot screen's compile-gap narration, added because of this.
- **Version bumps only via `dotnet add package`** (latest published) — never hand-edit `<Version>`.
  A capability not yet published is a hand-off to the coordinator, not a reach into MonoRepo source.

## Boundary (hard, from the agent charter)
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call, not this repo's.** Ask for a copy of
  `%LOCALAPPDATA%\Prism\prism-holo.bin` + `-vocab.txt` (+ `-iter.txt`) at a snapshot THEY pick. Drop
  at `Showroom/wwwroot/data/oracle-*` — the page fetches those exact paths, degrades gracefully
  (`_loadError`, no crash) if any are absent.
- **NuGet only, never MonoRepo `ProjectReference`.** Verify any API assumption against the actual
  published DLL before wiring new code to it — MonoRepo source can diverge from what's published
  (bit twice already: `HoloShape.ShiftsFor`'s true default `ratio` is `0.25`, not an ad-hoc guess).
  `HoloKernel` is the one deliberate exception — a sibling in-repo RCL, not MonoRepo.
- Never touch `AboutUs/site/*`, nav, or the shared design system — that's `website-owner`'s (own
  everything under `Showroom/` only; their static pages may *link* to a tool). Never launch the app
  / open a browser session — build-verify only (`dotnet build Showroom.csproj -c Release`);
  demonstrating a tool live is the user's to do.

## Standing technical facts
- **Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-
  channel routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never
  copy another tool's `MinShifts` verbatim.
- `golden: true` on every `HoloFormer` construction so far. WASM has no filesystem — no live-training
  tool can persist a checkpoint ("Reset brain" just drops the in-memory reference). WASM is
  single-threaded (`dotnet run`/dev server stays interpreted; published builds AOT-compile since
  2026-08-28, see Dependencies) — keep live-training shapes small (Creature `d=384,L=1`; Forecaster
  `d=128,L=1`).
- `HoloKernel.RefinementLoop.Observe`/`ObserveSequence` is the house pattern for live-training now
  (wraps `NewGrads()`→`IterAccumulate`/`StackIterAccumulateAllPos`→`Step`). `HoloFormer.TrainStep`
  still exists but does NOT honour the `Iters`/K-pass depth knob — never use it where K matters.
- `HoloFormer` ctor: `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1, embedSeed=null,
  seed=42, bindFfn=false, golden=false, normalize=true, unitary=false)`. `HoloShape` statics:
  `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank`, `CleanCapacity`, `InteractionBudget`, `EquivCompute(d,L,K)`,
  `InvisibleMultiplier(paramCount,d,L,K)` (all `(shifts,d)` unless shown).

## Build / verify
`dotnet build Showroom.csproj -c Release` — green as of 2026-08-28 (0/0) with all four tools + boot
screen + HoloKernel port wired in (`dotnet publish` also spot-checked, since deploy hard-couples to
it — see AboutUs\CLAUDE.md). No test project — verification is build-green + code review; live
behaviour is the user's to check (dev server: `dotnet run`, or the deployed `/tools/` URL).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O, never `Get-Content`/
  `Set-Content` on these files (non-ASCII punctuation throughout → mojibake risk).
- `Home.razor` card hrefs are ABSOLUTE (`/tools/<slug>`); `MainLayout.razor`'s nav (see Site plumbing)
  doesn't list individual tools — a new tool still needs a `Home.razor` gallery card, not a nav entry.
- `EvaluatedApplications.AlgFormer`'s `PrismFormer` namespace (not `AlgFormer`) is where
  `HoloFormer`/`HoloShape`/`SubwordVocab` actually live — easy to reach for the wrong `@using`.

# Showroom — CLAUDE.md (showroom-owner)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/` — see `wwwroot/index.html`). Every tool runs entirely
client-side: no server, no upload, the interesting compute happens in the visitor's own browser tab.
Charter: `MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — it's a sibling repo
that only ever *consumes* MonoRepo packages via published NuGet, never source.

## Purpose
Each tool = a real, working demo of a published `EvaluatedApplications.*` package's actual
capability, driven live by the visitor. No smoke and mirrors, no mocked output. Four tools in the
public gallery: **The Analyst** (HoloDb), **The Creature** (AlgFormer/HoloFormer + Tracer),
**The Forecaster** (AlgFormer/HoloFormer), **Prism** (AlgFormer/HoloFormer, a real trained
checkpoint autocomplete REPL). Plus one **unlisted** page (below) that's a client preview,
not a package-capability demo.

## Site plumbing
- `Program.cs`: standard Blazor WASM host; one scoped `HttpClient` with `BaseAddress =
  HostEnvironment.BaseAddress` (so relative fetches like `data/foo.json` resolve under `/tools/`).
- `App.razor` / `_Imports.razor` / `Layout/MainLayout.razor`: router + shared nav/footer chrome.
  `MainLayout.razor`'s `.nav-links`: `Home · HoloDb · Tools · NuGet` (4 items — `Tools` links back
  to `Home.razor`'s own gallery via `href="."`, resolving to `/tools/` under `<base href="/tools/">`;
  a new tool needs a `Home.razor` card, not a nav entry).
- `Home.razor` (`@page "/"`): the tool gallery. Each tool is a whole-card `<a class="card tool"
  href="/tools/<slug>">` (absolute path) with a `--cat` accent colour, a `live`/`soon` tag, a
  one-line desc, "Open X →". Mirror this shape for any new tool.
- `wwwroot/index.html`: `<base href="/tools/" />`, links the SHARED `/assets/site.css` (the AboutUs
  static site's root, so Showroom borrows the one design system) plus Showroom's OWN
  `wwwroot/css/boot.css` (see Boot screen) and Blazor's bundled `Showroom.styles.css`. Also carries
  the GitHub Pages SPA deep-link restore script and `window.analystDownload` (Blob download helper
  for The Analyst's CSV export).
- `Pages/NotFound.razor` (`@page "/not-found"`): router fallback.
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css`, Blazor-scoped to that component
  only. All four DUPLICATE the same base block (`.room`/`.crumb`/`.room-head h1`/`.lede`/`.badges`/
  `.err`/`.hint`/`.cr-controls`/`.cr-stats`/`.cr-curve`/`.cr-log`/`.outro`) — established house style
  (CSS isolation can't share a partial file across components without a real shared stylesheet
  import, nobody's introduced one). Reuse it verbatim for a new tool.

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL in this repo (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 1.5.0 — a
`ProjectReference` to it is the designed consumption path, not a MonoRepo boundary break. All three
live-brain tools are ported onto it (2026-08-28), verified against the real kernel source first, not
the extraction agent's description. Surface used: `ModelSpec` (shape + the S>1 invariant, and
`Validate()` which also rejects `Layers>1 && KPass>1` — AlgFormer 1.5.0's weight-tied K-pass is
single-layer only, verified live via `NotSupportedException`; irrelevant to all three tools today
since each is `Layers=1`, but a real constraint on ever growing one deeper while keeping K>1),
`HoloSession` (`Create(spec)` / `FromCheckpoint(bytes,kPass,serveAlpha)` — K/alpha mandatory,
`Stats()`), `AlphaRamp` (`Advance`/`Reconstruct`), `RefinementLoop` (`Observe`/`ObserveSequence`,
replaces the `NewGrads→IterAccumulate→Step` triple Creature/Forecaster each wrote independently),
`Decoding` (`DecodePolicy`/`Gate`/`DegenGuard`), `InspectorTrace` (`Inspector.Capture`/`Focus` —
NEW capability for Creature/Forecaster, which had no per-pass view before; both got an opt-in "🔍
inspect brain" toggle. Prism's own Inspector UI was REMOVED, see its own section below — the two
tools' toggles are unrelated and untouched). `ParallelMapping` isn't wired into any tool (inert, not
a gap).

**Cadence gotcha**: `RefinementLoop.Observe` advances its `AlphaRamp` on EVERY call, not once per
episode. Forecaster's old per-CLICK ramp already matched this exactly. Creature's old ramp was
per-EPISODE, so its `IterWarmSteps=300` is an **ESTIMATED, unmeasured** conversion (can't launch the
browser from here) — retune live if the ease-in looks too fast/slow.

**Prism's tokenizer swapped to the published `SubwordVocab`** (was a hand-rolled ~50-line greedy-
longest-match encoder). Verified safe first by reading `MonoRepo\AlgFormer\SubwordVocab.cs` directly:
`CharN=>CharVocab.N=96` already special-cases `Symbol(CharVocab.End)=="\n"` internally, matching
Prism's own "id 95 is both newline and the stop marker" quirk exactly; its ctor takes ONLY the
merges list (not the base chars, which it handles internally — a real gotcha vs. the old hand-rolled
version). Byte-for-byte equivalent semantics confirmed, not "close enough."

**Browser contract (user directive): visitors TRAIN, they never reshape.** "Grow Prism" in the
browser means refining a FIXED-shape model's weights via `RefinementLoop.Observe`/`ObserveSequence`
— that's the whole of it; layers/shifts/dim/context are chosen up front and immutable for the
session. `HoloFormer.GrowLayers`/`.GrowShifts` are real, published, in-place growth methods, but
they're a PrismStudio/server-side operation, not something a visitor triggers or that in-browser
training causes — `HoloKernel` deliberately doesn't wrap either (a bigger/better-trained model
reaches visitors via a new CHECKPOINT, not runtime shape mutation). Worth knowing regardless: the
`Layers>1 && KPass>1` finding above means growing Prism's layers server-side would cost it the
K-pass entirely at 1.5.0 (either/or, not both) — a fact about the package, not the browser tool.

## Boot screen — `wwwroot/index.html` + `wwwroot/css/boot.css`
Retro-terminal boot log, authentically real not decorative: real file names as the WASM host fetches
them (`loadBootResource` hook, pure observation, always returns `undefined` — zero added latency)
plus the framework's own real cumulative-bytes progress (`--blazor-load-percentage`/`-text`, set on
`document.documentElement` by the SDK's own boot script). `autostart="false"` +
`Blazor.start({loadBootResource})` in the next script tag (synchronous order, no `load`-event wait)
installs the hook before the real download starts. `boot.css` is Showroom's OWN file (`site.css`
boundary stays hard) but reuses its global tokens (`--bg`/`--ok`/`--mono`/`--spectrum-*`) for
on-brand styling for free. Prism's checkpoint fetch gets its own step (`Prism.razor`'s `!_loaded`
branch, `LoadStep`/`Begin`/`Finish`) narrating each real asset with real byte counts, reusing
`boot.css`'s classes directly — pure narration, no extra fetch/latency. `MainLayout.razor`'s brand
mark + `index.html`'s favicon carry the site-wide prism-triangle motif, matching all static pages.

## The Analyst — `Pages/Analyst.razor` (route `/analyst`)
In-browser data profiler + live SQL REPL over **HoloDb** (`Database.Open(null)`, in-memory). Sniffs
CSV/TSV/JSON/JSONL/plain-text, infers a type per column, bulk-loads into a real HoloDb table
(100k-row chunks), then profiles every column via HoloDb aggregate queries (COUNT/DISTINCT/
GROUP BY/min/max/mean/3σ-outliers). Also: regex entity extraction (emails/URLs/IPs/dates/money/
@mentions/#tags), computed "what the data says" insights (correlation, best-separating category,
trend, concentration), click-to-filter drill-down, a no-SQL chart builder → HoloDb `GROUP BY` → SVG
chart, a free-form SQL prompt + CSV export. Row cap 500k; entity scan capped 2M chars; upload cap
64MB. Six built-in live public feeds (USGS/NYC/Chicago/Seattle/movies) fetched client-side from
CORS-open sources, or paste any CORS-permitting feed URL. Charts are hand-rolled inline SVG, not a
library. `window.analystDownload` JS interop does the CSV save (Blob URL + synthetic click — `<a
download>` can be CSP-blocked).

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
series.

**Tokenisation** (ported from `MonoRepo\MarketSim\PriceForecaster.cs`; `STOCK_i` token DROPPED —
single-symbol demo): each candle → `[TIME_bucket][RETURN_bucket]`. `TimeBuckets=8` = hour-of-day
(UTC) mod 8 from the candle's real Unix timestamp. `RetEdges` ported VERBATIM: `{-0.0020,-0.0009,
-0.0004,-0.00003,0.00003,0.0004,0.0009,0.0020}` → 9 buckets, `FlatBucket=4`. Vocab=`8+9=17`.
**Known skew**: edges tuned for MarketSim's smaller simulated ticks, so ~65% of bundled transitions
land in the two outermost buckets — but direction split (what accuracy scores) stays near-balanced;
magnitude granularity is compressed, not direction.

**Model shape**: `Dim=128, Layers=1, KPass=2`, `CandleContext=128` → `MaxContext=256` tokens
(2/candle), `MinShifts=8` (verified no-op: `ShiftsFor(256,128)=16` already clears it). Caveat:
`CleanCapacity(16,128)=122` is under the 256-token window — a real v2 tuning knob, not a v1 blocker.

**Training loop** on **HoloKernel**: per tick, predict via `_session.Logits(ctx)`/`Inspector.Capture`
(opt-in) → `RefinementLoop.Observe(ctx, PriceBase+trueBucket)` (trains + advances the α-ramp) →
append the TRUE token to the tape. `IterWarm=40` clicks mapped straight onto `AlphaRamp` with no
conversion (the old loop already advanced once per click, matching `Observe`'s per-call cadence
exactly — unlike Creature). `Lr=0.005` reasoned (between MarketSim's `0.02` and Creature's
`0.0025-0.004`), not yet watched live.

**Data**: `wwwroot/data/forecaster-sample.json` — 450 REAL hourly AAPL closes (~3 months), fetched
once from Yahoo Finance's public chart JSON endpoint, bundled as a static asset. Training cursor
wraps the finite series when it runs out — fine for a demo, a real v2 upgrade path.

**Queued**: live CORS-open feed once one's confirmed browser-fetchable; a symbol picker; tuning
`Lr`/`IterWarm` against an actual observed run.

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
`Gate.Pick` replace the old hand-rolled `GateInfo`/`PickToken`/`TopFaces`/`TopAttn`.
`FromCheckpoint` REQUIRES K/alpha as ctor args
(kernel-enforced — `HoloFormer.Iters`/`.IterAlphaServe` are **NOT persisted** by `Serialize()`,
verified by round-tripping the real checkpoint, always read back `1`/`1`). This file peek-
deserializes the checkpoint once (`HoloFormer.Deserialize`) to read `Layers`/`ParamCount` before
K/alpha are known, then builds the real `HoloSession` from the same bytes (one negligible redundant
deserialize on a 381K-param model). `_stats` (`HoloSession.Stats()`) replaces the old hand-rolled
`EquivCompute`/`InvisibleX` properties.

**K is FIXED, not a control**: a structural fact about the trained checkpoint ("k is not a
parameter, its fixed to the model count" — user), not a visitor-exploreable knob — no slider exists.
`_k` sourced live from `oracle-stackk.txt`/`oracle-iterwarm.txt` (never hardcoded — verified live in
`HoloEngine.cs`: `OneShotStackK=8`, `OneShotIterWarm=20000`; `studio\CLAUDE.md`'s cached `3000` is
stale). `_trainedAlpha` reconstructed via `AlphaRamp.Reconstruct(rounds, addRound:0, iterWarm)` —
valid for a single-layer checkpoint only, falls back to 1.0 otherwise. Current snapshot
(rounds=24,360 > iterWarm=20,000) reconstructs to exactly 1.0 (ramp already done) — coincidental for
THIS snapshot, the mechanism matters for a future mid-ramp one.

**Generation loop**: one `_session.Logits(ctx)` call per character (`HoloSession.Logits` →
`Model.LogitsFor`, which honours `Iters`/K same as the KV-cache-free serving path always did),
`Gate.Pick` draws the token. Confidence gate = `DecodePolicy.Default` (`ConfidentThreshold=0.60`/
`Temperature=0.80`/`FloorK=3.0`/`DegenRepeat=4`, ported verbatim from PrismStudio's `HoloEngine`) +
`DegenGuard` — **verified necessary**: dry-running this algorithm against an earlier live checkpoint
snapshot (round ~21,720) produced a 100%-confidence GREEDY space-repeat on every short prompt tried,
a real repetition-collapse, not a demo bug. Current shipped snapshot (round 24,360) deserialized+
verified `Dim=1536,Layers=1,Shifts=16,ParamCount=381,056`, matching live `OneShot*` shape exactly.

**Inspector REMOVED 2026-08-28, for speed**: Prism used to full-recompute `Inspector.Capture`
(`InspectStackIter`+`InspectAttention`, pricier than a single `LogitsFor` pass) on EVERY emitted
character just to render a per-character trace panel — that per-character recompute was the actual
generation-speed cost, not the model. Removed entirely (panel markup, `Inspector`/`Gate.TopK` call
sites, `_lastTrace`/`CharTrace`/`PassRow`/`FaceItem`/`AttnItem`/`SymDisplay`/`InspectTopK`, the
"Full per-pass trace" badge, the dead `.or-inspector`/`.or-char`/`.or-pass*`/`.or-face`/`.or-attn*`
CSS). Generation now runs the lean `Logits`+`Gate.Pick` path above. Scoped to Prism only — Creature's
and Forecaster's "🔍 inspect brain" toggles are unrelated and untouched.

**Model stats framing**: real `ParamCount` vs. `_stats.EquivCompute` (`12·d²·L·K`, verified
`226,492,416` for this shape) explicitly framed as **compute-equivalence** (matching PrismStudio's
own status-bar wording, "compute-equiv"), not a claim of real stored parameters.

**Cold-start warm-up, compute + render (2026-08-28)** — user-reported: FIRST Continue click after
page load rendered the whole continuation at once (even the prompt echo, `_pendingPrompt`, failed to
render on its own); SECOND click streamed char-by-char at the tuned ~60ms pace correctly. Ruled out
"pending element not in DOM yet" (`_generating=true; StateHasChanged()` already runs before the
loop). Two theories, both addressed since a compute-only fix can't explain the prompt-echo symptom
(pure markup, no inference): (1) interpreted WASM (no AOT) pays a one-time cost on the FIRST call
through `_session.Logits`->`LogitsFor` (K=8 passes, d=1536), synchronous/no `await` so nothing paints
until it returns; (2) `.or-run.pending`'s markup (the `_generating==true` branch of `BuildRenderTree`)
sits inside the `!_loaded` `else` branch, never exercised by the boot log's own renders, so its IL's
first-ever execution is a visitor's first click. Fix, both in `OnInitializedAsync`: a throwaway
`_session.Logits(...)` warm-up (discarded) after `_session`/`_stats` build, then AFTER `_loaded=true`
(needed to select the right branch) a render warm-up — flip `_generating` true with throwaway
prompt/continuation values, one `StateHasChanged()`, revert immediately with NO `await` in between
(no visible flicker). Both wrapped in bare `try/catch` that swallows only (a miss must never surface
as `_loadError`; worst case is pre-fix behaviour); neither touches `_history`.

**Second, independent fix (direct user instruction)**: `await Task.Delay(60)` used to sit INSIDE the
per-character compute loop, serializing real inference to typing speed instead of just pacing the
reveal. `Ask()` is now a producer/consumer split over `System.Threading.Channels.Channel<int>`
(unbounded, single-reader/writer): a `ProduceAsync` local function runs `LogitsFor`/`Gate.Pick`/
`DegenGuard` with NO delay, writing each token the instant it's computed; a separate `await foreach`
over `channel.Reader.ReadAllAsync()` reveals one token per `Task.Delay(60)` tick, decoupled from
compute speed (WASM is single-threaded so this isn't literal parallelism, but it still un-serializes
compute from cadence). `DegenGuard`/`trailedOff`/`MaxReplyChars` unchanged; `_history.Add(...)` still
only after both loops finish. Minor deliberate behavior change: the `DegenGuard`-tripping token is
now written to the channel and revealed mid-stream (previously excluded from the live typewriter,
only baked into the final settled entry) — harmless.

Both build-verified only (0/0) — **neither confirmed with real profiling**, can't launch a browser.
Unknown whether the restructure also incidentally helped issue 1 (no longer blocking the UI thread
for an un-paced burst before the first yield). If the first-click stutter persists, treat both
theories as unproven and look elsewhere (first-paint cost of ANY new DOM subtree, or GC settling
from the checkpoint deserialize) before assuming these fixes were sufficient.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo and NOT in the public gallery — a private, share-by-link-only client
preview for the RecycleDAO PoC (`C:\Users\dongy\RecycleDAO`, separate repo, owned by
`recycledao-owner`; NEVER edit that repo from here). Deliberately absent from `Home.razor`'s gallery
and `MainLayout.razor`'s nav, and carries `<meta name="robots" content="noindex,nofollow">` via
`<HeadContent>` (same pattern as `AboutUs\site\recycledao-preview.html`, which is website-owner's).

**A full eBay-classifieds marketplace**: RCYT is EARNED by verified recycling and SPENT claiming
material other participants rescued from the waste stream — a real token sink, not just a reward
wallet. 21 screens off one `Screen` enum + `Nav` record-struct stack (real Back button); one
`<section class="app-page">` renders at a time, every navigation driven by a card/row/tile/action.

**Mint invariant (must never regress)**: `MintForApproval` is the ONLY method that appends to
`_mintLog`/increases `_totalMinted`/`_lifetimeMinted`, reachable from exactly two call sites (the
verifier queue's `ApproveSubmission`, and seeding). All other marketplace money movement only
*moves* RCYT between balances; no other path adds supply. Tier table verbatim from
`RecycleDAO/docs/demo-mechanics-spec.md` §2 (`Paper/Cardboard=3, Plastic=5, Glass=5,
Metal/Aluminum=8, Electronics/E-waste=15`). Seeding also runs through `MintForApproval`, so every
starting token still has a mint-log/ledger row.

**Chrome honesty**: header/search/category bar/filters genuinely live; only the top utility strip +
footer link columns (+ photo-upload box, notification toggles) stay inert, tagged `.mk-tag` "mockup".

**Hard boundaries kept** (recycledao-owner's charter): testnet-only banners on page head/checkout/
wallet; verification = manual human review, not solved fraud-proofing; NO referral/invite/share-to-
earn; no fiat/top-up/cash-out; no wallet-connect (disabled toggle); NO governance/voting screen. Sim
counterparty actions only fire from a labelled demo control, never a timer.

**Verified gotcha**: Blazor scoped CSS DOES apply the `b-*` scope attribute inside `RenderFragment<T>`
templates in the same `.razor` file — a shared `ListingCard`/`ListingRow` templated-delegate helper
styles correctly and beats duplicating card markup across screens.

## Dependencies (exact NuGet versions, `Showroom.csproj`)
- `Microsoft.AspNetCore.Components.WebAssembly` 10.0.8 (+ `.DevServer` 10.0.8, dev-only)
- `EvaluatedApplications.HoloDb` 1.4.0 — The Analyst
- `EvaluatedApplications.AlgFormer` **1.5.0** — The Creature, The Forecaster, Prism (`PrismFormer`
  namespace: `HoloFormer`, `HoloShape`, `CharVocab`, `SubwordVocab`) — needs `InspectStackIter`/
  `InspectAttention`/`DecodeFace`/`EquivCompute`/`InvisibleMultiplier`, none published before 1.5.0.
- `EvaluatedApplications.Tracer` 1.1.0 — The Creature (`Tracer.Helpers.GridTactics`)
- `EvaluatedApplications.EvalApp` comes in transitively (AlgFormer's own dependency); Showroom
  never references it directly
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism (see HoloKernel
  section). A sibling in-repo RCL, not a MonoRepo reference; itself NuGet-only against AlgFormer.
- `TargetFramework=net10.0`, `PublishTrimmed=false` (our libraries use reflection the trimmer can't
  fully see — a demo values reliability over a few MB of download)
- **Version bumps only via `dotnet add package`** (latest published) — never hand-edit `<Version>`.
  A capability not yet published is a hand-off to the coordinator, not a reach into MonoRepo source.

## Boundary (hard, from the agent charter)
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call, not this repo's.** What to ask for: a
  copy of `%LOCALAPPDATA%\Prism\prism-holo.bin` + `-vocab.txt` (+ `-iter.txt`, the round counter) at
  a snapshot THEY pick. Drop the files at `Showroom/wwwroot/data/oracle-*` — the page already fetches
  those exact paths and degrades gracefully (`_loadError`, no crash) if any are absent.
- **NuGet only, never MonoRepo `ProjectReference`.** Verify any API assumption against the actual
  published DLL (throwaway reflection probe, or a scratch console project) before wiring new code to
  it — MonoRepo source can diverge from what's actually published. This bit twice already:
  `HoloShape.ShiftsFor`'s true default `ratio` is `0.25`, not whatever an ad-hoc guess assumes.
  (`HoloKernel` is the one deliberate exception — a sibling in-repo RCL, not MonoRepo.)
- Never touch `AboutUs/site/*`, nav, or the shared design system — that's `website-owner`'s. Own
  everything under `Showroom/` only; `website-owner`'s static pages may *link* to a tool.
- Never launch the app / open a browser session — build-verify only (`dotnet build
  Showroom.csproj -c Release`). Demonstrating a tool live is the user's to do.

## Standing technical facts
- **Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-
  channel routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never
  copy another tool's `MinShifts` verbatim.
- `golden: true` on every `HoloFormer` construction so far. WASM has no filesystem — no live-training
  tool can persist a checkpoint (a "Reset brain" button just drops the in-memory reference). WASM is
  single-threaded/interpreted (no AOT) — keep live-training shapes small (Creature `d=384,L=1`;
  Forecaster `d=128,L=1`).
- `HoloKernel.RefinementLoop.Observe`/`ObserveSequence` is the house pattern for live-training now
  (wraps the same `NewGrads()`→`IterAccumulate`/`StackIterAccumulateAllPos`→`Step` triple).
  `HoloFormer.TrainStep(toks, answer, lr)` still exists but does NOT honour the `Iters`/K-pass depth
  knob — never use it where weight-tied extra passes matter.
- `HoloFormer`'s public ctor: `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1,
  embedSeed=null, seed=42, bindFfn=false, golden=false, normalize=true, unitary=false)`. `HoloShape`
  statics: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank(shifts,d)`, `CleanCapacity(shifts,d)`,
  `InteractionBudget(shifts,d)`, `EquivCompute(d,L,K)`, `InvisibleMultiplier(paramCount,d,L,K)`.

## Build / verify
```
dotnet build Showroom.csproj -c Release
```
Green as of 2026-08-28 (0 warnings, 0 errors) with all four tools + boot screen + HoloKernel port
wired in (`dotnet publish` also spot-checked, since deploy hard-couples to it — see AboutUs\
CLAUDE.md). No test project exists — verification is build-green + code review; live behaviour is
the user's to check (dev server: `dotnet run` here, or the deployed `/tools/` URL once pushed).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O, never `Get-Content`/
  `Set-Content` on these files (non-ASCII punctuation throughout → mojibake risk).
- `Home.razor` card hrefs are ABSOLUTE (`/tools/<slug>`); `MainLayout.razor`'s nav no longer lists
  individual tools (see HoloKernel section's nav-trim note) — a new tool still needs a `Home.razor`
  gallery card, just not a nav entry.
- `EvaluatedApplications.AlgFormer`'s `PrismFormer` namespace (not `AlgFormer`) is where
  `HoloFormer`/`HoloShape`/`SubwordVocab` actually live — easy to reach for the wrong `@using`.

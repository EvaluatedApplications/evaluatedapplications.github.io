# Showroom — CLAUDE.md (showroom-owner)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/` — see `wwwroot/index.html`). Every tool runs entirely
client-side: no server, no upload, the interesting compute happens in the visitor's own browser tab.
Charter: `MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — it's a sibling repo
that only ever *consumes* MonoRepo packages via published NuGet, never source.

## Purpose
Each tool = a real, working demo of a published `EvaluatedApplications.*` package's actual
capability, driven live by the visitor. No smoke and mirrors, no mocked output. Three tools listed
in the public gallery today: **The Analyst** (HoloDb), **The Creature** (AlgFormer/HoloFormer +
Tracer), **The Forecaster** (AlgFormer/HoloFormer). New tools follow the same shape. Plus one
**unlisted** page (see below) that's a client preview, not a package-capability demo.

## Site plumbing
- `Program.cs`: standard Blazor WASM host; one scoped `HttpClient` with `BaseAddress =
  HostEnvironment.BaseAddress` (so relative fetches like `data/foo.json` resolve under `/tools/`).
- `App.razor` / `_Imports.razor` / `Layout/MainLayout.razor`: router + shared nav/footer chrome.
  `MainLayout.razor`'s `.nav-links` lists every tool by relative href (`analyst`, `creature`,
  `forecaster`) — add a new tool here too, not just Home.razor's gallery.
- `Home.razor` (`@page "/"`): the tool gallery. Each tool is a whole-card `<a class="card tool"
  href="/tools/<slug>">` (absolute path, matches the deployed base href) with a `--cat` accent
  colour, a `live`/`soon` tag, a one-line desc, "Open X →". Mirror this shape for any new tool.
- `wwwroot/index.html`: `<base href="/tools/" />`, links the SHARED site stylesheet
  `/assets/site.css` (absolute path — resolves to the AboutUs static site's root, not `/tools/`,
  so Showroom borrows the one shared design system) plus Blazor's own bundled
  `Showroom.styles.css` (auto-aggregated from every `*.razor.css`). Also carries the GitHub Pages
  SPA deep-link restore script and `window.analystDownload` (client-side Blob download helper used
  by The Analyst's CSV export — no server involved).
- `Pages/NotFound.razor` (`@page "/not-found"`): router fallback.
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css`, Blazor-scoped to that component
  only. All three currently DUPLICATE the same base block (`.room`/`.crumb`/`.room-head
  h1`/`.lede`/`.badges`/`.err`/`.hint`/`.cr-controls`/`.cr-stats`/`.cr-curve`/`.cr-log`/`.outro`) —
  this is the established house style (CSS isolation can't share a partial file across components
  without a real shared stylesheet import, which nobody's introduced yet), not an oversight. Reuse
  it verbatim for a new tool; don't invent a different shape.

## The Analyst — `Pages/Analyst.razor` (route `/analyst`)
In-browser data profiler + live SQL REPL over **HoloDb** (`Database.Open(null)`, in-memory).
Sniffs CSV/TSV/JSON/JSONL/plain-text (RFC-4180-aware quoting), infers a type per column
(Integer/Decimal/Date/Boolean/Category/Text), bulk-loads into a real HoloDb table
(`db.BulkLoad("data", ...)` in 100k-row chunks), then profiles every column via HoloDb aggregate
queries (COUNT/DISTINCT/GROUP BY/min/max/mean/3σ-outliers). Also: regex entity extraction
(emails/URLs/IPs/dates/money/@mentions/#tags), computed "what the data says" insights (Pearson
correlation between numeric fields, which category best separates a measure, first-third-vs-last-
third trend, concentration), click-to-filter drill-down (re-profiles the loaded table against a
`WHERE`), a no-SQL chart builder (breakdown + aggregate → HoloDb `GROUP BY` → SVG chart), a free-
form SQL prompt with query suggestions and CSV export. Row cap 500k; entity scan capped at 2M
chars; upload cap 64MB. Six built-in live public feeds (USGS earthquakes, NYC 311/traffic, Chicago
crime, Seattle weather, movies) fetched client-side from CORS-open sources — "…or paste any CSV/
JSON feed URL" also works for any CORS-permitting endpoint. Charts are hand-rolled inline SVG
(bars/pie/line, `RenderChart`), not a library. `window.analystDownload` JS interop does the CSV
save (no `<a download>` — browser sandboxes/CSP can block that; a Blob URL + synthetic click works
everywhere including the Showroom's own hosting).

## The Creature — `Pages/Creature.razor` (route `/creature`)
A 20×20 grid the visitor draws (walls/start/apples) where a **HoloFormer** brain learns to forage,
live. Brain shape: `Dim=384, Layers=1, KPass(Iters)=2` (weight-tied extra pass, α ramps 0→1 over
`IterWarm=20` episodes — identity-init ease-in so the single-pass base learns first), `MaxCtx=32`
(a focused recent-trajectory window measured to converge faster than a longer one — dilution of
the decisive last-token signal was the failure mode), `MinShifts=8` floor (natural
`HoloShape.ShiftsFor(32,384)` returns 1 — a pure diagonal, no cross-channel routing — so it's
floored; derived via `bindRank=shifts·d/2`, see the file's own comment for the full reasoning, not
just copied elsewhere). Oracle: **Tracer**'s `GridTactics.Reachable` BFS gives a distance-to-
nearest-apple field; the brain is trained toward the DECISIVE move (advantage-weighted: best move
minus the mean of legal moves) so it commits to eating rather than lingering in the scent. Training
loop: `NewGrads()` → `IterAccumulate(ctx, target, g, KPass, KAlpha)` → `Step(g, lr)`, once per
informative step at episode end; `LogitsFor` honours `Iters` at serve time so serve always matches
the current ramped α. `ResetBrain` just drops the reference (WASM has no filesystem — nothing to
delete on disk).

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same **HoloFormer** substrate as The Creature, pointed at a price tape instead of a foraging grid.
Predicts the direction (and coarse magnitude) of the next hourly tick for one bundled real stock
series. Milestone 1, shipped 2026-08-26.

**Tokenisation** (ported from `MonoRepo\MarketSim\PriceForecaster.cs`'s proven scheme; the
`STOCK_i` token is DROPPED — single-symbol demo): each candle → a `[TIME_bucket][RETURN_bucket]`
token pair. `TimeBuckets=8` = hour-of-day (UTC) mod 8, computed from the candle's real Unix
timestamp (`DateTimeOffset.FromUnixTimeSeconds(...).TimeOfDay.TotalHours % 8`) — NOT MarketSim's
`p % TimeBuckets` index-modulo fallback (that fallback exists because MarketSim's seeded history
lacks real per-candle timestamps; our bundled data has real ones, so we use them). `RetEdges`
ported VERBATIM from `PriceForecaster.RetEdges`: `{-0.0020,-0.0009,-0.0004,-0.00003,0.00003,
0.0004,0.0009,0.0020}` → 9 buckets, `FlatBucket=4`. Vocab = `TimeBuckets(8) + PriceBuckets(9) =
17`. **Known skew**: these edges were tuned for MarketSim's small simulated poll-to-poll ticks; a
real *hourly* AAPL move is bigger, so ~65% of the bundled sample's transitions land in the two
outermost buckets (measured: bucket 0 = 138/449, bucket 8 = 155/449). Direction split (`Dir`: <4
down, >4 up, =4 flat) stays near-balanced (~209/5/235) — and direction is what accuracy scores, so
it's magnitude granularity that's compressed, not direction.

**Model shape**: `Dim=128` (MarketSim's tuned width), `Layers=1`, `KPass(Iters)=2` weight-tied,
α ramps 0→1 over `IterWarm=40` training clicks (Creature-style ease-in, roughly double Creature's
20 since one supervised tick-step here is a noisier signal than Creature's advantage-weighted
per-episode update — not measured, a reasoned starting point). `CandleContext=128` candles →
`MaxContext=256` tokens (2 tokens/candle). **Shifts**: `Math.Max(HoloShape.ShiftsFor(256,128),
MinShifts=8)`. Verified by reflecting the published `EvaluatedApplications.AlgFormer` 1.2.0 DLL
(`ShiftsFor(ctx,d,ratio=0.25)` two-arg call, matching how this code and Creature both call it):
`ShiftsFor(256,128)=16` on its own — already clears the `MinShifts=8` floor, so the floor is a
defensive no-op here, not the active constraint (unlike Creature, where the natural value floors
to a diagonal and the floor DOES bite). `MinShifts=8` itself is derived the same way as Creature's
— predicting 1-of-9 buckets is the same task-complexity order as Creature's 1-of-8 move choice, so
it earns the same `bindRank/d = shifts/2 = 4` target ratio. Honest caveat: `CleanCapacity(16,128)
=122` is under the 256-token window, so the older half of a full context can read back less
cleanly than the recent half — not a v1 blocker, a real v2 tuning knob. `golden: true` per standing
fact. `frozenPrefix: 0` (mirrors `PriceForecaster`'s own choice — no cell-identity structure here
needing partial freezing, unlike Creature's grid-cell vocab).

**Training loop** — same shape as Creature, NOT `HoloFormer.TrainStep` (that bare method doesn't
honour the K-pass depth knob): per click/tick, `LogitsFor(ctx)` predicts (honours `Iters` at serve
time) → score direction-correctness against the true bucket → `NewGrads()` → `IterAccumulate(ctx,
PriceBase+trueBucket, g, KPass, KAlpha)` → `Step(g, Lr=0.005)` → append the TRUE token (not the
prediction) to the growing tape → advance the α-ramp counter and re-sync `IterAlphaServe`. `Lr=
0.005` is a reasoned starting point (between MarketSim's batched-epoch `0.02` and Creature's
per-step `0.0025–0.004`), not yet watched live.

**Data**: `wwwroot/data/forecaster-sample.json` — 450 REAL hourly AAPL closes (~3 months, `{t:
unixSeconds, c:close}`), fetched once from Yahoo Finance's public chart JSON endpoint
(`query1.finance.yahoo.com/v8/finance/chart/AAPL?range=3mo&interval=60m`) and bundled as a static
asset (fetched via the scoped `HttpClient` at `OnInitializedAsync`, relative URL so it resolves
under `/tools/`). Not a live feed: Yahoo's chart endpoint doesn't send CORS headers, so a browser
can't fetch it directly at demo time (the earlier PowerShell probe hit no such block server-side,
but that's not evidence of a browser-CORS allow — untested from a browser and not to be assumed).
The training cursor wraps around this finite 449-transition series when it runs out — expected and
fine for a demo, worth another real series or a genuine live feed for v2.

**Queued (not built)**: live CORS-open price feed as a v2 upgrade path (mirror The Analyst's feed-
picker UX once a real CORS-permitting quote source is confirmed browser-fetchable); a symbol
picker if more than one bundled series is ever added; tuning `Lr`/`IterWarm` against an actual
observed training run (nobody has watched this one train yet — build-verified only, per this
agent's boundary on launching a browser).

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo and NOT in the public gallery — a private, share-by-link-only client
preview for the RecycleDAO PoC (`C:\Users\dongy\RecycleDAO`, separate repo, owned by
`recycledao-owner`; NEVER edit that repo from here). Deliberately absent from `Home.razor`'s gallery
and `MainLayout.razor`'s nav, and carries `<meta name="robots" content="noindex,nofollow">` via
`<HeadContent>` (same pattern as `AboutUs\site\recycledao-preview.html`, which is website-owner's).

**Rebuilt 2026-08-26 as a full eBay-classifieds marketplace** (client steer via Antonio: "should
feel like eBay classifieds… prototype all the screens a finished product needs"). Domain mapping
chosen: RCYT is EARNED by verified recycling and SPENT claiming material other participants rescued
from the waste stream — a circular-economy classifieds with a real token sink, not just a reward
wallet. 21 screens off one `Screen` enum + `Nav` record-struct stack (real Back button); one
`<section class="app-page">` renders at a time; every navigation is driven by a card/row/tile/action.

**Mint invariant (must never regress)**: `MintForApproval` is the ONLY method that appends to
`_mintLog` or increases `_totalMinted`/`_lifetimeMinted`, and it's reachable from exactly two call
sites — the verifier queue's `ApproveSubmission`, and seeding. All marketplace money movement
(`PlaceOrder` hold → `ConfirmCollected` release / `CancelOrder` refund / sale credit) only *moves*
RCYT between balances; no path adds supply. Decided submissions are REMOVED from `_pending`, not
disabled, so nothing double-mints. Tier table is verbatim from `RecycleDAO/docs/demo-mechanics-spec.md`
§2 (`Paper/Cardboard=3, Plastic=5, Glass=5, Metal/Aluminum=8, Electronics/E-waste=15`).

**Seeding**: 8 personas, 20 listings, 9 already-approved + 3 pending submissions, 1 thread, reviews,
notifications. The 67 RCYT opening balance is NOT assigned — seeded submissions run through
`MintForApproval` itself, so every starting token has a mint-log and ledger row. Marketplace
categories are a SUPERSET of the 5 reward item types (3 reuse-only categories have no mint rate).

**Chrome honesty**: the old "everything decorative" top nav is gone — header/search/category
bar/filters are now genuinely live (a browsable marketplace can't be inert). Only two inert blocks
remain, both tagged `.mk-tag` "mockup": the top utility strip and the footer link columns (plus the
photo-upload box and notification toggles). The honesty callout enumerates exactly which is which.

**Hard boundaries kept** (recycledao-owner's charter): testnet-only banners (`.testnet`) on the page
head, checkout and wallet; verification framed as manual human review, never solved fraud-proofing;
NO referral/invite/share-to-earn anywhere (stated explicitly in the FAQ); no fiat/top-up/cash-out
path; no wallet-connect (the Settings toggle for it is deliberately disabled); NO governance/voting
screen — spec §5 forbids previewing Governor/Timelock, which aren't built. Simulated counterparty
actions only ever fire from a labelled demo control ("Simulate a claim"), never a timer.

**Verified gotcha (don't re-derive)**: Blazor scoped CSS DOES apply the `b-*` scope attribute inside
`RenderFragment<T>` templates declared in the same `.razor` file — confirmed by emitting the Razor
source generator output (`/p:EmitCompilerGeneratedFiles=true`) and reading the generated
`__builder2` calls. So the shared `ListingCard`/`ListingRow` templates style correctly; a
templated-delegate helper is safe here and beats duplicating card markup across 4 screens.
## Dependencies (exact NuGet versions, `Showroom.csproj`)
- `Microsoft.AspNetCore.Components.WebAssembly` 10.0.8 (+ `.DevServer` 10.0.8, dev-only)
- `EvaluatedApplications.HoloDb` 1.4.0 — The Analyst
- `EvaluatedApplications.AlgFormer` 1.2.0 — The Creature, The Forecaster (`PrismFormer` namespace:
  `HoloFormer`, `HoloShape`)
- `EvaluatedApplications.Tracer` 1.1.0 — The Creature (`Tracer.Helpers.GridTactics`)
- `EvaluatedApplications.EvalApp` comes in transitively (AlgFormer's own dependency); Showroom
  never references it directly
- `TargetFramework=net10.0`, `PublishTrimmed=false` (our libraries use reflection the trimmer can't
  fully see — a demo values reliability over a few MB of download)
- **Version bumps happen only via `dotnet add package`** (picks up the latest published version) —
  never hand-edit a `<Version>` here; that's how this file stays honest about what's actually
  wired in. If a tool needs a capability not yet in the published version, that's a hand-off to the
  coordinator (flag the exact gap), not a reach into MonoRepo source.

## Boundary (hard, from the agent charter)
- **NuGet only, never MonoRepo `ProjectReference`.** Verify any API assumption against the actual
  published DLL (e.g. via a throwaway reflection probe, or a scratch console project referencing
  the same `PackageReference` version) before wiring new code to it — MonoRepo source can diverge
  from what's actually published. This bit twice already: `HoloShape.ShiftsFor`'s true default
  `ratio` is `0.25`, not whatever an ad-hoc guess assumes; always check via `GetParameters()` /
  `DefaultValue` rather than assuming.
- Never touch `AboutUs/site/*`, nav, or the shared design system — that's `website-owner`'s. Own
  everything under `Showroom/` only; `website-owner`'s static pages may *link* to a tool, that's
  the extent of the overlap.
- Never launch the app / open a browser session — build-verify only (`dotnet build
  Showroom.csproj -c Release`). Demonstrating a tool live is the user's to do.

## Standing technical facts
- **Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-
  channel routing. Re-derive a floor from `bindRank = shifts·d/2` for each tool's own d/context;
  never copy another tool's `MinShifts` number verbatim (Creature's 8 and the Forecaster's 8 landed
  on the same number by independent reasoning about comparable task complexity, not by copying).
- `golden: true` on every `HoloFormer` construction so far — keep it unless a specific, verified
  reason says otherwise.
- WASM has no filesystem — no live-training tool can persist a checkpoint. `HoloFormer.Serialize`/
  `Deserialize` exist on the published API (verified) but nothing here calls them; a "Reset brain"
  button just drops the in-memory reference. Design new tools with that constraint in mind.
- WASM is single-threaded/interpreted (no AOT) — keep live-training model shapes small (Creature
  `d=384,L=1`; Forecaster `d=128,L=1`; MarketSim's own server-side proven shape is `d=128,L=4,K=2`
  — ours is intentionally shallower than MarketSim's since it trains one step per click instead of
  a full epoch per server tick).
- The manual `NewGrads()` → `IterAccumulate`/`StackIterAccumulate` → `Step` loop is the house
  pattern for a live-training tool. `HoloFormer.TrainStep(toks, answer, lr)` exists on the
  published API but does NOT honour the `Iters`/K-pass depth knob — never use it for a tool that
  wants weight-tied extra passes.
- `HoloFormer`'s real public surface (verified against the published 1.2.0 DLL, not assumed from
  any other repo): constructor is `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1,
  embedSeed=null, seed=42, bindFfn=false, golden=false, normalize=true, unitary=false)` — named
  args after the first four positional ones, exactly how Creature and Forecaster both call it.
  `HoloShape` static helpers: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank(shifts,d)`,
  `CleanCapacity(shifts,d)`, `InteractionBudget(shifts,d)`.

## Build / verify
```
dotnet build Showroom.csproj -c Release
```
Green as of 2026-08-26 (0 warnings, 0 errors) with all three tools wired in. No test project exists
for Showroom — verification is build-green + code review; live behaviour is the user's to check
(dev server: `dotnet run` from this directory, or the deployed `/tools/` URL once pushed).

## Gotchas
- Windows/PS 5.1: edit via Read/Edit/Write or UTF-8-safe .NET I/O, never `Get-Content`/
  `Set-Content` on these files (non-ASCII punctuation throughout → mojibake risk).
- `Home.razor` card hrefs are ABSOLUTE (`/tools/<slug>`) matching the deployed base href;
  `MainLayout.razor`'s nav hrefs are RELATIVE (`<slug>`, resolved against the current base href
  from wherever the visitor currently is). Both need a new tool added, in their own style — don't
  copy one pattern into the other spot.
- `EvaluatedApplications.AlgFormer`'s `PrismFormer` namespace (not `AlgFormer`) is where
  `HoloFormer`/`HoloShape` actually live — easy to reach for the wrong `@using`.

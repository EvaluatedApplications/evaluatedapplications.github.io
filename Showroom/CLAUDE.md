# Showroom — CLAUDE.md (showroom-owner)

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/` — see `wwwroot/index.html`). Every tool runs entirely
client-side: no server, no upload, the interesting compute happens in the visitor's own browser tab.
Charter: `MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — it's a sibling repo
that only ever *consumes* MonoRepo packages via published NuGet, never source.

## Purpose
Each tool = a real, working demo of a published `EvaluatedApplications.*` package's actual
capability, driven live by the visitor. No smoke and mirrors, no mocked output. Four tools listed
in the public gallery today: **The Analyst** (HoloDb), **The Creature** (AlgFormer/HoloFormer +
Tracer), **The Forecaster** (AlgFormer/HoloFormer), **Prism** (AlgFormer/HoloFormer, a real
trained checkpoint + full per-pass inspector). New tools follow the same shape. Plus one
**unlisted** page (see below) that's a client preview, not a package-capability demo.

## Site plumbing
- `Program.cs`: standard Blazor WASM host; one scoped `HttpClient` with `BaseAddress =
  HostEnvironment.BaseAddress` (so relative fetches like `data/foo.json` resolve under `/tools/`).
- `App.razor` / `_Imports.razor` / `Layout/MainLayout.razor`: router + shared nav/footer chrome.
  `MainLayout.razor`'s `.nav-links` lists every tool by relative href (`analyst`, `creature`,
  `forecaster`, `prism`) — add a new tool here too, not just Home.razor's gallery.
- `Home.razor` (`@page "/"`): the tool gallery. Each tool is a whole-card `<a class="card tool"
  href="/tools/<slug>">` (absolute path) with a `--cat` accent colour, a `live`/`soon` tag, a
  one-line desc, "Open X →". Mirror this shape for any new tool.
- `wwwroot/index.html`: `<base href="/tools/" />`, links the SHARED `/assets/site.css` (absolute
  path — the AboutUs static site's root, not `/tools/`, so Showroom borrows the one design system)
  plus Blazor's own bundled `Showroom.styles.css`. Also carries the GitHub Pages SPA deep-link
  restore script and `window.analystDownload` (Blob download helper for The Analyst's CSV export).
- `Pages/NotFound.razor` (`@page "/not-found"`): router fallback.
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css`, Blazor-scoped to that component
  only. All four currently DUPLICATE the same base block (`.room`/`.crumb`/`.room-head
  h1`/`.lede`/`.badges`/`.err`/`.hint`/`.cr-controls`/`.cr-stats`/`.cr-curve`/`.cr-log`/`.outro`) —
  established house style (CSS isolation can't share a partial file across components without a
  real shared stylesheet import, nobody's introduced one). Reuse it verbatim for a new tool.

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
A 20×20 grid the visitor draws (walls/start/apples) where a **HoloFormer** brain learns to forage,
live. Brain shape: `Dim=384, Layers=1, KPass(Iters)=2` (weight-tied extra pass, α ramps 0→1 over
`IterWarm=20` episodes — identity-init ease-in), `MaxCtx=32` (a focused recent-trajectory window,
measured to converge faster than a longer one — dilution of the decisive last-token signal was the
failure mode), `MinShifts=8` floor (natural `HoloShape.ShiftsFor(32,384)` returns 1 — a pure
diagonal, no cross-channel routing — so it's floored; derived via `bindRank=shifts·d/2`, full
reasoning in the file's own comment). Distance field: **Tracer**'s `GridTactics.Reachable` BFS to
the nearest apple; the brain trains toward the DECISIVE move (advantage-weighted: best move minus
the mean of legal moves) so it commits to eating rather than lingering in the scent. Training loop:
`NewGrads()` → `IterAccumulate(ctx, target, g, KPass, KAlpha)` → `Step(g, lr)` once per informative
step at episode end; `LogitsFor` honours `Iters` at serve time. `ResetBrain` just drops the
reference (WASM has no filesystem).

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same **HoloFormer** substrate as The Creature, pointed at a price tape instead of a foraging grid.
Predicts the direction (and coarse magnitude) of the next hourly tick for one bundled real stock
series. Milestone 1, shipped 2026-08-26.

**Tokenisation** (ported from `MonoRepo\MarketSim\PriceForecaster.cs`; `STOCK_i` token DROPPED —
single-symbol demo): each candle → `[TIME_bucket][RETURN_bucket]`. `TimeBuckets=8` = hour-of-day
(UTC) mod 8 from the candle's real Unix timestamp (not MarketSim's index-modulo fallback — our
bundled data has real timestamps). `RetEdges` ported VERBATIM: `{-0.0020,-0.0009,-0.0004,-0.00003,
0.00003,0.0004,0.0009,0.0020}` → 9 buckets, `FlatBucket=4`. Vocab=`8+9=17`. **Known skew**: edges
tuned for MarketSim's smaller simulated ticks, so ~65% of bundled transitions land in the two
outermost buckets (measured 138/449, 155/449) — but direction split (what accuracy scores) stays
near-balanced (~209/5/235); it's magnitude granularity that's compressed, not direction.

**Model shape**: `Dim=128, Layers=1, KPass(Iters)=2` weight-tied, α ramps over `IterWarm=40` clicks
(double Creature's 20 — a single supervised tick-step here is noisier than Creature's advantage-
weighted update, reasoned not measured). `CandleContext=128` → `MaxContext=256` tokens (2/candle).
`Shifts=Math.Max(HoloShape.ShiftsFor(256,128), MinShifts=8)` — verified `ShiftsFor(256,128)=16`
already clears the floor (floor is a no-op here, unlike Creature where it bites); `MinShifts=8`
derived the same `bindRank/d=shifts/2=4` way as Creature's (1-of-9 buckets ≈ 1-of-8 moves in task
complexity). Caveat: `CleanCapacity(16,128)=122` is under the 256-token window — the older context
half reads back less cleanly (v2 tuning knob, not a v1 blocker). `golden:true`, `frozenPrefix:0`.

**Training loop** — same shape as Creature, NOT `HoloFormer.TrainStep` (ignores K-pass): per tick,
`LogitsFor(ctx)` predicts (honours `Iters`) → score vs true bucket → `NewGrads()` →
`IterAccumulate(ctx, PriceBase+trueBucket, g, KPass, KAlpha)` → `Step(g, Lr=0.005)` → append the
TRUE token to the tape → advance the α-ramp. `Lr=0.005` reasoned (between MarketSim's `0.02` and
Creature's `0.0025-0.004`), not yet watched live.

**Data**: `wwwroot/data/forecaster-sample.json` — 450 REAL hourly AAPL closes (~3 months), fetched
once from Yahoo Finance's public chart JSON endpoint, bundled as a static asset (not a live feed —
Yahoo's endpoint doesn't send CORS headers browser-side). Training cursor wraps the finite
449-transition series when it runs out — fine for a demo, a real v2 upgrade path.

**Queued**: live CORS-open feed once one's confirmed browser-fetchable; a symbol picker; tuning
`Lr`/`IterWarm` against an actual observed run (build-verified only so far).

## Prism — `Pages/Prism.razor` (route `/prism`)
A REPL over a real, point-in-time COPY of the user's own live `prism-holo.bin` HoloFormer checkpoint
from PrismStudio, with a full per-character Inspector trace underneath — same spirit as PrismStudio's
own Inspect tab (`HoloEngine.InspectResponse`/`GateInfo`/`PickToken`, sibling `PrismFormer` repo,
`studio\PrismGym\HoloEngine.cs`, read as a REFERENCE for data/semantics only — nothing here
`ProjectReference`s it; everything is published-NuGet API or a from-scratch reimplementation of
small, verified algorithms, same pattern as the Forecaster's ported tokenization). Renamed from
"The Oracle" 2026-08-28 (route `/oracle`→`/prism`, files `Oracle.razor`(`.css`)→`Prism.razor`(`.css`))
to match the product line's naming; checkpoint asset filenames (`oracle-brain.bin`/`-vocab.txt`/
`-rounds.txt`) deliberately kept as-is — internal names, no need to track the public tool name.
**Checkpoint asset status: SHIPPED** (`oracle-brain.bin` ~3.0MB, `-vocab.txt` 128B, `-rounds.txt` 5B
all present; loads fail gracefully, `_loadError`, if any ever go missing in a future build).

**Autocomplete, not chat (fixed 2026-08-28)**: trained on raw text continuation only, no turn-taking —
but the UI presented it as a two-party chat (`_transcript` of `(who,text)`, "you"/"prism" bubbles,
"Ask" button). Verified the generation loop itself never leaked cross-run context (`Ask()` seeds `seq`
fresh from `Encode(promptText)` every call) — a UI/copy bug, not a real turn-history leak. Reworked to
`_history: List<RunEntry(Prompt, Continuation, TrailedOff)>`, prompt-text immediately followed by
continuation-text in one run (Copilot-suggestion style, no speaker labels/bubbles), button "Continue"/
"continuing…", CSS `.or-chat/-transcript/-msg/-who/-text` → `.or-runs/-run-list/-run/-prompt/-cont/
-note`. Lede/hint/outro copy states per-run independence explicitly now. `Home.razor` dropped "Chat with".

**Published API used** (verified against the real 1.5.0 DLL — this tool is WHY AlgFormer was bumped
1.2.0→1.5.0): `HoloFormer.Deserialize(byte[])`, `.InspectStackIter(ctx,K,alpha)` (per-pass raw
"faces"), `.InspectAttention(ctx,K,alpha)` (per-pass source-position resonance rows),
`.DecodeFace(double[])` (decode a face against the codebook), `HoloShape.EquivCompute(d,L,K)`
(`12·d²·L·K`, dense-transformer-equivalent compute) and `.InvisibleMultiplier(paramCount,d,L,K)`
(ratio to real `ParamCount`) — drive the "model stats" cards. `PrismFormer.CharVocab.Lo/Hi/End`
(32/126/95) are the base-96-symbol alphabet. NOT used: `InspectRetrieval` (unverified semantics, not
called by the reference `InspectResponse`, left out rather than guessed at).

**GOTCHA, verified by round-tripping the real checkpoint through `Serialize()`/`Deserialize()`**:
`HoloFormer.Iters`/`.IterAlphaServe` (K-pass depth) are **NOT persisted** — always read back `1`/`1`.

**K is FIXED, not a control (2026-08-28, two passes)**: pass 1 defaulted the slider to the real
trained depth; the user then caught that a slider shouldn't exist AT ALL ("k is not a parameter, its
fixed to the model count") — K is a structural fact about the trained checkpoint, not something a
visitor explores. Slider UI removed entirely (`KSliderMax`/`OnKChanged` deleted); `_k` (renamed from
`_kUser`) is set ONCE at load from the real trained `OneShotStackK` and never mutated again. Shown
as a plain fact badge (`K=@_k pass(es)`, next to `d=`/layer(s)/shifts) and in the outro's "Being
honest about it" paragraph, not a `<input type=range>`. Still sourced live from `oracle-stackk.txt`/
`oracle-iterwarm.txt` (never hardcoded — same hand-off pattern as `oracle-rounds.txt`, both are the
user's own live-edited training knobs); verified live in `HoloEngine.cs` 2026-08-28: `OneShotStackK
=8`, `OneShotIterWarm=20000` (`studio\CLAUDE.md`'s `3000` is stale — the live `const` line is the
only reliable source). **Alpha reconstructed, not assumed 1.0**: `HoloEngine.AlphaFor`'s formula
(`clamp((trainedRounds-addRound)/iterWarm,0,1)`) ported to `_trainedAlpha` using shipped
`trainedRounds` + `addRound=0` (valid for a single-layer checkpoint only; falls back to 1.0 if
`Layers>1` or metadata is missing) — ALWAYS served now, no "what if" branch (there's nothing to
branch on with K fixed). Current snapshot (rounds=24,360 > iterWarm=20,000) reconstructs to exactly
1.0 (ramp already done) — coincidental for THIS snapshot, but the mechanism matters for a future
mid-ramp one.

**Tokenizer**: a from-scratch greedy-longest-match subword encoder/decoder against the published
`CharVocab` statics + the bundled `oracle-vocab.txt` merges — reproduces `MintTokenizer` exactly,
including its real quirk: token id `CharVocab.End` (95) doubles as BOTH `"\n"` AND the stop marker
(trained on single-line snippets, so newline=stop; `Normalize` maps input `\n`→space, so this id is
only ever produced as OUTPUT). Kept verbatim, not "fixed" — real trained behaviour.

**Generation loop**: full-recompute per character via `InspectStackIter`/`InspectAttention`/
`DecodeFace` (not `LogitsFor`/`Prime`/`Step` — no per-pass data, and plain `Prime`/`Step` aren't
verified to honour `Iters`, same caveat as `TrainStep`), so the Inspector trace always matches what
was generated. Confidence gate (`GateInfo`/`PickToken`, ported verbatim): greedy at top-1 ≥
`DecodeConfident=0.60`, else sample at `DecodeTemp=0.80` over a `mean+DecodeFloorK(3.0)σ` resonance
floor. A `DegenRepeat=4` guard (ported `ProbeDegenRepeat`) stops + labels a collapsed run rather than
padding silently — **verified necessary**: dry-running this exact algorithm against an earlier live
checkpoint snapshot (round ~21,720, `Iters=1`) produced a 100%-confidence GREEDY space-repeat on every
short prompt tried — a real repetition-collapse, not a demo bug. Re-check whatever snapshot ships
(current shipped snapshot: round 24,360, deserialized+verified `Dim=1536,Layers=1,Shifts=16,
ParamCount=381,056`, matches live `OneShot*` shape exactly — not stale).

**Model stats / "how it works" copy — labeling fixed 2026-08-28**: a user comparison against
PrismStudio's own status bar caught real copy drift, not a formula/snapshot bug (verified both sides:
`EquivCompute(1536,1,8)=226,492,416` matches exactly; checkpoint's real deserialized shape matches
live `OneShot*` — stale-snapshot and formula-mismatch both ruled out). Framing-only bug:
`MainForm.cs`'s status bar labels this "compute-equiv", never "parameters"
(`≈{Big(_equivParams)} compute-equiv (12·d²·L·K)`), but this page's outro called the same number an
"M-parameter dense transformer" — implying 226M real stored params vs the real `ParamCount`=381K.
Reworded to match PrismStudio's "compute-equivalence" framing + a one-line disclaimer it isn't a real-
param claim. The GPT-2-milestone roadmap paragraph already used compute-axis framing and needed no fix.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo and NOT in the public gallery — a private, share-by-link-only client
preview for the RecycleDAO PoC (`C:\Users\dongy\RecycleDAO`, separate repo, owned by
`recycledao-owner`; NEVER edit that repo from here). Deliberately absent from `Home.razor`'s gallery
and `MainLayout.razor`'s nav, and carries `<meta name="robots" content="noindex,nofollow">` via
`<HeadContent>` (same pattern as `AboutUs\site\recycledao-preview.html`, which is website-owner's).

**Rebuilt 2026-08-26 as a full eBay-classifieds marketplace** (client steer via Antonio). Domain
mapping: RCYT is EARNED by verified recycling and SPENT claiming material other participants
rescued from the waste stream — a real token sink, not just a reward wallet. 21 screens off one
`Screen` enum + `Nav` record-struct stack (real Back button); one `<section class="app-page">`
renders at a time, every navigation driven by a card/row/tile/action.

**Mint invariant (must never regress)**: `MintForApproval` is the ONLY method that appends to
`_mintLog`/increases `_totalMinted`/`_lifetimeMinted`, reachable from exactly two call sites (the
verifier queue's `ApproveSubmission`, and seeding). All other marketplace money movement only
*moves* RCYT between balances; no other path adds supply. Tier table verbatim from
`RecycleDAO/docs/demo-mechanics-spec.md` §2 (`Paper/Cardboard=3, Plastic=5, Glass=5,
Metal/Aluminum=8, Electronics/E-waste=15`). Seeding (8 personas/20 listings/etc.) also runs through
`MintForApproval`, so every starting token still has a mint-log/ledger row.

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
- `EvaluatedApplications.AlgFormer` **1.5.0** (bumped from 1.2.0 for Prism — needs
  `InspectStackIter`/`InspectAttention`/`DecodeFace`/`HoloShape.EquivCompute`/`InvisibleMultiplier`/
  `CharVocab`, none published before 1.5.0; verified via reflection against the real 1.2.0 vs 1.5.0
  DLLs, not assumed) — The Creature, The Forecaster, Prism (`PrismFormer` namespace:
  `HoloFormer`, `HoloShape`, `CharVocab`). The `HoloFormer`/`AlgFormer` public constructor and
  `Step`/`IterAccumulate` signatures are UNCHANGED 1.2.0→1.5.0 (checked before bumping) — Creature/
  Forecaster needed no code changes for this bump.
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
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call, not this repo's.** What to ask
  for: a copy of `%LOCALAPPDATA%\Prism\prism-holo.bin` + `-vocab.txt` (+ ideally `-iter.txt`, the
  round counter, for the "rounds trained" stat) at a snapshot THEY pick — checked 2026-08-27 that the
  live checkpoint at round ~21,720 currently produces a repetition-collapse (100%-confidence GREEDY
  space-repeat) on short generic prompts; worth asking whether to wait for a further-trained/healthier
  snapshot rather than freezing today's. Drop the three files at `Showroom/wwwroot/data/oracle-brain.
  bin` / `oracle-vocab.txt` / `oracle-rounds.txt` — the page already fetches those exact paths and
  degrades gracefully (clear `_loadError`, no crash) if any are absent.
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
  channel routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never
  copy another tool's `MinShifts` verbatim (Creature's 8 and Forecaster's 8 landed on the same
  number by independent reasoning, not by copying).
- `golden: true` on every `HoloFormer` construction so far — keep it unless a specific reason says
  otherwise. WASM has no filesystem — no live-training tool can persist a checkpoint (`Serialize`/
  `Deserialize` exist, verified, but nothing here calls them for that; a "Reset brain" button just
  drops the in-memory reference). WASM is single-threaded/interpreted (no AOT) — keep live-training
  shapes small (Creature `d=384,L=1`; Forecaster `d=128,L=1`; MarketSim's own server shape is
  `d=128,L=4,K=2` — ours is shallower since it trains one step/click, not a full epoch/tick).
- The manual `NewGrads()` → `IterAccumulate`/`StackIterAccumulate` → `Step` loop is the house
  pattern for live-training. `HoloFormer.TrainStep(toks, answer, lr)` exists but does NOT honour
  the `Iters`/K-pass depth knob — never use it where weight-tied extra passes matter.
- `HoloFormer`'s public ctor: `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1,
  embedSeed=null, seed=42, bindFfn=false, golden=false, normalize=true, unitary=false)` — unchanged
  1.2.0→1.5.0 (verified). `HoloShape` statics: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank(shifts,d)`,
  `CleanCapacity(shifts,d)`, `InteractionBudget(shifts,d)`, `EquivCompute(d,L,K)` (1.5.0+),
  `InvisibleMultiplier(paramCount,d,L,K)` (1.5.0+).

## Build / verify
```
dotnet build Showroom.csproj -c Release
```
Green as of 2026-08-27 (0 warnings, 0 errors) with all four tools wired in. No test project exists
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

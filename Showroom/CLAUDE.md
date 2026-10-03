# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-03 (council engine re-sync)

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

**Checkpoint refresh** (Prism's `oracle-brain.bin`+sidecars): a **data-only** refresh needs no
`dotnet publish` — raw-copy into `wwwroot/data` and `dist/data`, regenerate `oracle-brain.bin.gz`
via a plain `GZipStream` one-liner, cross-check `-stackk`/`-iterwarm` against PrismStudio's live
consts. Write sidecars via UTF-8-no-BOM .NET I/O, never `Set-Content -Encoding utf8` (prepends a
BOM). Covers Nano Stories and The Cartographer too — same checkpoint, same sidecars.

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
THE SAME MODEL as Prism — same weights, same training run, not a fine-tune. Shares the `"prism"`
`SessionHost` key and `HoloKernel.TokenVoice` byte-for-byte. Generation is a plain one-shot
continuation (not Prism's tagged chat format). Its own `DecodePolicy` (Focused/Balanced/Wild P
presets, since the 2026-09-24 TopP retightening below). Reproducible by seed (`Random(seed)` feeds
`Gate.Pick`). Honesty paragraph under the story states the real (~370K) parameter count so nobody
reads a generated sample as more capable than it is.

**Decode gate: TopP, not ResonanceSigma** (both Prism and Stories, matching the PrismStudio host) —
`FloorMode.ResonanceSigma` degenerates to an effective top-1 filter at 78-84% of positions on this
checkpoint, making `Temperature` inert (the real cause of Stories' old "near-deterministic"
behavior, not the model itself). Both pages build `Floor = FloorMode.TopP`; `FloorK` is vestigial
once `Floor=TopP`. **`P` is checkpoint-specific, re-measure on every re-mint** — as of r84,639,
P(top1)=0.199 (~57 effective candidates); Prism's chat uses `P=0.30` (~3); Stories' presets:
Focused `P=0.30`, Balanced `P=0.466` (~8), Wild `P=0.635` (~20) — never a conventional `p=0.9`,
which admits 200-300 tokens here and produces word salad. `ConfidentThreshold=0.60` unchanged on
both pages. Needs `EvaluatedApplications.Prism` >=1.3.0 (`FloorMode.TopP`/`DecodePolicy.P` don't
exist before that).

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
Paste or drop text; **Prose** (`ProseEngine`) mines it through a real rules-first grammar parser
(`MineText`, chunked+yielded by the page at ~200k-char boundaries — the library has no chunking hook
of its own), then recombines what it learned into sentences/Q&A pairs/packed conversations to copy
into a training corpus. The other genuine two-package composite (HoloDb `ProseStore` + AlgFormer
plausibility `HoloFormer`), chord `data-cat="holodb-algformer"`. Input cap 64MB (matches Analyst).
Plausibility scoring is a 3-way choice (None / score with Prism's checkpoint / train one on the
visitor's own text — measured ~0.22-0.24 ms/char/epoch, so defaults stay small with a live on-device
time estimate before committing). `ProseEngine.Plausibility` has no reset — `Prose.razor` re-mines a
fresh `_engine` if a prior run trained one and the mode has changed back.

## Council Spending Scanner — `Pages/CouncilSpending.razor` (route `/tools/council-spending`)
A real analytical tool over **HoloDb** on England's Local Government Transparency Code data
(councils publish every payment over £500) — not a model demo. Engine (`CouncilAudit/`) is
**vendored, not owned here** — built by the EA virtual-customer agent, copied in from
`C:\Users\dongy\VirtualCustomer\src\CouncilAudit`; future engine changes happen upstream, never
edited in place here. Loads pre-normalised Wokingham data (`wwwroot/data/councils/wokingham/
*.norm.csv.gz`, 6 years, ~217K rows) or a visitor's own CSV/xlsx into one shared in-memory
`Database.Open(null)` (same BulkLoad pattern as The Analyst). Runs mechanical exception tests and
drafts a Freedom-of-Information letter — no network calls, nothing sent from the page.
**Cross-council supplier search** (`RunNearestSupplierSearch`): a `<select>` toggle — **Graded**
(default) runs HoloDb's `NEAREST (supplierkey = '<key>') ... score` for ranked, typo/variant-
tolerant matching; **Exact substring** is a `LIKE '%key%'` fallback. Graded mode needs **HoloDb
>= 2.1.0** (holodb-owner's F-04 fix — `score` threw and ranking was unreliable before). Re-verified
2026-10-03 via a throwaway harness against the real 42,405-row FY2024-25 Wokingham file in this
page's exact 13-column schema: a truncated query ("A Wise Solution") ranks the real supplier
("A Wise Solution Ltd") first on both `supplierkey` and raw `supplier` (absolute `score` is lower
than holodb-owner's own narrower-schema number since the hologram is diluted across more columns —
rank order is what matters here and holds).

**UI pass (2026-10-03, live-site feedback)**: each exception test is now a collapsible `<button>`
panel (collapsed by default, expanded only if it holds a hand-verified example), rows paginate at
50/page inside a sticky-header, max-height scroll box (never renders thousands of rows at once —
was `Take(200)` flat before), with a per-panel text+year quick filter and click-to-sort columns.
Selection keys off `ExceptionItem.Key` (unchanged) so it survives paging/filtering; "select all"
operates on every FILTERED item (`FilteredItems`), not just the current page. Cross-council search
results reuse the same `table-scroll-fixed` bounded-panel class. **Evidence/highlighting, same
pass**: a row with `EvidenceKind != None` gets a "Show evidence" toggle — `AmountCompare` (test
1/2) shows sum-paid vs stated-invoice side by side with the signed difference, a neutral "looks
like 2x the invoice" note (points at test 5, not a conclusion), and test 2's opposite-sign (net ==
-gross) callout; `GroupEvidence` (test 3/4/5) shows every sibling row sharing a `GroupKey` aligned
in a mini table with matching columns highlighted one colour and differing columns another, plus a
legend. **Test 5 restructured**: one `ExceptionItem` per repeated LINE now (was one aggregate item
per group) so the actual duplicate rows pair up visually — this changed its reported exception
*count* (now counts rows, not groups). `CouncilDbBuilder/Program.cs` (the offline producer of
`exceptions.csv.gz`) was extended to emit `compareamount`/`groupkey`/`groupsize`/`oppositesign` —
this is precomputed data, not reconstructed client-side; re-run it (`dotnet run -c Release` from
`AboutUs/CouncilDbBuilder/`) and copy the regenerated `wwwroot/data/councils/wokingham/
exceptions.csv.gz` into `dist/` whenever `AuditEngine`'s own evidence-relevant fields change.

**Engine re-sync 2026-10-03 (DebtCharge fix) + full Reading**: `CouncilAudit/` re-vendored (AuditEngine
reordered cascade + `HasDebtChargeEvidence`, Models `MemberRowIndexes`, CsvReader CP850 fallback, new
`ReadingFixups.cs`; `HandCheckHelpers.cs` deliberately NOT vendored, CLI-only; SupportedCouncils' Bracknell
entry condensed). `CouncilDbBuilder` now derives Reading's file list from `SupportedCouncils.Reading.Years`
(minus `2021-05`, council-side export fault) and applies `ReadingFixups` per file; raw files in
`wwwroot/data/reading/reading-<tag>.<ext>` (63 files, from `VirtualCustomer/inbox/reading`). Verified counts,
Wokingham Schedule A: DebtCharge 22, NegativeNetSignFlip 3,026, Unreconciled 2,949, VatRoundingNoise 301,
EarlyPaymentProgramme 200, DoubleListing 2,327. Engine source change needs a full AOT publish (not data-only).
Page: `_readingMonths` is built from `SupportedCouncils.Reading.Years` minus `2021-05` (was a hardcoded 5-entry
list, which kept the UI at 5 months despite the data), so it can't drift; each council's licence block has a
collapsed per-file "retrieved" `<details>` (Wokingham 2 Oct, Reading 3 Oct 2026) and Reading's panel states the
excluded 2021-05. Load is on demand: first Reading load = exceptions file ~1.2 MB gz + ~30-220 KB per ticked period.

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
**Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-channel
routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never copy
another tool's `MinShifts` verbatim. `golden: true` on every `HoloFormer`. WASM has no filesystem
— nothing persists across a reset. WASM is single-threaded/interpreted — `Parallel.For`/
`IParallelMap` degrade to sequential, not a crash, but keep live-training shapes small and any
batch text/tensor work cooperatively yielded.
- `HoloFormer` ctor: `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1, embedSeed=null,
  seed=42, bindFfn=false, golden=false, normalize=true, unitary=false, growFromFront=true)`.
  `HoloShape` statics: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank`, `CleanCapacity`, `InteractionBudget`,
  `EquivCompute(d,L,K)`. `Face(id)`/`LayerFaces(toks)`/`InspectStackIter(Faces)`/`InspectStackIterFaces`/
  `InspectAttention`/`DecodeFace`/`EmbRow` all public (verified against the published 2.16.0 DLL).

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

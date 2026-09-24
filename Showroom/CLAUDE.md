# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-09-24

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/`). Every tool runs entirely client-side: no server, no
upload, the compute happens in the visitor's own browser tab. Charter:
`MonoRepo\.claude\AGENT-CHARTER.md` (applies here in full, incl. the §3 ~250-line budget, even
though this repo sits outside MonoRepo). This repo only ever *consumes* MonoRepo packages via
published NuGet, never source.

**Purpose**: each tool is a real, working demo of a published `EvaluatedApplications.*` package's
capability, driven live by the visitor — no smoke and mirrors. Seven tools: **The Analyst** (HoloDb),
**The Creature** (AlgFormer/HoloFormer + Tracer), **The Forecaster** (AlgFormer/HoloFormer),
**Prism** (AlgFormer/HoloFormer, trained-checkpoint chat REPL), **Nano Stories** (same checkpoint as
Prism, asked to write instead of chat), **The Cartographer** (same checkpoint again — a 2D map of the
representation-space path one next-token decision takes), **Prose** (HoloDb + AlgFormer, grammar-
mining corpus generator). Plus one **unlisted** page (below), a client preview.

## Site plumbing
- `Program.cs`: standard WASM host; one scoped `HttpClient`; `AddSingleton<HoloKernel.SessionHost>()`
  (shared model cache); `AddSingleton<ContentDbHost>()` (unrelated spike, `Pages/ContentDbSpike.razor`).
- Nav (`MainLayout.razor`) is `Home · Tools · NuGet` (3 items, lean by design). A new tool needs a
  `Home.razor` gallery card (`<a class="card tool" href="/tools/<slug>" style="--cat:...">`, a live/
  soon tag, a package `.ver` pill, `.desc`, `.go-in`), **not** a nav entry.
- `wwwroot/index.html`: `<base href="/tools/" />`; links `/assets/site.css` (shared design system) +
  Showroom's own `boot.css`/`depth.css`; GitHub Pages SPA deep-link restore; JS interop:
  `window.analystDownload(name, text, mime)` (Blob-URL save), `window.copyText(text)`.
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css` (Blazor CSS isolation can't share a
  partial across components) duplicating a shared base block verbatim (`.room`/`.crumb`/`.room-head
  h1`/`.lede`/`.hint`/`.err`/`.go`/`.outro`, plus whatever else that tool's shape needs) before its own
  classes — copy from `Prism.razor.css`/`Analyst.razor.css`. `wwwroot/css/voice.css` is the one real
  exception (a genuine global stylesheet, since `TokenVoiceControls` lives in the HoloKernel RCL).
- **Parallax depth/glow** (`wwwroot/css/depth.css`): 3 scroll-driven tiers (far = `<main>` wallpaper,
  near = `.room-head`, mid = a fixed list of panel classes), zero JS, reduced-motion-gated. Tinted per
  tool via `[data-cat]` on the outer `.room`: a single package name (`"algformer"`, `"holodb"`) sets
  `--glow-near`/`--glow-mid`; a genuine multi-package tool uses a chord (`"algformer-tracer"` Creature,
  `"holodb-algformer"` Prose) with `--glow-*-a`/`-b` + `sr-parallax-*-chord` keyframes. A new tool's
  own panel class (e.g. `.cg-panel`) must be added to the mid-tier selector list to get the glow at all
  — and must not carry its own `opacity` (see that file's "OPACITY-MULTIPLIER TRAP" comment).

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 2.16.0 + the `Prism`
package — a `ProjectReference` here is the designed path, not a MonoRepo boundary break. Surface:
- `ModelSpec` — shape + the **S>1 invariant enforced by construction**.
- `HoloSession.Create(spec)` / `.FromCheckpoint(bytes, kPass, serveAlpha)` — **K and alpha are
  mandatory args**: `HoloFormer.Iters`/`.IterAlphaServe` are NOT persisted by `Serialize()`, so a
  deserialized checkpoint always reads back `1`/`1`. `.Model` exposes the raw `HoloFormer` (public
  `Layers`/`Dim`/`Shifts`/`Context`/`Vocab`/`ParamCount`/`EmbRow`/`InspectStackIterFaces`/
  `InspectAttention`/`DecodeFace` all reachable this way — see The Cartographer below for a page that
  uses the inspector trio directly rather than through a HoloSession wrapper). `.Logits(ctx)` full
  recompute; `.NewServeCache()`/`.Prime()`/`.StepToken()` O(1)/token KV-cache path — bit-identical to
  `.Logits` only for a MULTI-layer model (Prism/Nano Stories); NOT verified for L=1 (Creature/
  Forecaster) — don't route those on without re-checking.
- `PrismCheckpoint` — `SessionKey` (`"prism"`, the canonical `SessionHost` key), `ResolveK`,
  `ReconstructAlpha` — the two formulas every tool serving Prism's checkpoint (Prism, Nano Stories,
  The Cartographer, Analyst's novelty scan, Prose's "Score with Prism" mode) must use IDENTICALLY.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool; every consumer of
  Prism's checkpoint shares one in-memory instance this page load. **Ephemeral** — no persistence,
  reload drops everything (WASM has no filesystem, none is wanted).
- `RefinementLoop.Observe`/`.ObserveSequence` — the live-training pattern (`NewGrads()`->
  `IterAccumulate`/`StackIterAccumulateAllPos`->`Step`), used by Creature/Forecaster only.
- `Decoding` comes from the **`Prism` package's** `Prism.Inference` namespace — `@using
  global::Prism.Inference` is required on any page referencing it (see Gotchas: the namespace-collision
  rule applies repo-wide, not just to the colliding page).
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)` — fetch+decompress a `.gz` sidecar via the
  BCL's `GZipStream` (no JS interop). GitHub Pages serves files byte-for-byte uncompressed, so a raw
  checkpoint needs a hand-shipped `.gz` sibling to download small.
- `TokenVoice`/`TokenVoiceControls.razor` — Prism's per-token voice (face→tone synth), shared
  byte-for-byte by Prism and Nano Stories; never quantise to a musical scale, never retune per tool.
- `DegenerateTail.Start(ids)`/`.SafePrefix(ids)` — trims/holds back a repeating tail mid-generation
  (Prism, Nano Stories only — The Cartographer never generates more than the single next token, so it
  has no degenerate-tail exposure).
- **Browser contract**: visitors **train**, never **reshape**. `GrowLayers`/`GrowShifts` are real but
  PrismStudio/server-side only; a better model reaches visitors via a new checkpoint, never a runtime
  shape mutation.

**Prism/Nano Stories operational constants** (re-measure on every checkpoint re-mint, bytes/token
changes): `MaxReplyStepsConst = 56` (Prism, chat turns) and `MaxStorySteps = 128` (Nano Stories) are
PINNED, not `Context/2` — that fraction silently broke on a past checkpoint swap. Both caps exist
because this checkpoint has never emitted its own STOP token in measured testing, so every reply/story
runs to its cap and ends mid-sentence — train it, don't paper over with a sentence-boundary heuristic.
Chat/story panes start empty (no seeded example). `Prism.razor`'s chat context (`BuildContextTokens`)
uses a tagged wire format (`user: Q\nprism: A` + STOP per turn) matching PrismGym's packed training
windows token for token; Nano Stories instead encodes its prompt as a plain continuation (that
checkpoint's narrative-text slice never saw chat tags). `HoloKernel/AsciiPunctuation.Fold` (curly
quotes/dashes/nbsp/ellipsis → ASCII) runs at every tokenizer entry point site-wide (Prism, Stories,
The Cartographer, Analyst's novelty scan, Prose's plausibility scoring) — `SubwordVocab.Fold` blanks
non-ASCII to a bare space otherwise, which silently mangled phone-autocorrected input before this fix.

**Checkpoint refresh** (Prism's `oracle-brain.bin`+`-vocab/-rounds/-stackk/-iterwarm.txt`): a
**data-only** refresh needs no `dotnet publish` — raw-copy into `wwwroot/data` and `dist/data`,
regenerate `oracle-brain.bin.gz` via a plain `GZipStream` one-liner, cross-check `-stackk`/`-iterwarm`
against PrismStudio's live consts. Write sidecars via `[System.IO.File]::WriteAllText(path, text, new
UTF8Encoding(false))`, never `Set-Content -Encoding utf8` (silently prepends a BOM). Covers Nano
Stories and The Cartographer too — same checkpoint, same sidecars, zero extra steps.

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

**Decode gate: TopP, not ResonanceSigma (RETIGHTENED 2026-09-24, both Prism and Stories, matching a
fix in the PrismStudio host)**. `FloorMode.ResonanceSigma` (mean + `FloorK`*sigma over the FULL
vocab, incl. every suppressed token) degenerates to an effective top-1 filter at 78-84% of positions
on this checkpoint and makes `Temperature` inert there — the real cause of Stories' old
"near-deterministic" note, not the model itself. Both pages now build `Floor = FloorMode.TopP`;
`FloorK` is vestigial once `Floor=TopP` (verified against real `Gate.Evaluate`). **`P` is
checkpoint-specific, re-measure on every re-mint** — as of r84,639 (first EVE-trained model,
refreshed 2026-09-24, NOT the prior r635,618 one), cumulative softmax mass to admit N candidates at
the FINAL face: `N=3→0.303, N=5→0.390, N=8→0.466, N=12→0.539, N=20→0.635, N=40→0.752`, P(top1)=0.199
(~57 effective candidates). Prism's chat uses `P=0.30` (~3). Stories' presets read off the same
table: Focused `P=0.30` (now literally == Prism's chat), Balanced `P=0.466` (~8), Wild `P=0.635`
(~20) — never a conventional `p=0.9` (`Default`'s own value): it admits 200-300 tokens here and
produces word salad. Re-measure the table (reconsider every P) if a future P(top1) moves off ~0.2.
`ConfidentThreshold=0.60` unchanged on both pages, matches PrismStudio's host value. Required bumping
`HoloKernel.csproj`'s `EvaluatedApplications.Prism` 1.0.2→1.3.0 (`FloorMode.TopP`/`DecodePolicy.P`
don't exist before 1.3.0, reflection-verified against 1.0.2/1.1.0/1.3.0 directly).

## The Cartographer — `Pages/Cartographer.razor` (route `/cartographer`, added 2026-09-24)
A 2D visualiser for **one** next-token decision — not a chat, not a generation loop. Reuses Prism's
exact checkpoint via the shared `"prism"` `SessionHost` key (usually already loaded if a visitor came
from Prism/Nano Stories). On submit: encodes the prompt (capped to `min(24, Stats().Context)` tokens,
truncating the front if longer), then calls `HoloFormer.InspectStackIterFaces`/`InspectAttention`/
`DecodeFace`/`EmbRow` directly off `HoloSession.Model` — no HoloKernel wrapper exists for these three
(a deliberate gap; they're read-only inspectors, not something worth wrapping).
- **Trajectory**: the LAST position's face at every `[boundary]` from `InspectStackIterFaces` (order:
  embed, then one point per (layer,pass) in compute order, then FINAL — `Layers*KPass+1` points total),
  joined into a path. Crystallisation (first boundary whose greedy top-1 already equals the final
  answer) is marked with a triangle, matching the studio inspector's own convention.
- **Projection**: PCA (hand-rolled power iteration, no numerics package — see the file's own comments)
  fit on the TRAJECTORY points only, so the path gets maximum 2D spread; the vocabulary cloud and
  attended positions are then projected into that SAME basis. The captured-variance fraction is always
  shown in the UI text, not a tooltip. A second basis (embed→final plane, via Gram-Schmidt) is offered
  as a cross-check, computed once per Visualize() call and free to toggle afterward (`RebuildProjection`
  reprojects without re-running the forward pass).
- **Attention**: `InspectAttention` gives one row per boundary (excluding FINAL, no query there)
  resonating the last position's query against every context key at THAT SAME depth. Top-4 by
  |weight| per boundary draw as lines to `grid[boundary][position]` — the attended position's CURRENT
  state at that depth, decoded via `DecodeFace`, not its raw t=0 embedding.
- **No training loop, ever** — read-only inspection of one forward pass. `HoloFormer.Map` defaults to
  `SequentialMap` (nothing here ever sets it to a real parallel implementation), so this is deadlock-
  safe on WASM's single thread; `HoloFormer` has no `MapAsync` twin of these inspectors yet (AlgFormer's
  own gotcha), so `await Task.Yield()` around the call — not a fake-async wrapper — keeps the tab
  responsive, same pattern `Prism.razor` uses.

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
- `EvaluatedApplications.AlgFormer` **2.16.0** (bumped from 2.8.0, 2026-09-24, routine latest-NuGet
  bump; `HoloKernel.csproj` bumped alongside it to avoid an NU1605 downgrade). `SubwordVocab.MaxLen`
  must be ≥16 to load a freshly-minted checkpoint (fixed at 2.8.0; still true at 2.16.0).
- `EvaluatedApplications.Prism` **1.3.0**, via `HoloKernel.csproj`'s own `PackageReference` (bumped
  from 1.0.2, 2026-09-24 — load-bearing this time, not just routine: `FloorMode.TopP`/`DecodePolicy.P`
  don't exist before 1.3.0, needed for the decode-gate retightening above; depends on AlgFormer
  >=2.15.0 per its nuspec, already satisfied by this project's 2.16.0).
- `EvaluatedApplications.HoloDb` **1.10.0** — Analyst, Prose.
- `EvaluatedApplications.Tracer` **1.1.0** — Creature.
- `EvaluatedApplications.Prose` **1.3.0** — Prose. A multi-package tool's version bump ripples to
  every other tool in this one `.csproj` (Prose forced `HoloDb`/`AlgFormer` up too, NU1605 otherwise);
  re-`dotnet build` the whole app after adding/bumping any tool, not just its own page.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism, Stories,
  Analyst, Prose, The Cartographer.
- `PublishTrimmed=true` + `RunAOTCompilation=true` — **AOT is load-bearing, not a perf luxury**: the
  Mono WASM interpreter can't execute IL in `Prism.Inference.DecodePolicy`'s static ctor without it.
  `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker alongside concurrent local training).
  `WasmEnableThreads=false` — real threading regressed a laptop's boot (higher core count spun up more
  worker-pool contention); nothing here dispatches across threads, so nothing to gain re-enabling it.
- **Version bumps only via `dotnet add package`** — never hand-edit `<Version>`.

## Boundary (hard, from the agent charter)
- **NuGet only, never MonoRepo `ProjectReference`.** Verify API assumptions against the actual
  published package before wiring new code (or against a matching version pulled into the local NuGet
  cache, if the exact just-published version isn't in `.csproj` yet). `HoloKernel` is the one
  exception — a sibling in-repo RCL, itself NuGet-only.
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call**; **Forecaster's live top-up** needs a
  Finnhub key this agent can't self-register.
- Never touch `AboutUs/site/*`, nav, or the shared design system — `website-owner`'s. Never launch
  the app / open a browser — build-verify only; demonstrating a tool live is the user's to do.

## Standing technical facts
- **Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-channel
  routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never copy
  another tool's `MinShifts` verbatim.
- `golden: true` on every `HoloFormer`. WASM has no filesystem — nothing persists across a reset.
- WASM is single-threaded/interpreted — `Parallel.For`/`IParallelMap` degrade to sequential, not a
  crash, but keep live-training shapes small and any batch text/tensor work cooperatively yielded.
- `HoloFormer` ctor: `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1, embedSeed=null,
  seed=42, bindFfn=false, golden=false, normalize=true, unitary=false, growFromFront=true)`.
  `HoloShape` statics: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank`, `CleanCapacity`, `InteractionBudget`,
  `EquivCompute(d,L,K)`. `Face(id)`/`LayerFaces(toks)`/`InspectStackIter(Faces)`/`InspectStackIterFaces`/
  `InspectAttention`/`DecodeFace`/`EmbRow` all public on `HoloFormer` (verified against the published
  2.16.0 DLL, not memory).

## Build / verify
`dotnet build Showroom.csproj -c Release` — green (0/0) with all seven tools + HoloKernel wired in.
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
  global::<Namespace>` — **not scoped to the colliding page**: every page compiles into the same
  `Showroom.Pages` namespace, so ANY page referencing `Prism.*`/`Prose.*` needs the `global::` form
  once ANY sibling page is named `Prism`/`Prose`, even if its own name doesn't collide. Affects:
  `Prism.razor`, `Stories.razor`, `Prose.razor`, `Analyst.razor` (The Cartographer references neither
  namespace, so needs none — re-check the moment it ever does).
- A new tool page gets the parallax/glow treatment free by reusing the house `.room`/`.room-head`/
  panel shape — it just needs its own `data-cat="<pkg>"` (or chord) on the outer `.room`, and its own
  main panel class added to `wwwroot/css/depth.css`'s mid-tier selector list.
- Razor reserves the bare `<text>` tag for raw-text-without-a-wrapper output — it CANNOT carry
  attributes (`RZ1023`). An SVG `<text x=".." y="..">` (The Cartographer's labels) must be built as a
  `MarkupString` from a C# string instead, HTML-encoding any interpolated content by hand.
- Multi-package tools force a real dependency-version bump for every tool in this one `.csproj` — re-
  check `dotnet build` after adding a new tool, not just its own page.

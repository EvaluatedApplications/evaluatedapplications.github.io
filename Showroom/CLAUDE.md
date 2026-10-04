# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-04 (big data and the Blazor runtime moved to the website-data repo; council scanner: request tray + letter, source rows, 21 councils, test at n=96)

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
**CSS pattern**: each tool has its own `Pages/<Tool>.razor.css` (isolation can't share a partial) duplicating a shared base block (`.room`/`.crumb`/`.room-head h1`/`.lede`/`.hint`/`.err`/`.go`/`.outro`);
copy from `Prism.razor.css`/`Analyst.razor.css`. `wwwroot/css/voice.css` is the one real global exception (`TokenVoiceControls`
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
- `Decoding` comes from the **`Prism` package's** `Prism.Inference` namespace: `@using global::Prism.Inference` (see the namespace-collision gotcha).
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)`: fetch + inflate the `.gz` checkpoint (Pages serves it uncompressed, hence the hand-shipped `.gz`); the ONE choke point every
  checkpoint consumer shares. `CheckpointF32.Unpack` runs inside it ("PF32" fp32 to f64, lossless): **cross-repo byte-exact contract with PrismStudio**, never change the wire format here alone.
- `TokenVoice`/`TokenVoiceControls.razor`: Prism's per-token voice (face to tone synth), shared byte-for-byte with Nano Stories; never quantise to a musical scale.
  `DegenerateTail.Start(ids)`/`.SafePrefix(ids)` trims a repeating tail mid-generation (Prism, Stories only).
- **Browser contract**: visitors **train**, never **reshape** (`GrowLayers`/`GrowShifts` are PrismStudio-only; a better model ships as a new checkpoint).

**Prism/Nano Stories operational constants** (re-measure on every checkpoint re-mint): `MaxReplyStepsConst = 56` (Prism) /
`MaxStorySteps = 128` (Stories) are PINNED, not `Context/2` (that broke on a checkpoint swap). The checkpoint never emits STOP, so
every reply/story runs to its cap and ends mid-sentence: train it, no sentence-boundary heuristic. `Prism.razor`'s chat context
(`BuildContextTokens`) is the tagged wire format (`user: Q\nprism: A` + STOP per turn) matching PrismGym's windows; Stories uses a plain
continuation. `HoloKernel/AsciiPunctuation.Fold` (curly quotes/dashes -> ASCII) runs at every tokenizer entry
(`SubwordVocab.Fold` blanks non-ASCII).

**Checkpoint refresh** (Prism's checkpoint + sidecars, shared by Stories/Cartographer/Analyst/Prose): data-only, no publish, NO site-repo change: write `prism/oracle-brain.bin.gz`
(`GZipStream`; raw `.bin` is not shipped, nothing fetches it) + `oracle-vocab/rounds/stackk/iterwarm.txt` (UTF-8 no BOM; cross-check `-stackk`/`-iterwarm` against PrismStudio) into
`C:\Users\dongy\website-data\prism\`, commit, push website-data only. Verify: `https://evaluatedapplications.github.io/website-data/prism/oracle-rounds.txt`.

## Data and runtime live in the website-data repo (2026-10-04)
`C:\Users\dongy\website-data` (GitHub `EvaluatedApplications/website-data`, Pages on main, served at `/website-data/` on the SAME origin as the site: no CORS) holds everything big, so
the site repo stops growing: `council-web/` (CouncilWebBuilder writes straight there), `prism/` (checkpoint + sidecars), `_framework/` (the Blazor runtime). `.gitattributes` there is
`* -text` (byte-exact, same reason as `dist/`). Still in the site: `wwwroot/data/forecaster-history.json` (+ its refresh workflow), `content.wal`; `dist/` is now 0.7 MB (was 364), `wwwroot` 0.3 MB (was 285).
- **ONE setting**: `window.EA_DATA_BASE` at the top of `wwwroot/index.html` (`/website-data/`; `website-data/` on host `localhost`). `Program.cs` reads it into `Services/DataUrl.cs`;
  every fetch is `DataUrl.For("prism/oracle-vocab.txt")` / `("council-web/...")`. Never write a literal `data/...` URL for these files.
- **Local dev**: run `scripts/link-data.ps1` once (junction `wwwroot/website-data` -> the repo, gitignored; `Showroom.csproj` drops it from Release publishes).
- **Runtime split**: `index.html`'s `loadBootResource` returns `EA_FRAMEWORK_BASE + file` (= `/website-data/_framework/`) for dotnetjs/dotnetwasm/assembly/globalization (`name` is already the
  fingerprinted file; the first call, `dotnet.js`, is resolved through the import map). `blazor.webassembly.*.js` stays in the site. Empty on `localhost` (dev serves its own runtime).
- **Source change = `scripts/publish-site.ps1`** (publish, rebuild `dist/`, copy the runtime to `website-data/_framework`, plain files only: Pages never serves `.br`/`.gz`). PUSH ORDER:
  website-data first, then the site. Re-run with `-Prune` after the site push is live (it deletes old runtime files; doing it earlier breaks the still-live site).
- **Check a build**: `node scripts/boot-check.mjs <dist> <website-data dir>` (headless Edge: Prism boots from the data origin, replies, no off-origin request) and
  `node scripts/council-perf.mjs <dist> 6 4096 100`.

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
A 2D visualiser for **one** next-token decision (not a chat, no generation loop) on Prism's exact checkpoint via the shared `"prism"` `SessionHost` key. Encodes the prompt (capped to
`min(24, Stats().Context)` tokens, front truncated), then calls `HoloFormer.InspectStackIterFaces`/`InspectAttention`/`DecodeFace`/`EmbRow` directly off `HoloSession.Model` (no
HoloKernel wrapper: read-only inspectors aren't worth one). **Trajectory**: the LAST position's face at every boundary (embed, each (layer,pass), FINAL) as a path; the first boundary whose
greedy top-1 equals the final answer is marked. **Projection**: power-iteration PCA fit on the trajectory points only (captured-variance fraction always shown; an embed-to-final
Gram-Schmidt basis as cross-check). **Attention**: top-4 by |weight| per boundary drawn to `grid[boundary][position]`. **No training loop, ever**; `HoloFormer.Map` defaults to
`SequentialMap` (deadlock-safe on WASM) and there is no `MapAsync`, so `await Task.Yield()` around the call keeps the tab responsive (as in `Prism.razor`).

## Prose — `Pages/Prose.razor` (route `/prose`)
Paste/drop text; `ProseEngine.MineText` mines it (page chunks+yields at ~200k chars), then recombines into sentences/Q&A/conversations.
HoloDb `ProseStore` + AlgFormer plausibility (None / Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch), chord`data-cat="holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines a fresh engine). Cap 64MB.

## Council Spending Scanner: `Pages/CouncilSpending.razor` (routes `/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`; SPEC_FOR_SHOWROOM.md items 9-26). Twenty-one councils, no HoloDb,
no engine in the browser. Public-audience, mobile-first, not editorial: audit terms, plain prose + counts + GBP, OGL credit, never a cause.
- **Adding a council** (done for 9 on 2026-10-04): a line in `CouncilWebData.Councils` (full name EXACTLY as the profile's, short name), a `("key","slug")` pair in the builder's `Slug()` (else it throws
  "no slug for X"), `CouncilTerms.NoScheduleA/NoScheduleD` from the exceptions (A or D rows = 0 and the profile says empty by construction; `NoDByNumbering` when numbers exist but never span payees), then re-run the builder.
  New cross files go in the builder's `cross/` list and a row in `BudgetTestPanel`'s set list. Profile prose needs no page code (flagged = quirk opening in capitals), but read the builder's REWORDED/HELD BACK
  output and grep profiles.json for working-file words (`prep`, "Prepare step" are reworded to "the scanner" in `Reword`; `XlsxReader` sentences dropped).
- **Data** (`website-data/council-web`, 292.6 MB, 4,202 files, none over 0.28 MB; `AboutUs\CouncilWebBuilder`, `dotnet run -c Release --project CouncilWebBuilder`,
  about 70-90 s, writes straight into the data repo (arg 1 / `COUNCIL_WEB_OUT` overrides), wipe the folder first so stale files go; it fails above 50 MB/file; second arg `profiles` rewrites
  only profiles.json.gz. Then commit + push website-data, nothing else): `index.csv`, per-council `months.csv`,
  month EXCEPTION slices (`<slug>/<YYYY-MM>.exceptions[.N].csv.gz`), month TRANSACTION slices (`<slug>/<YYYY-MM>[.N].csv.gz`, 1,333 files, 13 slim columns, fetched only by "See the
  source rows"), `cross/*.csv.gz`, `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`. After every `phoneexport`/`export`: re-run the builder (no site publish needed).
  Profile text is cleaned in the BUILDER, never at source: the engine's "Schedule A/B/D" and "the engine" become the page's words, sentences naming working files, sessions or
  checklists are dropped, empty notes held back; REWORDED/HELD BACK lines print for the profile owner (West Berkshire 3, RBWM 1 held back at last run). Never load a whole council.
- **Code**: `Services/CouncilWebData` (fetch, chunked inflate, caches; keeps the last two months' transaction bytes), `CouncilMonth` (`MonthScan`: byte-level scan, no string per line,
  groups opened on demand; reads `ExplainedBy`/`ExplainedMeaning` by header name), `CouncilSource` (`SourceScan`, `SourceState`), `CouncilChecksWeb` (check definitions, plain loops: LINQ over
  decimals/tuples is slow in WASM), `CouncilTerms` (wording, `TryDate` day-first: "03/04/2020" is 3 April), `FoiTray` (`FoiTray`, `FoiFacts`: item sentences and the letter);
  `Components/CheckPanel|MonthView|BudgetPanel|BudgetTestPanel|FoiTrayPanel|SourceBlocks`. Each load logs a `CW-PERF` console line.
- **Pattern readings** (item 22, as changed in Session 34): RecurringBatchRate and CadenceCatchUp are NOT classes. An Unclear group carries them in `ExplainedBy` + `ExplainedMeaning`;
  shown as a tag with the meaning as caption, the group stays open. Month summary: "N still open (Unclear + standing-payment surplus), of which K carry a pattern reading".
- **Request tray** (`FoiTrayPanel`, one letter per council): "Add to request" on every month group and cross-check row (not budget lines). The letter states facts and asks
  "Please provide the records you hold for this item, and the reason for it." No class meanings, readings, `Detail` text or loan rebuilds go in it (those are explanations). Address
  is the profile's verified `foi` (4 of 12 councils) else a placeholder. Caps: 300 items, 100 per letter. Copy/save only; lives for the page load. Tick and source buttons are
  PLAIN markup with their state on the row (`ExGroup.Source`, `CheckRow.Source`); a component per row cost about 5 ms each on a phone. `FoiTray.ItemsChanged` redraws lists, `Changed` the panel.
- **Source rows**: the tap fetches that month's transaction slice (all parts), scans the bytes for the group's transaction numbers (`AllTx` cap 80; a repeated-payment group also
  narrows to its supplier when that matches), lists up to 200 lines. Sampled 1,359 groups over 60 months: every one found its lines. A check row without a readable date has none.
- Measured (`scripts/council-perf.mjs`: headless Edge via CDP on a published AOT build, CPU 6x + 4 Mbps/100 ms, wall/worst stall; re-run 2026-10-04 on the website-data layout, one pass: in range, first open of the twins check stalled 641 ms): tick one item 55-73/74 ms; open the
  tray 365-433/287 ms the first time (profile fetch), about 190/60 with items; letter of 100 items opens 310-540/0; list of 15 groups 166-188/188; show 15 more worst stall 61-139;
  source rows of the first group 0.6 s (Wokingham, stall 104), 2.0-2.1 s (Bradford), 2.4-2.5 s (Leeds, Sheffield: 3-4 parts, about 4.8 MB raw; stall 55-64), a second group in the same month 0.25-0.5 s.
  Weakest: a tick in a 100-row list redraws the list (84-160 ms). First use of a check or month still stalls 0.3-0.5 s.
- 21 councils, same bar (CPU 6x, 4 Mbps, 100 ms; one pass 2026-10-04): hub ready 270 ms, boot to hub 1.4 s, hub list re-render 101 ms (21 cards); first council page (profiles.json 121 KB) 1.4 s wall, worst stall
  643 ms, later council pages 0.15-0.56 s; month pick 0.23-0.81 s (Cornwall 2021-10, largest month 6.7 MB raw: 1.8 s); source rows 0.6-2.5 s (Wakefield 2022-11: 4 parts, 5.6 MB, 2.5 s, stall 270 ms). Coventry's
  fact line reads "February 2001 to November 2033": a few typed dates in the file are data, shown as published (the profile says so).
- Classic in-browser engine page DELETED 2026-10-04 with `data/councils`; the unlinked raw `data/reading` + `data/wokingham` (100 MB) were deleted from wwwroot/dist the same day.
- Gotchas: `--` in csproj XML comments breaks load; BudgetPanel's `<text>` trick fails in code blocks; CSS is one scoped file using `.cs ::deep`; lists with stateful children need `@key`.
## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo, NOT in the gallery — a private, link-only client preview
(`C:\Users\dongy\RecycleDAO`, `recycledao-owner`'s repo; never edit it from here). Absent from
`Home.razor`/nav, `noindex,nofollow`. Mint invariant: `MintForApproval` is the only method that
increases `_totalMinted`.

## Dependencies (exact NuGet versions, `Showroom.csproj`, re-verified 2026-10-03)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.11**: **must stay version-equal to the installed WASM runtime pack** (a skew caused the 2026-09-06 outage: the interpreter hit
  IL from a mismatched native runtime and died silently at boot). Check `dotnet --list-runtimes` whenever the SDK moves.
- `EvaluatedApplications.AlgFormer` **2.20.1** (same pin in `HoloKernel.csproj`). `SubwordVocab.MaxLen` must be >=16 to load a freshly-minted checkpoint.
- `EvaluatedApplications.Prism` **1.3.3**, via `HoloKernel.csproj`: `FloorMode.TopP`/`DecodePolicy.P` need >=1.3.0; depends on AlgFormer >=2.15.0.
- `EvaluatedApplications.HoloDb` **2.1.0**: load-bearing for the Council Scanner (first version with a working `score` pseudo-column and graded `NEAREST`, F-04). Analyst, Prose, Council.
- `EvaluatedApplications.Tracer` **2.0.0** (Creature); `EvaluatedApplications.Prose` **1.3.2** (Prose). A multi-package tool's bump ripples to every tool in this one `.csproj`
  (NU1605 otherwise); re-`dotnet build` the whole app after adding/bumping any package.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism, Stories,
  Analyst, Prose, The Cartographer.
- `PublishTrimmed=true` + `RunAOTCompilation=true` — **AOT is load-bearing, not a perf luxury**: the
  Mono WASM interpreter can't execute IL in `Prism.Inference.DecodePolicy`'s static ctor without it.
  `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker alongside concurrent local training).
  `WasmEnableThreads=false` — real threading regressed a laptop's boot; nothing here dispatches
  across threads, so nothing to gain re-enabling it. **Version bumps only via `dotnet add
  package`** — never hand-edit `<Version>`.

## Boundary (hard, from the agent charter)
**NuGet only, never MonoRepo `ProjectReference`** (`HoloKernel`, a sibling NuGet-only RCL, is the one exception). Checkpoint hand-off is `prismstudio-owner`'s call;
Forecaster's live top-up needs a Finnhub key. Never touch `AboutUs/site/*`, nav or the shared design system (`website-owner`'s). Never launch the app / open a browser for a demo:
build-verify only (the headless-Edge scripts above are measurement harnesses, run on a published build).

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

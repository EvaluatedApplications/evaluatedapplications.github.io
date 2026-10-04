# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-10-04 (r3,970 window fix; council scanner: 25 councils incl. Stockport; by financial year; Wokingham social care view)

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

**Prism/Nano Stories constants** (re-measure on every checkpoint re-mint): `MaxReplyStepsConst = 160` / `MaxStorySteps = 448` are PINNED, not `Context/2` (r3,970: V=320 d=64 L=33 S=64 ctx=512 K=3,
~1.15 chars/token; the model now emits STOP in ~45% of replies, the cap is a backstop). **THE WINDOW RULE: prompt + reply must stay under `Context`, or every step past it re-Primes the whole window**
(~5.7 ms/token x 512 = ~2.9 s PER STEP; a measured 163.5 s chat turn 7). So Prism builds history with budget `Context - MaxReplySteps` (352), Stories clamps the prompt to `Context - MaxStorySteps` (64 tokens,
tail kept). Change either cap and the budget moves with it; keep each well under `Context`. `Prism.razor` context is the tagged wire format (`user: Q\nprism: A` + STOP per turn); Stories is a plain
continuation. `AsciiPunctuation.Fold` runs at every tokenizer entry. Check with `node scripts/chat-turns.mjs <dist> <website-data> 9 6` (9 turns, flags any over 6 s).

**Checkpoint refresh** (data-only, no publish, no site-repo change): write `prism/oracle-brain.bin.gz` (`GZipStream`) + `oracle-vocab/rounds/stackk/iterwarm.txt` (UTF-8 no BOM) into
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
- **Check a build**: `node scripts/boot-check.mjs <dist> <website-data dir>` (headless Edge: Prism boots from the data origin, replies, no off-origin request), `scripts/chat-turns.mjs` (N chat turns timed; env
  `PROMPT_SET=mid|mix|long` shapes the history: on the pre-fix build `mid` hit a 199.0 s turn 8; after, 12 turns max 3.1 s and 20 `mix` turns max 4.0 s), `scripts/tool-check.mjs` (Stories per variety + Cartographer counts/screenshot) and
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
own Focused/Balanced/Wild presets, seeded (`Random(seed)` -> `Gate.Pick`). States the real parameter count (read from the checkpoint; ~1.0M at r3,970) under the story.

**Decode gate: TopP, not ResonanceSigma** (Prism and Stories): ResonanceSigma degenerates to top-1 on this checkpoint so Temperature is inert.
Both pages build `Floor = FloorMode.TopP` (needs Prism >= 1.3.0); `P` is checkpoint-specific, re-measure on every re-mint and as training continues (r3,970, by the cumulative-mass rule:
chat P=0.43; Stories Focused 0.43 / Balanced 0.77 / Wild 0.89; r84,639 was 0.30 / 0.30, 0.466, 0.635; never a conventional 0.9 untested). `ConfidentThreshold=0.60`.
Page copy "plays its part three times" = K=3 (Prism's explainer + the TokenVoice comment); update it if K changes.

## The Cartographer — `Pages/Cartographer.razor` (route `/cartographer`, added 2026-09-24)
A 2D visualiser for **one** next-token decision (not a chat, no generation loop) on Prism's exact checkpoint via the shared `"prism"` `SessionHost` key. Encodes the prompt (capped to
`min(64, Stats().Context)` tokens, front truncated), then calls `HoloFormer.InspectStackIterFaces`/`InspectAttention`/`DecodeFace`/`EmbRow` directly off `HoloSession.Model` (no
HoloKernel wrapper: read-only inspectors aren't worth one). **Trajectory**: the LAST position's face at every boundary (embed, each (layer,pass), FINAL) as a path; the first boundary whose
greedy top-1 equals the final answer is marked. **Projection**: power-iteration PCA fit on the trajectory points only (captured-variance fraction always shown; an embed-to-final
Gram-Schmidt basis as cross-check). **Attention**: top-4 by |weight| per boundary drawn to `grid[boundary][position]`. Deep paths (r3,970 = 33 layers x K=3 = 100 boundaries): above 24 boundaries it draws 1 thin faint attention line per boundary (2 at full strength buried the path) and labels only embed/FINAL/crystal/every 11th; the table scrolls in a box. **No training loop, ever**; `HoloFormer.Map` defaults to
`SequentialMap` (deadlock-safe on WASM) and there is no `MapAsync`, so `await Task.Yield()` around the call keeps the tab responsive (as in `Prism.razor`).

## Prose — `Pages/Prose.razor` (route `/prose`)
Paste/drop text; `ProseEngine.MineText` mines it (page chunks+yields at ~200k chars), then recombines into sentences/Q&A/conversations.
HoloDb `ProseStore` + AlgFormer plausibility (None / Prism's checkpoint / train on the visitor's text, ~0.22-0.24 ms/char/epoch), chord`data-cat="holodb-algformer"`. `ProseEngine.Plausibility` has no reset (page re-mines a fresh engine). Cap 64MB.

## Council Spending Scanner: `Pages/CouncilSpending.razor` (routes `/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`; SPEC_FOR_SHOWROOM.md items 9-26). Twenty-five councils, no HoloDb,
no engine in the browser. Public-audience, mobile-first, not editorial: audit terms, plain prose + counts + GBP, OGL credit, never a cause.
- **Stockport** (added 2026-10-04, 115 months): a transaction number only from April 2025 (17 months) and 66 of 115 files carry an invoice date only, so it is NOT in `NoTransactionNumber` (twins/within-transaction
  DO run on the numbered months): `CouncilTerms.HasNumber`/`NotAvailable(slug, sch, year)` word A (one amount column) and D (no number before April 2025, nothing found after) per year; `YearNote` + `CannotCheck` carry the
  caveats; in no budget group (no Revenue Outturn held). A limit that depends on the year needs this per-year shape, not the all-or-nothing sets.
- **Adding a council** (done for 9, then Surrey/Essex/Hertfordshire, 2026-10-04): a line in `CouncilWebData.Councils` (full name EXACTLY as the profile's, short name), a `("key","slug")` pair in the builder's `TrySlug()` (else it is
  "NOT SHIPPED"), `CouncilTerms.NoScheduleA/NoScheduleD` from the exceptions (A or D rows = 0 and the profile says empty by construction; `NoDByNumbering` when numbers exist but never span payees; `NoTransactionNumber` when
  none is published: also makes `CheckCannotRun` say twins/within-transaction cannot run), `CouncilTerms.CannotCheck(slug)` (the "What cannot be checked" list on the page), `YearNote` (Hertfordshire's April 2025 threshold
  caption per year), then re-run the builder. Say plainly where a check cannot run: never an empty list that reads as clean. `budget_units.csv` Pool `confirmatory` = the first test group (it was never `stage1`); a council
  with units but none eligible gets the "cannot run, no government figure held here" line from `BudgetPanel`. Builder's `InternalSentence`/`Reword` also drop `XlsReader`/`FileDuplication` sentences, rename `rawcheck`.
  New cross files go in the builder's `cross/` list and a row in `BudgetTestPanel`'s set list. Profile prose needs no page code (flagged = quirk opening in capitals), but read the builder's REWORDED/HELD BACK
  output and grep profiles.json for working-file words (`prep`, "Prepare step" are reworded to "the scanner" in `Reword`; `XlsxReader` sentences dropped).
- **Load by financial year** (user: "I preferred it when I could choose what to load"). `LoadPicker` = tick-list of financial years (April-March; "No readable date" last), Select all/Clear, size line before any fetch, "Choose months" per year,
  ONE Load with progress + Cancel (keeps finished years). `YearBlock` per loaded unit: totals, `GapBars` (whole years only), schedule/bucket lists; top of view: per-year totals + `GapSummary`. `MonthScan` keeps raw bytes only for recent
  years (`RawBudget` 40 MB, LRU); an evicted year re-inflates via `EnsureRawAsync` (0.04-0.85 s) before a list opens (`NeedRaw`).
- **Data** (`website-data/council-web`, 448 MB, 5,396 files, largest 1.2 MB (website-data whole tree 652 MB incl. old+new runtime, .git 473 MB; Pages cap 1 GB); `AboutUs\CouncilWebBuilder`, `dotnet run -c Release --project CouncilWebBuilder`, about 70-90 s, writes straight into
  the data repo (arg 1 / `COUNCIL_WEB_OUT`; `COUNCIL_PART_CAP_MB` tests the split), a run only overwrites, so delete a stale file BY NAME (the 6 old `*.exceptions.2.csv.gz` went 2026-10-04); ships only councils with a `web_export/<slug>` folder (25 now; a council without one prints "NOT SHIPPED"; City of York has none yet); fails above 50 MB/file;
  second arg `profiles` rewrites only profiles.json.gz. Then commit + push website-data, nothing else): `index.csv`, per-council `months.csv` (exception columns describe the slim files) +
  `years.csv` (`Year,Months,Parts,TxRows,Net,ExceptionRows,ExceptionBytes,ExceptionGzipBytes`), **exception files in ONE slim format**: `<slug>/<YYYY-MM>.exceptions.csv.gz` (a month) and
  `<slug>/fy-<YYYY-YY>.exceptions[.N].csv.gz` (a year bundle, cut into parts of whole months only above 12 MB raw: none are today; largest year 1.2 MB gz / 11 MB raw). Format: a `@2022-11` marker
  line, a header, rows (Council/Year/SupplierKey dropped; TransactionGross empty when = Net; Detail + ExplainedMeaning on the first line of each group only; group ids per month section; last two columns `Gross` = stated Gross, kept only on a Schedule A row where it differs from Net and on NSquaredRows lines (before this the page showed A rows' summed TransactionGross as "gross"), and `Care` = "1" on the first line of a Wokingham B group whose lines all sit in care cost centres, joined from the month slice by transaction number: 6,826 of 12,174, matching the spec).
  Month TRANSACTION slices
  (`<slug>/<YYYY-MM>[.N].csv.gz`, 2,432 files) are fetched only by "See the source rows"; also `cross/*.csv.gz`, `profiles.json.gz`, `PREREG_BUDGET_TEST.txt`. After every `phoneexport`/`export`:
  re-run the builder (no site publish). Profile text is cleaned in the BUILDER (engine names -> the page's words, working-file sentences dropped; REWORDED/HELD BACK lines print for the profile owner).
- **Code**: `Services/CouncilWebData` (fetch, `InflateAsync`, caches), `CouncilLoad`, `CouncilMonth` (`MonthScan`: byte-level scan, no string per line, columns by header name), `CouncilSource`, `CouncilChecksWeb` (plain loops: LINQ over
  decimals is slow in WASM), `CouncilTerms`, `FoiTray`, `CouncilGap` (reads only `cross/budget_units.csv`; K3 EXCLUDES employee pay); `Components/LoadPicker|YearBlock|GapBars|GapPanel|CheckPanel|BudgetPanel|BudgetTestPanel|FoiTrayPanel|SourceBlocks|SocialCarePanel`. Each load logs `CW-PERF` lines.
- **N-squared runs** (Wokingham academy runs print a payee's n lines n times): Schedule A class `NSquaredListing` (explained style, caption "publication quirk, not extra money") and a B reading `NSquaredRows` (group stays open). Their value is the STATED Gross once (MonthScan sets `Value = max`), never the inflated row total; FOI facts state gross and row total only. Also `Plural`/`NumLines` in CouncilTerms: never write "N lines" by hand.
- **Care caption** (`Care` flag, Wokingham B groups) and **`SocialCarePanel`** (Wokingham page only, closed until tapped, 4 files in `wokingham/socialcare/`: concentration, providers, rates, invoice_date_repeats; `new_providers` and `companies_house_links` are not shipped): HHI/top-10 table, who is paid by year with share and a Companies House record (ALWAYS with its source; link hidden when the match is uncertain or contradicted by dates; date flags shown as sentences, never as a finding), nursing/residential weekly medians only, invoice-date repeats and new-large providers with the spec captions. Optalis = "council-owned company". No individual named.
- **Pattern readings**: RecurringBatchRate and CadenceCatchUp are NOT classes; an Unclear group carries them in `ExplainedBy` + `ExplainedMeaning` (tag + caption, group stays open).
- **Request tray** (`FoiTrayPanel`, one letter per council): "Add to request" on every group and cross-check row. The letter states facts and asks "Please provide the records you hold for this
  item, and the reason for it." No class meanings, readings, `Detail` text or loan rebuilds in it. Address = the profile's verified `foi` else a placeholder. Caps 300 items / 100 per letter. Tick
  and source buttons are PLAIN markup with state on the row (a component per row cost ~5 ms each on a phone).
- **Source rows**: the tap fetches that month's transaction slice (all parts), finds the group's transaction numbers (cap 80; a repeated-payment group narrows to its supplier), lists up to 200 lines.
- **Measured** (headless Edge, CPU 6x, 4 Mbps, 100 ms RTT): `scripts/council-load-perf.mjs <dist> 6 4096 100 <data> [big|select|cancel|care]` (`care` = Wokingham quirks, care captions, social care panel), `council-perf.mjs`,
  `gap-perf.mjs`. `ONLY=leeds` limits `big` to some councils; scenario `new` prints the Surrey/Essex/Hertfordshire notes. **Leeds 2022-23 Load is NOISE, not a regression** (3 interleaved passes, old vs new dist, same data): old 8.6/7.3/8.6 s, new 8.5/9.2/7.7 s,
  fetch-wait constant 2.54 s, inflate+scan swings 4.3-6.1 s run to run; the machine ran ~1.5-2 s slower that evening than at 4.9/6.4, so compare only interleaved passes of both builds, never a number from another hour.
  2026-10-04 Load wall / worst stall: Surrey 2025-26 5.4 s / 145 ms; Essex 7.5 s / 97; Hertfordshire 1.6 s / 0; earlier: Cornwall 2025-26 5.4 s / 207 ms; Sheffield 4.3 s / 63;
  Wokingham 2020-21 0.8 s / 57; Wokingham two years 1.9 s / 98. Social care panel first open (4 files, 50 KB gz) 1.3 s / 355 ms; year switch 0.3 s. Select all (earlier): Leeds 12 yrs 17.7 s / 131; Sheffield 16.1 s / 228;
  RSS ~1.15-1.3 GB. Open a list 27-300 ms; source rows 1.6-2.4 s. Page open 0.4-2 s, boot 1.0-1.4 s.
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

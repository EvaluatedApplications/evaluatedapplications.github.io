# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-09-13

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/`). Every tool runs entirely client-side: no server, no
upload, the compute happens in the visitor's own browser tab. Charter:
`MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — a sibling repo that only ever
*consumes* MonoRepo packages via published NuGet, never source.

**Purpose**: each tool is a real, working demo of a published `EvaluatedApplications.*` package's
capability, driven live by the visitor — no smoke and mirrors. Five tools: **The Analyst** (HoloDb),
**The Creature** (AlgFormer/HoloFormer + Tracer), **The Forecaster** (AlgFormer/HoloFormer),
**Prism** (AlgFormer/HoloFormer, trained-checkpoint chat REPL), **Prose** (HoloDb + AlgFormer,
grammar-mining corpus generator). Plus one **unlisted** page (below), a client preview.

**§3 note**: compacted 2026-09-13 (was ~1,215 lines of dated incident logs) to current-state facts
only — operational memory, not a changelog. Keep future additions terse; prune before growing past budget.

## Site plumbing
- `Program.cs`: standard WASM host; one scoped `HttpClient` (`BaseAddress =
  HostEnvironment.BaseAddress`); `AddSingleton<HoloKernel.SessionHost>()` (shared model cache, see
  below); `AddSingleton<ContentDbHost>()` (unrelated spike, `Pages/ContentDbSpike.razor`).
- Nav (`MainLayout.razor`) is `Home · Tools · NuGet` (3 items, lean by design). A new tool needs a
  `Home.razor` gallery card, **not** a nav entry. Brand mark: prism-triangle SVG (`M16 5L27 26H5Z`,
  7-stop ROYGBIV gradient), matches the static site's motif — sweep both by hand if it ever changes
  (no shared token file across the repo boundary).
- `Home.razor` (`@page "/"`): the gallery. Each tool is `<a class="card tool" href="/tools/<slug>"
  style="--cat:...">` — a single hex for a one-package tool, a hard-edged
  `linear-gradient(90deg, A 0%..50%, B 50%..100%)` + `--cat-root:A` for a genuine multi-package tool
  (Creature, Prose) — plus a `live`/`soon` tag, a package `.ver` pill, `.desc`, `.go-in`.
- `wwwroot/index.html`: `<base href="/tools/" />`; links `/assets/site.css` (shared design system) +
  Showroom's own `boot.css`/`depth.css`; boot-log narration (`loadBootResource` + WASM-compile-gap
  observer); GitHub Pages SPA deep-link restore; two reusable JS interop helpers:
  `window.analystDownload(name, text, mime)` (Blob-URL save — generic despite the name, reused by
  Prose) and `window.copyText(text)` (`navigator.clipboard.writeText`, added for Prose's Copy
  buttons).
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css` (Blazor CSS isolation can't share a
  partial file across components) duplicating a shared base block verbatim (`.room`/`.crumb`/
  `.room-head h1`/`.lede`/`.badges`/`.err`/`.hint`/`.controls`/`.go`/`.ghost`/`.progress-*`/`.speed`/
  `.outro`, plus `.steps`/`.dropzone`/`.dz-*`/`.paste-wrap`/`.paste` for a text/file-input tool) before
  its own classes. Copy from `Prose.razor.css` or `Analyst.razor.css` rather than inventing it fresh.
- **Parallax depth/glow** (`wwwroot/css/depth.css`): 3 scroll-driven tiers (far = `<main>` wallpaper,
  near = `.room-head`, mid = the tool's panel classes), zero JS, `prefers-reduced-motion`-gated.
  Tinted per tool via `[data-cat]` on the outer `.room`: one package name (`"holodb"`) sets
  `--glow-near`/`--glow-mid` to `var(--c-<pkg>, #fallback)` (resolves the static site's live token
  when present, else the hand-duplicated fallback hex). A genuine multi-package tool uses a
  **chord** (`"algformer-tracer"` Creature, `"holodb-algformer"` Prose): sets `--glow-*-a`/`-b`,
  routes onto `sr-parallax-*-chord` keyframes (two stacked `drop-shadow()`s, never a blended hue) via
  an override selector naming that tool's own panel classes — copy Prose's or Creature's block for a
  new chord tool (one `[data-cat="a-b"]` rule + 2 override selectors).

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer + the `Prism` package — a
`ProjectReference` here is the designed path, not a MonoRepo boundary break. Surface used:
- `ModelSpec` — shape + the **S>1 invariant enforced by construction** (`Validate()` throws on
  `MinShifts<2`; also rejects `Layers>1 && KPass>1`, moot today since every from-scratch tool is L=1).
- `HoloSession.Create(spec)` / `.FromCheckpoint(bytes, kPass, serveAlpha)` — **K and alpha are
  mandatory args**, never defaulted (`HoloFormer.Iters`/`.IterAlphaServe` are NOT persisted by
  `Serialize()` — a deserialized checkpoint always reads back `1`/`1`; source K/alpha from sidecar
  metadata). `.Logits(ctx)` full recompute; `.Stats()` -> `ModelStats`. `.NewServeCache()`/`.Prime()`/
  `.StepToken()` O(1)/token KV-cache path — verified bit-identical to `.Logits` **only for a
  multi-layer model** (Prism, L=6); NOT verified for L=1 (Creature/Forecaster) — don't route those on
  without re-checking.
- `AlphaRamp.Complete` — stateless no-op ramp (`Alpha` always `1.0`), passed to `RefinementLoop`'s
  ctor and to `session.ApplyServe(...)` right after acquiring a session (matters: a fresh session
  starts at `ServeAlpha=0` until the first `Observe()`). K-pass depth is read live off an
  already-proven checkpoint, not picked fresh per tool, so nothing needs a warm-up anymore.
- `RefinementLoop.Observe`/`.ObserveSequence` — the house live-training pattern (wraps
  `NewGrads()`->`IterAccumulate`/`StackIterAccumulateAllPos`->`Step`). `HoloFormer.TrainStep` exists
  but does **not** honour the K-pass depth knob — never use it where K matters.
- `Decoding` (`DecodePolicy`/`Gate`/`DegenGuard`) now comes from the **`Prism` package's**
  `Prism.Inference` namespace, not a HoloKernel copy — `@using global::Prism.Inference` is
  **required**, because `Showroom.Pages.Prism`/`.Prose` (the pages' own classes) shadow the bare
  `Prism`/`Prose` package namespaces. Same for `Prism.Lineage.LineageJournal.AlphaFor` (reconstructs
  a checkpoint's trained alpha from its round count) — `@using global::Prism.Lineage`.
- `InspectorTrace` — opt-in "inspect brain" trace, Creature/Forecaster only.
- `CheckpointFetch.FetchAndDecompressGzipAsync(http, gzUrl)` — fetches a `.gz` sidecar, decompresses
  with the BCL's own `GZipStream` (works inside WASM, no JS interop). GitHub Pages serves data files
  byte-for-byte uncompressed, so a raw checkpoint dropped in `wwwroot/data/` needs a hand-shipped
  `.gz` sibling to download small.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool. Every consumer of
  Prism's checkpoint (Prism, Analyst's novelty scan, Prose's "Score with Prism" mode) passes key
  `"prism"`, so whichever loads it first this page load, the rest reuse it instantly.
  `IsLoaded`/`Forget`/`Clear` round it out. **Ephemeral** — a reload drops everything, no
  persistence; WASM has no filesystem and none is wanted here.
- **Browser contract**: visitors **train**, never **reshape**. A tool's `HoloFormer` shape is fixed
  at construction for the session; `GrowLayers`/`GrowShifts` are real but PrismStudio/server-side
  only — a better model reaches visitors via a new checkpoint, never runtime shape mutation.

**Reply cap** — `MaxReplySteps` (renamed off `MaxReplyChars` 2026-09-14, the old name lied: the loop
always counted token STEPS). **Pinned to a measured constant, 56, NOT `Context/2` any more.** The
fraction silently broke on a checkpoint swap: context grew 192->512 (cap 96->256 steps) AND the
re-minted vocab emits far more per step (1.8 -> a MEASURED 3.13 bytes/token), so replies went ~170
-> ~800 chars. 56 steps ~= 175 chars, the length the previous checkpoint shipped at. **Re-measure on
every re-mint** (bytes/token changes). What the measurement showed, because it kills the obvious
theory: over 90 gated-decode runs the real-word rate is FLAT at 88-91% across all 256 steps — no
decay with length, so there is no "good portion" to cut at. Greedy loops early but the page does not
decode greedily; under the real gate repetition never fired inside 400 chars. The real defect is the
model emitted its own STOP token in **0 of 90 runs**, so every reply runs to the cap and ends
mid-sentence. Do NOT paper over that with a sentence-boundary heuristic (`feedback-no-chat-bandaids`).

**Opener** (`data/oracle-opener.txt`, a DATA refresh): **"What's happening?"** as of 2026-09-14,
set on the user's explicit instruction ("have it be, What's happening?"). **Shipped against the
measurement, deliberately — this is the user's call, don't silently "fix" it back.** Measured at the
live 56-step cap over 12 seeds, scored on real-word rate against a 78,796-word dictionary built from
the model's own corpus: it returns **75.7%** real words, the WORST of every candidate tested, and
drifts into transcript/subtitle register ("Hivice, Santral's wayve latefferet"). The two predecessors
scored 90.1% ("Hello, say something nice.", chosen by a 24-candidate x 12-seed sweep) and the best
question-form alternative measured is **"How are you today?" at 91.7%** — offered to the user, not
applied. Same-form control: "What is happening?" also measured poorly, so it is the phrasing, not
the apostrophe.

**ASCII apostrophe ONLY in the opener** (U+0027, byte 39) — never the typographic U+2019 the user
naturally types. `SubwordVocab.Fold` maps anything outside `CharVocab.Lo..Hi` (32..126) to a SPACE,
so a curly apostrophe would reach the model as "What s happening?" while the page still DISPLAYED
the curly form: a silent mismatch between what the visitor reads and what the model was given.
Verified byte-for-byte on the deployed file. Same trap applies to any smart quote, en/em dash or
ellipsis character in this file.

**Re-measure the opener on every checkpoint refresh** — a good opener for one set of weights is not
automatically good for the next.

**Checkpoint refresh** (Prism's `oracle-brain.bin`+`-vocab/-rounds/-stackk/-iterwarm.txt`, a
point-in-time copy from PrismStudio): a **data-only** refresh needs no `dotnet publish` — raw-copy
into `wwwroot/data` and `dist/data`, regenerate `oracle-brain.bin.gz` via a plain `GZipStream`
one-liner. Only a **source** change needs a full publish + `dist/` refresh. Cross-check
`-stackk`/`-iterwarm` against PrismStudio's live consts fresh every time. Write sidecars via
`[System.IO.File]::WriteAllText(path, text, new UTF8Encoding(false))`, never PowerShell
`Set-Content -Encoding utf8` (silently prepends a BOM).

## The Analyst — `Pages/Analyst.razor` (route `/analyst`)
In-browser data profiler + live SQL REPL over **HoloDb** (`Database.Open(null)`, in-memory). Sniffs
CSV/TSV/JSON/JSONL/plain-text, infers a type per column, bulk-loads (100k-row chunks), profiles every
column via HoloDb aggregates. Also: entity extraction, a no-SQL chart builder, click-to-filter
drill-down, a free-form SQL prompt + CSV export via `window.analystDownload`. Caps: 500k rows, 2M-char
entity scan, **64MB upload** — this app's own precedent for "how big can a paste/upload be" (Prose's
64MB cap matches it deliberately). Long O(rows)/O(chars) work is `async`, yielding every N
columns/patterns/rows — the house pattern every later tool follows. **Novelty scan** (opt-in): scores
a text column's surprisal against Prism's checkpoint via `SessionHost` key `"prism"`, capped at 120
values × 32 tokens, yielding every 16 tokens.

## The Creature — `Pages/Creature.razor` (route `/creature`)
A 20×20 grid the visitor draws where a **HoloFormer** brain learns to forage live, on **HoloKernel**.
`Dim=384, Layers=1, KPass=` live-read from `data/oracle-stackk.txt`, `MaxCtx=32`, `MinShifts=8`
(floored by the S>1 invariant). Distance field: **Tracer**'s `GridTactics.Reachable` BFS to the
nearest apple; trains toward the advantage-weighted decisive move. Training is a producer/consumer
pipeline: `EndEpisode()` writes to a `Channel.CreateBounded<EpisodeBatch>(4, DropOldest)`; a
background `RunTrainer` drains it, so simulation never blocks on training and a slow consumer drops
stale episodes for fresh ones. `ResetBrain` drops the session (WASM: nothing persists anyway).

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same HoloFormer substrate as Creature, on a real hourly AAPL tape. `Dim=128, Layers=1, KPass=` (same
live source), `CandleContext=128` -> `MaxContext=256`. Tokenisation ported from MarketSim:
`[TIME_bucket][RETURN_bucket]`, `Vocab=17`. Data: `wwwroot/data/forecaster-history.json` (~3,484 real
hourly AAPL candles, refreshable via `scripts/fetch-forecaster-history.ps1`); an optional live
Finnhub top-up activates only if `wwwroot/data/finnhub-key.txt` exists and NYSE is open. Real
candlestick chart; `RunOneAnimatedTick()` splits each tick into PREDICT (countdown) then
REVEAL+TRAIN (win/lose flash), paced by the speed slider.

## Prism — `Pages/Prism.razor` (route `/prism`)
A real chat REPL over a point-in-time copy of the user's live PrismStudio checkpoint. Rolling
turn-based context, **untagged wire format** (2026-09-09: dropped `GroupChat`'s `"user: "`/`"prism: "`
tags to buy back the ~23% of the context window they cost at ctx=192), capped to the checkpoint's
`Stats().Context` tokens via `CapRecent` (drops whole leading turns, never mid-turn). Generation:
`ServeCache`-based O(1)/token stepping, stops on `CharVocab.End`, `Prism.Inference.Gate`/`DegenGuard`
for confidence-gated decoding. `MaxReplyChars = Context/2` (measured: drift starts a half-window
before the full ceiling). `Prime()` **strips** the trailing newline rather than appending one
(appending was the measured cause of a stray junk token opening every reply). `_turns` capped +
render-batched (an earlier unbounded version was a real, fixed OOM).

**Audible tokens** (2026-09-13, OFF by default): each real generated token plays one short tone
(~110ms) synthesised straight from `HoloFormer.Face(id)` — a face is Dim doubles interleaved as
(cos, sin) pairs, i.e. a literal Fourier series, so even/odd indices feed `AudioContext.
createPeriodicWave(real, imag)` with no transform (JS helper: `window.prismAudio` in `index.html`,
lazily created — `.unlock()` is called from the mute toggle's own click, the gesture that unlocks
browser audio). Face is a free read-only lookup (no forward pass) — the deliberately NOT-built
richer mode is `LayerFaces`/the forward pass's own hidden state, skipped because it costs a real
extra O(context) recompute per token on top of the live KV-cache step, which would double the
visible per-token cadence. **Pitch comes off the FACE, not the token id (2026-09-14, user:
"make it fully authentic")** — the circular mean phase of the frozen identity band
(`FrozenPrefix/2` comps; every frozen component is unit modulus, measured 1.000 exactly on the live
checkpoint, so summing raw (cos,sin) pairs IS the circular mean), mapped LOGARITHMICALLY over
150-800Hz. That band is `PhasorCodec`'s signature for the token's TEXT, so a word keeps its pitch
across checkpoints and vocabularies, and pitch stays fixed for the life of a model while timbre
evolves with the learned tail. It replaced an integer hash of the token id mapped linearly in Hz,
which was arbitrary (subword ids are vocabulary-table order, so a re-mint re-pitched the whole
page). Measured over 1,024 tokens: even spread, 91-123 per tenth of the range against 102 expected.
The honest limit, stated in the code: a Fourier coefficient list has no fundamental of its own, so
some rule must supply f0; the rule is ours, every number it reads is the model's. **The DC slot is
now written explicitly as zero and the face starts at index 1** — `createPeriodicWave` treats index
0 as DC per spec, so passing the face straight through used to dump comp 0 into an inaudible
constant and shift every other comp down one harmonic. Volume slider is hard-capped via `VolumeCeiling=0.32`
before it ever reaches the audio graph. Persisted via `localStorage` (`prismAudio.getPref/setPref`).
**Synthesis is ADDITIVE and INHARMONIC as of 2026-09-14** (`window.prismAudio.partials`), replacing
`createPeriodicWave` the same day. The user heard that it "still sounds musical" after the pitch fix
and was right: `createPeriodicWave` can only place coefficient k at k x the fundamental, i.e. it
FORCES a harmonic series, and the harmonic series is the physical basis of tonality — so that one
API choice, not the pitch, was manufacturing the musicality. Proof it was the mapping and not the
notes: the pitches measured at CHANCE against 12-TET (22.6 cents mean error, 25.0 expected from
random) while the output still read as tonal. The face has no harmonic structure to justify it;
`Phasor`'s real `LinTheta` ratios run 1.00, 24.66, 34.43, 44.70 where a harmonic series runs 1, 2,
3, 4. Now each of the 128 components contributes ONE partial at its own frequency (derived from that
component's phase, log-spread `PartialSpread`=5 octaves above the base), its own magnitude, and its
own starting phase. MEASURED after the change: inharmonicity 0.239-0.255 where 0 is a pure harmonic
series and 0.25 is random, partials spanning ~250 Hz to ~17 kHz, nothing above 20 kHz. Rendered as
ONE `AudioBuffer` via a two-term sinusoid recurrence, NOT 128 oscillator nodes per note and NOT
`Math.sin` per sample (that would be ~675k sin calls per note on the generation thread); recurrence
verified numerically to 3.3e-11 worst-case error over a full note, ~3,600x below float32 storage
precision. The buffer is peak-normalised, which is level only and cannot touch the spectrum.
**Two things stay imposed and are stated in the code**: the 150-800 Hz base window and
`PartialSpread`. A phasor face has no time axis at all — its components are phases, not frequencies
— so any audification must invent the frequency axis; the point is that the invented part is now one
range mapping rather than an imposed harmonic structure. Do NOT "fix" the sound by quantising
pitches to a scale or going back to a harmonic render: explicitly refused by the user 2026-09-14
("I wanna hear the authentic sound"), both would paint structure onto the model that it does not have.

## Prose — `Pages/Prose.razor` (route `/prose`)
Paste or drop a body of text; **Prose** (`ProseEngine`) mines it through a real rules-first English
grammar parser in the tab (`MineText`, no filesystem needed), then recombines what it learned into
new sentences and Q&A pairs to copy into a training corpus (`data/text` for plain sentences,
`data/pairs` for every `prompt<TAB>target` format). The one other genuine two-package composite
alongside Creature — HoloDb (`ProseStore`) + AlgFormer (the optional plausibility HoloFormer) — chord
`data-cat="holodb-algformer"`.

**Input cap: 64MB**, matching The Analyst's own precedent. **Measured** (native-JIT harness against
the real published package, `EvaluatedApplications.Prose` 1.3.0): `MineText` scales near-linearly and
completed cleanly at every size tested (10K-64M chars) — 16.4s / ~237MB managed-heap growth at the
full 64MB, natively; WASM (interpreted, AOT covers only precompiled hot paths) will run slower, never
faster. `MineText` itself has no chunking hook, so mining is **chunked and yielded by the page, not
the library**: `ChunkText` splits the input at whitespace/newline boundaries near 200,000 chars and
calls `MineText` once per chunk with `await Task.Yield()` between — `ProseStore.Mine` accumulates
additively across calls, so this changes nothing about the mined result.

**Output is a set of independently tickable generators** — only what's ticked costs anything:
Sentences (`Generate(n, seed)`, plain corpus), Q&A pairs (`GenerateQa`), Q&A passages
(`GenerateQaPassages(n, sentencesPerPassage, seed)`), Two-turn pairs (`GenerateTwoTurnQa`, ticked by
default — already the `prompt<TAB>target` shape), **Conversations (packed)** (`PackConversations`,
below). One shared `seed`. Each gets its own labelled, independently copyable block (line/byte
counts, Copy via `window.copyText`, Download via `window.analystDownload`). **Avoid
`WriteCorpus`/`WriteQaPairs`** — both write to disk, useless in WASM; the `Generate*` methods
returning in-memory `List<T>` are what make this viable at all.

**Conversations (packed)** — a `data/text`-format generator, NOT `data/pairs`: training uses
ALL-POSITIONS loss over a fixed window, so one Q&A pair per window wastes most of a 512-token
forward pass. `PackConversations` packs random, UNRELATED `GenerateQa` pairs (simulated
topic-hopping, not a coherent thread) as many as fit before padding would be needed (never pads) —
15 packed turns yield ~15x the gradient of 1 at the same forward cost. Turn format is verbatim
`PrismFormer.GroupChat` wire tags (`"user: "`/`"prism: "`), matching real serving, not invented.
Blocks blank-line separated. Target size is **characters, not tokens** (stated in the UI) — measured
bytes/token across this project's vocabularies runs ~1.8-3.9, so ~1,000-2,000 chars stands in for a
512-token window. Shows blocks/avg turns/avg chars/total turns, so a visitor can tell if it's full.

**Plausibility scoring — a 3-way choice, `None` by default, nothing fetched/trained speculatively**:
1. **None** — Prose's own grammar output, unmodified. Fully useful here; every generator/format/
   copy/count works with zero model involved.
2. **Score with Prism** — the deployed checkpoint via `SessionHost` key `"prism"` (instant if any
   other tool already loaded it; fetched only on clicking Generate). Prose gives no way to inject an
   external scorer into its own best-of-K generation, so this **re-scores the generated text** after
   the fact: per line (or per Q&A answer/target), encode with the checkpoint's `SubwordVocab`, walk
   token-by-token through `session.Logits`, sum surprisal bits (same pattern as Analyst's novelty
   scan) — lower mean = more plausible English. Capped 150 lines × 24 tokens/block, yielding every 8
   tokens. `keep best N` (0 = keep all, ranked) truncates each block. Labelled honestly as one small
   general-purpose model's opinion, **not** a correctness filter (trained on an unrelated corpus).
3. **Train one on my text** — `TrainPlausibilityText(text, epochs, lr)`, trained on (a bounded slice
   of) the visitor's OWN text, judging "sounds like MY source" rather than English generally. Once
   trained, `Generate`/`GenerateQa*` already thread it into their own best-of-K internally — no extra
   wiring needed here. **Measured, load-bearing cost**: a native-JIT harness against the real package
   measured a stable **~0.22-0.24 ms/char/epoch** regardless of corpus size — 2,000,000 chars × 1
   epoch took ~440s natively; WASM will be slower. So: small defaults (30,000 chars, 6 epochs,
   lr=0.005, all adjustable); a **live on-device time estimate** before committing (an ≤8,000-char
   probe, 1 real epoch, timed and linearly extrapolated — measured on THIS device, never guessed,
   re-measured on every param change via `@bind:after`). **Real, disclosed Prose API gap**:
   `PlausFormer.Train` exposes no per-epoch callback, no cancellation, and `Plausibility` has no
   public setter — training is one single blocking call with no way to yield mid-call or inject an
   externally-hosted scorer (e.g. the same Prism session mode 2 already loads). The time-estimate UX
   is the best mitigation available from this side of the NuGet boundary; flagged as a version-gap
   for the coordinator, not something to route around by reaching into MonoRepo source.

**Gotcha found while building this**: `ProseEngine.Plausibility` has no reset/setter — once trained,
`Generate`/`GenerateQa*` use it forever after, even switching back to `None`/`Prism`. `Prose.razor`
guards it two ways: the estimate probe (mode 3) trains a throwaway `ProseEngine`, never `_engine`
itself; `GenerateOutputs` re-mines a fresh `_engine` if a prior run trained one and mode != `Train`.

Demo panel: after mining, `ProseEngine.Parse`/`Explain` run once over a small (≤1,500-char) prefix to
show up to 5 parsed sentences' real tags/roles — cheap, independent of the chunked mining, so a
visitor sees the grammar actually working.

## Unlisted: RecycleDAO marketplace prototype — `Pages/RecycleDaoDemo.razor` (`/recycledao-demo`)
NOT a package-capability demo, NOT in the gallery — a private, link-only client preview
(`C:\Users\dongy\RecycleDAO`, `recycledao-owner`'s repo; never edit it from here). Absent from
`Home.razor`/nav; `noindex,nofollow`. Mint invariant: `MintForApproval` is the only method that
increases `_totalMinted`. Only the top utility strip/footer/photo-upload/notifications stay inert,
tagged `.mk-tag` "mockup".

## Dependencies (exact NuGet versions, `Showroom.csproj`)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.11** — **must stay version-equal to the
  installed WASM runtime pack**; a skew caused a real 2026-09-06 outage (interpreter hit unrecognised
  IL from a mismatched native runtime, died silently at boot). Check `dotnet --list-runtimes` when
  the SDK moves.
- `EvaluatedApplications.HoloDb` **1.10.0** — Analyst, Prose.
- `EvaluatedApplications.AlgFormer` **2.8.0** — Creature, Forecaster, Prism, Prose (`PrismFormer`
  namespace: `HoloFormer`/`HoloShape`/`CharVocab`/`SubwordVocab`). 2.8.0 raised
  `SubwordVocab.MaxLen` 4 -> 16 to match what PrismStudio actually mints with; before it, a
  freshly-minted checkpoint threw `ArgumentException` in the `SubwordVocab` ctor and simply could
  not be deployed here. Verified against the published package with the live 928-subword vocab
  (301 entries over the old limit, longest 11): loads and round-trips. **A Prism checkpoint refresh
  minted after 2026-09-14 needs this version or newer.**
- `EvaluatedApplications.Tracer` 1.1.0 — Creature (`Tracer.Helpers.GridTactics`).
- `EvaluatedApplications.Prose` **1.3.0** — Prose (`Prose` namespace: `ProseEngine`/`QaPair`/
  `ParsedSentence`/`Pos`/`Tok`) — ground-truth-checked against the real package before wiring; API
  matches `MonoRepo\Prose\CLAUDE.md`'s own summary exactly. Restoring it forced `HoloDb`
  1.4.0->1.10.0 and `AlgFormer` 2.4.0->2.7.0 (NU1605 downgrade error otherwise) — a real bump for
  every other tool sharing those two packages, not just Prose.
- `EvaluatedApplications.Prism` (transitive, via `HoloKernel`) — `Prism.Inference`/`Prism.Lineage`.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism, Analyst, Prose.
- `PublishTrimmed=true` + `RunAOTCompilation=true` — **AOT is load-bearing, not a perf luxury**: with
  it off, the Mono WASM *interpreter* hit IL it doesn't implement (`Prism.Inference.DecodePolicy`'s
  static ctor) and died; with AOT on, that method is precompiled and the interpreter never sees it.
  `EmccLinkOptimizationFlag=-O1` (`-O2` OOMs the linker on this dev machine during concurrent training).
- `WasmEnableThreads=false` — real threading was tried and reverted same-day (a laptop's Continue
  button stuck disabled while a phone loading the identical deploy worked — likely the threaded
  runtime's own boot-time worker-pool sizing, not app code). Re-enable only once reproduced/fixed.
- **Version bumps only via `dotnet add package`** — never hand-edit `<Version>`.

## Boundary (hard, from the agent charter)
- **NuGet only, never MonoRepo `ProjectReference`.** Verify API assumptions against the actual
  published package before wiring new code. `HoloKernel` is the one exception — a sibling in-repo
  RCL, itself NuGet-only.
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call**; **Forecaster's live top-up** needs a
  Finnhub key this agent can't self-register (drop at `wwwroot/data/finnhub-key.txt`, absent by
  default, degrades gracefully).
- Never touch `AboutUs/site/*`, nav, or the shared design system — `website-owner`'s. Never launch
  the app / open a browser — build-verify only; demonstrating a tool live is the user's to do.

## Standing technical facts
- **Shifts must be > 1, always** — at S=1 every relation-bank is a pure diagonal, zero cross-channel
  routing. Re-derive a floor from `bindRank = shifts·d/2` per tool's own d/context; never copy
  another tool's `MinShifts` verbatim.
- `golden: true` on every `HoloFormer`. WASM has no filesystem — nothing persists across a reset.
- WASM is single-threaded/interpreted (AOT covers only precompiled hot paths) — `Parallel.For`/
  `IParallelMap` degrade to sequential, not a crash, but keep live-training shapes small and any
  batch text work cooperatively yielded (see Prose above for a library call with no chunking hook).
- `HoloFormer` ctor (verified via reflection against `AlgFormer` 2.7.0's real DLL, not memory):
  `(vocab, shifts, layers, maxContext, dModel=0, frozenPrefix=-1, embedSeed=null, seed=42,
  bindFfn=false, golden=false, normalize=true, unitary=false, growFromFront=true)`. `HoloShape`
  statics: `ShiftsFor(ctx,d,ratio=0.25)`, `BindRank`, `CleanCapacity`, `InteractionBudget`,
  `EquivCompute(d,L,K)`. `Face(id)`/`LayerFaces(toks)`/`InspectStackIter(Faces)`/`DecodeFace` also
  confirmed present on this version — see Prism's audible-tokens entry above for `Face`'s shape.

## Build / verify
`dotnet build Showroom.csproj -c Release` — green (0/0) with all five tools + HoloKernel wired in.
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
  global::<Namespace>` — a plain `@using` resolves to the page's own class instead. Affects:
  `Prism.razor` (`global::Prism.Inference`/`.Lineage`), `Prose.razor` (`global::Prose`/
  `Prism.Lineage`), `Analyst.razor` (`global::Prism.Lineage`). A future tool named after any package
  needs the same check.
- A new tool page gets the parallax/glow treatment free by reusing the house `.room`/`.room-head`/
  panel shape — it just needs its own `data-cat="<pkg>"` (or chord) on the outer `.room`.
- Multi-package tools force a real dependency-version bump for every tool in this one `.csproj`
  (Prose's restore bumped HoloDb/AlgFormer for the other four tools too) — re-check `dotnet build`
  after adding a new tool, not just its own page.

# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-09-21

Blazor WebAssembly app at `C:\Users\dongy\AboutUs\Showroom`, published under `/tools` on the public
site (`AboutUs` repo, base href `/tools/`). Every tool runs entirely client-side: no server, no
upload, the compute happens in the visitor's own browser tab. Charter:
`MonoRepo\.claude\AGENT-CHARTER.md`. This repo is NOT the MonoRepo — a sibling repo that only ever
*consumes* MonoRepo packages via published NuGet, never source.

**Purpose**: each tool is a real, working demo of a published `EvaluatedApplications.*` package's
capability, driven live by the visitor — no smoke and mirrors. Six tools: **The Analyst** (HoloDb),
**The Creature** (AlgFormer/HoloFormer + Tracer), **The Forecaster** (AlgFormer/HoloFormer),
**Prism** (AlgFormer/HoloFormer, trained-checkpoint chat REPL), **Nano Stories** (AlgFormer/HoloFormer,
the SAME checkpoint as Prism asked to write instead of chat), **Prose** (HoloDb + AlgFormer,
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
  **Exception (2026-09-21)**: `wwwroot/css/voice.css` is a real shared GLOBAL stylesheet (not
  component-scoped), styling `HoloKernel.TokenVoiceControls` — since that component lives in the
  HoloKernel RCL rather than under `Pages/`, the usual per-page duplication doesn't apply; both
  `Prism.razor.css` and `Stories.razor.css` correctly carry NO `.sound-*` rules of their own anymore.
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
- `PrismCheckpoint` (2026-09-21) — `SessionKey` (`"prism"`, the canonical `SessionHost` key) plus the
  two formulas (`ResolveK`, `ReconstructAlpha`) Prism and Nano Stories both need IDENTICALLY to
  correctly serve the shared checkpoint — a mismatch here would silently serve a crippled model.
- `SessionHost.GetOrCreateAsync(key, factory)` — keyed by **model**, not tool. Every consumer of
  Prism's checkpoint (Prism, Nano Stories, Analyst's novelty scan, Prose's "Score with Prism" mode)
  passes key `"prism"` (`PrismCheckpoint.SessionKey`), so whichever loads it first this page load, the
  rest reuse it instantly. `IsLoaded`/`Forget`/`Clear` round it out. **Ephemeral** — a reload drops
  everything, no persistence; WASM has no filesystem and none is wanted here.
- `TokenVoice` / `TokenVoiceControls.razor` (2026-09-21) — Prism's per-token voice (face→tone synth,
  swayed beat pacing, render-batch cadence), lifted out of `Prism.razor` so Nano Stories shares it
  byte-for-byte instead of re-deriving it. `TokenVoiceControls` is this RCL's first Razor component,
  styled by the global `wwwroot/css/voice.css` (not component-scoped CSS) so both consuming pages
  render it identically. Persists mute/volume under the SAME localStorage keys Prism always used —
  deliberately one shared preference, not per-tool. The full tuning history (mid-bass -> robot -> 80s
  arcade robot, the additive/inharmonic rewrite, the reversed note/flavour mapping) now lives in this
  type's own doc comment, including the two standing rules ("never quantise to a scale", "don't retune
  per tool") — not duplicated here or per-page anymore.
- `DegenerateTail.Start(ids)` (2026-09-21, lifted out of `Prism.razor`) — trims a repeating tail (e.g.
  "ERE ERE ERE") a generated sequence ends on; covers the period>1 gap `Prism.Inference.DegenGuard`
  itself doesn't catch. Shared by Prism and Nano Stories. **`DegenerateTail.SafePrefix(ids)`
  (2026-09-22)** is the streaming companion: the same scan one repetition short of the real trigger, so
  a repeat that is still FORMING is held back from the DOM instead of being painted and then yanked.
  **Both pages now run the trims INSIDE the generation loop, not after it** (user: "have the cut happen
  then, not at the end") — `Prism.razor`/`Stories.razor` call `DegenerateTail.Start` every step and
  break on a hit, painting via their own `PaintPrefix` helper. `Stories.razor`'s whole trim policy is one
  method, `Trim(List<int>)`, called both for every story-ending paint and for the returned text, so the
  animation's last frame and the finished story can't disagree.
  **`MaxSentences` (was 5) REMOVED 2026-09-22 on user request**, along with the live sentence counting
  that stopped generation on the capping sentence. It was counted honestly by eye, but it was almost
  certainly fitted to an ENGINE BUG rather than to the model: the last position of a full context window
  was structurally untrainable, so its prediction head decoded as confident noise (MEASURED on the
  deployed checkpoint: 26.6 bits, P(top-1) 0.60, against ~5.7 bits at every other position), and with a
  5-token prompt that window fills around token 66 of generation, which is about where a fifth sentence
  lands. Fixed in AlgFormer 2.12.1 (scorer takes one token of overflow) + 2.12.2 (the corpus window draw
  can reach its own ceiling; without it 2.12.1 never fired). **Until a post-fix checkpoint ships, stories
  visibly degrade past ~5 sentences instead of being cut there — that is intended, so the real onset can
  be re-counted honestly.** `MaxStorySteps` and the sentence-boundary trim (drops an incomplete trailing
  fragment) both stay. Prism's own `MaxReplySteps = 56` is the same kind of constant and wants the same
  scrutiny on the next refresh.
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
**Nano Stories has its own separate cap, `MaxStorySteps`=128** (same vocab, roughly double this one,
since a short story reads longer than a chat turn) — intentionally a different constant on a different
page, not a shared value; re-measure both together on the next checkpoint refresh, not just this one.

**No example exchange on load** (removed 2026-09-15, user: "remove the first prompt"): the chat opens empty on the "Say hi" hint. The old seed exchange (an opener from `data/oracle-opener.txt` plus the model's reply, last opener "What's happening?") is gone, along with the code that fetched the opener; `oracle-opener.txt` may still sit in `wwwroot/data` and `dist/data` but nothing reads it. Its cold-start job survives as a silent warm-up (one prime + one step through the serve cache, result discarded) so the first real reply is not slower. If an example exchange is ever wanted back, re-measure the opener against that checkpoint first, and use an ASCII apostrophe: `SubwordVocab.Fold` maps a typographic one to a space.

**"How Prism works, as sound" section** (2026-09-15, user request): a condensed version of the site's
"Meaning as chords" page (`site/holoformer.html`, website-owner's, linked not copied) under the chat, plus a
"What you're hearing" part tying the audible tokens to the model. Its specifics are checkpoint-bound: "128
tones" = Dim/2 and "plays its part twice" = StackK 2, so a new shape or K means updating that copy AND Nano
Stories' own copy of the same claim (it shares the voice, not the copy block). Never let it drift into "you
are hearing the model think": the sound is the token's face, not the forward pass.

**Chat context, 2026-09-15**: `BuildContextTokens` replaced the text transcript: each finished exchange is
`Encode("user: Q\nprism: ") + Encode(A) + [STOP]` back to back, the pending question is its prompt run alone,
and capping drops whole leading exchanges. This mirrors PrismGym's `PackedPairSource` (`PackPairWindows`), so
the context matches packed training token for token, including the STOP between turns. (The voice/sound
tuning that used to be logged here now lives in `HoloKernel/TokenVoice.cs`'s own doc comment — see the
HoloKernel section above — since it's shared by Prism and Nano Stories, not Prism-only history anymore.)

**Smart-punctuation input fix 2026-09-16** (`HoloKernel/AsciiPunctuation.cs`, applied at every tokenizer entry
point: `Prism.razor`/`Stories.razor`, `Analyst.razor` novelty scan, `Prose.razor` plausibility scoring).
`SubwordVocab.Fold` maps every char outside printable ASCII to a SPACE, so a visitor typing on a phone
(iOS/Android autocorrect the plain apostrophe to U+2019) was asking the model "what s this", and the model
answered the damaged question — it read as the model being bad at contractions when the input was broken
before it arrived. `AsciiPunctuation.Fold` maps curly quotes/primes/dashes/nbsp/ellipsis to ASCII twins
first, a NO-OP on ASCII (0 of 95 printable chars altered, returns the SAME string instance when nothing
folds). KEEP IT IDENTICAL to `MintTokenizer.AsciiTwin` in the PrismFormer studio — train-time and serve-time
tokenisation disagreeing is what caused the underlying mess (30,639 stripped contractions over 25.8% of the
live chat.tsv). The serving-side `SubwordVocab.Fold` in MonoRepo AlgFormer still folds straight to space;
fixing it there needs a NuGet publish, so this Showroom-side fold is the layer protecting the site today.

**Checkpoint refresh** (Prism's `oracle-brain.bin`+`-vocab/-rounds/-stackk/-iterwarm.txt`, a
point-in-time copy from PrismStudio): a **data-only** refresh needs no `dotnet publish` — raw-copy
into `wwwroot/data` and `dist/data`, regenerate `oracle-brain.bin.gz` via a plain `GZipStream`
one-liner. Only a **source** change needs a full publish + `dist/` refresh. Cross-check
`-stackk`/`-iterwarm` against PrismStudio's live consts fresh every time. Write sidecars via
`[System.IO.File]::WriteAllText(path, text, new UTF8Encoding(false))`, never PowerShell
`Set-Content -Encoding utf8` (silently prepends a BOM). Covers Nano Stories too — same checkpoint, same
sidecars, zero extra steps (it reads them itself on a cold load, or reuses Prism's already-loaded session).

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
turn-based context, **tagged wire format** (2026-09-15, user request once the model trained on pairs; reverses the 2026-09-09 untagged format): `user: X\nprism: Y\n` per turn and `prism: ` appended by `Prime`, line for line `StudioModel.Serve`, matching `HoloEngine`'s `GroupChat.AsChat` pair wrapping. A text `\n` encodes to a SPACE (both tokenizers fold it); id 95 STOP only DECODES as `\n` and is only ever appended after a trained answer, so turn separators never become STOP. Tags cost ~9 tokens/exchange at ctx=512 (the 23% cost that justified untagged was at ctx=192), capped to the checkpoint's
`Stats().Context` tokens via `CapRecent` (drops whole leading turns, never mid-turn). Generation:
`ServeCache`-based O(1)/token stepping, stops on `CharVocab.End`, `Prism.Inference.Gate`/`DegenGuard`
for confidence-gated decoding. `MaxReplyChars = Context/2` (measured: drift starts a half-window
before the full ceiling). `Prime()` **strips** the trailing newline rather than appending one
(appending was the measured cause of a stray junk token opening every reply). `_turns` capped +
render-batched (an earlier unbounded version was a real, fixed OOM).

**Audible tokens** — LIFTED into `HoloKernel.TokenVoice`/`TokenVoiceControls.razor` (2026-09-21), see
the HoloKernel section above. Off by default; each generated token plays a short chord straight from
`HoloFormer.Face(id)` (additive/inharmonic synthesis — NOT `createPeriodicWave`, which would force a
harmonic series onto data that has none), paced to a swayed beat while sound is on. The full tuning
history (mid-bass -> robot -> 80s arcade robot pitch window, the note/flavour reversal, the measured
inharmonicity/spread numbers) now lives in `TokenVoice.cs`'s own doc comment, not here — this page's
own `GenerateReplyAsync` just calls `_voice.WaitForBeatAsync()`/`.PlayTone()`/`TokenVoice.ShouldRepaint`.
Standing rule, unchanged: never quantise a partial's frequency to a musical scale, and don't retune
this voice per tool — Nano Stories shares it exactly, not a retuned copy.

## Nano Stories — `Pages/Stories.razor` (route `/stories`)
The literal pitch, stated plainly because it's true: THE SAME MODEL as Prism — same `oracle-brain.bin`,
same weights, same single training run, not a fine-tune, not a sibling. `SessionHost` key `"prism"` (via
`PrismCheckpoint.SessionKey`) — whichever of the two tools a visitor opens first pays the real download,
the other reuses that in-memory `HoloSession` instantly. Shares `HoloKernel.TokenVoice`/
`TokenVoiceControls` with Prism byte-for-byte (see the HoloKernel section above) — same sound, same
swayed-beat cadence, token-by-token reveal, nothing retuned per tool.

**Generation is a plain one-shot continuation, not a chat turn** — deliberately NOT
`Prism.razor`'s `user:`/`prism:` tagged wire format: this checkpoint's corpus is mostly plain narrative
text (TinyStories), so a story prompt is encoded as ordinary continuation text, the shape that slice of
training data actually looked like. `MaxStorySteps`=128 (own constant, see the Reply cap note above).

**The decode gate, not the model, is why a generator reads as deterministic** — the diagnosed root cause
this tool was built to work around: production's `FloorK`=3.0 (`mean + 3*sigma`) overshoots this
checkpoint's own argmax at 67% of positions (argmax sits only 2.73 sigma above the logit bulk on
average), so `Gate.ResonanceFloor` caps the floor AT the max and exactly one token clears it — sampling
from a one-token nucleus is deterministic by accident, ~95-98% of the time, despite only 22.2% of
positions being genuinely confident enough to be legitimately greedy. **Its own `DecodePolicy`,
constructed explicitly** (not `Prism.razor`'s `Policy`, never touched) — three named presets (Focused
FloorK=2.0 / Balanced FloorK=1.5, default / Wild FloorK=1.0), each an exact MEASURED point from the
FloorK sweep the diagnosis was built from (see `Stories.razor`'s own `PolicyFor`/`FloorKFor` comment for
the full table) — not interpolated or guessed. Tuned by reasoning from that table, not by generating and
listening (this agent cannot open a browser — Showroom charter boundary); flagged for the user to try
live and retune the three FloorK values if a round of listening says otherwise.

**Reproducible by seed** — `Random(seed)` feeds `Gate.Pick`; "Tell me another" always draws a fresh
`Random.Shared.Next()` seed, "Replay this seed" reruns the exact same prompt/variety/seed and reproduces
the same story. **Skip to the end** (visible only while generating) drops pacing and per-token sound for
the REST of that one generation — chosen over speeding the cadence up, since the cadence is part of the
character (explicit brief instruction); the story still streams to completion, just without the wait.

**Honesty, by design**: the caveat paragraph under the story states the real param count and an actual
generated sentence with weak semantics, so a visitor understands what a ~370K-parameter model can (and
can't) do — never oversold as a coherent storyteller.

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
  global::<Namespace>` — a plain `@using` resolves to the page's own class instead. **This is NOT
  scoped to the colliding page itself** — every page compiles into the same `Showroom.Pages`
  namespace, so ANY page referencing `Prism.*`/`Prose.*` needs the `global::` form once ANY sibling
  page is named `Prism`/`Prose`, even if its own name doesn't collide (`Stories.razor` needs
  `global::Prism.Inference` for exactly this reason). Affects: `Prism.razor` (`global::Prism.
  Inference`), `Stories.razor` (`global::Prism.Inference`), `Prose.razor` (`global::Prose`/
  `Prism.Lineage`), `Analyst.razor` (`global::Prism.Lineage`). A future tool referencing either
  package's namespace needs the same check, regardless of its own name.
- A new tool page gets the parallax/glow treatment free by reusing the house `.room`/`.room-head`/
  panel shape — it just needs its own `data-cat="<pkg>"` (or chord) on the outer `.room`.
- Multi-package tools force a real dependency-version bump for every tool in this one `.csproj`
  (Prose's restore bumped HoloDb/AlgFormer for the other four tools too) — re-check `dotnet build`
  after adding a new tool, not just its own page.

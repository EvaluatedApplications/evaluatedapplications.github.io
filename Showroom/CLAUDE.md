# Showroom — CLAUDE.md (showroom-owner)

**Last verified:** 2026-09-04

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
  `MainLayout.razor`'s `.nav-links`: **`Home · Tools · NuGet`** (2026-09-02, 3 items, down from 4 —
  see "Nav scope" below; `Tools` links to `Home.razor`'s gallery via `href="."`; a new tool needs a
  `Home.razor` card, not a nav entry). This entry used to read "`Home · HoloDb · Tools · NuGet`",
  already stale before this pass (the live file actually had `Packages` in that 3rd slot, not
  `HoloDb` — never caught until this pass verified the real file against this doc).

### Nav scope — resolved 2026-09-02 (user decision, content/app split hardened)

Direct user decision, relayed as a resolution both `website-owner` and this agent got independently
(no shared `<SiteNav>` component built as a result — see below): "I prefer the HTML versions, as the
Blazor-only pages are for apps, not sharing info." This hardens the existing content/app split into
a firm principle — the static HTML site (`AboutUs/site/`) is the sole home for informational/
shareable content; `Showroom/` exists ONLY for interactive tool apps, never to restate or duplicate
info content. Practical effect on THIS repo's nav: `MainLayout.razor`'s `Packages` link
(`/packages.html`, the static site's package-info index) was dropped — Showroom's nav is scoped
strictly to Showroom's own tool list, not to informational pages that already live on the static
site (those pages already link INTO Showroom's tools where relevant; the reverse direction — a tool
page linking back OUT to package-info prose — isn't this app's job). Grepped `packages.html` across
`Showroom/` first to confirm the nav link was the only reference (it was — `Home.razor` and every
tool page link straight to package NuGet pages or nothing, never to the static site's info gallery).
New shape: `Home` (href=`/`, escape hatch back to the marketing/info site — kept; this is wayfinding,
not restated content, and matches the static site's own convention of a textual `Home` link
alongside the brand-mark logo link, not a redundancy specific to this decision), `Tools` (href=`.`,
Showroom's own gallery — correctly scoped, this IS "Showroom's own tool list"), `NuGet` (external,
the actual package-distribution channel, not AboutUs content, kept). Net: 4 items -> 3.
**`SiteKit.Components` RCL (nav/brand/footer) — assessed again given this resolution, still not
built, judgment updated**: the earlier open question was whether the two navs' *item counts* should
be unified via a shared component; that's now moot, since the two navs' JOBS are genuinely different
(info-nav: `Home · Packages · NuGet`; app-nav: `Home · Tools · NuGet`) and always will be — there is
no shared nav-items list to factor out, only a shared *shape* (brand mark + a lean link row), which a
parameterized-items-list component would barely simplify over two small hardcoded files. Re-assessed
per-piece:
- **Nav**: keep fully local. No shared value once the item lists genuinely diverge by design, not by
  neglect.
- **Footer**: keep fully local. `AboutUs/site/**`'s footer is `© year · 3-4 links`; Showroom's is
  `© year · Home` (one link) — same asymmetry as the nav, plus it's small enough (one line) that
  hand-sync risk is negligible.
- **Brand mark (the SVG logo)**: the one piece with REAL, demonstrated drift risk — `AboutUs/
  CLAUDE.md`'s own record: the 2026-08-28 prism-triangle motif sweep hit all 17 static pages but
  "stopped dead at the repo boundary," and this file's brand mark had to be fixed by hand afterward
  (confirmed still correct as of this pass — same triangle path `M16 5L27 26H5Z`, same 7-stop ROYGBIV
  gradient, byte-checked against `Layout/MainLayout.razor`'s current markup). This is the one
  candidate genuinely worth componentizing — but its only real cross-repo consumption path is
  `AboutUs/CLAUDE.md`'s in-flight `SiteKit.Render` (an `HtmlRenderer`-based pipeline that could render
  a shared Razor brand-mark component into the static pages too), and that pipeline is explicitly
  NOT YET wired into any real `site/**/*.html` page as of 2026-09-02 (3 of 17 pages proven in a
  sandboxed PoC only, per that doc's own "still fully INERT w.r.t. the deployed site's PAGES" note).
  Building a `SiteKit.Components` RCL for the brand mark today would only ever be consumed by THIS
  repo (a trivial refactor of markup that's already inline and already correct) — zero real
  drift-prevention benefit until `SiteKit.Render`'s page pipeline actually ships. **Conclusion: not
  worth building yet.** Revisit specifically if/when `SiteKit.Render` reaches the point of rendering
  real static pages (not just its PoC) — at that point the brand mark is the one piece worth pulling
  into a shared component; nav and footer stay local even then, per the reasoning above. Until then,
  the working discipline stays what it already is: whenever the brand motif changes, sweep both
  repos by hand and byte-check the SVG path/gradient stops match (same check just re-run this pass).
- `Home.razor` (`@page "/"`): the tool gallery. Each tool is a whole-card `<a class="card tool"
  href="/tools/<slug>">` (absolute path) with a `--cat` accent colour, a `live`/`soon` tag, a
  one-line desc, "Open X →". Mirror this shape for any new tool. `Pages/NotFound.razor`
  (`@page "/not-found"`) is the router fallback.
- `wwwroot/index.html`: `<base href="/tools/" />`, links the SHARED `/assets/site.css` (Showroom
  borrows the one design system) plus Showroom's OWN `wwwroot/css/boot.css` (see Boot screen) and
  `wwwroot/css/depth.css` (see "Scroll-tied parallax depth" below) and Blazor's bundled
  `Showroom.styles.css`; also carries the GitHub Pages SPA deep-link restore script and
  `window.analystDownload` (Blob download helper for The Analyst's CSV export).
- **CSS pattern**: each tool has its own `Pages/<Tool>.razor.css`, Blazor-scoped, all four DUPLICATE
  the same base block (`.room`/`.crumb`/`.room-head h1`/`.lede`/`.badges`/`.err`/`.hint`/
  `.cr-controls`/`.cr-stats`/`.cr-curve`/`.cr-log`/`.outro` — established house style, CSS isolation
  can't share a partial file across components); reuse it verbatim for a new tool.

## HoloKernel — `ProjectReference ..\HoloKernel\HoloKernel.csproj`
A sibling RCL in this repo (`AboutUs\HoloKernel`), itself NuGet-only against AlgFormer 2.2.0 (bumped
from 1.5.0 2026-09-04, see "Checkpoint refresh + AlgFormer 2.2.0 bump" below) — a
`ProjectReference` to it is the designed consumption path, not a MonoRepo boundary break. All three
live-brain tools are ported onto it (2026-08-28). Surface used: `ModelSpec` (shape + the S>1
invariant; `Validate()` also rejects `Layers>1 && KPass>1` — AlgFormer 1.5.0's weight-tied K-pass is
single-layer only, verified via `NotSupportedException`; irrelevant today since all 3 tools are
`Layers=1`, but a real constraint on ever growing one deeper while keeping K>1), `HoloSession`
(`Create(spec)`/`FromCheckpoint(bytes,kPass,serveAlpha)` — K/alpha mandatory, `Stats()`), `AlphaRamp`
(`Advance`/`Reconstruct`), `RefinementLoop` (`Observe`/`ObserveSequence`, replaces the
`NewGrads→IterAccumulate→Step` triple each tool wrote independently), `Decoding` (`DecodePolicy`/
`Gate`/`DegenGuard`), `InspectorTrace` (`Inspector.Capture`/`Focus` — NEW for Creature/Forecaster,
opt-in "🔍 inspect brain" toggle; Prism's own Inspector UI was REMOVED, own section below),
`CheckpointFetch` (`FetchAndDecompressGzipAsync` — NEW 2026-08-31, see "Checkpoint gzip
precompression" below).
`ParallelMapping` isn't wired into any tool (inert, not a gap).

### Checkpoint gzip precompression — `HoloKernel/CheckpointFetch.cs` (2026-08-31)

Real user report: `/tools/prism` "stuck downloading the checkpoint" — `oracle-brain.bin` was 3.29 MB
a few days earlier, is 3.64 MB now, and grows every training round (the checkpoint's own context
keeps expanding). GitHub Pages is a plain static host: it serves this file byte-for-byte with no
Content-Encoding negotiation or on-the-fly compression, unlike Blazor's own `_framework/` assets
(which get `.br`/`.gz` sidecars from `dotnet publish` and are resolved by the framework's own boot
loader) — a raw data file dropped into `wwwroot/data/` never picks that up automatically. Confirmed
by reading the actual publish output, not assumed: even a fresh `dotnet publish` does NOT produce a
`.br`/`.gz` sidecar for `oracle-brain.bin` (the small text sidecars — `oracle-vocab.txt` etc. — and
even `forecaster-history.json` DO get one; `oracle-brain.bin`/`.bin.gz` are the two exceptions, for
reasons not chased down since it doesn't matter here — this fix doesn't depend on that mechanism at
all).

**Fix**: ship one manually-precompressed sidecar, `oracle-brain.bin.gz` (plain gzip at rest, produced
by any dumb `GZipStream` one-liner — no dependency on the Blazor build pipeline), and decompress it
client-side in C# with the BCL's own `System.IO.Compression.GZipStream` —
`HoloKernel.CheckpointFetch.FetchAndDecompressGzipAsync(HttpClient, url)` fetches the `.gz` bytes and
returns `(byte[] Bytes, int CompressedLength)`. **Deliberately NOT the browser-native
`DecompressionStream` API via JS interop** — `GZipStream` already works inside a Blazor WebAssembly
runtime (the WASM runtime pack ships a WASM-compiled zlib, confirmed by grepping the linker's own
invocation: `libSystem.IO.Compression.Native.a`/`libz.a` are already linked into every build), so this
gets the same result as zero new JS surface, zero new browser-API compatibility question, and reuses
the exact fetch pattern (`HttpClient.GetByteArrayAsync`) every tool already uses. Two real call sites
share it (`Prism.razor`'s own checkpoint load, and `Analyst.razor`'s independent lazy load for its
novelty-scan feature, which fetches the same checkpoint a second way if Prism itself hasn't loaded it
first this page load) — same "one real behaviour, not two copies that can drift" reasoning HoloKernel
already applies to `AlphaRamp`/`Decoding.Gate`.

**Failure mode, deliberately not band-aided**: no fallback to a raw uncompressed `.bin` was built. A
`.gz` fetch or `GZipStream` decompression failure throws, exactly like an uncompressed
`GetByteArrayAsync` failure always did — both call sites already wrap their whole load sequence in a
try/catch that surfaces a real, visible error (`_loadError` on Prism's boot screen, `_novError` on
Analyst's novelty-scan panel) rather than leaving the tool silently stuck. A raw-`.bin` fallback would
have doubled the maintenance surface (two fetch paths to keep correct) for a failure mode this repo's
own error-surfacing already handles honestly — judged not worth it for a demo tool.

**Measured on the checkpoint shipped 2026-08-31**: 3,638,308 B raw -> 1,985,514 B gzip (~55% of raw,
~1.65 MB saved per fresh visit) — real weight data (not text) doesn't compress as aggressively as the
small sidecar files do, but it's a real, meaningful cut, and it directly targets the reported "stuck
downloading" symptom (smaller transfer, same content).

**Keeping `oracle-brain.bin.gz` in sync on every future interim checkpoint refresh — the one command
the coordinator needs, no rebuild required.** The coordinator's own interim-refresh routine (raw copy
of `oracle-brain.bin`/`-vocab.txt`/`-rounds.txt`/etc. from PrismStudio's live output straight into
`wwwroot/data/` and `dist/data/`, no `dotnet publish`, since these are plain data files with no SRI
hash) needs exactly ONE extra step after copying the fresh `oracle-brain.bin` into both dirs — plain
.NET `GZipStream`, runnable from any PowerShell session, no Showroom-build tooling involved:

```powershell
$in = [System.IO.File]::ReadAllBytes('C:\Users\dongy\AboutUs\Showroom\wwwroot\data\oracle-brain.bin')
foreach ($dir in 'C:\Users\dongy\AboutUs\Showroom\wwwroot\data','C:\Users\dongy\AboutUs\Showroom\dist\data') {
    $out = [System.IO.File]::Create((Join-Path $dir 'oracle-brain.bin.gz'))
    $gz = New-Object System.IO.Compression.GZipStream($out, [System.IO.Compression.CompressionLevel]::Optimal)
    $gz.Write($in, 0, $in.Length); $gz.Dispose(); $out.Dispose()
}
```

Read the fresh `.bin` ONCE, write the `.gz` into both data dirs — same shape as the existing raw-copy
step, just one more file, still zero `dotnet publish`/rebuild involved. The raw `oracle-brain.bin`
itself is deliberately left in place in both dirs alongside the `.gz` (unused by any fetch path now,
but harmless to keep — a natural fallback source for regenerating the `.gz`, and removing it wasn't
asked for). **This command needs a rebuild ONLY if `CheckpointFetch.cs`'s decompression logic itself
ever changes** — the `.gz` file format and this regeneration step are completely decoupled from the
Blazor build; a plain `dist/`-artifact refresh (no source change) never touches this file.

Verified end-to-end for the 2026-08-31 checkpoint: compressed the real shipped `oracle-brain.bin`,
round-tripped it back through `GZipStream.Decompress` and confirmed the result is byte-identical to
the original (`SequenceEqual` over both 3,638,308-byte arrays) before shipping the `.gz` — not just
"the command ran," an actual correctness check on the real file.

**Alpha-ramp REMOVED (2026-08-28, direct instruction)**: Creature and Forecaster used to each build
their own `AlphaRamp(warmSteps)` easing K-pass composition in from 0 over the first N steps/clicks —
a leftover from when each tool picked its OWN made-up `KPass=2` as if it were a fresh design choice
needing a gentle warm-up. That premise is gone now that K is live-read from Prism's own already-
proven trained depth (see below): there's nothing to ease into. Both tools now pass
`HoloKernel.AlphaRamp.Complete` (a shared, stateless `WarmSteps=0` singleton — `Alpha` is always 1.0
regardless of `Steps`) straight into `RefinementLoop`'s ctor, and each also calls
`_session.ApplyServe(AlphaRamp.Complete)` immediately after acquiring the session — this second call
matters: without it, a freshly-created `HoloSession` starts at `ServeAlpha=0` (its own ctor default)
until the FIRST `Observe()` call sets it, so an untrained model would still serve un-composed for any
early decision made before training starts (e.g. Creature's early high-epsilon exploration). Neither
tool stores the ramp on an instance field anymore — nothing needs to read its state, since it never
changes. The old per-step/per-tick `α NN%` log/UI prints were dropped site-wide (always-100% is not
information) rather than left showing a permanent, meaningless number. `RefinementLoop`/`Observe`
still take a `ramp` parameter — kept as-is, not narrowed to assume-always-complete, since it's shared
HoloKernel infrastructure and a future tool could legitimately want a real ease-in; Creature/
Forecaster just always pass the no-op instance now. **Prism's tokenizer swapped to the published
`SubwordVocab`** (was a hand-rolled
greedy-longest-match encoder) — verified against `MonoRepo\AlgFormer\SubwordVocab.cs`:
`CharN=>CharVocab.N=96` already special-cases `Symbol(CharVocab.End)=="\n"`, matching Prism's own
quirk; ctor takes ONLY the merges list (base chars handled internally, a gotcha vs. the old version).
**Browser contract (user directive): visitors TRAIN, they never reshape.** "Grow Prism" means
refining a FIXED-shape model's weights via `RefinementLoop.Observe`/`ObserveSequence` — layers/
shifts/dim/context are chosen up front, immutable for the session. `HoloFormer.GrowLayers`/
`.GrowShifts` are real, published, in-place growth methods, but a PrismStudio/server-side operation —
`HoloKernel` deliberately doesn't wrap either; a better model reaches visitors via a new CHECKPOINT,
never runtime shape mutation.

**K-pass is a single, live-read source of truth (2026-08-28 fix)**: Creature/Forecaster used to each
hardcode their own `const int KPass = 2` — a made-up number. Both now fetch `data/oracle-stackk.txt`
(the same sidecar `Prism.razor` fetches for its own checkpoint) in `OnInitializedAsync`, parse into
`_kPass`, and use that as `ModelSpec.KPass` — mirroring Prism's exact fetch/parse/fallback shape
(`GetStringAsync`→`Trim()`→`int.Parse`, any failure swallowed, falls back to K=1 like Prism's own
bare-checkpoint fallback). Live value: `oracle-stackk.txt`=8 (was hardcoded 2). Each tool still
trains its own separate model from scratch on its own task domain (Creature: grid nav vocab=`W*H+8`;
Forecaster: price buckets vocab=17) — sharing Prism's checkpoint/weights/vocab is architecturally
impossible; only the K *number* is now shared. If the checkpoint is retrained at a different K and
the deployed sidecar changes, both tools pick it up next page load, no code change. **The Analyst's
novelty-scan already did this correctly** (verified, not assumed) — `EnsurePrismLoadedAsync` reads
`_prismSession.KPass` off the real loaded session, same `?? 1` fallback; no fix needed there.
**Alpha-ramp/K interaction is now moot** (see "Alpha-ramp REMOVED" above) — both tools serve and
train at full K-pass composition from the first move/tick regardless of K's magnitude, so the old
"is 300/40 steps still a gentle-enough ease-in at the real K=8" open question no longer applies;
there is no ease-in to retune.

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

## Scroll-tied parallax depth + spotlight glow — `wwwroot/css/depth.css` (2026-08-28)
Ports the CONCEPT from `AboutUs/site/assets/site.css`'s "SCROLL-TIED PARALLAX DEPTH + SPOTLIGHT
SHADOWS" system (that file, search that string) into Showroom's own page shape — not the same
selectors, since Showroom's tool pages have no `.hero`/`.sec` window layout at all (a single
centred `.room` column: `.room-head` then a sequence of panel blocks). Own Showroom-owned file
(`wwwroot/css/depth.css`, linked in `index.html` after `site.css`/`boot.css`) — `site.css` itself
was never touched, per the hard boundary. Zero JS, gated entirely inside
`@media (prefers-reduced-motion:no-preference)`, same as the static site.

**Three tiers, re-derived for this page shape, not copy-pasted**: tier "far" = an ambient wallpaper
wash on `<main>` (the one element every route renders inside, `Layout/MainLayout.razor`), tied to
`scroll(root)`, no shadow — same 3-stop accent/data-blue/coral mix `site.css`'s own wallpaper uses.
Tier "near" = `.room-head` (crumb+h1+lede+badges, the first thing on every tool page — Showroom has
no decorative hero SVG like `.prism-beam`, so the hero text block itself plays that role) plus
`.hero` (Home.razor's own gallery page reuses `site.css`'s `.hero` class verbatim). Tier "mid" =
every panel-shaped block a tool actually presents, one shared rule: `.stat`/`.cr-curve`/`.cr-log`/
`.cr-stage`/`.fc-stage`/`.or-runs` (Creature/Forecaster/Prism) + `.chart-card`/`.col-card`/`.chip`
(Analyst, which has no `.stat`/`.cr-*` shape of its own). Magnitude starts from `site.css`'s
ALREADY-TRIPLED real-device-corrected numbers (its own history: an initial subtle pass was found
imperceptible and tripled) — not the original subtle pass. **Angle re-derived, not copied**:
`.prism-beam` uses an off-centre shadow (right-aligned in its hero); `.room-head`/the mid-tier
panels here are all centred blocks inside a centred `.room` column, so both tiers use `site.css`'s
own centred/"mid"-tier straight-down (X=0) convention instead.

**Deliberately excluded, both checked for the same opacity-multiplier trap `site.css` documents
(an element's own `opacity` silently dims a `filter:drop-shadow()` composited under it)**:
`.dropzone` (Analyst) — its `.dropzone.busy` state carries `opacity:.6`, would dim the glow mid-
upload, simpler to leave out than special-case a transient state; `.card.tool` (Home.razor's
gallery cards) — `site.css`'s own `.card:hover{transform:translateY(-2px)}` would lose that fight
to a running scroll-linked animation's `transform` value for the same property, silently breaking
the existing hover-lift affordance. A real fix needs a wrapper element (animate the wrapper, leave
`.card`'s own hover transform alone) — flagged as follow-up, not attempted.

**Colour — RETINTED to the real per-package palette (2026-08-28, follow-up pass)**: the placeholder
4-bucket `data`/`ml` tint from the first pass (below, kept as history) is gone. `[data-cat]` on each
tool's `.room` is now a real dependency name, matching the website's own per-package hex table
(`AboutUs/CLAUDE.md`'s "Per-package palette" — single source of truth, hand-duplicated here since
there's no shared token file across the repo boundary, same known limitation on record both sides):
`data-cat="holodb"` (Analyst, HoloDb only — was wrongly sharing `--c-data` blue with 3 unrelated
tools), `data-cat="algformer"` (Forecaster/Prism, verified AlgFormer-only — Forecaster's own `.razor`
carries no Tracer `@using`/call, confirmed by grep before assuming), `data-cat="algformer-tracer"`
(Creature, verified via its own `@using Tracer.Helpers` + `GridTactics.Reachable` call — a genuine
two-package composite, same situation as the static site's `prose.html`). Each `[data-cat]` rule now
reads `var(--c-holodb, #66c1aa)` / `var(--c-algformer, #5998ff)` / `var(--c-tracer, #f0796a)` — since
`site.css` IS linked into this same page (`wwwroot/index.html`), the `var()` resolves to the live
token when present, falling back to the hand-typed hex (this file's own duplicated copy) otherwise —
same fallback-authoritative convention this file already used pre-recolour. **Creature's chord is a
real two-tone hard-edged glow, not a placeholder single hue**: two NEW keyframes,
`sr-parallax-near-chord`/`sr-parallax-mid-chord`, each stacking TWO `drop-shadow()` layers (one per
`--glow-*-a`/`--glow-*-b`) instead of one — mirrors `site.css`'s own `ea-parallax-mid-chord`
mechanism exactly (two shadow layers, never a `color-mix()`'d third hue), extended to BOTH the near
and mid tiers here (unlike `site.css`, whose near tier is the page-specific `.prism-beam` and so
never needed a chord variant — Showroom's near tier, `.room-head`, appears on every tool page and so
needed the same treatment). Wired via higher-specificity override selectors
(`.room[data-cat="algformer-tracer"] .room-head` / `...{.stat,.cr-curve,.cr-log,.cr-stage}`) that
only touch `animation-name`, same one-line-per-extension pattern `site.css` itself documents.
`Home.razor`'s own gallery cards retinted to match, verified against the static site's own
`index.html` markup before mirroring it (not invented independently): Analyst `--cat:var(--c-holodb,
#66c1aa)`; Forecaster/Prism `--cat:var(--c-algformer, #5998ff)`; Creature a hard-edged 2-stop
`linear-gradient(90deg, var(--c-algformer,...) 0%..50%, var(--c-tracer,...) 50%..100%)` +
`--cat-root:var(--c-algformer, #5998ff)` (a single-colour fallback, unused by any current Showroom
CSS rule since `Home.razor` doesn't run the static site's `os-chrome` mobile icon-grid mechanism, but
kept for consistency with the source-of-truth pattern and future-proofing). Also swept the wallpaper
wash's one decorative (non-per-page) blue stop from the now-gone `--c-data` bucket token to the
neutral `--spectrum-5` stop (same hex, `#4aa3ff` — a values-preserving rename, not a colour change),
mirroring the equivalent fix `site.css` made for its own decorative wallpaper stop.

Verified structurally only (no live browser, per this repo's own boundary): `dotnet build
Showroom.csproj -c Release` green (0/0) after the change; `depth.css` confirmed present at source and
picked up by the static-web-assets discovery manifest (`staticwebassets.build.json`); brace/paren
balance on the whole file confirmed via a full-file regex count (26/26 braces, 145/145 parens);
grepped all four tool `.razor` files + `Home.razor` for any stray `--c-data`/`--c-ml`/
`data-cat="data"`/`data-cat="ml"` leftover — none found. Not verified: how the recoloured glow/chord
actually reads live — same disclosed limitation as every other CSS pass in this file, the coordinator/
user should eyeball all four tool pages (Analyst teal-green vs. Prism/Forecaster blue, no longer
collapsed together) and Creature's two-tone blue/coral chord at ≥901px on a real screen.

**Original placeholder pass (2026-08-28, superseded above, kept as history)**: `.room[data-cat="data"]`
(Analyst) / `.room[data-cat="ml"]` (Creature/Forecaster/Prism) set `--glow-near`/`--glow-mid`, reusing
`site.css`'s then-existing `--c-data`/`--c-ml` bucket tokens (the same ones `Home.razor`'s tool cards
tagged with at the time) — flagged then as a placeholder pending the website's per-package palette
work; that work has since landed and this file has been repointed at it, above.

## Real WASM multithreading — LANDED then REVERTED, same day (2026-08-28) — `wwwroot/coi-serviceworker.js`

**REVERTED.** `WasmEnableThreads` is back to `false` in `Showroom.csproj`. Real device report, same
day this landed: `/tools/prism`'s Continue button stuck permanently disabled on a laptop, while a
phone loading the exact identical deploy worked fine. That platform split is the tell — the .NET WASM
threaded runtime sizes/spins up its pthread worker pool from `navigator.hardwareConcurrency` at boot,
and a laptop routinely reports far more logical cores than a phone, so more workers race to
fetch/compile/instantiate redundant copies of the WASM module in the background right after
`Blazor.start()` resolves and the page paints. Emscripten-based multithreaded WASM runtimes have a
documented deadlock class here too (worker→main-thread calls proxy through a synchronous
`Atomics.wait`, which can stall if the main thread's event loop is itself busy servicing the
worker-spawn/compile queue at that exact moment) — a race whose odds scale with worker count, i.e.
core count, i.e. exactly a laptop-not-phone split. **Could not reproduce/confirm the exact mechanism
live** (no browser here, per this repo's own boundary) — but this app had **zero current consumers**
of real threading (`ParallelMapping.cs`'s own measured finding: `chunks==1` always, at every shape
this site runs), so the revert was already flagged as "the cheapest option of all... pure downside for
zero benefit right now" in this same file *before* this incident — the incident is exactly the trigger
that was flagged as missing then. Reverting erases the whole failure class at zero feature cost, and
as a side effect also removes the ORIGINAL reason the crossOriginIsolated boot-gate/coi-serviceworker
existed at all: a non-threaded module has no `SharedArrayBuffer` requirement, so it boots in any
browser, isolated or not — including the LinkedIn in-app WebView that motivated the gate below in the
first place. `index.html`'s boot script is back to a direct, unconditional `Blazor.start()` (generic
`.catch` → the same `#boot-fallback` panel, now worded for any startup failure, not isolation
specifically); `index.html` also now actively unregisters any stale `coi-serviceworker.js`
registration on every load, so a laptop that visited on the one day threading was live doesn't stay
stuck behind it. `wwwroot/coi-serviceworker.js` is left in place, unreferenced, with a header note —
not deleted, in case threading is deliberately re-tried later with this exact regression reproduced
and fixed first. **Re-verified**: `dotnet build Showroom.csproj -c Release` green (0/0), and the build
output no longer produces a `dotnet.native.worker.*.mjs` (confirms the non-threaded runtime pack is
what actually built, not just that the flag was accepted). **Not verified live** — same disclosed
boundary as every claim below; the user/coordinator should confirm the Continue button now enables on
the same laptop that reported it stuck, on a fresh deploy of `dist/`.

The rest of this section is kept as history (what was actually built, and why, while it was live) —
useful if threading is ever re-attempted, not a description of the current deployed state.

Real OS threads (pthreads over `SharedArrayBuffer`) inside the WASM runtime, NOT the cooperative
async-interleaving pattern the tools' own training loops use (that's `HoloKernel`/`ParallelMapping`'s
territory, untouched by this). Two parts, both required together:

1. **`Showroom.csproj`: `<WasmEnableThreads>true</WasmEnableThreads>`** — pulls in the threaded
   runtime pack at build time (`wasm-tools` workload, already installed for `RunAOTCompilation`, is
   the only prerequisite). **Verified compatible with the existing `PublishTrimmed=true` +
   `RunAOTCompilation=true` + `-O1` combination** — this three-way combo has real historical bugs
   elsewhere, so it was actually build+publish tested, not assumed: `dotnet build -c Release` is
   green (0/0), and a full `dotnet publish -c Release` (AOT+trim+threads together) succeeds end to
   end (exit 0), ~4m40s-5m15s wall time on this dev machine, producing `_framework/
   dotnet.native.worker.<hash>.mjs` (the pthread bootstrap worker script — confirms the threaded
   runtime pack actually built, not just that the flag was accepted) alongside the usual
   `dotnet.native.<hash>.{js,wasm}`. Grepped the shipped `dotnet.native.<hash>.js` for confirmation
   it sizes its own thread pool from `navigator.hardwareConcurrency` (present, not guessed). Native
   module size: **27.4 MB raw / 8.15 MB gzip / 5.2 MB brotli** (threaded) vs. the pre-threading
   ~24.8-27.0 MB raw at the same `-O1` (see the Dependencies section's AOT comment) — threading adds a
   modest, not dramatic, size cost on top of the existing AOT module.

2. **`wwwroot/coi-serviceworker.js`** solves the actual hard part: real multithreaded WASM requires
   the page to be cross-origin-isolated (`Cross-Origin-Opener-Policy: same-origin` +
   `Cross-Origin-Embedder-Policy: require-corp`/`credentialless` response headers, which unlock
   `SharedArrayBuffer` and set `self.crossOriginIsolated = true`) — GitHub Pages is static hosting
   with **no server-side header control**, confirmed by serving a fresh flat-file publish through a
   from-scratch local static server that (like GH Pages) sets neither header on any response,
   including `.wasm`/`.mjs`/`.js`. Vendored (self-hosted, not CDN-referenced) **coi-serviceworker
   v0.1.7** (Guido Zuidhof, MIT) — this exact widely-deployed implementation (pyodide.org and others),
   not hand-rewritten, but its whole mechanism was traced end-to-end and is documented in the file's
   own header comment before trusting it: on first visit it registers itself AS a Service Worker and
   forces one `location.reload()`; from the second load on, its own `fetch` handler injects COOP/COEP
   onto every response the SW intercepts (including the page's own navigation response), achieving
   isolation from byte one of that load. Default mode is COEP `credentialless` (not `require-corp`) —
   deliberate: The Analyst's own free-form external CORS-permitting feed URLs are cross-origin
   no-cors fetches this app doesn't control the CORP headers of, and `credentialless` doesn't block
   those the way strict `require-corp` would. Loaded as the very FIRST script in `index.html`, before
   anything else, for the earliest possible registration/reload timing.

**What was verified, and how (build/static evidence only — see the hard limitation below)**:
`node --check` on the vendored file (syntax valid); full AOT+trim+threads publish succeeds twice in a
row (once before, once after adding the SW file + its `index.html` wire-up, to make sure the SW file
itself actually lands in the publish output — it does); served the fresh publish output through a
from-scratch Node static file server (root-cause-correct MIME map, `.wasm`→`application/wasm` etc.,
confirmed via `Invoke-WebRequest` against every relevant file) rather than the Blazor dev server,
specifically because the dev server auto-adds COOP/COEP itself when `WasmEnableThreads` is set and so
would NOT have exercised the coi-serviceworker path at all — confirmed via that same static server
that the origin sends **no** COOP/COEP headers on anything, i.e. the workaround is genuinely load-
bearing here, nothing else is silently providing isolation. Also added a real (not decorative) boot-
log diagnostic line in `index.html` (`window.crossOriginIsolated` + `navigator.hardwareConcurrency`,
reusing the existing boot-log narration pattern) so this is self-reporting and visible on every real
page load going forward, not just checked once at build time.

**HARD LIMITATION — could NOT verify end-to-end in an actual browser, per this repo's own boundary
("never launch the app / open a browser session yourself")**: `self.crossOriginIsolated === true` on
a real loaded page, the one-reload first-visit UX actually completing cleanly, and real Worker
threads visibly spinning up in devtools are all UNVERIFIED — build/publish success does not prove
runtime correctness, and this is exactly the kind of claim that needs a live check, not code-reading.
**The user (or coordinator) must confirm this live** before calling it done: serve a fresh
`dotnet publish Showroom.csproj -c Release -o <dir>` output's `wwwroot/` as flat static files (NOT
`dotnet run` — the dev server hides this exact problem, see above) under a `/tools/` path prefix,
load it in a real browser, and in devtools check (1) Console/Application tab: the page reloads once
automatically, then stays put, (2) `self.crossOriginIsolated` is `true` in the console after that
settle, (3) the boot log's own new diagnostic line agrees, (4) Sources/Threads or the Task Manager's
"process" view shows multiple Worker processes once a tool actually runs.

**Also flagged, not fixed (out of scope — deliberate, see the task's own scope boundary)**: no tool's
training loop dispatches work across real threads yet. Today's per-step training pattern in Creature/
Forecaster produces `IParallelMap` `chunks==1` even at `parallelism:4` (see `HoloKernel/
ParallelMapping.cs`'s own measured finding) — real gains from this new capability need a batching
restructure of the training loops first, which is its own follow-up task, not attempted here.

### Real incident: silent infinite hang in LinkedIn's in-app browser (2026-08-28) — fixed, boundary confirmed

Real device report: `/tools/prism` opened inside LinkedIn's iOS in-app WebView stalled forever right
after "loader dotnet.js" — no error, no fallback, stuck at "connecting…". **User confirmed a real
standalone browser works fine** — an embedded/in-app-WebView problem (these commonly restrict or
silently no-op Service Worker registration), not a general regression.

**Confirmed via evidence (grepped the shipped runtime), not assumed**: a `WasmEnableThreads=true`
build cannot gracefully continue single-threaded when isolation never activates — no in-place fallback
exists. `dotnet.js` contains an unconditional assert that THROWS if `SharedArrayBuffer` is missing
(`qe(!1,"SharedArrayBuffer is not enabled on this page...")`, no degrade branch); `dotnet.native.js`
unconditionally requests `new WebAssembly.Memory({shared:true})`; and — the real structural root cause
— the compiled `dotnet.native.<hash>.wasm` itself was linked `--shared-memory --import-memory` (seen
in this build's own `emcc`/`wasm-ld` invocation), a categorically different WASM module shape than a
non-threaded publish. No runtime flag can un-bake that from an already-compiled binary.

**Fix shipped: fast-detect + honest fallback UI, never a silent hang** — `wwwroot/index.html` +
`wwwroot/css/boot.css`. `Blazor.start()` is now GATED behind confirmed `window.crossOriginIsolated`:
two instant fast-fail checks (`!isSecureContext`, `!('serviceWorker' in navigator)` — conditions
coi-serviceworker.js itself already silently gives up on) plus a single bounded 7s wait for the normal
register+reload cycle (settles <1s normally; a real reload interrupts the timer first, so this never
false-fires in the healthy case). On timeout, a `#boot-fallback` panel reveals instead of proceeding:
honest explanation ("common inside an app's built-in browser... look for 'Open in Browser'"), a
**Retry** button (`location.reload()`) and a **Copy link** button (`navigator.clipboard`).
`Blazor.start(...).catch(...)` is an added defensive net, not the primary fix — the primary fix is
that the fatal assert is now structurally unreachable, since boot is only attempted once isolation is
already confirmed. Does NOT make threading work inside a restricted WebView (outside this page's
control); does guarantee the visitor is never stuck with zero feedback/way-forward again.

**Real dual-build graceful degradation — evaluated per the coordinator's explicit ask, NOT built**:
the only way to get a genuinely automatic non-threaded fallback (vs. a message) is a real second
`WasmEnableThreads=false` publish served from a second path with a pre-boot chooser redirect. Real and
buildable, but bigger/separate: needs `deploy.yml` (website-owner's) to `dotnet publish` twice — a
considered regression to the 2026-08-28 decision that CI no longer publishes Showroom at all — and
roughly doubles the committed `dist/` artifact (~55 MB today). One thing that DOES make it cheap
whenever it's picked up: since no tool's training loop dispatches across real threads yet (`chunks==1`
always, see above), the non-threaded build is **behaviourally identical** to the threaded one today —
no feature-parity work, just redirect plumbing + size/CI cost. **Cheapest option of all, flagged not
acted on**: `WasmEnableThreads` has zero consumers today, so it's pure downside (this failure class)
for zero benefit right now — removing it until a training loop actually uses real threads would erase
the whole failure class at zero cost, cheaper than the fallback fix or a dual-build. Not done
unilaterally (a same-day deliberate infra investment, not this task's call to revert) — flagged as a
live option for the coordinator alongside the dual-build path.

**Verified**: `dotnet build Showroom.csproj -c Release` green (0/0). `node --check` on
`coi-serviceworker.js` (unchanged, re-checked anyway). `boot.css` brace balance (52/52). Full re-read
of `index.html` — well-formed. **Not verified live** (no browser, per this repo's boundary) — user
needs to re-test the LinkedIn WebView case once deployed, plus ideally a standalone browser first to
reconfirm the happy path is unaffected. **Deploy note**: source-only change; `Showroom/dist/` needs a
fresh `dotnet publish` + copy to reach production — not done here per this task's own instruction
(no AOT publish, don't touch `dist/`) — flagged for the coordinator, same "hard coupling" discipline
already on record in `AboutUs/CLAUDE.md`.

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

**Responsiveness sweep (2026-08-28)**: `RunProfile()` (the per-column aggregate rebuild, up to 500k
filtered rows × however many numeric columns each pay their own `NumericStatsDb` scan) and
`ExtractEntities` (7 regex passes over up to 2M chars) used to run fully synchronously with zero yield
points — and, worse, `ApplyFilter`/`RemoveFilter`/`ClearFilters` (wired to every chart-bar/mini-row
click, the single most frequent post-load interaction) called the then-sync `RunProfile` with no
`_busy` gate at all, so a filter click could freeze the tab with no visual feedback. Fixed: `RunProfile`
is now `async Task`, yielding every 8 columns; `NumericStatsDb` is now `async Task<...>`, yielding every
100k rows within each of its up to 4 O(rows) passes (a no-op below that threshold — no added latency on
a typical small profile); `ExtractEntities` is now `async Task<List<Entity>>`, yielding BETWEEN its 7
regex patterns (can't yield mid-`Regex.Matches`, so this bounds the worst uninterrupted stretch to one
pattern instead of all seven) with a `_phase` update ("Scanning for entities…") so the progress bar
doesn't visibly stall at 90%. `ApplyFilter`/`RemoveFilter`/`ClearFilters` are now `async Task`, toggling
the SAME `_busy` flag the drop/upload/feed flow already gates every button off of — filter-chip/clear
buttons gained `disabled="@_busy"` to match the hbar/mini-row buttons that already had it. Build-verified
only (0/0); not measured live, so the "would this actually have frozen visibly" call is architectural
(unyielded O(rows)/O(chars) loops on a common click path), not a profiled number — same disclosed
limitation as every other CSS/behaviour change in `AboutUs\CLAUDE.md`'s own passes. `BuildCharts`/
`BuildInsights`/`BuildReplChart` were checked and left alone — all operate on already-capped result sets
(top-6/12-bucket-histogram/`LIMIT 400`/`ReplRowCap=200`), no real unyielded loop found there.

## The Creature — `Pages/Creature.razor` (route `/creature`)
A 20×20 grid the visitor draws (walls/start/apples) where a **HoloFormer** brain learns to forage
live, on **HoloKernel** (see above). Brain shape: `Dim=384, Layers=1, KPass=` live-read from
`data/oracle-stackk.txt` (2026-08-28, see "K-pass is a single, live-read source of truth" above),
`MaxCtx=32` (a
focused recent-trajectory window — measured to converge faster than a longer one; dilution of the
decisive last-token signal was the failure mode), `MinShifts=8` (natural `ShiftsFor(32,384)` returns
1 — floored by `ModelSpec`'s own S>1 invariant now). Distance field: **Tracer**'s
`GridTactics.Reachable` BFS to the nearest apple; trains toward the DECISIVE move (advantage-
weighted: best move minus the mean of legal moves), `LearningRate` set per-item from the advantage
weight. `ResetBrain` drops `_session`/`_loop` and tears down the training pipeline below (WASM has
no filesystem, so nothing persists across a reset).

**Training is a producer/consumer pipeline (2026-08-28, direct instruction)**, not inline-blocking
anymore: the old `EndEpisode()` used to iterate that episode's whole experience list and call
`RefinementLoop.Observe(...)` synchronously before the next episode's simulation could start — a real
training stall between every episode. Now `EndEpisode()` (the PRODUCER) packages the finished
episode's experience into an `EpisodeBatch` record and writes it to a
`Channel.CreateBounded<EpisodeBatch>(4)` with `FullMode=BoundedChannelFullMode.DropOldest`, then
immediately resets simulation state for the next episode — it never awaits training. A separate
`RunTrainer` background task (the CONSUMER, started once per brain in `ToggleRun`, fire-and-forget,
own try/catch per batch so one bad batch can't silently kill future training) drains the channel and
runs the actual `_loop.Observe(...)` calls, same advantage-normalized-LR logic the old inline code
had. Bounded+DropOldest is the BCL's native "cap it, FIFO, newer displaces older" primitive — chosen
specifically so it doesn't need hand-rolled eviction; per the user's own framing, "new runs are
better than old ones," so a slow consumer drops STALE queued episodes in favor of fresher ones rather
than growing unbounded. WASM is single-threaded (see `HoloKernel/ParallelMapping.cs`'s own findings)
so this is cooperative interleaving via async awaits, not real parallelism — same house pattern as
`Prism.razor`'s `Ask()` producer/consumer split, adapted for bounded+evicting instead of unbounded.

**What moved to the consumer, and why**: `_curve` (apples-eaten sparkline), `_bestEaten` ("best
haul," which also gates the learning-rate choice), and the per-episode training-result log line now
all update inside `RunTrainer` when a batch is ACTUALLY trained, not inside `EndEpisode` when it's
merely simulated — so the UI never claims an episode was learned from before it (maybe) was, and a
batch the channel drops (displaced by a fresher one) never shows up as "learned" at all. `_lastEaten`,
`_episode` (the "episodes" stat), and `_eps` decay stay in the producer: they're plain facts about
what the SIMULATION just did ("the creature just ate N apples," "M episodes have run"), true the
instant they happen and not a claim about training — so episodes visibly keep incrementing at full
simulation speed even while training lags behind. A small "· N queued" note is appended to the
training-log header (`_trainChannel.Reader.Count`) so the lag itself is visible, not hidden. Each
`EpisodeBatch` captures its own episode number and `_eps` value at production time (not read live from
the consumer) so a queued batch's eventual log line reports what was true when it was simulated, not
whatever the live fields have drifted to by the time it's dequeued.

## The Forecaster — `Pages/Forecaster.razor` (route `/forecaster`)
Same **HoloFormer** substrate as The Creature, pointed at a price tape instead of a foraging grid.
Predicts the direction (and coarse magnitude) of the next hourly tick for one real stock series.
**Tokenisation** (ported from `MonoRepo\MarketSim\PriceForecaster.cs`; `STOCK_i` token
DROPPED — single-symbol demo): each candle → `[TIME_bucket][RETURN_bucket]`. `TimeBuckets=8` =
hour-of-day (UTC) mod 8. `RetEdges` ported VERBATIM: `{-0.0020,-0.0009,-0.0004,-0.00003,0.00003,
0.0004,0.0009,0.0020}` → 9 buckets, `FlatBucket=4`. Vocab=`8+9=17`. **Known skew**: edges tuned for
MarketSim's smaller simulated ticks, so ~65% of bundled transitions land in the two outermost
buckets — direction split (what accuracy scores) stays near-balanced; magnitude granularity is
compressed, not direction.

**Data (reworked 2026-08-29 — was a fixed 450-row close-only bundle the training cursor just wrapped
forever, effectively memorising a small closed set)**: `wwwroot/data/forecaster-history.json` — **~3,484
real hourly AAPL OHLC candles (~2 years)**, pulled build-side from Yahoo's `v8/finance/chart` endpoint
(same one the old 450-row bundle came from — that endpoint has NO `Access-Control-Allow-Origin` header
at all, verified live in `Showroom/todo/forecaster-live-data-research.md`, so it can only ever be
fetched server/build-side, never from a visitor's browser). `Showroom/scripts/fetch-forecaster-history.ps1`
is the pull script — re-run it any time to refresh the bundle (writes the new file, refuses to
overwrite with fewer than 450 rows as a sanity floor). **Not yet wired into CI** — a scheduled GitHub
Actions refresh was designed (propose the YAML to the coordinator; `.github/workflows/` is outside this
repo's `Showroom/`-only boundary) but not added from here. **Optional live top-up** (page-load only,
`TryFetchLiveTopUpAsync` in `Forecaster.razor`): if `wwwroot/data/finnhub-key.txt` exists (absent by
default — no key was registered from this environment, same hand-off shape as Prism's checkpoint, see
Boundary below) AND NYSE is open right now (`IsNyseOpenNowUtc`, real IANA `America/New_York` conversion,
fails CLOSED on any resolution error), one live Finnhub `/quote` tick is fetched and appended as the
newest candle before tokenisation. Every failure mode (no key, market closed, network error,
rate-limited) is silent/non-fatal — `_liveNote` narrates it in the UI when a real attempt was made,
stays null on the common "no key file" case, exactly like `oracle-stackk.txt`'s own fallback pattern.
Old `forecaster-sample.json` deleted (superseded).

**Chart (reworked 2026-08-29, direct instruction)**: `ChartSvg()` now renders real candlesticks (body
= open/close, wick = high/low, coloured up/down) with a time-axis row along the bottom (~6 labels,
`MM/dd HH:mm` UTC) — was a bare close-price line with hit/miss dots. **Predict-ahead + countdown +
win/lose beat**: `RunOneAnimatedTick()` (replaces the old synchronous `TrainOnceStep`) splits every
tick into two visible phases — PREDICT (compute the guess, show it in a dashed `.fc-predict-strip`
with a CSS countdown bar) then, after a real `Task.Delay(_revealMs)` the bar's `animation-duration`
also uses, REVEAL+TRAIN (train on the true label, flash a win/lose badge). `_revealMs` is derived from
the speed slider (`Math.Max(120, 700 - _speed*55)`) — **this changes what the slider controls**: it
used to pick how many ticks trained per animation frame (a burst-of-N-then-pause loop); it now paces
the visible reveal cadence directly, a deliberate throughput-for-visibility tradeoff. `TrainOnce` and
`ToggleRun` both go through the same `RunOneAnimatedTick`, so a manual single click gets the same
predict/countdown/reveal beat as continuous run.

**Model shape**: `Dim=128, Layers=1, KPass=` live-read from `data/oracle-stackk.txt` (same as
Creature, see above), `CandleContext=128` → `MaxContext=256` tokens
(2/candle), `MinShifts=8` (no-op: `ShiftsFor(256,128)=16` already clears it; `CleanCapacity(16,128)=
122` is under the 256-token window — a real v2 tuning knob, not a v1 blocker). **Training loop** on
**HoloKernel**: per tick, predict via `_session.Logits(ctx)`/`Inspector.Capture` (opt-in) →
`RefinementLoop.Observe(ctx, PriceBase+trueBucket)` (trains inline, one tick at a time — unlike
Creature, Forecaster's training was NOT moved to a producer/consumer pipeline; only its now-removed
alpha-ramp was touched in the 2026-08-28 pass, see "Alpha-ramp REMOVED" above) → append the TRUE
token to the tape. `Lr=0.005` reasoned (between MarketSim's `0.02` and Creature's `0.0025-0.004`),
not yet watched live. **Data**: see the "Data (reworked 2026-08-29)" paragraph above —
`wwwroot/data/forecaster-history.json`, ~3,484 real hourly AAPL OHLC candles (~2 years) + an optional
live top-up; training cursor still wraps the (now much larger) finite series when it runs out.
**Queued**: wiring the fetch script into a scheduled CI workflow (YAML proposed, not added — see
above); a symbol picker; tuning `Lr` against a real run.

**Pacing history (superseded 2026-08-29)**: the loop used to run a "burst of up to `_speed`=10
`TrainOnceStep()` calls with a 1ms yield between each, then a longer between-burst pause" — tuned
2026-08-28 for raw throughput/responsiveness, not visibility. That shape is GONE — see the
"Predict-ahead + countdown + win/lose beat" paragraph above: `RunOneAnimatedTick()` now runs one
tick per loop iteration with a real, visible `_revealMs` delay between predicting and revealing, and
the speed slider paces THAT instead of a computed-ticks-per-frame burst. This is a deliberate
throughput-for-visibility tradeoff (max training rate dropped by roughly an order of magnitude at the
top of the slider), not an oversight — kept here as history since a future perf pass on this file
should know the old burst-tuning reasoning no longer applies to the current loop shape.

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
**Cold-start fix (2026-08-28)**: first Continue click used to render the whole continuation at once
instead of streaming — fixed with a throwaway `_session.Logits(...)` warm-up + a throwaway render
warm-up in `OnInitializedAsync` (unrelated to the pacing history below, still in place).

**Streaming architecture, corrected 2026-08-30 (real user complaint, watching the live site: "i can
see now that u have faked the letters being typed, its still being pre-generated then fake typed")**.
The 2026-08-28 "fix" replaced an inline `await Task.Delay(60)` with a producer/consumer split over an
unbounded `Channel<int>` — a producer ran the real `Logits()`/`Gate.Pick` loop with NO delay, writing
every token to the channel the instant it was computed, while a separate consumer drained the channel
and revealed one token per a FIXED ~60ms `Task.Delay` tick, fully decoupled from how long compute
actually took. That was a real, deliberate design from early in the project (tiny context, likely
near-instant compute, an artificial pacer was the only way to get any visible cadence at all) — but
it never stopped being what the user's complaint describes: compute finishes first, then a timer
fake-types it out. **The channel is gone.** Measured with a throwaway console harness (native x64
JIT, not WASM — see caveat below) against the real, currently shipped checkpoint (`oracle-brain.bin`:
`Dim=1536`, `K=8` read live from `oracle-stackk.txt`, `Context=52` tokens, ~245k rounds trained — the
context keeps growing +4/20,000 rounds, so re-measure if it grows a lot further): one real `Logits()`
call now costs **mean=97ms / median=98ms / p90=107ms / range 72-114ms** (N=60, JIT-warmed first) —
already inside typing-cadence territory running as fast native code, and the actual deployment is
WASM (AOT-compiled per `Showroom.csproj`'s `RunAOTCompilation`, still measurably slower than native
JIT for numeric hot loops), so genuine in-browser per-token compute is at least that slow, plausibly
slower, never faster. `Ask()` is now a single loop: compute one token, decode+reveal it immediately
(`StateHasChanged()`), `await Task.Yield()` (NOT a delay — zero artificial wait, exists only so the
browser actually paints the just-computed token before the next real ~100ms+ `Logits()` call blocks
the UI thread again), compute the next. No `Channel<int>`, no `Task.Delay(60)`, no pacer of any kind
left in this path — real compute latency IS the visible cadence now, genuinely, not a simulation of
one. **`OnInitializedAsync`'s example-round generation (seeds `_history`'s first entry before
`_loaded=true`) was checked and deliberately left as its own straight, undelayed loop** — it already
had no artificial pacer (never did), and it happens entirely behind the boot screen before the
history UI renders at all, so there is nothing for a visitor to ever see "streamed" there — it's
displayed as an already-complete historical run once the page loads, same as any other `_history`
entry, never with the `.pending`/typing-cursor treatment `Ask()`'s own in-progress run gets. Nothing
to rip out there; touching it would only add pointless yields to the boot path.
**Verified**: `dotnet build Showroom.csproj -c Release` green (0/0). Native-JIT timing harness
(throwaway, not committed) confirms real per-token compute latency at the live checkpoint shape —
this is the evidence base for "real compute is slow enough to serve as its own pacer," not a guess.
**Not verified live** (no browser here, per this repo's own boundary) — the actual in-browser WASM
per-token latency (expected ≥ the native numbers above, direction only, not measured) and the
resulting visible cadence still need a real check: open `/tools/prism`, hit Continue, and confirm
each character/token visibly appears as it's computed (open devtools' Performance/Network tab if you
want to confirm no request fires and the UI thread is genuinely blocked per-token, not faking a
render).

**OOM investigation + fix (2026-08-30) — real bug report: `Arg_OutOfMemoryException`, "happened
after some period of use."** Landed right after two changes: `MaxReplyChars` 40→600, and the
streaming rework above (per-token `StateHasChanged()`, no pacer). Investigated all 4 hypotheses
against the real code rather than guessing which one:
1. **Confirmed, the primary fix.** `Ask()`'s loop called `StateHasChanged()` after every real
   token — up to 600 render+diff cycles in ONE submission, unbounded by anything (the old fake-typing
   pacer had bounded this to whatever a ~60ms tick produced). Blazor rebuilds and diffs this
   component's whole render tree on every call. Fixed by **batching the render, not the compute**:
   `RenderBatch=4` — every token is still computed and appended to `revealed` immediately and
   unconditionally (nothing about compute pacing changed), but the DOM only repaints (and the O(n)
   `Decode(revealed)` rebuild only runs) once every 4 tokens, or on the stopping/final token so the
   visitor never sees stale state. This is a real, different thing from the removed fake-typing pacer:
   that decoupled *reveal timing* from compute; this only throttles how often an *already-revealed*
   token gets painted. See the method-comment in `Ask()` for the full reasoning.
2. **Confirmed, the secondary fix.** `_history` (`List<RunEntry>`) had no cap at all — grew for the
   life of the page, one entry per submission, forever. Every one of those entries' own DOM subtree is
   re-walked by Blazor's diff on every `StateHasChanged()` call (see #1), so an unbounded list compounds
   an unbounded render count: total render-diff work across a session was effectively unbounded on
   *both* axes at once, which is a very plausible slow-growth-to-OOM path over "many submissions in
   one sitting" (matches the report's own "after some period of use" framing better than a single big
   submission would). Fixed with a `HistoryCap=40` + `AddHistory()` helper, same "drop oldest"
   spirit as Creature's `_log`/`_curve` and Forecaster's `_log`/`_accCurve` caps (see those sections
   above) — set lower than their 200-250 because each Prism run can hold up to `MaxReplyChars`(600)
   chars of real text plus its own markup, not one short log line.
3. **Confirmed real, fixed as a minor cleanup, not the primary driver.** The per-token context slice
   used LINQ (`seq.Skip(seq.Count - take).ToArray()`) — allocates an iterator object AND the
   destination array, called up to 600×/submission (in both `Ask()` and the identical
   `OnInitializedAsync` example-generation loop). Deduplicated into one `BuildContext(seq, maxContext)`
   helper using `List<T>.CopyTo` (array-only allocation). Real GC pressure reduction, but nowhere near
   the scale of #1/#2 — `_session.Logits(ctx)` itself (a Vocab-sized `double[]` allocation + the
   ~100ms+ compute) already dwarfs this per token.
4. **Investigated, ruled a minor/non-driving factor.** Checkpoint context is ~48-52 tokens and grows
   +4/20,000 rounds — this only affects the size of the per-token `ctx` array (tens of ints) and the
   `Logits` return array (Vocab-sized doubles), both already-necessary per-token allocations that scale
   with model size regardless of anything in this task. Real, but not a growth-over-a-session driver
   the way #1/#2 are — flagged, not chased further.

**What was NOT done**: no delay/timer was reintroduced anywhere — `RenderBatch` groups already-computed
tokens for painting, it does not change when or how fast tokens are computed. `Ask()`'s
`await Task.Yield()` calls are unchanged in kind (still zero-wait, still exist only so the browser gets
a chance to paint before the next real compute call blocks the thread again) — there are just fewer of
them now (once per batch instead of once per token).

**Verified**: `dotnet build Showroom.csproj -c Release` green (0/0) after the change. **Not verified
live** (no browser here, per this repo's own boundary) — this is exactly the kind of claim that needs a
real device/session check: the user should open `/tools/prism`, watch devtools' Performance or Memory
tab (take a heap snapshot, submit several long (near-600-char) replies back to back, take another
snapshot) over an extended session, and confirm memory growth now plateaus/is bounded rather than
climbing unboundedly. Also worth eyeballing that streaming still visibly reads as "live" at
`RenderBatch=4` (should — 4 tokens at ~100ms+ each is still a visible ~400ms+ per repaint, not a
single instant dump) rather than feeling batchy/jumpy; if it ever needs re-tuning, `RenderBatch` is
the one number to move (lower = smoother but more renders, higher = fewer renders but chunkier reveal).

**Checkpoint fetch now gzip-precompressed (2026-08-31)**: `data/oracle-brain.bin` -> `data/
oracle-brain.bin.gz`, decompressed client-side via `HoloKernel.CheckpointFetch.FetchAndDecompressGzipAsync`
(full reasoning, measured sizes, and the exact one-command regeneration recipe for future interim
refreshes: "Checkpoint gzip precompression" under the HoloKernel section above). Both boot-log steps
(`Begin("data/oracle-brain.bin.gz")` on first load, and the reused-session narration) were relabelled
to the `.gz` filename so the boot log always names the file actually being fetched. No fallback to the
raw `.bin` was added — a fetch/decompress failure surfaces through the same outer try/catch that
already turns any checkpoint-load failure into a visible `_loadError`, never a silent hang.
`Analyst.razor`'s independent `EnsurePrismLoadedAsync` lazy-load (novelty scan) was updated the same
way, same helper, same reasoning — it fetches the identical checkpoint through a second code path
when Prism itself hasn't loaded it first this page load.

**Checkpoint refresh history (2026-09-04, five passes same day) — consolidated 2026-09-04, was 5
near-duplicate dated entries each restating the same verification recipe; compacted per §3 dedupe,
no fact dropped, just de-repeated.** Current ground truth: round **152,476**,
`Vocab=192,Dim=1536,Context=32,Shifts=16,Layers=1,ParamCount=516,288`, `oracle-stackk.txt=2`/
`oracle-iterwarm.txt=100` (matches `HoloEngine.cs`'s live `OneShotStackK`/`OneShotIterWarm` consts,
re-checked fresh every pass, never carried forward), AlgFormer pinned at **2.2.0**.

**Standing refresh recipe** (every pass below follows this, stated once): copy the snapshot's
`prism-holo.bin`/`-vocab.txt`/`-iter.txt` from `%LOCALAPPDATA%\Prism-MainSnapshot\snapshots\r0NNNNNN\`
(never the live-writing `%LOCALAPPDATA%\Prism\` dir directly — no dedicated "oracle export" tool
exists anywhere in PrismFormer/PrismGym, grepped `Program.cs`'s full `mode ==` dispatch, so this
raw-copy convention is the only hand-off shape there's ever been) into `oracle-brain.bin`/
`-vocab.txt`/`-rounds.txt`; deserialize the fresh `.bin` through a throwaway console app pinned at
AlgFormer 2.2.0 and round-trip `Serialize()` byte-identical before trusting it (catches a format
regression instead of assuming one can't recur); cross-check `oracle-stackk.txt`/
`oracle-iterwarm.txt` fresh against `HoloEngine.cs`'s live consts every time (never carried forward —
PrismStudio's own CLAUDE.md calls these "the user's own live-hand-edited knob... never trust a stale
number"); write every `.txt` sidecar via `[System.IO.File]::WriteAllText(path, text, new
UTF8Encoding(false))`, never PowerShell `Set-Content` (**real gotcha, hit once, r139,306 pass**:
`Set-Content -Encoding utf8` silently prepends a UTF-8 BOM — caught because a 6-char round number
came out 9 bytes); regenerate `oracle-brain.bin.gz` via the `GZipStream` recipe (HoloKernel section
above) and round-trip decompress-verify byte-identical to the raw `.bin` in both `wwwroot/data` and
`dist/data`; if source changed too, a full `dotnet publish Showroom.csproj -c Release` +
`robocopy /MIR` into `dist/`, confirming the publish output's own regenerated checkpoint bytes match
the hand-copied ones exactly (proves publish never silently re-touches the data files).

**The five passes, in order** (round : what changed beyond the checkpoint itself, if anything):
- **r129,136** — the checkpoint moved `CheckpointFormat` v1->v2 (128->192 vocab, 96 base `CharVocab`
  chars + 96 merges), unreadable by the then-pinned AlgFormer 1.5.0 (`CheckpointFormatException`,
  confirmed by actually trying). Bumped `Showroom.csproj`+`HoloKernel.csproj` to AlgFormer 2.2.0
  (`dotnet add package`), which surfaced a real breaking API change (2.0.0: every `StackIter*` entry
  point takes per-layer `double[] alpha`, not a scalar) at 2 call sites — `InspectorTrace.Capture`
  (`InspectStackIter`/`InspectAttention`) and `RefinementLoop.ObserveSequence`
  (`StackIterAccumulateAllPos`) — fixed with a uniform length-`model.Layers` array (every Showroom
  tool is `Layers=1`; AlgFormer's own docs guarantee this reproduces the old scalar path bit-for-bit
  — a signature adapter, not a behavior change). `RefinementLoop.Observe` (the single-layer oracle)
  untouched, still scalar by design.
- **r135,406** — data-only, plus a **lede/outro copy cut ~80%** (direct user instruction): the long
  lede + 7-paragraph `.outro` explainer (11,566 chars) replaced with a ~2,255-char 2-paragraph
  version framing this as a technical demo, not a finished product. `KFactDetail`/`RoundsPhrase` C#
  properties are now unused (left in place, harmless).
- **r139,306** — `_prompt` field default reverted `"Th"` -> `""` (direct user correction: real
  selectable text in the live input box was a papercut a visitor had to delete first);
  `placeholder="Th"` and the boot-time `examplePrompt` left alone (neither is the live box). This is
  the pass that caught the `Set-Content` BOM gotcha now folded into the standing recipe above.
- **r143,776** — generation length clipped to the model's own context instead of a flat cap (direct
  user instruction: "clip the prism output to its context length"). `const int MaxReplyChars = 600`
  -> `int MaxReplyChars => _stats?.Context ?? 32`, live off the loaded checkpoint (same
  live-not-hardcoded discipline `_k`/`_trainedAlpha` use). **Supersedes** the 2026-08-30 "let it run
  until it actually stops" instruction recorded in the streaming-architecture section above — the
  flat 600 cap let a run wander ~18x past the one window the model can actually see; a straight 1x
  multiple (cap = `_stats.Context`) is the point past which "continuing" stops being continuation
  with any of the original prompt still in view. `CharVocab.End`/`DegenGuard` still fire first when
  they fire at all — this only tightens the outer backstop.
- **r152,476** — data-only, no source change; a full `dotnet publish`+`robocopy /MIR` was still run
  per this pass's explicit instruction (overriding the normally-cheaper interim-refresh path the
  "Checkpoint gzip precompression" section above documents) rather than a bare data-file copy.

**Not verified live on any of the five passes** (no browser here, per this repo's boundary) — the
user should confirm `/tools/prism` loads the current (r152,476) checkpoint, the input box starts
empty, generation stops within one context-window's worth of characters, and Ask/Continue works with
the 192-vocab shape, on a fresh `dist/` deploy.

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
- `EvaluatedApplications.AlgFormer` **2.2.0** (bumped from 1.5.0, 2026-09-04 — see "Checkpoint refresh
  + AlgFormer 2.2.0 bump" under Prism below) — The Creature, The Forecaster, Prism (`PrismFormer`
  namespace: `HoloFormer`, `HoloShape`, `CharVocab`, `SubwordVocab`) — needs `InspectStackIter`/
  `InspectAttention`/`DecodeFace`/`EquivCompute`/`InvisibleMultiplier`, none published before 1.5.0.
  **2.0.0+ is REQUIRED to read the live PrismStudio checkpoint format (v2)** — 1.5.0 only reads v1 and
  throws `CheckpointFormatException` on a v2 file; also carries a real breaking change (StackIter entry
  points take per-layer `double[] alpha`, not a scalar) — both HoloKernel call sites were adapted, below.
- `EvaluatedApplications.Tracer` 1.1.0 — The Creature (`Tracer.Helpers.GridTactics`). `EvalApp` comes
  in transitively (AlgFormer's own dependency); Showroom never references it directly.
- `ProjectReference ..\HoloKernel\HoloKernel.csproj` — Creature, Forecaster, Prism (see HoloKernel
  section). A sibling in-repo RCL, not a MonoRepo reference; itself NuGet-only against AlgFormer.
- `TargetFramework=net10.0`, `PublishTrimmed=true` + `RunAOTCompilation=true` (landed 2026-08-28, an
  EXPERIMENT testing whether AOT is worth it for this app's numeric hot loops; `dotnet publish` only,
  not `dotnet run`/dev server — trimming was off over an "EvalApp reflection" worry, now believed
  stale, not independently re-verified). Cost: `dotnet.native.*.wasm` becomes one large AOT module
  (~8 MB compressed) — see Boot screen's compile-gap narration, added because of this.
- `WasmEnableThreads=false` (landed true 2026-08-28, REVERTED same day after a real-device regression
  — see "Real WASM multithreading" below for the incident). While it was `true` it coexisted cleanly
  with `PublishTrimmed`+`RunAOTCompilation` (full `dotnet publish -c Release` succeeded, exit 0, 0/0,
  ~4m40s-5m15s wall time either way) — that compatibility fact stands if threading is ever re-tried,
  it just isn't what's deployed today.
- **Version bumps only via `dotnet add package`** (latest published) — never hand-edit `<Version>`.
  A capability not yet published is a hand-off to the coordinator, not a reach into MonoRepo source.

## Boundary (hard, from the agent charter)
- **Checkpoint hand-off (Prism) is `prismstudio-owner`'s call, not this repo's.** Ask for a copy of
  `%LOCALAPPDATA%\Prism\prism-holo.bin` + `-vocab.txt` (+ `-iter.txt`) at a snapshot THEY pick. Drop
  at `Showroom/wwwroot/data/oracle-*` — the page fetches those exact paths, degrades gracefully
  (`_loadError`, no crash) if any are absent.
- **Same hand-off shape, new instance (Forecaster's live top-up)**: a Finnhub API key is a real
  self-serve, instant, no-card signup (`finnhub.io/register`) that this agent cannot do itself (no
  browser/email verification available here). Drop the key as plain text at
  `Showroom/wwwroot/data/finnhub-key.txt` to activate the live top-up; absent by default, the tool
  degrades gracefully to the historical bundle alone (see The Forecaster's "Data" section above).
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
  tool can persist a checkpoint ("Reset brain" just drops the in-memory reference). **Runtime
  threading model, corrected 2026-08-28 twice the same day**: real multithreaded WASM
  (`WasmEnableThreads=true`) was landed, then REVERTED after a real-device regression (a laptop's
  Continue button on `/tools/prism` stuck permanently disabled while a phone loading the same deploy
  was fine — see "Real WASM multithreading" below for the full incident). Both the dev server
  (`dotnet run`) and published builds are single-threaded/interpreted again — same house pattern this
  fact used to describe pre-2026-08-28. `Parallel.For`/`IParallelMap` degrade to sequential, not a
  crash, but training can be visibly slow on a deep/wide config; keep live-training shapes small
  (Creature `d=384,L=1`; Forecaster `d=128,L=1`).
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
- A new tool page picks up the parallax/glow treatment (`wwwroot/css/depth.css`) FOR FREE as long as
  it reuses the house `.room`/`.room-head`/`.stat`/`.cr-curve`/`.cr-log`/`.outro` shape (see Site
  plumbing's "CSS pattern" note) — it just needs its own `data-cat="data|ml"` on the outer
  `<div class="room">` (see "Scroll-tied parallax depth" section) to get the right glow tint instead
  of the site-accent default.

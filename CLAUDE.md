# AboutUs — CLAUDE.md (website-owner)

Public site repo for `evaluatedapplications.github.io`. Static HTML content (indexable, instant)
+ a Blazor WebAssembly tools app under `/tools` (The Analyst, The Creature). You (website-owner)
own PRESENTATION — design, cohesion, rendering, nav, deploy. Package owners own CONTENT, authored
in `MonoRepo/<Pkg>/docs/site.md`. You never edit package source or `docs/site.md` — flag stale
content to the owner instead. You never commit/push (coordinator commits, user pushes → Pages).

## Site map (what exists, 2026-08-28)

Root static pages (`site/*.html`), all built on the shared design system:
- `index.html` — **tools-first homepage (2026-08-28 pivot, see "Tools-first pivot" below)**: company
  pitch, then the 4-card tool gallery (Analyst/Creature/Forecaster/Prism) as the entire top-level
  content, then a slim "Powered by" package-chip strip (`.pkg-strip`, no cards/descriptions) linking
  to `/packages.html`. The 11-package gallery and the "how it fits together" flow diagram used to
  live here inline — they now live on `packages.html` (below), not duplicated on the homepage.
- `packages.html` — **NEW (2026-08-28)**: the relocated 11-package gallery in 4 categories
  (Foundation / Data / Machine learning / Spatial & games) + the "how it fits together" flow
  diagram, near-verbatim from the old `index.html#packages` section, wrapped in the standard plain
  page template (no `os-chrome`). This is now the real `/packages.html` URL every page's nav/footer/
  crumb/`.related` "Packages" link points at — no more same-page-anchor special-casing.
- `holodb/index.html` — HoloDb HUB page (lean bespoke nav: Home / Benchmarks / Docs / Packages /
  NuGet — "Try it live" and "How it works" live as hero CTAs/first section instead of nav items).
  The richest page on the site: race demo, benchmark tables, capability grid, deploy options.
  Links out to `holodb.html`, `holodb-client.html`, `holodb-protocol.html`.
- `holodb.html` — HoloDb benchmark methodology sub-page (full method + every number, in/out of
  process vs DuckDB/SQL Server). Linked from the hub's `#benchmarks` section.
- `holodb/manual/index.html` — HoloDb manual (prose docs, `.prose`/`.toc` template).
- `algformer.html` — AlgFormer package page (BOTH cores: softmax `AlgFormer` + holographic
  `HoloFormer`, per `AlgFormer/docs/site.md`). Links onward to `holoformer.html` for the deep dive.
- `holoformer.html` — "meaning as chords" explainer article, specifically about the HoloFormer
  holographic core. Demoted from primary destination to a linked deep-dive off `algformer.html`
  (crumb: Home / AlgFormer / HoloFormer, explained). Its own bespoke concept-card layout, built on
  the shared tokens via a page-local `<style>` block layered on `site.css`.
- `algformer-gpu.html`, `evalapp.html`, `evalapp-neural.html`, `phasor.html`,
  `holodb-client.html`, `holodb-protocol.html`, `holovoxel.html`, `prose.html`, `tracer.html` —
  one page per remaining package, all built on the plain shared template (see below).
- `articles.html` — **NEW (2026-08-30)**: the personal-writing index (NOT package docs — see the
  dedicated "Articles" section below, right after the page-template paragraph, for the full
  content model, the empty-state shape, and the exact per-article publishing workflow).
  `articles/_example.html` is a deliberately unlisted, noindexed TEMPLATE for the per-article page
  shape — not a real page, never link it from anywhere.
- `404.html` — SPA-fallback bounce for `/tools/*` deep links + a friendly not-found page.
- `sitemap.xml`, `robots.txt`, `.nojekyll` — kept in sync with the page set above.

Non-content: `Showroom/` (Blazor WASM app, publishes to `/tools`) — a SEPARATE concern from the
static content pages; don't fold tool code into `site/`. `.github/workflows/deploy.yml` builds
`Showroom` and copies `site/` + the published `wwwroot` into one `_site/` artifact for Pages.

**`SiteKit/` (2026-09-02, the reusable-toolkit plan — see "Platform initiative" below +
`docs/platform-architecture.md`)**: `tokens/core.css`+`brand-ea.css` + `COMPONENTS.md` (component
inventory) + `README.md`. **`tokens/` is now LIVE, not inert** (closed 2026-09-02): `site/assets/
site.css` `@import`s `SiteKit/tokens/{core,brand-ea}.css` right after its top comment instead of
restating their values inline — verified equivalent by parsing both the pre-edit `:root`/
`body[data-cat]` text and the token files into `selector→{prop=value}` maps and diffing
programmatically (11/11 selectors, identical, after fixing one real drift the diff caught: `brand-
ea.css`'s chord rule had picked up a stray `--glow-near` override the live site never had).
`deploy.yml`'s "Assemble site" step now also `cp -r SiteKit/tokens/. _site/SiteKit/tokens/` — the
`@import`'s relative path resolves to `/SiteKit/tokens/...` once served from the Pages artifact
root, so this copy is load-bearing, not cosmetic (without it the import 404s live and every custom
property on the deployed site goes undefined). Full record: `platform-architecture.md` §10.
**3 real C# projects, Phase 1 core + Phase 2 (16 of 17 pages proven 2026-09-02)**:
`SiteKit.Spec/` (the declarative `PageSpec` record types + fluent builder, zero deps —
`CardSpec` gained `CatRootOverride` in the Phase-1 pass; the second Phase-2 batch, same day, added
`HeroSpec.LimHtml`+`InstallMaxWidthPx`, `SnippetSpec.DescBeforeHtml`, `SectionSpec.ExtraHtml` (on
`Prose`), two new `SectionKind`s `StackFlow`/`Raw`, and `PageSpec.PageStyleHtml`; the THIRD batch,
same day, closed every remaining composer gap — 4 new `SectionKind`s (`ToolGrid`/`Compare`/
`ConceptArticle`/`ProseArticle`), `PageSpec.Hero` made nullable, plus a long tail of small typed
overrides (`SeoSpec.OgType`/`RobotsMeta`, `PageSpec.TailScriptHtml`/`LeadingHtml`/`MetaCharset`/
`NavBurgerAriaLabel`/`NavItemsOverride`, `HeroSpec.RawBodyHtml`/`CrumbHtml`/`ExtraBodyHtml`/
`ExtraClass`/`LeadingCommentHtml`, `SectionSpec.SectionId`/`LeadingCommentHtml`/`IntroHtml`/
`LimStyleAttr`, `CardSpec.LimHtml`/`PreBodyHtml`/`OmitCatStyle`, `RelatedLink.CssClass`) — full
list and reasoning below),
`SiteKit.Render/` (the EvalApp-native render pipeline — `PackageReference
EvaluatedApplications.EvalApp 1.7.0`, NuGet-only, same boundary `HoloKernel` uses for AlgFormer;
`HeroComposer` had two real bugs fixed across the first two passes, plus several more real
gaps the third batch's diffs caught directly — `og:type` hardcoded to `"website"`, a missing
CardGrid intro-paragraph slot, an unconditional `.grid` wrapper on ToolGrid — see below),
`SiteKit.Render.PoC/`
(ports 16 pages through the real pipeline in ONE run and diffs each output against its live file —
all 16 verified IDENTICAL after normalizing away pure whitespace/line-wrap AND under an independent
whitespace-stripped byte compare, full record in `platform-architecture.md` §9/§10/§11).

**Phase 1 + Phase 2's first batch (2026-09-02, morning)**: `phasor.html` (Phase 1), then
`prose.html`+`tracer.html`, deliberately picked to be structurally DIFFERENT from Phasor (a real
two-tone `CatOverride`+`CatRootOverride` chord, `Category` != `CategoryDotVar`, no `.prism-beam`,
no `ClosingStack`, a bare single-snippet Snippets section, a CardGrid section that also carries a
`.lim`) — doing so surfaced a real `HeroComposer` bug (it emitted the `.hero-content` z-index
wrapper unconditionally; the live site only nests it on the 5 pages that also carry `.prism-beam`
— Phasor alone, being one of those 5, could never have caught this), now fixed and conditional on
`HeroSpec.ShowPrismBeam`.

**Phase 2's second batch (2026-09-02, afternoon)**: `evalapp.html`, `holovoxel.html`,
`holodb-client.html`, `holodb-protocol.html`, `evalapp-neural.html`, `algformer-gpu.html` — chosen
to surface every remaining composer gap in one pass. New, additive `SiteKit.Spec`/`SiteKit.Render`
capabilities, each proven necessary by an actual page: `HeroSpec.LimHtml` (a `.lim` aside between
the CTA row and Related pills — `evalapp.html`, `holodb-protocol.html`), `SectionSpec.StackFlow` (a
`.sec-head` section whose body is a `.stack` + `.flow` diagram row, distinct from `ClosingStack` —
`evalapp.html`'s "What you'd otherwise assemble"), `SectionSpec.Raw` (a `.sec-head`-framed raw-HTML
escape hatch, not yet a typed table spec — `evalapp.html`'s "None of this is invented from nothing"
`<table>`), `SectionSpec.ExtraHtml` on `Prose` (raw HTML between the prose paragraph and `.lim` —
`holovoxel.html`'s `.shots` before/after figure grid), `SnippetSpec.DescBeforeHtml` (a lead-in
BEFORE its own snippet, vs. the pre-existing `DescAfterHtml` which reads as introducing the NEXT
one — `holodb-protocol.html`, where even the FIRST snippet needs a lead-in), and
`PageSpec.PageStyleHtml` (a verbatim page-local `<style>` block in `<head>` — `holovoxel.html`'s
`.shots` CSS, one of only 3 pages site-wide still carrying page-local CSS). **One real bug the diff
itself caught** (not speculative): `HeroComposer` hardcoded `.install`'s `max-width` to `520px`;
`holodb-protocol.html`/`algformer-gpu.html` actually use `560px` on the live site (longer NuGet
package names) — the first run reported a genuine 1-line diff on both, fixed via a new
`HeroSpec.InstallMaxWidthPx` parameter (default 520), re-verified clean. `holodb-client.html`/
`evalapp-neural.html`/`algformer-gpu.html` needed ZERO new capability — deliberate controls proving
the by-then-larger surface already generalizes; `algformer-gpu.html` is also the first page
site-wide with zero Prose/Snippets/StackFlow/Raw sections (3 plain CardGrids only), proving that
shape is valid too.

**Nav-item-count question (Showroom 4 vs. static site 3-6), RESOLVED 2026-09-02, direct user
decision**: "I prefer the HTML versions, as the Blazor-only pages are for apps, not sharing info" —
hardens content/app as a firm split. No shared `<SiteNav>` component is planned; the two navs stay
independently scoped (full record: `docs/platform-architecture.md`'s "Open questions" section,
`SiteKit/COMPONENTS.md` entry 2). Nothing about this site's own nav changed as a result — `Home`
already is the tools front door post-pivot.

**Still fully INERT w.r.t. the deployed site's PAGES** (the tokens/CSS half is live, see above, but
`site/**/*.html` itself is untouched): `deploy.yml` doesn't build `SiteKit.Spec`/`SiteKit.Render`/
`SiteKit.Render.PoC`, no page markup was touched, the PoC's generated output lands only in a
gitignored `bin/**/out/` folder. `evalapp-owner`'s design review (Phase 0.5) is done — the original
two-pipeline sketch in `platform-architecture.md` §3.2 was wrong (wouldn't compile: `ForEach` takes
a build-time step-DSL callback, not a per-item delegate; `ICompiledPipeline<T>` isn't a valid step)
and has been replaced with real, building, running code: one compiled tree, nested
`ForEach<SiteRenderJob>` inside `ForEach<PageRenderJob>` (sites → pages), fixed `Tunable` bounds,
no `.WithTuning()`.

**Phase 2's third batch (2026-09-02, same day) — the 7 previously-flagged composer-gap pages, all
now proven, 16/17 total.** `algformer.html` (`.card.tool` "Try it live" gallery + the "Two cores,
same shape" `.cmp` comparison), `holoformer.html` (the bespoke 7-card concept-explainer article),
`articles.html` + `articles/_example.html` (the `.prose`/`.toc` template + the personal-writing
index), and all 3 HoloDb pages (`holodb/index.html` the hub, `holodb.html` the benchmarks
sub-page, `holodb/manual/index.html` the manual) — every one verified structurally AND
whitespace-stripped-byte IDENTICAL against its live file, same discipline as the first two
batches. New, additive `SiteKit.Spec`/`SiteKit.Render` capabilities, each proven necessary by an
actual page (not spec'd speculatively):
- **`SectionKind.ToolGrid`** (`ToolCardSpec`: Title/Href/Tag/nullable-Ver/DescHtml/GoInText) — the
  `.card.tool` gallery shape, shared verbatim by `algformer.html`'s 3-card `.grid` gallery and the
  HoloDb hub's lone, `.grid`-less "The Analyst" card (`SectionSpec.ToolGrid(...,
  omitGridWrapper:true)` — a real, live markup difference the diff itself caught, not assumed).
- **`SectionKind.Compare`** — `algformer.html`'s two-card `.cmp` "AlgFormer vs HoloFormer" block,
  reusing `CardSpec` verbatim (same `.card`/`.card-top` shape CardGrid uses) inside a `.cmp` wrapper
  instead of `.grid`, plus one trailing prose paragraph.
- **`SectionKind.ConceptArticle`** (`ConceptCardSpec`: glyph SVG + kick label + h2 + N paragraphs +
  anchor callout; `ConceptCompareCardSpec` for the closing "ordinary vs. this model" `.cmp`) — the
  whole bespoke `<main class="sec">` body of `holoformer.html`'s 7-card "meaning as chords"
  explainer, the single richest new typed shape this batch, chosen over piling `Raw`/`ExtraHtml`
  onto a 7-times-repeating component per the standing "type what recurs" rule.
- **`SectionKind.ProseArticle`** (`ProseArticleSpec`: CrumbHtml/H1/optional ByelineHtml/optional
  LedeHtml/optional Related pills/optional TocItems/BodyHtml) — the `<main class="wrap"><article
  class="prose">` shell shared by all 3 "prose-template" pages (`articles/_example.html`,
  `holodb.html`, `holodb/manual/index.html`), paired with **`PageSpec.Hero` becoming nullable**
  (no `<header class="hero">` at all on these 3 — the pipeline's RenderHero step now skips
  `HeroComposer` entirely when `Hero is null`). Each page's own h2-sectioned body content stays raw
  `BodyHtml` — genuinely one-off prose/tables/snippets per page, not worth typing further, same
  "type the shell, escape-hatch the one-off content" split as `SectionSpec.Raw`.
- **`HeroSpec.RawBodyHtml`** — a full escape-hatch override of the entire hero-body (crumb through
  related, all bypassed) for a hero widget-heavy enough that the typed fields would mostly go
  unused while fighting the ones that don't fit: the HoloDb hub's hero has no crumb, no `.facts`
  pills, a `.race` benchmark widget between the lede and the CTA row, and `.install` AFTER
  `.cta-row` (every other page puts install first). `HeroSpec.CrumbHtml`/`ExtraBodyHtml` are the
  lighter-weight siblings used where the standard hero shape mostly fits (`holoformer.html`'s
  2-hop crumb + `.thesis` figure pair between lede and related; `articles.html`'s 1-hop crumb on a
  facts-only hero).
- **`SectionSpec.SectionId`** + **`SectionSpec.LeadingCommentHtml`** / **`HeroSpec.LeadingCommentHtml`**
  — `id="..."` on `<section class="sec">` for same-page anchors (`articles.html`'s `#articles`; the
  HoloDb hub's `#how`/`#workload`/`#benchmarks`/`#features`/`#deploy`), and raw HTML (in practice,
  an `<!-- HOW IT WORKS -->`-style comment) emitted immediately before a section/hero tag — the
  HoloDb hub carries one such marker before every one of its 8 top-level blocks; reproduced
  verbatim rather than dropped, since the structural-diff tokenizer treats comment text as real
  content.
- **`CardSpec.LimHtml`** (a second `.lim` INSIDE one card, after its own `.desc` — distinct from
  the section-level `.lim` after the whole grid), **`CardSpec.PreBodyHtml`** (raw HTML, typically a
  `.snip` code sample, before a card's own `.desc`), **`CardSpec.OmitCatStyle`** (drops
  `style="--cat:..."` entirely rather than falling back to the section default — the HoloDb hub's
  3 "Deploy" cards carry no `--cat` at all on the live page) — all first exercised by the HoloDb
  hub's "Capabilities"/"Deploy" CardGrids.
- **`SectionSpec.IntroHtml`** (CardGrid) — a lead-in `.desc` paragraph BEFORE the `.grid`, distinct
  from the existing `.lim` which trails after it; **`SectionSpec.LimStyleAttr`** — an inline
  `style="..."` on that trailing `.lim` (the hub's "Deploy" section is the first with one,
  `margin-top:16px`, every other CardGrid `.lim` is unstyled).
- **`SectionSpec.ClosingStackWithInstall`** (`ClosingInstallCommand`) — an `.install` chip between
  a ClosingStack's `<p>` and `.cta-row`, the hub's "Get started" closing block.
- **`SeoSpec.OgType`** (default `"website"`, `"article"` for the 4 explainer/reference pages) and
  **`SeoSpec.RobotsMeta`** — both were REAL, unhandled gaps the diff itself caught (HeadComposer
  had `og:type` hardcoded to `"website"` until `holoformer.html`'s diff failed on it), not
  speculative additions.
- **`PageSpec.TailScriptHtml`** (full `<script>` override; null = the standard year+copy-button
  script), **`PageSpec.LeadingHtml`** (raw HTML before `<!DOCTYPE html>` —
  `articles/_example.html`'s own publishing-recipe comment), **`PageSpec.MetaCharset`** (default
  `"utf-8"`; `holodb.html` live-carries the uppercase literal `"UTF-8"`, reproduced not "fixed"),
  **`PageSpec.NavBurgerAriaLabel`** (default `"Toggle menu"`; `holodb.html` live-carries `"Menu"`),
  **`PageSpec.NavItemsOverride`** (per-page nav item list — most pages share one site-level
  `NavSpec`, but `holoformer.html`'s NuGet link points at the AlgFormer package directly instead of
  the site's usual profile URL, and the 3 HoloDb pages each carry their own extra nav items), and
  **`RelatedLink.CssClass`** (the HoloDb manual's own "Manual" nav link carries `class="active"`) —
  a cluster of small, real, page-level literal divergences the byte-identity check surfaced one at
  a time, each fixed as a typed override rather than a special case in the composer.
- **`HeroSpec.ExtraClass`** — an extra space-separated class on `<header class="hero ...">` (the
  HoloDb hub's own `hd-hero` page-local class).

All additions are backward-compatible (new optional fields/params with defaults, or new enum
cases) — none of the 9 pages proven in the first two Phase-2 batches needed to change, re-verified
clean in the same run as these 7. `SiteKit.Render.PoC/Program.cs` now builds and diffs all 16 pages
in one pipeline run; `articles.html`/`articles/_example.html`'s hand-authored originals are 2 of
the 16 despite `articles/_example.html` being a noindexed, unlinked template (not one of the 17
"routable" pages CLAUDE.md's Site map counts) — included because the task explicitly named it as
needing the same `.prose`/`.toc` composer support as `articles.html` itself.

**Still not ported: `index.html`, `packages.html`** — neither was named in the composer-gap flag
this batch closes (both are plain package/tool gallery grids, already close to the proven
CardGrid/ToolGrid shape), so they're left as the one remaining gap in the 17-page count rather than
assumed done. Still fully INERT w.r.t. the deployed site's PAGES (see below) — this whole batch,
like the two before it, only proves the pipeline CAN reproduce these pages; it doesn't cut any of
them over.

**All 11 current MonoRepo packages have a page**: Phasor, EvalApp, EvalApp.Neural, AlgFormer
(+HoloFormer deep-dive), AlgFormer.Gpu, HoloDb (+benchmarks), HoloDb.Protocol, HoloDb.Client,
HoloVoxel, Prose, Tracer. All 11 are catalogued on `packages.html`; only 6 (HoloDb, AlgFormer,
Tracer + transitively EvalApp/Phasor) currently power a live tool — see "Tools-first pivot" below.

**Unlisted client page (not part of the routable site — do not "fix" this by adding it anywhere)**:
`site/recycledao-preview.html` — a private progress preview for the RecycleDAO client PoC
(`C:\Users\dongy\RecycleDAO`, a separate repo outside AboutUs, owned by `recycledao-owner`), built for
the user to share with the client (Antonio) by direct link only. Deliberately NOT in `index.html`'s
gallery, NOT in `sitemap.xml`, NOT in any nav/footer, and carries `<meta name="robots"
content="noindex,nofollow">`. Standalone minimal header (brand only, no nav-links) rather than the
usual site nav, since it isn't an EA product page. **Re-rendered again 2026-09-01** (second render
same day, this repo's `docs/status-brief.md` moved further after the first re-render at commit
`f7f6dcd`) from RecycleDAO's own `docs/status-brief.md` (current source of truth, written specifically
for this non-technical client audience). Content still reflects the 2026-08-29 pivot from a
recycling-specific submit/verify/reward token PoC to a general "digital parliament" platform —
dependency-tracked claims with a retraction cascade (family-tree analogy), pairwise contradiction
flagging, a currencyless self-minted peer-to-peer ledger (competing-local-currencies analogy), a
reputation layer built from hand-checkable arithmetic over both (public-credit-report analogy), and a
Reed-Solomon erasure-coded storage layer underneath the ledger — plus five things new in this pass:
**burn/redemption** (any holder, not just the issuer, can destroy their own held balance; the
lifetime-minted figure stays an immutable historical fact, only "still outstanding" drops), a
**Phasor-based plausibility pre-check** ahead of the exact Reed-Solomon shard reconstruction (a cheap
approximate check that flags a corrupted-but-present piece fast, before the expensive exact rebuild
runs), a **holographic commitment embedded in the block header** (a compact fingerprint of a block's
own contents baked into the tamper-evidence alongside its hash), **multi-tenancy** (hard-partitioned
per-community claim graph/ledger/reputation stores, own tables per community, no shared-schema
visibility filter to leak through — got its own new "How it's built" card), and a **real multi-process
socket proof of shard distribution** (several genuinely separate OS processes on ONE machine trading
Reed-Solomon shards over real loopback TCP and reconstructing the block; explicitly still
same-machine only, NOT real multi-machine networking — also its own new card, and called out again in
"Not built yet," which dropped its old "no multi-tenancy" line since that's now built and replaced it
with the real-multi-machine-deployment caveat). "What it is, in plain terms" gained a 5th capability
card (resilient storage promoted from an implementation detail to its own top-level concept, per the
brief's own framing) alongside the original 4 (dependency cascade / contradiction flagging /
currencyless ledger / reputation). The original ERC-20/Governor/Timelock recycling-token work is
still framed as historical (still built, still tested, not being extended), still its own "How it's
built" card. Same minimal shape as both prior renders (standalone brand-only header, no nav-links, 5
`.sec` blocks + closing `.stack`, noindex; tag-balance re-verified: section/div/p/h2/h3/article/span
all matched) — only the words changed, not the template. Re-render on request when the PoC's
milestone status changes (next source-of-truth check: RecycleDAO's `docs/status-brief.md`, falling
back to its `CLAUDE.md` for any structural fact the brief doesn't cover); never link it from anywhere.

## Design system

**One stylesheet**: `site/assets/site.css` (its `:root`/`body[data-cat]` token values now live in
`SiteKit/tokens/{core,brand-ea}.css` via `@import`, 2026-09-02 — see the `SiteKit/` paragraph above
for the rewire + its verification; edit the token FILES, not `site.css`, for any future palette/
token change). Dark, UNCONDITIONALLY — this is a branded visual
identity (Dark Side of the Moon), not a neutral utility UI, so it never defers to the visitor's
system/browser colour-scheme. **Changed 2026-08-28**: a `prefers-color-scheme:light` palette
override used to exist (`:root` block + a `.prism-beam` opacity tweak) and was REMOVED, direct user
instruction after real-phone testing showed it firing and washing the brand out to white/pastel on
a phone in light mode ("get rid of light pallets then dark always"). Don't reintroduce a light
palette without an explicit, separate request — and if one's ever wanted, gate it behind an opt-in
control, not automatic OS detection. Design tokens: `--bg/--bg-2/--surface/--surface-2`,
`--border/--border-2`, `--ink/--ink-soft/--ink-faint`, `--accent/--accent-ink`, `--spectrum`
(brand gradient) — plus the **per-package palette (2026-08-28, supersedes the old 4-bucket Data/ML/
Spatial model — see "Per-package palette" subsection right below for the full table + reasoning)**
and `--cat-root`, a per-card companion custom prop (set alongside `--cat` only where `--cat` itself
holds a gradient) for the few CSS call sites that need a real solid colour, `--ok/--warn/--bad`,
`--radius`, `--wrap` (1080px), `--font`/`--mono`. This same
file also styles the Blazor tools shell's loading/error UI
(`#app:has(.loading-progress)`, `#blazor-error-ui`) via the shared tokens, but that's the ONLY
reach into `Showroom/`'s presentation from here — its own component styles are `showroom-owner`'s
territory (see "Prism motif" below for a live coordination flag on this boundary).

**Per-package palette (2026-08-28, supersedes the 4-bucket Foundation/Data/ML/Spatial model)** —
direct user correction after seeing the live site: "still white blue purple and green, I want the
full spectrum... red through purple, not only the cold colours." The old bucket model had a hard
ceiling of "however many buckets exist" (3 cool hues + white), not "however many packages exist," so
every one of the 11 real packages now maps to the table below instead. **THE hex table, single
source of truth** (also duplicated into `site.css`'s own `:root` comment, since there's no shared
token file across the AboutUs/Showroom repo boundary — same "brand-mark-stopped-at-repo-boundary"
limitation already on record under "Platform initiative" below; a parallel `showroom-owner` task
consumes this same table to retint Prism/Analyst/Creature/Forecaster, it does NOT get its own
independent picks):

| Package | Token | Hex | Family / placement reasoning |
|---|---|---|---|
| Phasor | `--c-foundation` (gradient) | — | Foundation, undispersed beam. Unchanged, confirmed correct by the user, not a domain colour. |
| EvalApp | `--c-foundation` (gradient) | — | Foundation, same as Phasor. |
| Tracer | `--c-tracer` | `#f0796a` | Foundation-only leaf (warm end, stop 1/8 of the ROYGBIV run). |
| HoloVoxel | `--c-holovoxel` | `#f09b5c` | Foundation-only leaf (warm end, stop 2/8), adjacent to Tracer. |
| HoloDb.Client | `--c-holodb-client` | `#e9ba53` | HoloDb family, depends on HoloDb.Protocol (stop 3/8). |
| HoloDb.Protocol | `--c-holodb-protocol` | `#a9cf5f` | HoloDb family, depends on HoloDb (stop 4/8). |
| HoloDb | `--c-holodb` | `#66c1aa` | HoloDb family anchor (stop 5/8) — adjacent to AlgFormer on purpose, see Prose below. |
| AlgFormer | `--c-algformer` | `#5998ff` | AlgFormer family anchor (stop 6/8) — adjacent to HoloDb on purpose. |
| AlgFormer.Gpu | `--c-algformer-gpu` | `#877dff` | AlgFormer family, depends on AlgFormer only (stop 7/8). |
| EvalApp.Neural | `--c-evalapp-neural` | `#c07dff` | AlgFormer family, depends on AlgFormer + Foundation-dropped EvalApp (stop 8/8). |
| Prose | *(no token — see below)* | — | Composite, always the HoloDb+AlgFormer chord, never its own hex. |

All 8 hexes are the SAME `--spectrum` gradient re-sampled at 8 even points (t=0,1/7,2/7…1) rather
than an invented palette — still one brand gradient, just finer-grained than the old 7 named ROYGBIV
stops. Placement is dependency-derived: each real package FAMILY (HoloDb: HoloDb/`.Protocol`/
`.Client`; AlgFormer: AlgFormer/`.Gpu`/EvalApp.Neural; the 2 Foundation-only leaves Tracer/HoloVoxel)
occupies a contiguous run of stops, ordered by real dependency chain within the family (e.g. HoloDb
→ HoloDb.Protocol → HoloDb.Client, matching who `ProjectReference`s whom), and the families are
spread across the FULL spectrum (warm end = the two foundation-only leaves, middle = HoloDb family,
cool end = AlgFormer family) so the site finally shows genuine warm-to-cool range, not 3 cool hues.
**Prose has no dedicated stop.** Unlike the other 8, it is not a single-domain product with one real
dependency chain — its own `docs/site.md` says outright "Depends on: HoloDb ... and AlgFormer" — so
it is structurally a COMPOSITE, same as a multi-package Showroom tool, and always renders as the
two-tone hard-edged chord of `var(--c-holodb)`+`var(--c-algformer)` (card `--cat`, mobile icon tile,
pkg-strip swatch — all of it, not just the page's ambient glow) rather than owning a hex nothing else
would reference. HoloDb sits at stop 5 and AlgFormer at stop 6 (adjacent) specifically so this chord
reads as one smooth neighbouring blend instead of two hues yanked from opposite ends of the wheel.

**How this reaches every page (mechanism, unchanged shape from the old bucket system, just reading
finer tokens now)**: `body[data-cat="<package-name>"]` (was `"foundation"|"data"|"ml"|"spatial"`, now
the literal package name — `"tracer"`, `"holovoxel"`, `"holodb"`, `"holodb-protocol"`,
`"holodb-client"`, `"algformer"`, `"algformer-gpu"`, `"evalapp-neural"`, or `"foundation"`) drives the
`--glow-near`/`--glow-mid` parallax tint via the same `body[data-cat="..."]` rules near `:root` in
`site.css`; the CHORD attribute value renamed from `"data-ml"` to `"holodb-algformer"` (prose.html
only, the one genuinely two-package page) for the same reason — the attribute now names real
packages, not a bucket pair. Every product page's own `.sec-head .dot` / `.card` `--cat` was swept to
its own single package hue (previously several packages shared one bucket colour, e.g. `algformer-
gpu.html`/`evalapp-neural.html`/`prose.html`/`holoformer.html` all used to read as identical
`--c-ml` indigo — now each is visibly its own hue). Two real, pre-existing MISCOLOURINGS were fixed as
part of this sweep, not just retinted: **The Analyst tool card was `--c-spatial` (green)** on both
`index.html` and `holodb/index.html` despite depending on HoloDb only — now `--c-holodb`, matching the
user's own named example ("Analyst draws on HoloDb only... need their own distinct package-level
hue"). **Prism was flat `--c-ml`** (shared with 4 other packages) despite depending on AlgFormer
only — now `--c-algformer`, the user's other named example. A few genuinely NON-dependency decorative
uses of the old bucket tokens (an "ordinary transformer" cold-blue contrast metaphor on
`holoformer.html`, a DuckDB competitor benchmark bar on `holodb/index.html`) were repointed to the
neutral `--spectrum-5` stop instead of any package token, specifically so they don't newly imply a
false dependency now that colours are package-specific rather than a shared decorative bucket.
`algformer.html`'s "AlgFormer (softmax) vs HoloFormer (holographic)" two-core comparison cards (both
genuinely part of the SAME package) use `--c-algformer-gpu` / `--c-algformer` respectively as a
still-contrasting but honest pair — not a fake HoloDb dependency the old `--c-data` pick implied.
**Flagged for `showroom-owner`** (not touched from here, out of this agent's ownership per charter
§0): Prism/Analyst/Creature/Forecaster in `Showroom/` should retint to this same table — Prism →
`--c-algformer` `#5998ff`, Analyst → `--c-holodb` `#66c1aa`, Creature → the AlgFormer+Tracer chord
(`#5998ff`+`#f0796a`), Forecaster → `--c-algformer` `#5998ff` — so the two repos read as one system
despite the missing shared token file.

Reusable components: `.site-nav` (sticky, CSS-only mobile burger via
`.nav-toggle` checkbox hack — lean, always a flat list of 4-6 plain links, see Navigation below)
+ `.related` (compact contextual cross-link pills in the hero, see Navigation),
`.hero`/`.eyebrow`/`.lede`/`.facts`/`.fact`, `.sec`/`.sec-head`,
`.grid`/`.card` (the package-card pattern, `--cat` custom prop sets the left accent bar,
`.card-link` stretched-link overlay makes the whole card clickable — see Navigation),
`.install` (copy-button code chip), `.btn`/`.btn-primary`/`.btn-ghost`, `.crumb` (breadcrumb),
`.stack` (callout box, `.flow` for pipeline diagrams), `.prose`/`.toc` (manual/docs pages),
`.snip`/`.lim` (code-sample box / caveat note — added 2026-08-26 for the product-page rollout,
reused by 9+ pages), `footer.site`. **Added 2026-08-28 (tools-first pivot)**: `.card.tool` (class
selector, not `a.tool` — matches both the plain `<a class="card tool">` shape still used by
algformer.html's mini tool gallery, and the `<article class="card tool">` + `.card-link` overlay
shape the homepage's 4 tool cards now use so they can nest a `.powered` "Powered by" pill row
without invalid nested-anchor markup — same reason package cards are `<article>`, not `<a>`);
`.powered`/`.powered-label` (the pill row itself, tool-card-scoped, smaller/quieter than `.related`);
`.pkg-strip`/`.pkg-chips`/`.pkg-chip` (the homepage's slim package-chip strip below `#tools` —
deliberately NOT a `.sec`/`.sec-head`, so it never grows an os-chrome window panel and can't read as
a second top-level gallery). **`.hero-bar`/`.hero-body`/`.hero-content`/`.win-dots` + the
`body.os-chrome` opt-in system** (window-panel chrome, taskbar/dock nav, mobile icon grid — added
2026-08-28, **swept to all 16 routable static pages 2026-08-28 (later same day)**, see the dedicated
"OS chrome" section below for the full component list, the completed sweep, and the chord-glow
mechanism). A handful of legacy pages (`evalapp.html` pre-rewrite,
`holodb/index.html`, `holodb.html`) used to duplicate these tokens in a local `<style>` block;
`evalapp.html` was migrated onto `site.css` in the 2026-08-26 rewrite (see Reconciliations). The
two HoloDb pages still carry a local `<style>` (bespoke charts/race-demo/table markup that isn't
reused elsewhere) but declare the SAME token values, so they read as one brand, not a fork — if a
token in `site.css` ever changes, grep those two files' `<style>` blocks too.

**Vertical rhythm (2026-08-28 baseline-grid pass)** — direct user request to recalibrate the
type/spacing scale onto a real baseline grid instead of ad-hoc px values. `--rhythm:28px` (new
`:root` token) is derived from body's own type, not invented: `font-size:17px * line-height:1.65 =
28.05px`, rounded to a clean 28px. body's own `line-height:1.65` was deliberately left UNITLESS,
not switched to `var(--rhythm)` directly — an absolute px line-height would inherit literally into
every smaller-font descendant (every mono badge/pill/label site-wide), inflating their line boxes
to a full 28px regardless of their own font-size; unitless is standard CSS practice and still
computes to ~28px for body-sized text. Since 28 divides evenly by 4, the working sub-grid is
exactly the quarter-baseline, **7px** (also tokenised: `--rhythm-q:7px`, `--rhythm-h:14px` half,
`--rhythm-3q:21px` three-quarter) — every recalibrated vertical margin/padding/gap/line-height in
`site.css` is now a whole multiple of 7px, preferring the rounder rungs (14/21/28/35/42/49/56...)
wherever the original ad-hoc value already sat close to one. Fluid/clamp type (`.hero h1`, `.lede`)
kept a tuned unitless line-height ratio instead of a fixed px value (an absolute value would
decouple from font-size across the clamp range) — chosen so both clamp endpoints land within ~1-2px
of a clean quarter-baseline multiple. Headings (h1/h2/h3) keep a tight, legible base ratio for
their OWN line box (a single ratio can't land every heading instance's differing font-size on the
grid at once) and the rhythm is preserved instead via each heading's own vertical MARGIN, which
every selector (`.sec-head h2`, `.card h3`, `.stack h2`, `.prose h2/h3`) now sets explicitly to a
clean 7px-multiple. **Deliberate, documented exception**: inline pill/badge/chip vertical padding
under 7px (`.tag`, `.ver`, `.fact`, `.pkg-chip`, `.related a`, `.powered a`, `.copy`, `.stack .flow
span`, the small inline code chips, `.win-dots`/`.nav-burger`'s own icon-glyph spacing) was left OFF
this grid on purpose — decorative micro-UI atoms, not part of the vertical reading flow; forcing a
7px floor onto them would visibly bloat compact badges for no rhythm benefit, since their POSITION
in the page flow is already governed by their parent's on-grid margin/gap. Every reusable component
was swept: `.hero`/`.eyebrow`/`.lede`/`.facts`/`.fact`, `.sec`/`.sec-head`, `.grid`/`.card` (+
`.card.tool`/`.powered`/`.pkg-strip`), `.stack`/`.flow`, `.related`, `.crumb`, `.btn`, `.prose`/
`.toc`, `.snip`/`.lim`, `footer.site`, and the whole OS-CHROME block (hero-bar/hero-body, the
`.sec:has(.sec-head)` window panel, the taskbar/dock, the `#packages`/`#tools` mobile grids), plus
the Blazor loading/error UI. `.facts`/`.grid`/`.stack .flow` (the task's named wrapping-row
examples) had their `gap` shorthand split into `row-gap`(on-grid)/`column-gap`(unchanged, horizontal
out of scope) — extended the same split to `.related`/`.powered`/`.pkg-chips` for consistency, since
they're the same "wrapping pill row" pattern. **Local `<style>` block drift fixed as a direct
consequence**: `holodb.html`'s and `holodb/index.html`'s local `.snip`/`.lim` redefinitions (and one
inline-styled `.snip`/`.install` instance in each) were exact duplicates of the OLD site.css values
— updated to the new recalibrated values so they don't silently diverge from the shared component
now that the shared default moved (same gotcha the "two HoloDb pages" paragraph above already
documents — checked `holoformer.html`/`holovoxel.html`'s local `<style>` blocks too: their vertical
spacing there is either genuinely bespoke, page-local components (`.shots`, `.race-row`, `.stat`,
`table.cmp` — not shared-class duplicates, correctly left alone) or pre-existing inline overrides
that already diverged from the old default before this pass (not staled BY this pass, left as
intentional one-offs). No horizontal spacing was touched anywhere (explicitly out of scope).

**Prism motif (2026-08-28 restyle)** — direct user request, referencing Pink Floyd's *Dark Side of
the Moon* cover: deep-black field + a precise geometric prism refracting a beam into a spectrum,
read as "progress/refinement," explicitly NOT pride-flag styling. What changed, site-wide (all 17
`site/**/*.html`, via the shared tokens/components, no per-page hand-tweaking):
- **Deeper black**: `--bg #050608` (was `#0a0c11`), `--bg-2/--surface/--surface-2/--border/--border-2`
  all stepped down to match. `--ink-faint` bumped `#6b7486`→`#7c869c` in the same pass — darkening the
  bg alone only ever *helps* contrast for light-on-dark text (verified: body/soft text sit ~17:1/~7.9:1,
  comfortably AAA), but `--ink-faint` was already borderline-sub-AA (~4.16:1) against the old bg for the
  small mono labels it's used for (`.eyebrow`, `.related`, `.fact`, `.pkgid`), and darkening the bg
  alone wouldn't have fixed that — so it got a deliberate lighten to ~5.55:1 (comfortably AA) in the
  same pass rather than shipping a font-size class of text that stayed marginal. Light-mode palette
  (`prefers-color-scheme:light`) already carried its own `--ink-faint:#8a94a8` override and was
  untouched at the time — checked, not a regression. **Since removed entirely (2026-08-28, see
  Design system above)**: the whole light palette was later found to defeat this same brand identity
  on a real phone in light mode and was deleted outright, so this paragraph is now historical record
  of that pass's reasoning, not a description of current CSS.
- **`--spectrum` reordered to true ROYGBIV** (was an arbitrary 6-stop purple→blue→teal→green→gold→coral
  run): `#f0796a`(R) `#f0a15a`(O) `#e6c450`(Y) `#7bd86a`(G) `#4aa3ff`(B) `#7d7dff`(I) `#c07dff`(V), also
  exposed as flat `--spectrum-1..7` vars for use outside a `linear-gradient()` context (e.g. individual
  SVG `stroke`s). Deliberately reuses existing brand hues where they already sat near a ROYGBIV slot
  (G=`--c-spatial`/`--ok`, B=`--c-data`, R=`--bad`) rather than introducing a parallel palette — the
  4 category dot colours (`--c-foundation/--c-data/--c-ml/--c-spatial`) were left alone (categorical,
  not spectral; changing them would ripple into every card accent on every page for no requested
  reason). This flows automatically into every page via `.beam` (the 3px top strip, already spanned
  every page) and the brand mark/favicon — no page markup edit needed for that part.
- **Brand mark + favicon → an actual prism triangle** (was a hexagon/lozenge outline): same viewBox,
  same `.mark`/favicon `<link>` slots, path swapped to `M16 5L27 26H5Z` (a clean triangle), gradient
  stops swapped to the 7-stop ROYGBIV above. Applied identically across **all 17** `site/**/*.html`
  files via a scripted exact-string replace (mechanical, verified via a hit-count report per file —
  every file hit exactly once for the mark, 16/17 for the favicon, `404.html` correctly has no
  favicon link at all) — a brand identity glyph can't be half-migrated without being a cohesion
  regression, so this one WAS swept everywhere in one pass, unlike the hero graphic below.
- **`.prism-beam` (hero graphic — deliberate subset, NOT swept everywhere)**: a small decorative
  inline SVG (white beam → triangle outline → 7-line ROYGBIV fan), CSS-positioned absolute behind
  the hero text (`.hero` now `position:relative;overflow:hidden`, `.hero>.wrap` lifted to
  `z-index:1`), right-aligned, capped `min(40vw,520px)` wide, `opacity:.65`, hidden below 900px so
  it can't collide with hero copy once it wraps to fewer chars/line on tablet. Originally on exactly
  2 pages (`index.html`, `algformer.html`), scoped up **2026-09-01** to **5 of 16 pages**: those two
  plus `phasor.html`, `holoformer.html`, `holovoxel.html`. Reasoning per added page (a deliberate,
  reasoned subset, not a full sweep — chosen for flagship/product feel or a real thematic tie to the
  beam's own "undispersed light → spectrum" story, not just decoration):
  - `phasor.html` — the strongest thematic fit on the whole site: its own per-package palette row
    (Design system, above) already calls Phasor "Foundation, undispersed beam" — the white source
    the SVG itself draws before it hits the triangle. Simple hero shape (facts/install/cta-row/
    related, no competing widget), plenty of empty right-side space at desktop width for the beam
    to occupy — low collision risk.
  - `holoformer.html` — the "meaning as chords" deep-dive already frames its whole thesis as a
    spectrum/tones metaphor (its own hand-drawn `.thesis` figures: "scattered points" vs "a stack of
    tones", the second literally 4 parallel coloured lines) — the beam's dispersion-into-lines visual
    echoes that figure rather than competing with it. `.thesis` is capped `max-width:560px` on a
    1080px `.wrap`, leaving right-side room at desktop width.
  - `holovoxel.html` — a visual/rendering engine page (holographic near-crisp/far-fuzzy LOD), plain
    hero shape like Phasor's, no competing widget, benefits from a striking hero graphic the way a
    graphics-adjacent product page should.
  Deliberately LEFT OUT, with reasons (not an oversight): **`holodb/index.html`** — the richest page
  on the site, but its hero already carries a real visual centerpiece (the animated `.race` bar-chart
  demo) as the thing that's supposed to draw the eye; layering a second decorative graphic behind it
  risked competing for attention on the one hero that least needs help, so skipped pending a
  real-device look rather than guessed as fine. **`algformer-gpu.html`, `evalapp.html`,
  `evalapp-neural.html`, `holodb-client.html`, `holodb-protocol.html`, `prose.html`, `tracer.html`,
  `packages.html`** — secondary/dependent packages or (for `packages.html`) an index/listing page,
  not a marketing-flagship hero; adding a decorative beam here would read as sprawl without the
  thematic tie the 5 live pages have, exactly the "busy, not deliberate" outcome the task warned
  against. Extending further is still a fast follow (same markup block: the `.prism-beam` SVG as the
  first child of `.hero-body`, real content wrapped in `.hero-content` right after it) whenever a
  specific page earns the same reasoning.
- **Fixed a real staleness while in here**: `holodb/index.html`'s inline "writes → accumulator" SVG
  diagram (its own bespoke graphic, not reusable) had 4 attribute values hardcoded to the OLD
  `--ink-faint`/`--surface`/`--border`/`--border-2` hex literals instead of `var(...)` — those would
  have gone visibly stale (lighter than the new page background) the moment the tokens changed. Fixed
  to the new hex values in place. Swept the rest of `holodb.html`/`holodb/index.html`/`holoformer.html`'s
  local `<style>` blocks for the same pattern first — everything else in all three already consumes
  `var(--token)` rather than duplicating literals, so it inherited the restyle for free; this one inline
  SVG was the only exception found. **Gotcha for next time a token value changes**: grep
  `#[0-9a-fA-F]{6}` across `site/**/*.html` (not just the two `<style>`-block pages) — hardcoded hex can
  hide in inline SVG attributes on ANY page, not only the two pages already known to carry local
  `<style>` blocks.
- **Coordination flag, not acted on**: Showroom (`Showroom/`, the Blazor tools app: The Analyst /
  Creature / Forecaster / **Prism**) is `showroom-owner`'s territory per charter — did NOT touch
  `Showroom/**`. The Prism tool sharing its name with this whole visual motif is an obvious, real
  tie-in (matching triangle/spectrum treatment in Prism's own UI chrome) but is a call for the
  coordinator to route to `showroom-owner`, not something to reach into from here.

**The page template** (used verbatim by every one of the 10 plain product pages, and by
`index.html`/`holoformer.html`'s nav/footer): `<div class="beam">` → `<nav class="site-nav">`
(brand mark + Home/HoloDb/Packages(→`/#packages`)/NuGet, identical on every page — lean, no
dropdown/JS) → `<header class="hero">`
(breadcrumb, eyebrow, h1, lede, `.facts` chips, `.install`, `.cta-row`, then a `.related` pills row
— see Navigation) → a sequence of
`<section class="sec">` (What it is / Why it's useful as a `.grid` of `.card`s / Key features /
Get started with a `.snip` code sample / a `.lim` caveat note) → `footer.site` → the copy-button +
year script (identical). New product pages should copy this shape exactly, not invent new layout —
that IS the cohesion mechanism (§0 of the agent charter).

**Articles (2026-08-30, NEW content type — the user's own writing, not package docs).** Direct
user request: "an articles page where I can publish writings ... don't need a fully built blog,
just a way to present interesting articles." This is genuinely different from every other page on
the site: the words are the USER'S OWN essays, not rendered from a package's `docs/site.md`, so
there is no owner-content pipeline for this — you (website-owner) hold the article text once the
coordinator hands it over and build the page directly, same as any other rendering task, just with
no source-of-truth doc to pull facts from (there are no "facts," it's an essay).
- **`site/articles.html`** — the index. Built on the plain `os-chrome` template (own `<header
  class="hero">`, no `.facts`/`.install`/`.related` pills — those are product-page furniture this
  page doesn't need). Body content is `<section class="sec" id="articles"><div class="articles">`
  (the list) `<div class="stack articles-empty">` (the honest zero-state, "Nothing published yet").
  **No fake/placeholder articles were added** — the site's own no-mocked-content ethos — so today
  the `.articles` div is empty except for a big HTML **comment** containing one fully-written
  `<article class="article-item">` block (title `<h3><a>`, `<time class="article-date">`,
  `<p class="article-summary">`) with numbered instructions right above it. **Publishing a real
  article is exactly**: copy that commented block, fill in title/href/date/one-line summary, paste
  it as the FIRST child of `<div class="articles">` (newest first, plain reverse-chron, no
  tags/categories — deliberately not built, matches "don't need a fully built blog"), then delete
  the `<div class="stack articles-empty">` block once there's at least one real entry.
- **`site/articles/_example.html`** — the per-article page shape, proven once so a real article
  doesn't have to re-derive it, built on the SAME `.prose`/`.toc` typography `holodb/manual/
  index.html` uses (`<main class="wrap"><article class="prose">`, no hero, no `.grid`/`.card`
  anywhere — real long-form reading measure, not a product page). **This file is a TEMPLATE, not a
  real page**: leading HTML comment with the full copy-to-publish recipe (7 numbered steps), a
  visible amber `.stack` banner right after the nav ("TEMPLATE — not a published article"),
  `<meta name="robots" content="noindex,nofollow">`, and — same as `recycledao-preview.html`'s
  precedent — deliberately NOT linked from `articles.html`, any nav/footer/`.related`, or
  `sitemap.xml`. `.toc` is called out in the template as OPTIONAL (only worth it for a piece with
  several headed sections; a short essay can delete that block and just flow as plain h2s).
- **Publishing workflow, end to end** (also written inline in `_example.html`'s own leading
  comment, kept here too so it isn't only discoverable by opening that file): (1) copy
  `articles/_example.html` → `articles/<slug>.html` (kebab-case, matches the rest of the site's URL
  style — see `holodb-client.html`), delete the leading comment + noindex meta + amber banner, fill
  in title/description/canonical/OG/JSON-LD `Article` block/crumb/h1/date/body copy; (2) copy the
  commented block in `articles.html` into `<div class="articles">`, fill it in, delete the
  `articles-empty` stack once real; (3) add the new URL to `sitemap.xml` (one-off essay =
  `monthly`/`0.5`, matching the site's existing pattern for reference pages); (4) update this
  section's own published-count/slug list below.
- **Reachability, deliberately NOT a nav item**: adding a 4th slot to the just-normalized 3-item
  `Home · Packages · NuGet` nav (see Navigation section) would have re-broken the exact "one nav
  shape everywhere" cohesion win closed only two days earlier, for a page that (today) has zero
  content to show. Used the site's OTHER documented reachability path instead: `articles.html`
  added to `footer.site`'s link set on `index.html` (now `Packages · Articles · HoloDb · Docs ·
  NuGet`) and `packages.html` (now `Home · Articles · HoloDb · NuGet`) — the two top-level index
  pages. Reachability walk: Home → footer → Articles = 1 click; Packages → footer → Articles = 1
  click; any deep product page → Home (1) → footer Articles (1 more) = 2 clicks, inside the site's
  own ≤2-click invariant. `articles.html`'s own footer stays the plain `Home · Packages · NuGet`
  subset (it doesn't need to link to itself). No other page's nav/footer was touched — if Articles
  ever earns enough real content to be a primary destination, revisit the nav-item question then,
  don't preempt it now with zero published pieces.
- **Published so far: three.** `ctx8-and-the-reverse-grow.html` ("Growing the Wrong Way," dated
  2026-09-02) — coordinator-authored follow-up to `ctx4-plateau.html`: the ctx=4→ctx=8 fork's
  ~96,000-round non-recovery, the debunked "hard-mine-first-then-grow" hunch (with the honest
  correction that the LR-starvation explanation was checked and ruled out), the pivot to fast
  200-round-tier growth, and the position-freshness structural bug this surfaced (every grow was
  demoting the single most load-bearing context position to make room for a fresh, untrained one)
  — plus the `growFromFront` fix that resulted, including an honest correction of the author's own
  first (wrong) safety reasoning. Written directly from session context, same first-person voice,
  same `.toc`+h2 shape as the other two. `nobody-read-the-warning.html` ("Nobody Read the Warning," dated
  2026-08-30) — the user's own essay, handed over already voice-rewritten and approved verbatim; this
  agent only adapted formatting (h2 sections, `.toc`, a `.lim` italic closing-note aside) into the
  existing `_example.html` shape, no wording changed. `ctx4-plateau.html` ("The Four-Token Ceiling,"
  dated 2026-08-31) — a coordinator-authored summary of the ctx=4 hard-overtraining plateau experiment
  (see `monorepo-owner-agents`/`prism-swarm-studio` context), written in the user's established
  first-person voice directly from the session's own findings (no separate handoff draft this time —
  content and formatting done together, same shape as `_example.html`: `.toc` + h2 sections, no
  hero/card chrome). `articles.html`'s `<div class="articles">` now holds both real
  `<article class="article-item">` entries (newest first, the honest empty-state `.stack` block long
  since deleted) plus the copy-paste template comment, kept in place for the next piece. JSON-LD type
  used on both: `BlogPosting` (not `Article` — a personal argumentative essay reads as a blog post, and
  this site's `articles.html` already functions as the blog index; `Article` stays the right pick for
  anything more reference/documentation-shaped like the 3 `TechArticle` explainer pages). Both added to
  `sitemap.xml` (`monthly`/`0.5`, matching the one-off-essay pattern the workflow already specifies).
  Byline convention (unchanged, both pieces follow it): a single `<p class="article-date">` line
  reading `Evaluated Applications · Published <time>...` (no separate pen name — same author identity
  as the rest of the site's copy).

**Footer link set** (every page): `footer.site .mono` carries `© <year>` + 3-4 internal links, a
second bottom-of-page path into the graph beyond the top nav. Plain product pages + `index.html` +
`holoformer.html`: `Home · Packages(/#packages) · HoloDb · NuGet`. The 3 HoloDb pages use a
page-appropriate subset (hub: `Home · Packages · Docs · Benchmarks · NuGet`; benchmarks: `Home ·
HoloDb · Manual · NuGet`; manual: `Home · HoloDb · Packages · NuGet`). **Flagged 2026-08-28**: the
top nav's HoloDb-pinning was fixed this pass (see Navigation section), but this footer set still
privileges HoloDb the same way and wasn't touched — out of scope for the nav fix as asked (that was
specifically "top-level nav"), but it's the same residual pattern and a candidate for the same fix
next time the footer is touched.

**SEO tags** (every page): unique `<title>` + description + canonical + OG/Twitter tags, all present.
JSON-LD before `</head>`: `Organization`+`WebSite` on the index, `SoftwareApplication`
(version/url/downloadUrl/offer price 0, matching the nothing-license-gated stance) on all 11 package
pages, `TechArticle` on the 3 explainer/reference pages. Keep `softwareVersion` in sync with the
`<Version>` ground truth below when a package bumps — it goes stale exactly like the hero chip does.

## Content-doc sources (per package, `MonoRepo/<Pkg>/docs/site.md`)

The words on every product page are pulled from these files (+ `PACKAGE.md`/`CLAUDE.md`/the
`.csproj <Version>` for facts) — never invented. All 11 exist and were current as of 2026-08-26:
`Phasor`, `EvalApp`, `EvalApp.Neural`, `AlgFormer`, `AlgFormer.Gpu`, `HoloDb`, `HoloDb.Protocol`,
`HoloDb.Client`, `HoloVoxel`, `Prose`, `Tracer`. If a `docs/site.md` goes stale (version bump,
new feature, license-wording change) the OWNER updates it; you re-render the page from it, you
don't patch the page's prose independently of that source.

**Version ground truth**: always read the package's own `.csproj <Version>` — `docs/site.md` can
lag by a patch. As of 2026-08-26: Phasor 1.0.3, EvalApp 1.6.1, EvalApp.Neural 1.0.1, AlgFormer
1.5.0, AlgFormer.Gpu 1.3.0, HoloDb 1.7.7, HoloDb.Protocol 1.0.2, HoloDb.Client 1.4.0, HoloVoxel
1.3.0, Prose 1.0.2, Tracer 1.1.2. Each package's own `CLAUDE.md` can ALSO lag the csproj (found
EvalApp's and AlgFormer.Gpu's CLAUDE.md one patch behind their csproj on this pass) — the csproj
`<Version>` element is the only source that can't be stale.

## Reconciliations (2026-08-26 first full 11-page render) — open flags only

Closed items from that pass (evalapp.html rewrite onto shared `site.css`, HoloDb version sync,
AlgFormer/HoloFormer split, license-stance wording, index.html package count) are already reflected
in the Site map / Design system sections above and not restated here. Still-open flags:
- **evalapp-owner**: `evalapp.html` dropped an old, unsourced benchmark suite (no provenance in any
  EvalApp-owned doc, and `about-us-evalapp.md` itself says not to quote unverified numbers) rather
  than caveat it. If real, reproducible benchmark numbers ever land in an owned doc, re-render the
  page to include them — the shape (a "what you'd otherwise assemble" comparison) is ready for it.
- **holovoxel-owner**: only the near-view `before_dated.png`/`after_holoform.png` pair is used on
  `holovoxel.html`. The `far_before_pointsampled`/`far_after_smoothed` pair was deliberately skipped
  — visual inspection showed "after" reading MORE artifacted than "before", the opposite of the
  filenames' claim (likely mislabeled/unrelated capture). Flag if a correct far-LOD pair turns up.

**2026-08-28: Prism "you be the judge" benchmark section, `algformer.html` — ADDED then REVERTED same
day, relocation not deletion.** First added as a standalone lone-`.stack` `<section class="sec">`
(no `.sec-head`) inserted between "Try it live" and "The problem it solves": cited TinyStories
(arxiv.org/abs/2305.07759) as the closest published benchmark for how small a language model can be
and still write legible English, stated Prism's checkpoint spec via `.facts`/`.fact` pills (`d=1536`,
1 layer, `shifts=16`, `~28` tokens context, `128`-token vocab, `~410K` real stored parameters), then
linked to `/tools/prism` inviting the visitor to type something in and judge for themselves. **Direct
user feedback, same day**: "wasn't relevant on algformer page — that's just the package, not applied
to anything." `algformer.html` describes the AlgFormer/HoloFormer package itself (an engine/library
page), not a specific running model a visitor can test, so a "you be the judge" CTA pointing at a
live checkpoint felt out of place there — the content belongs on Prism's OWN tool page instead
(`Showroom/Pages/Prism.razor`, a `showroom-owner` task, in progress as of this note), not on the
library page that merely links to the tool. **Removed the whole section verbatim** (the `<section
class="sec">` between "Try it live" and "The problem it solves" — h2, both `<p>`s, the `.facts` pill
row, the closing CTA `<a class="btn btn-primary" href="/tools/prism">`), re-verified tag balance via
the same scripted open/close-tag count used when it was added (section/div/p/h2/h3/span/a/article all
matched post-removal). `algformer.html`'s "Try it live" gallery (the 3 `.card.tool` links to Prism/
Creature/Forecaster) and hero CTA were UNCHANGED by either the add or the revert — those are
legitimate cross-links to the tools, not the reverted content. **The underlying figures
(`~410K`/`d=1536`/`shifts=16`/`~28`/`128`, sourced from `Showroom/wwwroot/data/oracle-brain.bin` ≈
3.29 MB) and the TinyStories citation aren't lost** — they're a candidate source for whoever renders
the equivalent content directly onto `Prism.razor`; flag to `showroom-owner`/coordinator rather than
re-deriving from scratch if that page wants the same benchmark framing.

## Navigation — the reachability contract (owned by this agent, not the coordinator)

**The invariant**: every page reachable in ≤2 clicks from every OTHER page's nav, without a global
mega-menu bloating every page (tried a 5-column/17-item `<details>` dropdown 2026-08-26, user
feedback: too dense, too large on mobile, and it duplicated what the homepage already does — see
below for the revised shape).

**The shape (revised 2026-08-28, FOURTH pass — the tools-first pivot, see "Tools-first pivot" below
for the full rationale)**: three layers, each doing ONE job.
1. **Lean top nav, identical shape everywhere**: `Home · Packages(→/packages.html) · NuGet` — 3
   items, plain text links, no dropdown, no JS. **Changed 2026-08-28 (tools-first pivot)**: the
   `Tools(→/#tools)` slot was DROPPED sitewide (previously the 3rd item on the 3 pages that had it:
   `index.html`, `phasor.html`, `holodb/index.html` — the other 14 pages never had it, see below) —
   once the homepage's entire top-level content IS the tools grid, a nav item that scrolls to
   `/#tools` is redundant with `Home` itself. `#tools` keeps its `id` for deep-linking even without a
   nav entry pointing at it (a tool page's own `.related`/CTA links, or an external link, can still
   land there directly). Net effect: nav shrinks from 4→3 items on the 3 pages that had `Tools`, well
   under the ~6-item compactness ceiling. A few pages still add 1-2 page-appropriate items on top of
   the 3 universal ones (the HoloDb hub: `Home · Packages · Benchmarks · Docs · NuGet`, 5 items,
   down from 6; the manual: adds `Manual`+`The Analyst`) but NEVER more than ~6 items total.
   **Rollout status**: all 17 `site/**/*.html` files (barring `404.html`/`recycledao-preview.html`,
   neither of which ever carried a standard nav) now point their `Packages` link at the real
   `/packages.html` URL — this part of the sweep WAS done sitewide in the same pass, since it's a
   mechanical href swap (not an `os-chrome`/layout change) and leaving it half-done would have left
   14 pages' "Packages" nav link silently landing on a homepage with no `#packages` section anymore
   (a real reachability break, not a cosmetic one — see "Tools-first pivot" below). The `os-chrome`
   window/taskbar/dock TREATMENT itself is still only on those same 3 pages, unchanged status, still
   paused pending review — see "OS chrome" below; don't conflate the two, only the chrome/window
   styling is still mid-sweep, the nav-item-count and href-target changes above are sitewide-done.
2. **`packages.html` is the routable index for packages; `index.html` is the routable index for
   tools.** `packages.html`'s `.grid` lists every one of the 11 packages with a fully clickable card
   (`.card-link` stretched-link overlay, not just a small "Explore →" — see below); nav's "Packages"
   link sends you straight there. `index.html`'s `#tools` grid IS the tools index (0 clicks from
   Home, since it's the entire top-level page content) plus a "Powered by" pill row per tool card
   linking straight to that tool's real constituent package page(s). This is why the nav doesn't
   need to enumerate all 12+ pages itself: `Home`/`Packages` (1 click) → any package card (1 click) =
   every package page ≤2 clicks from anywhere; any tool is 0-1 click from Home directly.
3. **`.related` pills for contextual 1-click jumps.** A small pill row (2-4 sibling links + "All
   packages →" to `/packages.html`) in the hero of every product/reference page (15 of 17, all but
   `index.html`/`packages.html` themselves — neither needs to link to itself). Curated per page by
   what's actually relevant, e.g. `phasor.html` → EvalApp/AlgFormer/HoloDb/HoloVoxel;
   `holodb-protocol.html` → HoloDb/HoloDb.Client; `holoformer.html` → AlgFormer/AlgFormer.Gpu/Phasor.
   This is what makes closely-related pages 1 click apart instead of always routing back through the
   homepage. CSS: `.related` in `site.css`, always paired with a `.related-label` and a
   `.related-all` pill.

**Site-wide rules that make it hold**:
- **RESOLVED 2026-08-28 (tools-first pivot)**: the old rule here — "`#packages` must NEVER be a bare
  same-page anchor except literally inside `index.html`, always write `/#packages`" — is now MOOT.
  Packages live at a real URL (`/packages.html`) with no same-page-anchor special-casing anywhere;
  every page's link is a normal href. Nothing to remember here anymore; kept as a one-line historical
  note so nobody re-adds the old special-case logic by habit.
- A gallery/index card that links onward must be clickable across its WHOLE area, not just a small
  "Explore →" text. Pattern (all 11 cards on `packages.html`, and — since 2026-08-28 — all 4 tool
  cards on `index.html` too, once they needed to nest a "Powered by" pill row): keep the card as
  `<article class="card">` (it nests a second link — NuGet, or a powered-by pill — so the card itself
  can't be an `<a>` — invalid nested-anchor markup), add a full-bleed
  `<a class="card-link" href="..." aria-label="...">` as the FIRST child,
  `.card-link{position:absolute;inset:0;z-index:1}`, and lift `.install`/`.links`/`.note`/`.powered`
  to `z-index:2` so their own inner links/copy-button stay independently clickable above the overlay.
  Tool cards that DON'T nest any link (e.g. algformer.html's mirror gallery) can stay the simpler
  whole-card `<a class="card tool">` shape — both shapes share the `.card.tool` CSS selector (class,
  not tag-qualified) so either renders identically.
- Don't reach for a global mega-menu again for "make X reachable" — reach for (a) the `packages.html`
  card grid staying exhaustive and fully clickable, (b) the homepage tools grid staying exhaustive for
  tools, and (c) a `.related` pill on the relevant pages. A dropdown only earns its keep for something
  genuinely global and rarely-changing (there wasn't one here); it was cut once it turned out to
  duplicate the homepage and hurt mobile.

**§5 VERIFICATION DISCIPLINE for this site**: checking that every `href` resolves to a real file is
NOT proof of reachability. The proof is a reachability WALK: starting from `index.html`'s nav AND
from one deep page's nav (e.g. `phasor.html`), using ONLY the nav + visible on-page links (no
address-bar typing), list the click-path to every other page, confirm ≤2 clicks, AND separately
check the nav is compact on a narrow (≤640px) viewport — that was the failing case both times this
got re-litigated. Do this walk any time the nav, page set, or card grid changes; record it in the
task's return message. An href-audit alone is not this check (that was the 2026-08-26 mistake).

**SEO/nav facts still true**: `sitemap.xml` lists all 17 pages (16 + the new `packages.html`) + the
4 live tool routes + `/tools/`.
Every page's `footer.site` carries a second internal-link path beyond the top nav. JSON-LD on every
page (`Organization`+`WebSite` on the index, `SoftwareApplication` on all 11 package pages,
`TechArticle` on the 3 explainer pages). Forward cross-links exist base→extension
(`algformer.html`→`algformer-gpu.html`, `evalapp.html`→`evalapp-neural.html`) as well as
extension→base. `site/assets/nav.js` is a retired stub (was the mega-menu's outside-click-close
helper, nothing references it now — safe to delete outright, kept only so no stray reference 404s).
**2026-08-26 housekeeping pass**: showroom-owner shipped a third tool, The Forecaster
(`/tools/forecaster`, AlgFormer/HoloFormer on a real hourly AAPL tape) — found it live in
`Showroom/Home.razor` but MISSING from `index.html`'s `#tools` gallery and `sitemap.xml`; added
both (card mirrors Analyst's shape with a `.ver` package-tag pill, `--c-ml` accent matching
Creature/AlgFormer's category colour) and re-ran the reachability walk (still 1 click from Home).
The earlier flag about `Home.razor` tagging Creature `soon` vs the site's `live` was checked by
showroom-owner this pass and is stale/not reproduced (both tools show `live` there).

**2026-08-28 pass**: a fourth tool, "The Oracle" (checkpoint REPL + live per-pass Inspector), was
built in `Showroom/` but never linked from this repo — same gap as the Forecaster's. Checked
`Showroom/Pages/` mid-task and caught a concurrent rename landing live (Oracle.razor →
`Prism.razor`, route `/oracle`→`/prism`, `MainLayout.razor` nav label/href and `Home.razor`'s card
both updated in the other repo) — re-verified against the actual files before finishing rather than
shipping the pre-rename name/route. Added: (1) a `Prism` card to `index.html`'s `#tools` gallery
(mirrors the other three, `--c-ml` accent) (2) `/tools/prism` to `sitemap.xml` (3) on
`algformer.html` — the natural home, since Prism/Creature/Forecaster are all direct AlgFormer/
HoloFormer demos and NONE were linked from that page before this pass — a `.btn-ghost` hero CTA
("Talk to a trained checkpoint, live →") plus a new "Try it live" section (`.grid` of all three
`.card.tool`s, same component as `index.html`'s gallery) inserted right after the hero, before "The
problem it solves". Reachability walk: Home → `#tools` → Prism = 1 click; `algformer.html` → hero
CTA or "Try it live" card = 1 click. No nav items added (lean nav unchanged on every page), so
mobile compactness is unaffected structurally. `holoformer.html` (the deep-dive page) was left
without its own tool link — it's one crumb-click from `algformer.html`, which now carries the link,
so it stays within budget without adding a fourth divergent cross-link spot.

**2026-08-28 pass (nav de-arbitrary-ing, folded into the OS-chrome pass below)**: reachability walk
re-run after swapping the pinned `HoloDb` nav slot for `Tools`, on the 3 pages that carry the new nav
shape (`index.html`, `phasor.html`, `holodb/index.html`). From `phasor.html`'s nav: `Home`(1)→
`#packages`→any of 11 cards(1) = 2 clicks to every package incl. HoloDb; `Tools`(1)→`/#tools`→any of
4 tool cards(1) = 2 clicks to every tool; `NuGet`(1) = 1 click, external. HoloDb is additionally 1
click via `phasor.html`'s own `.related` pill (unchanged, still lists HoloDb). From `index.html`
itself: `Packages`/`Tools` land on the same page (no click needed, already visible) → 1 click to any
card/tool. From `holodb/index.html`'s nav (6 items, at the compactness ceiling): same `Home`/
`Packages`/`Tools` ≤2-click guarantee, plus its own page-specific `Benchmarks`(same-page anchor,
0 extra click)/`Docs`(1 click). No href was removed anywhere — HoloDb lost its one guaranteed
1-click universal slot but gained nothing-lost in the ≤2-click invariant, which was never depending
on that slot in the first place (`Packages` already covered it). Checked narrow-viewport
compactness: the new nav is 4 items on 15 of 17 pages (down from 4) and 6 on the HoloDb hub (same
count as before, just reordered) — never above the ~6-item ceiling.

**2026-08-28 pass (tools-first pivot — implementing `docs/brand-identity.md`, all 3 flagged decisions
confirmed by the user beforehand: `--c-ml`→Indigo, orphan packages stay unannotated, `Tools` nav item
dropped)**: `index.html` restructured to tools-only top-level content (nav → hero → `#tools` grid,
each card retinted to a real "chord" — Analyst `var(--c-data)` unchanged, Creature a new two-tone
`--c-ml`/`--c-spatial` gradient with a `.powered` pill row to AlgFormer+Tracer, Forecaster/Prism
`var(--c-ml)` unchanged in markup but retinted via the token move → new slim `.pkg-strip` package-chip
row → footer). The old `#packages` gallery + closing `.stack`/`.flow` moved near-verbatim to a NEW
page, `packages.html` (own title/description/canonical/OG/`CollectionPage` JSON-LD, added to
`sitemap.xml`), on the plain (non-chrome) template. `site.css`: `--c-foundation` is now the literal
`--spectrum` gradient (+ `--c-foundation-solid:#fff` companion), `--c-ml` moved off-spectrum-pink
`#e879c8` → Indigo `--spectrum-6` `#7d7dff`; added `--cat-root` (solid-colour fallback for the few
call sites `--cat` holding a gradient breaks — fixed the one real one, the `os-chrome` mobile
icon-tile rule's 3 `color-mix()`/gradient-stop `var(--cat,...)` references, all repointed through
`var(--cat-root,var(--cat,var(--accent)))`); added `.card.tool`/`.powered`/`.pkg-strip` components
(see Design system above). Sitewide mechanical fix bundled in the SAME pass (not the paused
`os-chrome` sweep — a plain href swap, no layout/chrome touched): every `/#packages` href on the
other 14 pages (nav, crumb, `.related-all`, footer, CTA) repointed to `/packages.html`, since leaving
them pointing at a same-page anchor that no longer exists on `index.html` would have been a real
reachability regression (nav "Packages" landing on a homepage with nothing to scroll to), not just a
cohesion nit — this was necessary for THIS pass to not break the ≤2-click invariant, not scope creep.
`algformer.html`'s own Creature mirror card (in its "Try it live" gallery) was also retinted to the
same two-tone gradient for the same reason: leaving one instance of "the Creature card" flat and
another two-tone in the same pass would have been a new, avoidable inconsistency.
Reachability walk re-run on the real new markup: from `index.html`, every tool is 0 clicks (visible)
→1 click (card-link); every package is 1 click via `.pkg-strip` chip OR 2 clicks via `Packages`→card
(both paths exist, both within budget). From `packages.html`: `Home`(1)→any tool(0-1) or any other
package(1 more via `Packages`, self, already there). From a deep page (e.g. `phasor.html`):
`Home`(1)→`index.html`(any tool 0-1 click more) = ≤2; `Packages`(1)→`packages.html`→any card(1) = 2.
From the HoloDb hub (now 5 nav items, down from 6): same ≤2-click guarantee, one item lighter.
Narrow-viewport compactness improved or held, never regressed — checked per page, not assumed
uniform: `index.html`/`phasor.html`/`packages.html` now carry the leanest 3-item `Home · Packages ·
NuGet` nav (was 4 on the first two, `Tools` dropped); `holodb/index.html` is 5 items (was 6, same
drop); the OTHER 10 plain product pages (`algformer.html`, `algformer-gpu.html`, `evalapp.html`,
`evalapp-neural.html`, `holodb-client.html`, `holodb-protocol.html`, `holoformer.html`,
`holovoxel.html`, `prose.html`, `tracer.html`) never carried `Tools` in the first place and are
UNCHANGED at 4 items (`Home · HoloDb · Packages · NuGet` — the older HoloDb-pinned shape, only the
`Packages` href value was fixed, not the item count/order — that shape-level fix is still the paused
`os-chrome`/nav-sweep, out of scope for this pass); `holodb.html` stays 5 items, `holodb/manual/
index.html` stays 6 — both also just an href fix, not a shape change. No href was removed from the
site; `#tools`'s `id` still exists for deep-linking despite losing its nav entry on the 3 pages that
had it. Tag/brace balance
**RESOLVED 2026-08-28, later same day**: the "OTHER 10 plain product pages… UNCHANGED at 4 items"
gap above is now closed — swept the same `<div class="nav-links">` fix onto all 10 (`algformer.html`,
`algformer-gpu.html`, `evalapp.html`, `evalapp-neural.html`, `holodb-client.html`,
`holodb-protocol.html`, `holoformer.html`, `holovoxel.html`, `prose.html`, `tracer.html`): dropped the
arbitrary pinned `<a href="/holodb/">HoloDb</a>` line, matching the exact 3-item `Home · Packages ·
NuGet` shape the 3 already-fixed pages carry. `holodb.html` (5 items) and `holodb/manual/index.html`
(6 items) were correctly left untouched — their `HoloDb` nav link points back to their own hub, a
legitimate family cross-link, not the arbitrary sitewide pin this sweep targeted. Reachability walk
re-confirmed: HoloDb is unaffected (still 2 clicks via `Home→Packages→card` from any of the 10, still
1 click via each page's own untouched `.related` pill, which was already curated per page and still
lists HoloDb where relevant). Narrow-viewport nav is now 3 items on all 15 non-HoloDb-family pages
(down from 4), matching `index.html`/`phasor.html`/`packages.html` exactly — the whole site finally
carries ONE nav shape outside the 3 legitimately-different HoloDb-family pages, closing the last
"arbitrary pin" cohesion gap this doc had been carrying open since the pivot.
verified on every file touched (16 HTML files + `site.css`) via a scripted open/close-tag count, not
just read-back — no live browser available, so this is a structural check, described as that, not a
rendered screenshot. **Deviation from `docs/brand-identity.md`, and why**: the doc's own §4 only
explicitly scoped the `/#packages`→`/packages.html` href fix as "simpler, not harder" going forward,
not as a mandatory same-pass sitewide edit — did it anyway (see reasoning above) because leaving it
undone would have shipped a broken invariant on `index.html`'s own first day live, which is worse
than the minor scope stretch of touching 14 files' hrefs (not their layout/nav-item-count/chrome).

Desktop/wide viewports get a window-panel treatment (bordered "window" frame around `.hero` and
every `.sec` that carries a `.sec-head`, a titlebar with decorative traffic-light dots, the nav
restyled as a floating taskbar/dock); narrow viewports get a phone-home-screen treatment (nav
collapses to a fixed bottom icon dock, the `#packages`/`#tools` galleries become an app-icon grid).
**Chrome only** — no JS, no draggable/resizable windows, static HTML/CSS unchanged in cost; every
element it touches is real, already-linked markup (nav `<a>`, `.card-link`, `a.tool`) — narrowing a
mobile package card to an icon hides its description text, never its link.

**Opt-in via `<body class="os-chrome">`.** **SWEPT SITE-WIDE 2026-08-28** (direct user instruction:
"Homepage looks great, parallax plus themed colours, proceed to apply this to all pages including
the showcase pages"). Was on exactly 3 pages (`index.html`, `phasor.html`, `holodb/index.html`,
kept as the reference implementation this sweep read from, unchanged); now on **all 16 routable
static pages**: those 3, plus `algformer.html`, `algformer-gpu.html`, `evalapp.html`,
`evalapp-neural.html`, `holodb-client.html`, `holodb-protocol.html`, `holoformer.html`,
`holovoxel.html`, `prose.html`, `tracer.html`, `packages.html`, `holodb.html`, and
`holodb/manual/index.html`. Only `404.html` and the deliberately-unlisted `recycledao-preview.html`
are excluded — neither ever carried the standard nav/hero template this system hooks into, so
neither is a gap. Two page shapes needed two different treatments:
- **10 "hero-template" pages** (`algformer.html`, `algformer-gpu.html`, `evalapp.html`,
  `evalapp-neural.html`, `holodb-client.html`, `holodb-protocol.html`, `holoformer.html`,
  `holovoxel.html`, `prose.html`, `tracer.html`) plus `packages.html` (11 total) got the FULL
  recipe below: `class="os-chrome"` on `<body>`, hero content re-wrapped in
  `.hero-bar`/`.hero-body` (`algformer.html` additionally got `.hero-content` per the pre-existing
  gotcha, since it carries `.prism-beam` — moved the beam from a `.wrap` sibling to the first child
  of `.hero-body`, matching `index.html`'s exact shape). `.sec`/`.sec-head` window framing and the
  taskbar/dock needed no further markup, per the zero-markup design.
- **2 "prose-template" pages** (`holodb.html`, `holodb/manual/index.html`) use `<main class="wrap">
  <article class="prose">` — no `<header class="hero">`, no `.sec`/`.sec-head` at all — so the
  window-panel framing rule (`.sec:has(.sec-head)`) and the hero-bar mechanism have literally nothing
  to hook into on these two; only `class="os-chrome"` was added to `<body>`, which activates the
  wallpaper glow and the taskbar/dock nav restyle and nothing else. This is the correct, minimal
  application of the SAME opt-in flag to a structurally different template, not a partial sweep.
- **One genuine design consequence, not a bug, flagged for a real-device look**: `packages.html`
  wraps all 4 category `.grid`s (Foundation/Data/ML/Spatial) inside ONE `<section class="sec"
  id="packages">`, unlike `holodb/index.html`'s many separate single-`.sec-head` sections. Because
  `.sec:has(.sec-head)` matches on ANY descendant `.sec-head`, this renders as ONE large glass panel
  spanning all 11 package cards (with a horizontal divider band at each category's `.sec-head`, only
  the first — Foundation — getting the traffic-dot/rounded-top treatment `:first-child` triggers) —
  a "package browser in one window" read, not four stacked windows. This is what the existing
  zero-markup mechanism produces by construction; not treated as a defect, but worth an explicit look
  since it's a different composition than every other swept page.
- **`holoformer.html`'s single big `<main class="sec">`** similarly wraps its whole concept-card
  article in one `.sec`, with exactly one `.sec-head` roughly two-thirds of the way down (the
  "Why it isn't just a small transformer" comparison) — same mechanism, so the ENTIRE article
  becomes one glass window, and that one `.sec-head` reads as a mid-page divider band (not a
  titlebar, since it isn't `:first-child`) rather than getting traffic dots. Its `.wrap`'s own
  inline `style="max-width:900px"` still wins over the panel's `max-width:none` override (inline
  style beats an external stylesheet rule regardless of specificity), so the prose column width is
  unaffected — only the padding/background/border/blur of the glass treatment newly apply. Flagged
  as unverified-without-a-browser, not assumed broken.

**Components, all in `site.css`** under the "OS CHROME" banner comment (search that string to find
the whole system in one place):
- **`.hero-bar` + `.hero-body`**: the hero splits into a titlebar (`.hero-bar`, desktop-only, hidden
  by default so tablet/mobile renders `.hero-body`'s children exactly like the old flat hero) and a
  body wrapper. `.hero-bar` carries `.win-dots` (3 real `<i>` elements, red/amber/green via the
  existing `--bad`/`--warn`/`--ok` tokens — reused, not new colours) + a `.hero-bar-title` mono label
  (`"phasor.app"` style). The whole `.hero-bar` is `aria-hidden="true"` — it's a decorative echo of
  info already in the real `.crumb`/`.eyebrow`/`h1`, same pattern as `.prism-beam`.
- **`.sec` window framing is ZERO-MARKUP**: `body.os-chrome .sec:has(.sec-head)` gets a bordered
  panel; `.sec-head` itself grows a `::before`/box-shadow trio of 3 dots (no new elements — CSS
  generated content with `content:""` carries no accessible text, so nothing needs `aria-hidden`
  here). Because this hooks off `.sec`/`.sec-head`, which every page already has identically, **this
  part of the chrome needs no HTML changes at all once a page's `<body>` carries the class** — the
  sweep to the remaining 14 pages is `class="os-chrome"` + the hero restructure below, nothing more.
  Sections that are just a lone `.stack` callout (no `.sec-head`, e.g. every page's closing "get
  started"/"how it fits together" CTA) are deliberately left unframed — `.stack` is already its own
  bordered card; a second frame around it would double-border, not read as a widget.
- **Taskbar/dock is `.site-nav` restyled**, not a new element: ≥901px it floats as a rounded,
  blurred chrome bar off the top edge with each `.nav-links a` rendered as a small app-tile pill
  (colour cycled through `--spectrum-1..7` by `nth-child`, shared between the desktop pill size and
  the mobile dock size so they read as one component in two sizes). ≤640px the same `.nav-links`
  becomes a `position:fixed` bottom icon dock (burger/checkbox hidden, `env(safe-area-inset-bottom)`
  padding) instead of the plain site's CSS-only slide-down menu — this is the "phone home screen"
  status-bar-and-dock read, using the exact same links, same hrefs, same count as everywhere else.
- **`#packages`/`#tools` icon grid (≤640px only)**: the real `.card`/`a.tool` elements collapse to
  compact tiles — `.card::before` (normally the 3px category accent bar) becomes a 56px rounded-
  square "icon" tinted by the existing `--cat` category colour, `.desc`/`.install`/`.links`/`.pkgid`/
  `.note`/`.tag`/`.go-in` are hidden (not removed — still real DOM, just not shown at this size), and
  the pre-existing `.card-link` stretched-link overlay (or the tool card's own `<a>`) still covers
  the whole tile, so every icon is still a real, focusable, hrefed link. A `:active` tap-scale is
  gated inside `@media (prefers-reduced-motion:no-preference)`; reduced-motion gets no transform at
  all (nothing to opt out of, satisfies the requirement by construction rather than by exception).
  Scoped to `#packages`/`#tools` specifically — other `.grid`s (e.g. phasor's "Key features" cards)
  are NOT navigation entry points and correctly stay plain reading cards on mobile, not icons.
- **`body.os-chrome` wallpaper**: two faint `radial-gradient`s (accent + data-blue, ~7-9% mixed in)
  over the base `--bg`, at every viewport width — cheap (no image asset), and reads as "windows/
  icons floating over a desktop" in the gaps between panels/tiles.
- **Breakpoints reused, not invented**: `min-width:901px` for all window/taskbar chrome (matches the
  existing `.prism-beam` mobile-hide breakpoint), `max-width:640px` for the dock/icon-grid (matches
  the existing mobile-burger breakpoint). 641-900px (tablet) is a deliberate no-op fallback — plain
  base layout, already responsive, already tested — rather than a third half-tuned chrome variant.

**`.prism-beam` z-index gotcha (only matters on pages that carry both `.prism-beam` and
`os-chrome` — as of 2026-09-01 that's all 5 pages carrying the beam: `index.html`, `algformer.html`,
`phasor.html`, `holoformer.html`, `holovoxel.html`, all of which are on `os-chrome`)**: once
`.hero > .wrap` gets an opaque window-panel background, a `.prism-beam` positioned as a *sibling* of
`.wrap` (the pre-chrome markup) would sit fully behind that new opaque panel and vanish. Fixed by
moving `.prism-beam` to be the first child *inside* `.hero-body` instead, with the real hero text
wrapped in one more div, `.hero-content` (`position:relative;z-index:1`), so the beam
(`position:absolute;z-index:0`, unchanged CSS) paints behind the text but on top of the now-opaque
panel background — and its bleed (`right:-40px`) gets tastefully clipped by the panel's
`overflow:hidden` instead of hanging off the page. **This is the one wrapper the "zero-markup"
os-chrome claim above doesn't hold for**: any page that ever gains `.prism-beam` in the future needs
its real hero content wrapped in `.hero-content` too if (and only if) that page is also on
`os-chrome` — `phasor.html`/`holoformer.html`/`holovoxel.html` didn't carry this wrapper before
2026-09-01 (their hero content sat directly in `.hero-body`, same shape `algformer.html` used to have
pre-chrome) and got it added in the same edit that added their beam markup, per this exact gotcha.

**Verified this pass**: reachability walk re-run (see Navigation section above) — unaffected by
chrome, since no href changed, only presentation + a wrapping-div restructure of the hero. Read all
3 files back after editing to confirm well-formed nesting (hero-bar/hero-body/hero-content close
correctly, no orphaned tags). Did NOT verify in an actual browser (no visual proof beyond reading the
generated HTML/CSS) — the coordinator/user should eyeball the 3 pages at a real ≥901px and a real
≤640px width before this is swept further; described here as the shape built, not a screenshot-
verified render.

**Real-device review pass (2026-08-28, second round) — 3 findings, all fixed in `site.css` only
(no HTML changes except index.html's `data-initial` attributes below)**:

1. **"Lost dark side of the moon" (user's live-review flag)**. Diagnosed by reading the actual
   CSS, not guessed: the culprit was NOT the panels being light-toned (`--surface`/`--surface-2` are
   still near-black, `#0d0f16`/`#12151d`) — it was (a) EVERY window panel (`.hero > .wrap`,
   `.sec:has(.sec-head)`) painting a flat OPAQUE fill, turning ~90% of the page's visible area from
   "void black with floating content" into wall-to-wall bordered graphite-grey boxes — the void that
   made the beam/spectrum dramatic was mostly gone, replaced by generic bordered-card chrome; and
   (b) the new `body.os-chrome` wallpaper glow used only 2 stops, both cool (accent-violet + data-
   blue) — textbook "purple-blue SaaS hero gradient," the most generic possible dark-dashboard
   cliché, and specifically NOT the brand's full ROYGBIV since it never showed the warm half. Fixed
   by (a) making `.hero > .wrap` and `.sec:has(.sec-head)` translucent + `backdrop-filter:blur(...)`
   ("glass" rather than opaque — the void + wallpaper now bleed through behind content, unifying with
   the taskbar's pre-existing glass treatment instead of fighting it) and giving each window a 2px
   `::before` top edge painted with the literal `--spectrum` gradient (the same one the page-top
   `.beam` uses) so every window reads as cut from the same prism, not just grey-bordered; (b) adding
   a third wallpaper stop using `--spectrum-1` (warm coral) low-centre, so the ambient glow spans
   warm-to-cool instead of cool-only. Traffic-light dots (`.win-dots`, `.sec-head`'s `::before` trio)
   were checked and left alone — `--bad`/`--warn`/`--ok` already equal or nearly equal
   `--spectrum-1`/`-3`/`-4` (the prism-motif pass deliberately reused those slots), so they're already
   brand-spectrum colours, not a generic macOS red/amber/green; the "3 dots" affordance itself is the
   intended OS-chrome window-control idiom, not a bug.
2. **Mobile icon tiles were blank colour with zero glyph** (user's flag, confirmed live on-phone by
   coordinator screenshot: "just a coloured square", true for all 15 tiles — 4 tools + 11 packages).
   `.card::before`'s `content:""` was replaced with `content:attr(data-initial)` inside the existing
   ≤640px icon-grid block, plus flex-centring + bold-letter styling, so each tile now shows a 1-2
   letter mark (`Ph` Phasor, `EA` EvalApp, `Db`/`Cl`/`Pt` the HoloDb family, `Af`/`Gp` AlgFormer(.Gpu),
   `En` EvalApp.Neural, `Ps` Prose, `Tr` Tracer, `Hv` HoloVoxel, `An`/`Cr`/`Fc`/`Pm` the 4 tools) — no
   new DOM element, no image/font asset, just an attribute + `attr()` in `content`. **Gotcha for any
   future card added to `#packages`/`#tools`**: it needs its own `data-initial="Xx"` on the
   `<article class="card" ...>` / `<a class="card tool" ...>` opening tag or its mobile tile silently
   reverts to a blank tinted square (graceful, not broken, but loses the fix) — the sweep recipe below
   should carry this forward once `#packages`/`#tools` markup exists on more pages (today only
   `index.html` has those ids, so this is the only file needing the attribute).
3. **Real functional bug (not cosmetic): the mobile bottom icon dock was broken**, found from a
   phone screenshot (only a lone "NuGet" tile visible, mispositioned near the top, overlapping the
   brand text) — root-caused in the CSS, not guessed: `.site-nav` carries `backdrop-filter:blur(10px)`
   UNCONDITIONALLY (the base sticky-nav rule, active on all 17 pages at all widths). Per the CSS spec,
   `backdrop-filter` (like `transform`/`filter`/`perspective`/`contain`) on an ancestor establishes a
   NEW containing block for any `position:fixed` descendant. `.nav-links` (the dock) is fixed at
   ≤640px, so it was pinning to the bottom edge of `.site-nav`'s own ~52px box — which sits at the TOP
   of the page — instead of the viewport, collapsing the "bottom dock" into a sliver overlapping the
   top bar, with only the rightmost item (NuGet, clear of the brand text) reading as legible. Fixed by
   `body.os-chrome .site-nav{backdrop-filter:none}` inside the existing ≤640px block — the dock keeps
   its own independent `backdrop-filter:blur(14px)`, so the frosted look is unaffected; only the
   (mostly-invisible-at-52px) top-bar blur is dropped on mobile. **This bug is real on ALL 3 os-chrome
   pages** (the rule is in the shared `.site-nav`/dock CSS, not page-specific) and would have shipped
   identically to any of the other 14 pages the moment they're swept onto `os-chrome` — worth an extra
   look when that sweep happens, though the fix already lives in the shared stylesheet so no further
   action is needed per-page.

Still NOT verified in an actual browser by this agent (no visual proof beyond reading the generated
CSS/HTML and reasoning from the CSS containing-block spec for #3) — the reasoning for all 3 fixes is
recorded inline in `site.css` next to each change; the coordinator/user should re-check on a real
phone (the same device/screenshot that caught #2/#3) before this is considered closed.

**Real-device review pass (2026-08-28, third round) — mobile nav layout bug + icon redesign, both
in `site.css` only, no HTML changes on any of the 3 pages**:

4. **Real layout bug: the mobile dock rendered as a big vertical list (4 full-width stacked rows,
   icon-over-label) instead of the compact fixed-bottom row.** Root-caused by reading the cascade,
   not guessed: the legacy CSS-only mobile-menu block (base `@media (max-width:640px){ .nav-links{...}
   }`, used by the 14 plain non-chrome pages, unscoped) still matched on the 3 os-chrome pages too.
   The os-chrome dock rule (`body.os-chrome .site-nav .nav-links`, specificity 0,3,1) correctly won
   the fight for `display`/`position` against the legacy rule's 0,1,0 — so the dock genuinely was
   `position:fixed` — but the dock rule never re-declared `flex-direction`/`flex-basis`, so those TWO
   properties fell through to the legacy, lower-specificity rule's `flex-direction:column;
   flex-basis:100%`, turning the dock's 4 tiles (each already icon-over-label internally) into one
   more level of stacked full-width rows. **Fixed two ways**: (a) scoped the entire legacy block to
   `body:not(.os-chrome)` so it can never apply on chrome pages regardless of which properties it
   sets (the structural fix — exclude the two rule sets' SCOPES from each other, not a per-property
   specificity race); (b) hardened the dock rule itself with explicit `flex-direction:row;
   flex-basis:auto` as defense in depth. Also added `-webkit-backdrop-filter` alongside every
   `backdrop-filter` declaration that touches `.site-nav` (base, desktop taskbar, mobile-clear) —
   unprefixed `backdrop-filter` is supported in modern Safari, but the previous mobile fix
   (`backdrop-filter:none`) only cleared the unprefixed property, leaving a `-webkit-backdrop-filter`
   gap if the base rule ever gained the prefix (it now has, so the mobile override needed pairing).
   Same class of bug as the earlier containing-block fix — same lesson: two rule sets meant to be
   mutually exclusive must exclude each other's scope outright, verified by reading every
   `@media (max-width:...)` block touching `.nav-links`/`.site-nav` in source order, not by reading
   one rule in isolation.
5. **Design feedback (user, direct quote): "I don't like these buttons, the colour is not good it
   should be prism dark side of the moon theme, not pastels."** The nav icon marks (`.nav-links
   a::before`, shared by both the desktop taskbar pills and the mobile dock) were filled two-stop
   ROYGBIV gradient rounded-squares — brand hues, but rendered as solid soft blobs that read as a
   generic iOS app-icon grid, not the sharp geometric prism/thin-beam language the rest of the site
   uses (`.prism-beam` is thin coloured LINES fanning off a triangle against void black, never a
   filled shape). Redesigned: a thin-bordered (1.5px) DIAMOND (`transform:rotate(45deg)` on a
   3px-radius square — a facet, echoing the brand triangle mark rather than a stock rounded-square
   icon), void-dark (`var(--surface)` fill, `var(--border-2)` outline) and essentially invisible at
   rest against the black chrome — each link's own text label already carries the meaning — that only
   shows a spectrum hue as a thin edge + soft glow (`box-shadow`) on `:hover`/`:focus-visible`/
   `.active`. Each item still cycles one spectrum stop via `nth-child` (unchanged assignment order,
   still covers up to 6 items for the HoloDb hub nav), now stored as a `--tile` custom property
   consumed only by the interactive-state rule, never as a permanent fill. Applies identically to
   both the desktop pill icons (≥901px, 14px→11px, sharpened radius 5px→2.5px) and the mobile dock
   icons (≤640px, 22px→17px, radius 7px→4px) since both breakpoints only override size/radius on the
   one shared base rule — verified by reading both breakpoint-specific blocks after the change, not
   assumed from touching just one.

Verified via brace-count parity on the whole `site.css` (198 open / 198 close) and by reading the
full nav-related CSS back after editing — this agent still has no live browser, so this is a cascade
read + structural check, not a rendered screenshot. All 3 os-chrome pages (`index.html`, `phasor.html`,
`holodb/index.html`) share byte-identical nav markup (`<input class="nav-toggle">` /
`<label class="nav-burger">` / `<div class="nav-links">`, no page-local `<style>` override of any of
these classes on any of the 3), so the fix in `site.css` alone reaches all 3 without per-page edits —
confirmed by grepping each page's nav markup and `holodb/index.html`'s local `<style>` block. The
HoloDb hub's 6-item nav (`Home/Packages/Tools/Benchmarks/Docs/NuGet`) was specifically checked against
the `nth-child(1)`..`nth-child(6)` icon-colour assignments — no gap. Did NOT touch: the light-palette
removal, glass window panels, or the `#packages`/`#tools` mobile icon grid (`.card::before`,
`data-initial`) — all confirmed working per the screenshot and out of this pass's scope. The
coordinator/user should re-check on the same real phone before this is considered closed.

**Real-device review pass (2026-08-28, fourth round) — `#tools` mobile redesign + a re-trace of the
Creature icon-tile bug, `site.css` only (no HTML changes, `index.html` untouched)**:

6. **User's own words, real phone screenshot of `#tools`**: "Looks like garbage, bigger space for
   each app with explanation under not buttons, it's should funnel people in." The icon-grid
   treatment `#tools` shared with `#packages` (56px tile + one-word label, `.desc`/`.powered`/`.tag`/
   `.ver`/`.install` all hidden) was the wrong shape for a section whose whole job is convincing a
   visitor to tap into a live tool. **Fixed by pulling `#tools` out of the shared icon-grid selector
   set entirely** and giving it its own mobile block: single-column `.grid` (`grid-template-columns:
   1fr`), the real desktop `.card` un-shrunk (name, `live` tag, package chip, full `.desc`, the
   "Powered by" `.powered` pill row, and `.go-in` all stay visible — nothing new hidden), `.go-in`
   additionally styled as a bordered pill so it reads as a clear CTA. `#packages`'s icon grid is
   UNCHANGED (still the app-icon pattern) — checked, not assumed, that this was the right split: the
   user's screenshot and complaint were `#tools`-specific, and `#packages` is a reference index (11
   items), not something being pitched for a visitor to "try" the way a tool is.
7. **Traced, not fixed further (real bug status: inconclusive from static reading)**: the Creature
   tool tile showed bare "Cr" text with NO coloured tile/background behind it (screenshot), while
   Analyst/Forecaster/Prism's tiles rendered correctly. Traced every rule painting the mobile icon
   tile (`body.os-chrome #packages .grid > .card::before`, the only one — `#tools` no longer shares
   it, see finding 6): all 3 `var(--cat,...)` references were ALREADY routed through
   `var(--cat-root,var(--cat,var(--accent)))` from an earlier pass this session, and `index.html`'s
   Creature card already sets `--cat-root:var(--c-ml)` explicitly alongside its two-tone gradient
   `--cat` — the fallback chain resolves to a real solid colour on paper, parens/braces balanced,
   no bare `var(--cat,...)` call site found anywhere else in `site.css` (grepped exhaustively). Could
   NOT reproduce a remaining syntax defect by reading the CSS alone (no live browser here) — did not
   guess a further patch on an unconfirmed cause. **Resolved the actual exposure by construction
   instead**: since `#tools` (the only page this pattern was live on) is now off the icon-grid
   pattern entirely per finding 6, there is zero `color-mix()`/gradient-math left running on any
   `#tools` mobile rule — only a plain `.card::before` 3px accent bar, `background:var(--cat,
   var(--accent))`, which takes a gradient natively as a background-image (no math applied, already
   noted safe in an earlier pass's comment). The bug can no longer manifest for tools regardless of
   root cause. `#packages`'s copy of this same mechanism is presently DORMANT (packages.html isn't
   on `os-chrome` yet, per the Site map above) — kept correct for whenever it joins, flagged here so
   the two `--cat-root`-bearing package cards (Phasor, EvalApp) get a real-device check first when
   that happens, not assumed fine from this pass.
8. **Real, fixed bug (coordinator-flagged mid-task, verified before landing)**: the mobile icon-tile
   `::before` (`#packages`, now) never set its own `opacity`, so it silently inherited `opacity:.85`
   from the unrelated desktop 3px accent-bar rule (`.card::before` near the top of the file, tuned
   for a thin bar, never meant for a 56px filled tile) — compounded with the tile's own gradient
   fading its second stop to a 45% `color-mix()`, genuinely saturated brand colours (`--c-data
   #4aa3ff`, `--c-ml #7d7dff`) read pastel/washed-out. Fixed with an explicit `opacity:1` on the
   rule. Only reachable on `#packages` today (dormant, same caveat as finding 7) since `#tools` no
   longer uses this rule at all.
9. **Bug NOT reproduced from current code, flagged as likely already-covered**: the user's screenshot
   also showed "Powered by HoloDb"/"Powered by AlgFormerTracer" pill rows overlapping across card
   boundaries. Root-caused the MECHANISM (not just this instance): the icon-grid `.card` sets
   `overflow:visible` on a 76px-wide flex column, so anything with `.powered`'s normal flex-wrap
   sizing (built for a 330px+ desktop card) has nowhere to wrap and bleeds into the neighbouring grid
   cell — but reading the CURRENT file, `.powered` was already in `#tools`'s icon-grid hide-list
   (`display:none`) before this pass started, so this exact overlap should NOT have been reproducible
   from the code as found; either the screenshot predates that hide-list landing, or it's a real gap
   this agent didn't independently locate. Moot either way after finding 6: `#tools` no longer takes
   the `overflow:visible`-narrow-column layout branch at all, so `.powered` can only wrap inside its
   own full-width card now, never escape it, regardless of which explanation is true.

Verified via brace/paren-count parity on the whole `site.css` (212 open / 212 close braces, 430/430
parens) and by reading the edited block back in full. Still no live browser — every claim above is a
static CSS trace, described as that. The coordinator/user should re-check `#tools` on the same real
phone before this is considered closed, specifically: the Creature tile (finding 7, unconfirmed root
cause even though the exposure is now closed by construction) and the new single-column product-card
`#tools` layout (finding 6) actually reading as "bigger, funnel-y" rather than just "taller."

**Sweep recipe (COMPLETED 2026-08-28 — kept here as the recipe for any FUTURE page, not a TODO
anymore)**: add `class="os-chrome"` to `<body>`; nav-links already read the current 3-item
`Home · Packages · NuGet` shape sitewide (the `Tools` slot this recipe used to add was dropped
sitewide in the 2026-08-28 tools-first pivot — see Navigation above, don't reintroduce it here),
keep any page-specific extra items after `NuGet`; wrap the hero's real content in `<div
class="hero-bar" aria-hidden="true"><span class="win-dots">...</span><span
class="hero-bar-title">NAME.app</span></div><div class="hero-body">...</div>`; if the page also
carries `.prism-beam` (as of 2026-09-01: `index.html`, `algformer.html`, `phasor.html`,
`holoformer.html`, `holovoxel.html` — see the Prism motif section above for the full list and why),
additionally wrap the real text in
`.hero-content` per the gotcha above. If the page has no `<header class="hero">` at all (the two
`.prose`-template pages), skip the hero-bar/hero-content step entirely — just the `class` on
`<body>`. Everything else (`.sec` window framing, taskbar styling) needs no further HTML changes —
it's already live in `site.css` and activates the moment `os-chrome` is on the page. This recipe was
applied to all 13 remaining pages in the 2026-08-28 sweep (see "Opt-in via `<body
class="os-chrome">`" above for the full before/after and the two page-shape variants) — the next
time a NEW page is added to the site, follow this same recipe from the start rather than shipping
it pre-chrome and sweeping it later.

### Chord glow tinting — the consolidated multi-domain colour mechanism (2026-08-28)

Direct user instruction, twofold: (1) "ensure colour theming and things that use multiple packages
have chorded two tone or tri tone theming" — extend the existing single-hue page glow to a genuine
multi-tone glow on pages whose real subject spans more than one domain; (2) a follow-up, "It needs
to consolidate the theming so it's easy to update all parts with CSS or shared components etc" —
make sure this and every other colour concern resolves through ONE token chain, not per-page
hand-copies, so a future colour change is a one-token edit, not a site-wide hunt (this is exactly
the failure mode a concurrent `--c-ml` retune that same session exposed — see "The `--c-ml`
cross-check" below).

**Single source of truth per concern (read this before touching any colour on this site)**:
- **The domain hue itself**: `--c-foundation` / `--c-data` / `--c-ml` / `--c-spatial` in `:root`,
  `site.css`. Nothing else should ever hardcode one of these hex values — every call site (card
  `--cat`, `.sec-head .dot`, chip swatches, the glow tokens below) reads through `var(--c-*)`, so a
  hue retune (like the `--c-ml` Indigo→Violet correction, below) reaches every consumer for free.
- **A single package/tool card's accent**: the `--cat` custom property, set inline per `.card`/
  `.card.tool` (`style="--cat:var(--c-ml)"` etc.), consumed by `.card::before`'s accent bar and the
  mobile icon tile. Unchanged by this pass.
- **A card that represents MORE than one domain** (a tool spanning packages, e.g. the Creature —
  AlgFormer/ML + Tracer/Spatial): `--cat` holds a hard-edged 2-stop `linear-gradient()` of the
  relevant `var(--c-*)` tokens in **canonical domain order** (the order the 4 tokens are declared in
  `:root`, i.e. the same Foundation → Data → ML → Spatial order `packages.html`'s own sections run
  in — NOT literal ROYGBIV hue position, which would put Spatial's green before Data's blue; this is
  the site's own established category order, already how the Creature card orders ML before
  Spatial), weighted per DOMAIN not per package count (two packages, one domain each, so a plain
  50/50 split), with Foundation dropped from the chord entirely (Phasor/EvalApp underlie nearly
  everything and carry no distinguishing signal — a chord of "everything is secretly Foundation too"
  would be meaningless). `--cat-root` is set alongside it to ONE solid fallback colour (the first
  domain in the chord) for the few CSS call sites (`color-mix()`, the mobile icon-tile gradient)
  that structurally cannot take a gradient as input. This formula already existed for tool cards
  (Creature, on `index.html` and `algformer.html`'s mirror) before this pass — unchanged by it.
- **A PAGE's own ambient glow** (the scroll-tied parallax drop-shadow tint): ONE attribute,
  `<body data-cat="...">`, read by a handful of `body[data-cat="..."]` rules near `:root` in
  `site.css` that set `--glow-near`/`--glow-mid` (single-domain pages: `foundation` | `data` | `ml`
  | `spatial`, four rules total, unchanged) — these two custom props are then the ONLY thing the
  `ea-parallax-near`/`ea-parallax-mid` `@keyframes` read via `color-mix(in srgb, var(--glow-near|
  --glow-mid) N%, transparent)`. A page never needs its own keyframes; changing a page's glow is
  changing one attribute value.
- **A PAGE whose real subject is genuinely multi-domain** (today: only `prose.html`, Data+ML — see
  below): the SAME `data-cat` attribute takes a compound value (`data-cat="data-ml"`), read by ONE
  additional `body[data-cat="data-ml"]{--glow-mid-a:var(--c-data); --glow-mid-b:var(--c-ml);}` rule
  (same `var(--c-*)` source tokens as the card chord above — not a second hardcoded pair), consumed
  by a second keyframes pair, `ea-parallax-mid-chord` (identical transform/timing/angle math to the
  single-tone `ea-parallax-mid`, only the `filter` carries TWO stacked `drop-shadow()` layers, one
  per domain colour), wired in via a same-specificity-plus-one override
  (`body.os-chrome[data-cat="data-ml"] .hero > .wrap, ... .sec:has(.sec-head){animation-name:
  ea-parallax-mid-chord}`) that only touches `animation-name` — every other `animation-*` property
  still cascades from the base single-tone rule, so there's exactly one place (the keyframes) that
  encodes "two drop-shadows instead of one," not N page-specific copies. Extending this to a THIRD
  chord (e.g. a hypothetical ML+Spatial page) is: one new `body[data-cat="ml-spatial"]` line setting
  `--glow-mid-a/-b`, reusing the SAME `ea-parallax-mid-chord` keyframes (already generic over
  `--glow-mid-a`/`-b`, not hardcoded to Data/ML) — only the override selector's `[data-cat="..."]`
  value needs adding, not a new keyframes block. A genuine tri-tone (3 domains) would need a third
  `--glow-mid-c` and a third stacked `drop-shadow()` in a new keyframes pair — not built since no
  page needs it today, but the pattern extends the same way.
- **Why `prose.html` and only `prose.html`**: it's the one package page whose own "Get started"
  section states a real cross-domain dependency in the owner's own words — "Depends on: HoloDb (the
  storage engine that holds mined grammar knowledge) and AlgFormer (supplies the optional
  plausibility-scoring model)" — Data + ML, not Foundation (Phasor/EvalApp are transitive under both,
  dropped per the rule above). Checked every other package/page against its own "Depends on"/
  dependency note before ruling it single-domain (AlgFormer.Gpu: ML only, GPU accel of ML;
  EvalApp.Neural: depends on EvalApp/Foundation, dropped, + AlgFormer/ML, so still single-ML since
  Foundation drops out; HoloDb.Client/Protocol: Data only; Tracer/HoloVoxel: built on Phasor/
  Foundation, dropped, so single-Spatial) — none of the other 10 package pages have a second
  non-Foundation domain, so they correctly keep the existing single-tone glow, unchanged, per the
  task's own instruction not to force multi-tone where it isn't real. `index.html`/`packages.html`
  still carry no `data-cat` at all (unchanged) — they're not about one product's domain, single or
  multi, so they correctly fall through to the `:root` default (`--accent`), same as before this pass.
- **What this buys**: retuning any `--c-*` hue, or ever adding a new chord page, is a change in ONE
  of the two places above (the `:root` token, or one `body[data-cat="..."]` line) — never a per-page
  CSS rewrite, and never two independent formulas (tool-card chord vs. page-glow chord) that could
  drift apart, since the page-glow chord literally reads the same `var(--c-*)` tokens the card chord
  reads, in the same canonical order.

**The `--c-ml` cross-check (mid-task correction, coordinator relay, user: "The colours need
redoing, foundation is white ok, data is blue machine learning is purple but it's very close to
blue, spatial is green, not a good spectrum range")**: the coordinator retuned `--c-ml` in `:root`
from Indigo `--spectrum-6` (`#7d7dff`, ~30° from Data's Blue in HSL) to Violet `--spectrum-7`
(`#c07dff`, ~61° from Data) directly in `site.css` while this task was in flight. Because every
chord/glow/card call site in this pass reads `var(--c-ml)` rather than a literal hex, that retune
propagated to this task's new work automatically with zero edits needed on my part — the `prose.html`
chord and every ML-category page's glow already resolve to the new Violet. Per the coordinator's
explicit ask, grepped `7d7dff` across the 5 ML-category pages (`algformer.html`,
`algformer-gpu.html`, `evalapp-neural.html`, `prose.html`, `holoformer.html`) specifically (not the
other 12 files, which legitimately keep `#7d7dff` forever as the eternal `--spectrum-6` stop in the
brand-mark/favicon SVG gradient — that token didn't move, only which category variable points at
it): found exactly 2 hits per file on every one of the 5, both confirmed by content to be that same
brand-mark/favicon gradient, not a hardcoded category dot/accent. No stale hardcoded ML colour found
on any page. One stale CODE COMMENT (not a live rule) in `site.css`, near the `#packages` mobile
icon-tile opacity-fix note, still read the old hex as an example value — corrected in place to note
the retune rather than silently going stale. This cross-check is itself the proof the consolidation
above works as designed: a hue change needed exactly one token edit plus a grep-confirm, not a
per-page hunt.

**Verified this pass**: `site.css` brace/paren-count parity before/after (232/232 → 237/237 braces,
573/573 → 608/608 parens — the delta is exactly the 2 new `body[data-cat]` comment+rule blocks, the
`ea-parallax-mid-chord` `@keyframes`, and the chord override rule, nothing unexpected). Every one of
the 13 newly-swept HTML files re-checked for tag balance after editing (`<div>`/`</div>`,
`<header>`/`</header>`, `<body>`/`</body>` counts, plus exactly one `hero-bar`/`hero-body`/
`os-chrome` occurrence each, and exactly one `hero-content` on `algformer.html` alongside its one
`prism-beam`) — all balanced, no orphaned tags. `prose.html`'s `data-cat="data-ml"` attribute value
confirmed present and correctly formatted. No hardcoded stale `--c-ml` hex found on the 5 ML-category
pages (see the cross-check above). **Still not verified in an actual browser** (no visual proof
beyond reading the generated HTML/CSS and reasoning from the CSS cascade/specificity rules, same
disclosed limitation as every other pass in this file) — specifically unverified: whether
`packages.html`'s single large glass panel (all 4 categories in one `.sec`) and `holoformer.html`'s
single large glass panel (one `.sec-head` two-thirds down the article) read well on a real ≥901px
screen, whether the two-tone `prose.html` glow is visually distinct/pleasant rather than muddy where
the two drop-shadows overlap, and whether the wider 3-page-tested chrome system holds up identically
on the 10 newly-swept hero-template pages (different content lengths/card counts than the 3
reference pages) at both ≥901px and ≤640px. The coordinator/user should eyeball at least
`packages.html`, `prose.html`, and one plain page (e.g. `tracer.html`) at both breakpoints before
this sweep is considered fully closed.

**Recommendation, not acted on (Showroom is out of scope for this agent)**: the same chrome system
(taskbar-style nav, window-panel framing) would read as a natural extension into `Showroom/`'s own
UI for a cohesive OS feel across the whole site+tools experience — flagged for the coordinator to
route to `showroom-owner` if wanted, not something to reach into from here.

### Real bug fix: hard-cut parallax shadow (2026-08-28, bundled into the palette pass below)

Real phone screenshot, coordinator relay: the hero panel and the homepage tool cards showed a
"HARSH, hard-edged black rectangular cut instead of a smooth shadow falloff" at the panel's bottom
edge — user's own diagnosis, "likely not in the same container," pointed at the right neighbourhood.
**Root-caused, not guess-patched**: `.hero > .wrap` and `.sec:has(.sec-head)` (the glass window
panels) both carry their own `overflow:hidden` (needed for `.prism-beam`'s bleed and the panels' own
rounded corners) AND, since the scroll-tied parallax work above, an animated tier-"mid"
`filter:drop-shadow()`. Per the CSS Filter Effects spec, an element's own `overflow:hidden` clips
that SAME element's `filter` output at its tight rectangular border-box — `box-shadow` is NOT subject
to this (spec-guaranteed, cross-browser), which is why the pre-existing STATIC elevation shadow on
these same two elements never showed the bug, only the newer animated glow did. Fixed by moving tier
"mid"'s animated glow from `filter` onto an extra `box-shadow` layer: a `--elev-shadow` custom
property (set once per element, right next to its own static box-shadow) is read by BOTH the resting
rule and every `ea-parallax-mid`/`ea-parallax-mid-chord` keyframe step via `var(--elev-shadow)`, so
the one shared keyframes pair still composes each element's own distinct static layers correctly
(hero and `.sec` have different elevation values) without duplicating them by literal value at every
keyframe step. Tier "near" (`.prism-beam`) was deliberately left on `filter` — it has no `overflow`
of its own; the clipping it experiences comes from its ANCESTOR `.hero`'s overflow, which is the
pre-existing, intentional "tastefully clipped" behaviour already documented for that beam, not the
same same-element bug. Verified: `site.css` brace/paren parity (237/237 → 242/242 braces after this
plus the palette work below, 619/619 → 637/637 parens) and a full re-read of both edited panel rules
+ both edited keyframes blocks. **Not verified on a real device** (no live browser here) — the
coordinator/user should re-check the same phone screenshot's two panels before this is closed.

### Per-package palette rollout (2026-08-28) — files touched + verification

Full hex table + reasoning lives under Design system > "Per-package palette" above; this entry is
just the sweep record. **`site.css`**: `:root` token block (8 new `--c-*` tokens replacing `--c-data`/
`--c-ml`/`--c-spatial`, `--c-foundation`/`--c-foundation-solid` unchanged), the `body[data-cat="..."]`
glow-tint rules (9 single-package rules + the renamed `"holodb-algformer"` chord rule), the chord
`animation-name` override selector, the wallpaper's decorative blue stop (`--c-data`→`--spectrum-5`,
not a package token — purely ambient, not per-page), and 3 historical comment blocks that would
otherwise have gone stale (the Creature icon-tile bug trace, the glow mechanism doc, the chord-
override doc) — left the OLD hex/token names inside dated bug-trace comments as written (accurate
history of what was actually being debugged at the time) but added a pointer to where the live tokens
now live. **13 HTML files swept**: 6 single-package pages via a straight `var(--c-X)`→`var(--c-Y)`
substitution (`holodb-protocol.html`, `algformer-gpu.html`, `evalapp-neural.html`, `tracer.html`,
`holodb-client.html`, `holovoxel.html` — each was already internally consistent, using only ONE
bucket token throughout, confirmed by a per-file grep before touching it, not assumed); 3
`data-cat`-only pages with no `--c-*` usage of their own (`holodb.html`, `holodb/manual/index.html`,
plus the attribute on `holodb/index.html`); 3 mixed-usage pages requiring line-by-line judgement
(`algformer.html` — the Creature mirror chord + the softmax/holographic comparison cards, see Design
system for the reasoning; `holoformer.html` — the "ordinary transformer" cold-contrast metaphor
repointed to neutral `--spectrum-5`; `holodb/index.html` — the DuckDB competitor bar to
`--spectrum-5`, the Analyst tool card's real miscolouring fixed to `--c-holodb`, and 6 capability-grid
cards that were cycling through all 4 old buckets purely for decorative variety, collapsed to the
one honest `--c-holodb` since none of those 6 cards describe a different package); and 3 genuinely
multi-package pages (`index.html` — Prism/Creature/Forecaster/Analyst tool cards + the `.pkg-strip`
chips, both real miscolourings fixed here too since they're mirrored from the same source as
`holodb/index.html`'s Analyst card; `packages.html` — all 11 cards + 3 family section-head dots;
`prose.html` — every card promoted from flat `--c-ml` to the genuine HoloDb+AlgFormer two-tone
hard-edged chord, not just the page's ambient glow attribute). **Verified**: a sitewide grep for
`--c-data|--c-ml|--c-spatial` after the sweep returns hits ONLY inside `site.css`'s own dated
historical-comment text (8 hits, all inside comments, none in a live rule/selector) — zero live
references anywhere in `site/**/*.html`. `div`/`article`/`section`/`body` tag-balance re-checked on
all 14 touched HTML files (all matched pre/post). `site.css` brace/paren parity 242/242 braces,
637/637 parens. **Not verified in an actual browser** — same disclosed limitation as every other CSS
pass in this file; the coordinator/user should eyeball `packages.html` (11 distinct card hues),
`prose.html` (the two-tone chord), and `index.html`'s `#tools` grid (Prism blue vs Analyst teal-green,
no longer collapsed together) on a real screen before this is considered closed.

**Flagged for `showroom-owner`** (own task, not touched from here): retint `Showroom/`'s Prism/
Analyst/Creature/Forecaster to this same table (values in the Design system table above) so the two
repos read as one system — same "brand-mark-stopped-at-repo-boundary" limitation already on record
under "Platform initiative" below, no shared token file exists across the repo boundary so this has
to be a duplicated-by-hand hex match, not an automatic inheritance.

## Deploy

`.github/workflows/deploy.yml`, triggered on push to `main` (Pages Source must be "GitHub
Actions", one-time repo setting). You (website-owner) never run this or commit/push — leave
changes in the working tree; the coordinator batch-commits and pushes to publish.

**CI does NOT run `dotnet publish` (changed 2026-08-28).** Steps are now just: copy `site/*` →
copy the PRE-BUILT `Showroom/dist/` → copy `SiteKit/tokens/` (added 2026-09-02, see the `SiteKit/`
paragraph above — `site/assets/site.css` now `@import`s `SiteKit/tokens/{core,brand-ea}.css` by a
relative path that resolves to `/SiteKit/tokens/...` once served from the Pages artifact root; this
copy step is what makes that import resolve live instead of 404ing — if `SiteKit/tokens/` ever
moves or gets a new file, keep this copy step and the `@import` paths in `site.css` in sync) →
upload-pages-artifact → deploy. Reason (for the Showroom half): this build runs
`RunAOTCompilation` + `PublishTrimmed=true` (see Platform initiative below), and AOT's
Emscripten/native compile step is slow enough that redoing it in CI on every push burns real
minutes for no reason when Showroom's own source hasn't changed. `Showroom/dist/` is a
COMMITTED build artifact (~55 MB, a deliberate `.gitignore` exception) — build+publish locally
(`dotnet publish Showroom/Showroom.csproj -c Release -o <tmp>`, copy `<tmp>/wwwroot` over
`Showroom/dist/`) and commit the result IN THE SAME COMMIT as any Showroom source change.

**REAL INCIDENT (2026-08-28): committing `dist/` without `.gitattributes` broke the whole app in
production.** Blazor's fingerprinted filenames encode a content hash, and `index.html` embeds a
Subresource Integrity (SRI) hash for several assets — Windows git's default line-ending
normalization (LF→CRLF) rewrote bytes in several committed files (`index.html`, both
`dotnet.*.js` runtime files, `Showroom.styles.css`, a checkpoint data file), so the served file no
longer matched its own embedded hash and the browser HARD-BLOCKED loading it entirely ("Failed to
find a valid digest... resource has been blocked" → "Failed to start platform" → nothing works).
The `warning: ... LF will be replaced by CRLF ...` git prints on every commit of `dist/` files was
wrongly read as cosmetic noise across several commits — it was an active corruption warning for
build artifacts whose exact bytes matter. **Fixed** with a repo-root `.gitattributes`:
`Showroom/dist/** -text -diff` — git never touches line endings/encoding for anything under that
path again. If `dist/` is ever rebuilt from scratch (not just refreshed), sanity-check
`git status`/`git add` produces NO CRLF-conversion warnings for anything under `dist/` before
committing — if one appears, `.gitattributes` isn't covering that path.

**THE HARD COUPLING FLIPPED, DOESN'T DISAPPEAR.** Old risk: a Showroom compile error blocked
the whole site's deploy (CI built Showroom itself, so a break there was loud and visible). New
risk, arguably worse because it's SILENT: CI no longer builds or checks Showroom at all, so if
`Showroom/dist/` is stale (source changed, dist/ wasn't rebuilt) or was built from broken source,
CI will happily deploy it — nothing red, no failed job, the live tools just silently misbehave or
run old code. There is no CI safety net for this anymore; the discipline is entirely on whoever
commits: rebuild `dist/` locally, confirm the LOCAL build succeeded, before pushing. Before
assuming a Showroom content/behavior change is live, check that `Showroom/dist/`'s own
`_framework` file hashes actually changed in the commit, not just that the source `.razor` file did.

## Platform initiative (in flight, 2026-08-28) — verified facts

**2026-09-02: superseded as the plan-of-record by `docs/platform-architecture.md`.** Direct
strategic instruction: this site becomes a reusable toolkit for future client sites, not a
bespoke AboutUs homepage — read that doc for the actual architecture. Content/app boundary
re-verified correct (16-static+4-Blazor split stays; the real gap is authoring/sharing, not
runtime placement). **Corrected mid-session on a second direct instruction**: not a generic
Razor-Class-Library-plus-static-generator design — every EA product is declarative-first
(EvalApp/HoloDb/PrismSpec), so pages get the same treatment: a declarative `PageSpec` record
authored via an EvalApp-styled fluent builder, rendered by an engine built AS a real EvalApp
pipeline (each render concern — head/nav/hero/sections/static-file-write/island-placeholder — a
genuine EvalApp `Step`, so rendering many pages across many client sites is a resource-gated,
tuned fan-out EvalApp already does, not a bespoke second engine). `HtmlRenderer`/`JSComponents`
are still real and used, demoted to "how one render step implements its fragment" / "how an
island mounts client-side," not the top-level driver. **Flagged, not decided unilaterally**: this
uses EvalApp for a build-time batch workload outside its documented consumer set — `evalapp-owner`
sign-off is a real blocking step (Phase 0.5) before any of this is implemented, concrete open
questions listed in the doc's §4.5. Phase 0 (token + component-inventory extraction into
`AboutUs/SiteKit/`) is DONE and unaffected by the correction (pure CSS/data, orthogonal to the
render-engine choice) — `SiteKit/tokens/{core,brand-ea}.css` (value-preserving extraction, INERT,
not yet imported by anything) + `SiteKit/COMPONENTS.md` (the full component inventory +
AboutUs-specific-vs-reusable-core split) + `SiteKit/README.md`. Nothing in `site/` or `Showroom/`
was changed to do this — `site/assets/site.css` and `Showroom/wwwroot/css/*` remain the live,
load-bearing files. The showroom-owner hand-off for the next phase is written out in full in that
doc's own §8, ready to dispatch as its own task whenever the coordinator picks this up. This
section below is kept as the verified-facts HISTORY the new doc's reasoning is built on (payload
measurement, the HtmlRenderer/JSComponents spikes, the incident/tuning log) — still true, just no
longer the plan-of-record framing at the top.

A multi-cycle initiative is running to turn the site into a Blazor app platform with Prism (the
in-browser HoloFormer) woven into the site rather than isolated at `/tools/prism`. Architecture is
still being steered; these MEASURED facts hold whichever way it lands:

- **Payload reality (measured, not estimated)**: publishing an equivalent net10.0 Blazor WASM app
  with Showroom's exact package set (HoloDb 1.4.0 / AlgFormer 1.5.0 / Tracer 1.1.0,
  `PublishTrimmed=false`) produces **28.0 MB raw / 10.8 MB gzip / 8.3 MB brotli** across 213 files.
  The EA assemblies are a *rounding error* in that (AlgFormer 329 KB, EvalApp 297 KB, Tracer 298 KB,
  HoloDb 175 KB, Phasor 12 KB ≈ 1.1 MB total) — the weight is untrimmed BCL
  (`System.Private.CoreLib` 4.8 MB, `System.Private.Xml` 3.0 MB, `System.Data.Common` 1.0 MB).
  Plus Prism's checkpoint `oracle-brain.bin` = **2.9 MB**. A static content page here is 12-36 KB
  + 15 KB CSS. **That ~250x gap is the single number that decides the architecture**: content pages
  must not be made to pay the app's boot cost.
- **Server-free Razor→static-HTML works** (spiked + run on this exact SDK, 10.0.400):
  `Microsoft.AspNetCore.Components.Web.HtmlRenderer` + a `ServiceCollection`/`ILoggerFactory` in a
  plain console app (`Sdk="Microsoft.NET.Sdk.Razor"`, `OutputType=Exe`) renders a component to clean
  static HTML — **no Kestrel, no server, no crawl step**. Output carries **no `<!--Blazor` markers**
  and correctly escapes (`&amp;`, `&lt;script&gt;`); note it emits non-ASCII as numeric entities
  (`&#x2014;` for an em dash), which is actually encoding-proof and sidesteps this repo's mojibake risk.
- **Mounting a Blazor component into an arbitrary static page is first-party and present in 10.0.8**
  (verified by reflection over the real DLLs, not from memory):
  `RootComponentMappingCollection.Add(Type, selector[, ParameterView])` and `.JSComponents`, plus
  `JSComponentConfigurationExtensions.RegisterForJavaScript<T>(identifier[, javaScriptInitializer])`
  in `Microsoft.AspNetCore.Components.Web`. This is the mechanism for boot-on-demand Prism islands
  inside otherwise-static pages.
- **`PublishTrimmed=false` is worth ~7 MB of mobile download** and its stated justification is
  contradicted by ground truth: `Showroom.csproj`'s comment blames "EvalApp step factory" reflection,
  but `MonoRepo/EvalApp/docs/site.md` and EvalApp's `CLAUDE.md` both state EvalApp "ships trimmable
  and AOT-compatible". One of the two is wrong — open question for `evalapp-owner`.
- **The `site/` ↔ `Showroom/` ownership boundary is a live cohesion fault line.** The 2026-08-28
  prism-triangle brand mark was swept across all 17 static pages but stopped dead at the repo
  boundary: `MainLayout.razor`'s brand mark and `wwwroot/index.html`'s favicon kept the retired
  hexagon + 3-stop gradient. **Brand half now CLOSED** — flagged from here, fixed by showroom-owner
  in their own files (same triangle path + 7-stop ROYGBIV). **Still open**: Showroom's nav is 7 items
  vs. the static site's lean 4, past the ~6-item compactness bar, and it grows with every new tool.
  The durable lesson is the mechanism, not this instance: a brand/nav change swept by hand across one
  side of an ownership boundary WILL stop at that boundary. Shared chrome in a common component
  library is the only real fix; until then, any site-wide visual sweep needs an explicit
  coordinator hand-off to showroom-owner in the same cycle.
- **The design system is desktop-first**, contrary to the new mobile-first mandate: every layout
  breakpoint in `site.css` is subtractive `@media (max-width: …)` (640px and 900px, only two), so
  base styles target desktop and mobile is an override. A mobile-first rebase means `min-width`
  breakpoints and auditing the 15 `:hover`/small-`font-size` occurrences for touch.
- **Scroll-tied parallax depth + spotlight shadows — BUILT (2026-08-28)**, `site.css` only, search
  "SCROLL-TIED PARALLAX DEPTH" for the whole system in one place (right after the `body.os-chrome`
  wallpaper `background` rule). Three layer-aware tiers, each a native CSS scroll-driven animation
  (`animation-timeline: scroll(root)`/`view()`) — zero JS, no scroll listener anywhere, gated entirely
  inside `@media (prefers-reduced-motion:no-preference)` (nesting `@media (min-width:901px)` for tiers
  2-3): **tier "far"** = the `body.os-chrome` wallpaper's own `background-position`, tied to
  `scroll(root)` (completes only over the WHOLE document scroll — the slowest layer, no shadow, it IS
  the backdrop everything else casts onto); **tier "near"** = `.prism-beam`, tied to its own `view()`
  (exits as the hero scrolls away, `translateY(0)->(-22px)` at first build); **tier "mid"** =
  `.hero > .wrap` + every `.sec:has(.sec-head)` panel, tied to `view()` capped to
  `animation-range:entry 0% entry 45%` (settles `translateY(10px)->0` at first build, as each scrolls
  into view). Calibrated almost-imperceptible ON PURPOSE at this first build (small px/blur values) —
  **since found too subtle on a real device and tripled/re-tuned, see the "Parallax/glow MAGNITUDE
  increase" entry further down this file for the current numbers; this paragraph is the original build
  history, not the live tuning.** Not verified live at the time, no browser available, verification was
  brace/paren-balance (226/226, 496/496 after this pass) + a full re-read of the inserted block.
  **Lighting/shadow layer (folded in same pass, user: "the shadows also generate appropriately for
  the perceived depth and motion, and angle with the centre of the page being a spotlight")**: each
  tier's own `@keyframes` step ALSO animates `filter:drop-shadow()` alongside its `transform` — one
  coupled signal, not a second timeline, so motion and shadow can't desync. Deliberately `filter`, not
  an animated `box-shadow`: `.hero > .wrap`/`.sec:has(.sec-head)` already carry a tuned STATIC
  multi-layer `box-shadow` (glass elevation + inset highlight, second real-device pass) that an
  animated `box-shadow` would have had to silently re-duplicate inside every keyframe; `filter`
  composites additively on top, untouched. ANGLE is static per element, computed from where it
  actually sits (`.prism-beam` right-aligned in the hero -> right-of-centre -> shadow lower-LEFT;
  the centred panels -> straight down) and never itself scroll-reactive; SIZE/opacity is the dynamic
  part — each keyframe pair's drop-shadow X/Y is an exact scalar multiple of the other (same ratio at
  both ends) so linear interpolation can only slide along that one ray, never drift angle mid-scroll.
  "Near" and "mid" deliberately grow in OPPOSITE keyframe directions (near: prominent->receding,
  shrinks; mid: arriving->settled, grows) because they're opposite phases of a `view()` timeline
  (exit vs. entry), not an inconsistency — both encode the same rule, "larger shadow = closer/more
  present," just at different points in each tier's own lifecycle. No element on the page today sits
  left-of-centre, so the lower-right case is unexercised (documented in `site.css`, same X-offset-sign
  convention extends cleanly if one shows up later). **Honest gap**: `filter` is not a pure-compositor
  property like `transform`/`opacity` and can trigger repaint as it's driven — kept blur/spread radii
  small and the affected element count low (≤ ~7 panels on the richest page) to bound the cost, but
  real jank/no-jank on a live device is unverified, same caveat as every other motion change this
  session. Motion hooks (`prefers-reduced-motion` gating) existed in `site.css` before this pass and
  were reused as designed, not newly added.

**Shadow→glow colour inversion + per-page category tint (2026-08-28, same day, two follow-up
passes)**: the `drop-shadow()` keyframes above originally used `rgba(0,0,0,...)` — dead on arrival
against `--bg:#050608` (a black shadow on a near-black field has ~zero contrast by construction).
User: "since the website is black can we have the shadows be bright instead of dark. Like inverted."
Fixed by recolouring only (angle/size/opacity math untouched): the two keyframe pairs now read
`color-mix(in srgb, var(--glow-near|--glow-mid) N%, transparent)` at the SAME N% the old rgba() alpha
carried (.20/.08 near, .08/.24 mid). Two new custom props carry the colour: `--glow-near` (defaults
`#fff` in `:root` — `.prism-beam` is a literal white light source in its own SVG, so white is
page-independent) and `--glow-mid` (defaults `var(--accent)` — the hero/`.sec` window panels have no
light source of their own, so they read as catching ambient light). **Follow-up same day**: coordinator
relayed the user wants the glow colour-coded PER PRODUCT PAGE, not one flat colour sitewide. Rather
than duplicate keyframes per page, both props are overridden via `body[data-cat="foundation|data|
ml|spatial"]` rules near the `:root` tokens (reusing `--c-foundation-solid`/`--c-data`/`--c-ml`/
`--c-spatial` — the SAME tokens the `.related` dots/`.pkg-strip` chips/card `--cat` accents already
use, so the glow always agrees with the rest of that page's colour language) — one attribute swap per
page, zero new CSS per page. `data-cat` was added to all 14 category-bearing pages' `<body>` tags now
(Foundation: `phasor.html`/`evalapp.html`; Data: the HoloDb family, all 5; ML: `algformer.html`/
`algformer-gpu.html`/`evalapp-neural.html`/`prose.html`/`holoformer.html`; Spatial: `tracer.html`/
`holovoxel.html`), even though the glow itself only VISUALLY activates on pages that also carry
`os-chrome` (today: `index.html`, `phasor.html`, `holodb/index.html`) — inert but future-proof on the
other 11, so the pending `os-chrome` sweep (see "Sweep recipe" below) never needs to remember this
step. `index.html`/`packages.html` carry no `data-cat` (no single product focus) and fall through to
the `:root` default — `--accent` for the panels, matching the site's own default brand hue used
elsewhere on the flagship (nav pills, buttons) rather than one product's colour borrowed for it.
Categories were cross-checked against each page's own `.sec-head .dot` `--c-*` colour (already live
on every page) before assigning, not guessed. Verified via brace/paren-count parity on the whole
`site.css` (232/232 braces, 547/547 parens) after both passes; still no live browser, described as a
structural/cascade check as always.

**Parallax/glow MAGNITUDE increase (2026-08-28, real-device follow-up, user: "Can we increase the
intensity of parallax and the brightness of the inverted shadow; it really is imperceptible")**. The
first pass (above) was deliberately tuned near-imperceptible by design ("close inspection" depth cue,
comment literally said "if you can point at the motion mid-scroll... it's tuned too high") — that
undershot on a real device. This pass is a straight magnitude jump, not a redesign: the scalar-multiple
keyframe-pair mechanism (each pair's X:Y ratio locked, so angle can't drift mid-scroll), the `--glow-
near`/`--glow-mid` per-page `data-cat` tinting mechanism, the `filter:drop-shadow()`-not-`box-shadow`
choice, and the tier structure/timelines are all UNTOUCHED — only the numbers inside each `@keyframes`
step changed. Before → after, all in `site.css`'s `ea-parallax-*` `@keyframes`:
- **Tier "far"** (wallpaper `background-position`, tied to `scroll(root)`): travel tripled,
  `0% 4%, 0% -3%, 0% 2%, 0% 0%` → `0% 12%, 0% -9%, 0% 6%, 0% 0%`.
- **Tier "near"** (`.prism-beam`): `translateY` tripled, `0 → -22px` becomes `0 → -70px`. Drop-shadow
  blur/spread tripled, `8px/3px → 24px/12px` (X/Y kept the same -1:2 ratio, scaled up: `-5px 10px →
  -16px 32px`, `-2px 4px → -8px 16px`). Alpha raised ~2.75-3x, `20%/8% → 55%/25%` — raised MORE than a
  flat 3x on this tier specifically: while retuning, found `.prism-beam`'s own base rule carries a
  static `opacity:.65` (unrelated to the parallax rules, pre-existing, kept for the beam's "low enough
  to read as texture" look), which multiplies the WHOLE element's rendered output AFTER the
  `drop-shadow` filter composites — the exact same class of bug as the documented icon-tile
  `opacity:.85` bleed-in (finding 8, above), checked for deliberately per this task's instruction, and
  real: the near-tier glow was quietly ~35% dimmer than its keyframe alpha implied. Compensated by
  pushing this tier's alpha higher than a flat tripling would give, rather than touching the beam's own
  `opacity:.65` (a separate, intentional design decision, out of scope for this task).
- **Tier "mid"** (`.hero > .wrap` + every `.sec:has(.sec-head)`): checked first for the same
  opacity-multiplier trap — neither carries its own `opacity` property (confirmed by reading both
  rules), so no hidden dampening here. Settle distance tripled, `translateY 10px → 0` becomes
  `30px → 0`. Drop-shadow Y/blur tripled, `4px/5px → 12px/15px` and `12px/16px → 36px/48px`. Alpha
  raised ~2.3-2.5x, `8%/24% → 20%/55%`.
Reasoning for the magnitude picked: the user's word was "imperceptible," which calls for a real,
unmistakable jump rather than a token nudge — tripling the positional/blur numbers (the geometry) and
roughly 2.3-3x on alpha (with the near tier pushed further to cancel out the newly-found `.65` element-
opacity multiplier) was chosen so both tiers land at a comparable EFFECTIVE peak brightness on screen
(near: 55%×.65≈36% effective; mid: 55% effective, no multiplier) instead of a flat "×3 everywhere" that
would have left near looking dimmer than mid for a reason invisible in the keyframe source. Did NOT
touch: the `--glow-near`/`--glow-mid` custom-property/`data-cat` tinting mechanism (still fully
per-page, still color-mix against the same two props), `.prism-beam`'s own `opacity:.65` (compensated
around, not removed), the static per-element shadow ANGLE (X:Y ratios preserved exactly), or the
`animation-timeline`/`view()`/`scroll()` wiring. Verified: brace/paren-count parity on the whole
`site.css` (232/232 braces, 565/565 parens — paren count rose from adding explanatory comment text, not
new rules) and a full re-read of the edited `@keyframes`/comment blocks; still no live browser, this
remains a structural/cascade check, not a rendered screenshot — the coordinator/user should confirm the
new intensity reads right on the same real device that flagged it as imperceptible.

**Real, pre-existing mobile horizontal-overflow bug found and fixed (2026-08-28, same day, user:
"on mobile horizontal overflow is not great it goes off screen often")**. User confirmed this predates
the parallax/pivot work, so that was ruled out as the cause rather than chased. Root-caused by reading
the CSS, not guessed: the base `.grid{grid-template-columns:repeat(auto-fill,minmax(330px,1fr))}`
rule — used by every "Key features"/"Why it's useful" card grid on every product page (34 occurrences
across 14 files, not just the homepage/packages galleries) — never had a narrow-viewport override
anywhere in `site.css`. `auto-fill` can wrap to a new row but can't shrink a track below its 330px
floor, so `.grid` + `.wrap`'s 24px×2 padding forces a 378px minimum content width regardless of
viewport. Any real phone narrower than that (iPhone SE/mini at 320-375px, most budget/older Android at
360-393px — all common, hence "often") pushed the whole page wider than the viewport: real horizontal
scroll, not a rare edge case. Checked `html`/`body` for a masking `overflow-x:hidden` first (per the
task's own instruction) — none existed anywhere in the file, so nothing was hiding the symptom; the
width was genuinely escaping. Fixed at the actual source, not with a blanket `overflow-x:hidden`
band-aid (would have silently clipped real card content instead of fixing the layout): added
`@media (max-width:640px){.grid{grid-template-columns:1fr}}` right after the base rule — single-column
stacking, the same proven pattern `#tools`'s existing os-chrome mobile block already uses. Scoped to
the base `.grid` class (not chrome-gated), since the bug reproduces on all 14 plain pages today, not
just the 3 `os-chrome` ones; the higher-specificity ID rules `body.os-chrome #packages .grid`/`#tools
.grid` (ID selectors win regardless of source order) are untouched and still apply on the 3 pages that
carry them. Audited other overflow candidates the task flagged and found them already safe, not
guessed clean: `.powered`/`.pkg-chips`/`.stack .flow` all already carry `flex-wrap:wrap`; no `100vw`
anywhere in `site.css`; `.install code` has `overflow-x:auto` which per spec zeroes its flex-item
automatic min-width (the standard "shrinkable flex item with its own scrollbar" pattern, already
correct); `.chartwrap svg{min-width:640px}` on `holodb/index.html` sits inside `.chartwrap{overflow-x:
auto}`, so it scrolls within its own box rather than pushing the page; `evalapp.html`'s wide comparison
table is likewise wrapped in `.tbl-wrap{overflow-x:auto}`. `.grid`'s 330px floor was the one real,
unguarded, sitewide gap. Verified via brace-count parity (site.css unaffected by prior passes' counts
plus this addition) and a tag-balance re-read of every HTML file this session touched — no live
browser, same disclosed limitation as every other CSS change in this doc.

**Homepage `#tools` reorder by technical achievement (2026-08-28, user: "can we reorder home page in
order of technical achievement per tool? Prism should be first obv")**: `index.html`'s `#tools` grid
order changed from Analyst/Creature/Forecaster/Prism to **Prism, Creature, Forecaster, Analyst** — no
card content/copy/chord/powered-by list touched, sequence only. Reasoning for the 3 the user left to
judgement: Prism first per direct instruction (the deepest single artefact — a real trained d=1536
checkpoint over a subword text vocab, plus the only tool with a per-character/per-layer Inspector).
Creature and Forecaster share the identical live-training HoloFormer core (per `HoloKernel`'s own
finding, "the same loop The Creature uses") so they're ranked by system breadth, not model depth:
Creature integrates TWO packages in real-time (AlgFormer brain + Tracer pathfinding, embodied
world-navigation) against Forecaster's one (AlgFormer only, a single financial time series) — broader
live-integrated system, so Creature ranks above Forecaster. Analyst last: a genuinely substantial
achievement (a real in-browser SQL engine, HoloDb) but on a different axis entirely (no neural
model/training involved), and the user's own framing ("Prism should be first obv") reads as ranking
by the neural/transformer-depth axis specifically. Flagged for the user to redirect if they intended
Analyst to rank differently. Verified: `data-initial` attributes moved with each card (mobile icon-tile
marks stay correct), tag/div balance re-checked on `index.html` (27/27 divs, 1/1 nav, 1/1 body).

### `HoloKernel/` — the shared model kernel (Phase 1, landed 2026-08-28 — **ported into all three
live-brain tools by showroom-owner the same day**, status correction from showroom-owner with
ground truth in hand; rest of this section is the coordinator/kernel side's to maintain)

New Razor Class Library at `AboutUs/HoloKernel/` (`net10.0`, `Sdk.Razor`, root namespace
`HoloKernel`). Coordinator-approved location: inside `AboutUs`, **NuGet-only**
(`EvaluatedApplications.AlgFormer` 1.5.0 + `Microsoft.AspNetCore.Components.Web`) — never a MonoRepo
`ProjectReference`, same hard boundary Showroom keeps.

**Why it exists**: Prism has an Inspector and no training loop; Creature and Forecaster have the
*identical* hand-written `NewGrads() -> IterAccumulate -> Step` loop and no Inspector (verified in
the sources — Forecaster's own comment says it is "the same loop The Creature uses"). Those are the
same refactor from opposite ends.

Files: `ModelSpec.cs` (shape + the **S>1 invariant enforced by construction**, throws on
`MinShifts<2`), `HoloSession.cs` (model + K/alpha, `ModelStats`), `AlphaRamp.cs` (the identity-init
ease-in + `Reconstruct` for checkpoint metadata), `RefinementLoop.cs` (`Observe` single-position /
`ObserveSequence` all-positions), `Decoding.cs` (`DecodePolicy`/`Gate.Evaluate`/`Gate.Pick`/`TopK`/
`DegenGuard`), `InspectorTrace.cs` (`PassSnapshot`/`PositionTrace`/`Inspector.Capture`/`Focus`),
`ParallelMapping.cs` (the `IParallelMap` seam).

**Verified, not assumed** (31-check smoke suite, run green; build 0 warnings / 0 errors):
- Reproduces both tools' independently-recorded shape derivations exactly: `ShiftsFor(32,384)=1` so
  Creature's floor genuinely bites to 8; `ShiftsFor(256,128)=16` so Forecaster's floor is a no-op;
  `CleanCapacity(16,128)=122 < 256` surfaces as a real flag.
- `HoloSession.FromCheckpoint` **requires** K and alpha as arguments — because a round-trip through
  `Serialize()`/`Deserialize()` provably loses them (confirmed live: reads back `Iters=1`,
  `IterAlphaServe=1`). The gotcha is now structurally impossible to forget, not a footnote.
- **`StackIterAccumulateAllPos`'s `scoreP` parameter is NOT an offset** — measured against the real
  DLL: it is a **count** of positions to score, saturating at `sequence.Length - 1`, and the returned
  loss is a **SUM** not a mean (31 positions -> ~87.5). The kernel's first draft assumed "score from
  this index onward" and was wrong; it now normalises to a per-position mean so a training curve
  can't leap when a tool switches modes. Do not trust that parameter name.
- `AlphaRamp.Reconstruct(24_360, 0, 20_000) == 1.0`, matching Prism's real shipped snapshot.
- **The weight-tied K-pass is SINGLE-LAYER ONLY.** With `Layers>1` and `K>1`, both `LogitsFor` and
  `IterAccumulate` throw `NotSupportedException("Iter oracle: L=1 only.")` — but
  `StackIterAccumulateAllPos` with `K>1` succeeds on a multi-layer model. That asymmetry is a trap:
  a deep model can be TRAINED at K>1 and then fail only when you try to serve it. `ModelSpec.Validate`
  now rejects the combination at construction. Relevant to whoever picks a shape (PrismStudio /
  server side), NOT to the browser — see the browser contract below.

### THE BROWSER CONTRACT: train-only, fixed shape (user directive, 2026-08-28)

**In the browser, visitors TRAIN the model; they never change its shape.** "Grow Prism" means
refining the existing weights of a fixed-shape model via `RefinementLoop.Observe` /
`ObserveSequence` — that is the whole of it. Layers, shifts, dim and context are chosen up front and
are immutable for the life of a browser session.

`HoloFormer.GrowLayers` / `GrowShifts` are real capabilities on the published package, but they are a
**PrismStudio / server-side operation**. The browser platform neither triggers nor exposes them.
`HoloKernel` deliberately does not wrap either method; `HoloSession.Model` makes them *reachable*,
which is a pragmatic escape hatch, not an invitation — a "grow the model" control does not belong in
a tool UI. Shipping a bigger or better-trained model to visitors is done by publishing a different
CHECKPOINT, not by mutating shape at runtime.

This also keeps the mobile budget honest: shape changes would invalidate a downloaded checkpoint and
force a re-fetch of megabytes, which is exactly what the layered load strategy exists to avoid.

### Session lifetime: one shared session per page load, ephemeral (user directive, 2026-08-28)

The model loads **once per page load** and that same in-memory session is reused as the visitor
navigates between tools — not a fresh model per tool. Refinement a visitor does is **lost on reload,
by design**: no local storage, no save-back, no cross-session sync. Explicitly do NOT build
persistence machinery for this; if "keep my progress" is ever wanted it is a new decision.

`HoloKernel/SessionHost.cs` is the whole mechanism: `GetOrCreateAsync(key, factory)` over a
`Lazy<Task<HoloSession>>`, registered as `AddSingleton<SessionHost>()`. In Blazor WASM a singleton's
lifetime IS the page load, so the DI lifetime and the intended ephemerality are already the same
thing — nothing extra to enforce. The one part done carefully is load de-duplication: concurrent
callers await the SAME load, because Prism's checkpoint is ~2.9 MB and two tools racing on first
navigation is ordinary, not exotic. `Forget(key)` backs a "reset brain" control; `Clear()` is what a
reload does anyway. There is no `CheckpointStore` and none is wanted — `HoloSession.Export()` exists
only to make the K/alpha round-trip verifiable, NOT as a save feature to build UI on.

**Open structural question, flagged not solved**: "one session shared across tools" holds only for
tools that share a model. The three current tools do NOT — Creature is vocab 408 / d=384 / ctx=32,
Forecaster is vocab 17 / d=128 / ctx=256, Prism is d=1536 with a text subword vocab. A `HoloFormer`
has exactly one `Vocab`/`Dim`/`Context`, so those three cannot be one object as they currently stand.
`SessionHost` is therefore keyed by MODEL, not by tool: tools sharing a checkpoint share one instance,
and the rest each get one long-lived instance instead of a rebuild per navigation. That delivers the
actual intent (don't reload the model when navigating; lose it on refresh) without pretending
incompatible shapes can be a single session. Whether the tools should converge on one shared brain is
a real design question for the coordinator, not something to assume.

### The `IParallelMap` seam — investigated, and RULED OUT for this site

Prompted by `evalapp-owner`'s deadlock finding. Two independent measured grounds, both recorded in
`ParallelMapping.cs` so nobody re-derives them:
- **Not currently dangerous**: a fresh `HoloFormer.Map` defaults to `PrismFormer.SequentialMap`
  (verified), `PrismEval+EvalMap` is a *non-public* type reachable only by explicitly assigning
  `PrismEval.Cpu`, and **nothing in `Showroom/` sets `.Map` at all** (grepped). So the existing tools
  are safe as they stand; the sync-over-async hazard is strictly opt-in.
- **And not useful either**: instrumenting the seam with a spy across every shape this site runs
  (Forecaster d=128, Creature d=384, Prism d=1536; Layers 1/2/4; via `LogitsFor` and
  `TrainEpoch(parallelism:4)`), `chunks` was **always 1** against `minForParallel` of 2 — 0 of 112
  calls on the batch path ever reached the parallel threshold. The fan-out is width-1 at our shapes,
  so even a perfect async implementation would gate one item and buy nothing.

**Conclusion**: EvalApp's role in the browser is the OUTER loop (cooperative yielding, progress,
cancellation around a long run, concurrency 1) — NOT this seam. That stands regardless of whether
`algformer-owner`'s async `IParallelMap` redesign lands. `ParallelMapping.EnsureBrowserSafe()` exists
as a loud known-bad-list check (it rejects `PrismEval.Cpu`) so a future port fails at wire-up rather
than as a silently frozen tab. Scoped claim: measured on these CPU paths at these shapes only.

**Deploy risk: live now, verified low.** `Showroom.csproj` carries a `ProjectReference` to
`HoloKernel` as of the port above — `deploy.yml`'s `dotnet publish Showroom` now pulls it in for
real. Both `dotnet build` and `dotnet publish Showroom/Showroom.csproj -c Release` were re-verified
green after the port (0 warnings/errors), so the hard-coupling risk noted above is checked, not
theoretical, for this change specifically.

**Boundary**: `Showroom/Pages/*.razor` is showroom-owner's — porting the tools onto this kernel is
DONE (all three, one at a time as designed, not a big-bang port; see `Showroom\CLAUDE.md`'s own
HoloKernel section for the per-tool detail, including the tokenizer swap and the browser-contract
correction below).

**Self-maintenance note**: this file is now ~1150 lines (the "~360 lines" figure above went stale
across several passes and wasn't caught until this one — corrected here rather than left wrong), well
over the ~200-line guide. Still deliberately not compacted — the platform initiative above will
obsolete large parts of the Site map / Design system sections, and this pass's own os-chrome sweep +
chord-glow mechanism added a further ~150 lines of dated history that should compact into a handful
of durable "how it works now" paragraphs once things settle. The compaction pass should happen when
the architecture lands or the next time this file is opened for an unrelated reason, not deferred
indefinitely — flagging that the deferral itself needs an actual trigger, not just "later."

## Gotchas

- **New (2026-09-02): before hand-sweeping a token/component change across N pages again, check
  `SiteKit/COMPONENTS.md` first.** It's the documented single source of truth for what a
  component's markup/prop contract SHOULD be (transcribed from the real files, kept current), even
  though Phase 1's real Razor Class Library doesn't exist yet — check a sweep's target shape
  against that doc before typing it into 17 files from memory of `site.css`, and update the doc's
  entry if the sweep changes the contract. See `docs/platform-architecture.md` for why.
- Windows/PS 5.1: edit via the editor tools (Read/Edit/Write) or UTF-8-safe .NET I/O; never
  `Get-Content`/`Set-Content` on these files (em dashes, arrows, non-ASCII punctuation throughout
  → mojibake risk).
- The two HoloDb pages (`holodb/index.html`, `holodb.html`) and `holoformer.html` carry
  page-local `<style>` blocks ON TOP OF `site.css` (bespoke SVG charts, race-demo bars, concept
  glyphs) — this is intentional (their visuals are one-off, not reusable components) but means a
  global token change must be checked against those local blocks too, not just `site.css`.
- `evalapp.html`'s "None of this is invented from nothing" table is hand-styled inline (no shared
  `.tbl`/table class exists in `site.css` yet) — if a third page wants a similar table, extract a
  shared class into `site.css` first rather than copy-pasting the inline styles a third time.
- New package coming: bootstrap its page the same way — read `MonoRepo/<Pkg>/docs/site.md` (+
  `PACKAGE.md`/`CLAUDE.md`/csproj `<Version>` for facts), copy the page template shape from any
  existing plain product page (e.g. `phasor.html`) INCLUDING its lean nav (`Home · Packages ·
  NuGet` — 3 items, see Navigation section; `Packages` points at `/packages.html`, NOT `/#packages` —
  that same-page-anchor form is retired sitewide since the 2026-08-28 tools-first pivot) and its
  `.related` pills row, add it to `packages.html`'s gallery (pick a category colour, add the
  `.card-link` overlay — this alone makes it reachable in ≤2 clicks from every page via Home/Packages
  → the gallery, so it's the step that actually matters — NOT `index.html`, which is tools-only now),
  then add it into the `.related` pills of its closest 1-2 siblings (not every page — contextual, not
  exhaustive), add the nav crumb pattern, add it to `sitemap.xml`, and update the package count in
  `index.html`'s hero facts, `packages.html`'s hero facts, and this file's Site map section. If it
  also powers a live tool, add a `.powered` pill linking to it on that tool's homepage card too. Re-run
  the reachability walk (Navigation section, above) before calling it done, and check the nav is still
  compact on a narrow viewport.

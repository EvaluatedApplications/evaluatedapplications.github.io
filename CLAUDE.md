# AboutUs — CLAUDE.md (website-owner)

Public site repo for `evaluatedapplications.github.io`. Static HTML content (indexable, instant)
+ a Blazor WebAssembly tools app under `/tools` (The Analyst, The Creature). You (website-owner)
own PRESENTATION — design, cohesion, rendering, nav, deploy. Package owners own CONTENT, authored
in `MonoRepo/<Pkg>/docs/site.md`. You never edit package source or `docs/site.md` — flag stale
content to the owner instead. You never commit/push (coordinator commits, user pushes → Pages).

## Site map (what exists, 2026-08-26)

Root static pages (`site/*.html`), all built on the shared design system:
- `index.html` — landing page: company pitch, tool gallery (Analyst/Creature), then the full
  package gallery in 4 categories (Foundation / Data / Machine learning / Spatial & games), then
  a "how it fits together" flow diagram.
- `holodb/index.html` — HoloDb HUB page (bespoke nav: How it works / Benchmarks / Try it live /
  Docs / NuGet). The richest page on the site: race demo, benchmark tables, capability grid,
  deploy options. Links out to `holodb.html`, `holodb-client.html`, `holodb-protocol.html`.
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
- `404.html` — SPA-fallback bounce for `/tools/*` deep links + a friendly not-found page.
- `sitemap.xml`, `robots.txt`, `.nojekyll` — kept in sync with the page set above.

Non-content: `Showroom/` (Blazor WASM app, publishes to `/tools`) — a SEPARATE concern from the
static content pages; don't fold tool code into `site/`. `.github/workflows/deploy.yml` builds
`Showroom` and copies `site/` + the published `wwwroot` into one `_site/` artifact for Pages.

**All 11 current MonoRepo packages have a page**: Phasor, EvalApp, EvalApp.Neural, AlgFormer
(+HoloFormer deep-dive), AlgFormer.Gpu, HoloDb (+benchmarks), HoloDb.Protocol, HoloDb.Client,
HoloVoxel, Prose, Tracer.

## Design system

**One stylesheet**: `site/assets/site.css`. Dark-first (`:root`), light palette under
`prefers-color-scheme:light`. Design tokens: `--bg/--bg-2/--surface/--surface-2`,
`--border/--border-2`, `--ink/--ink-soft/--ink-faint`, `--accent/--accent-ink`, `--spectrum`
(brand gradient), 4 category colours `--c-foundation` (purple, Phasor/EvalApp) /
`--c-data` (blue, HoloDb family) / `--c-ml` (pink, AlgFormer family/EvalApp.Neural/Prose) /
`--c-spatial` (green, Tracer/HoloVoxel), `--ok/--warn/--bad`, `--radius`, `--wrap` (1080px),
`--font`/`--mono`. Reusable components: `.site-nav` (sticky, CSS-only mobile burger via
`.nav-toggle` checkbox hack), `.hero`/`.eyebrow`/`.lede`/`.facts`/`.fact`, `.sec`/`.sec-head`,
`.grid`/`.card` (the package-card pattern, `--cat` custom prop sets the left accent bar),
`.install` (copy-button code chip), `.btn`/`.btn-primary`/`.btn-ghost`, `.crumb` (breadcrumb),
`.stack` (callout box, `.flow` for pipeline diagrams), `.prose`/`.toc` (manual/docs pages),
`.snip`/`.lim` (code-sample box / caveat note — added 2026-08-26 for the product-page rollout,
reused by 9+ pages), `footer.site`. A handful of legacy pages (`evalapp.html` pre-rewrite,
`holodb/index.html`, `holodb.html`) used to duplicate these tokens in a local `<style>` block;
`evalapp.html` was migrated onto `site.css` in the 2026-08-26 rewrite (see Reconciliations). The
two HoloDb pages still carry a local `<style>` (bespoke charts/race-demo/table markup that isn't
reused elsewhere) but declare the SAME token values, so they read as one brand, not a fork — if a
token in `site.css` ever changes, grep those two files' `<style>` blocks too.

**The page template** (used verbatim by every one of the 10 plain product pages, and by
`index.html`/`holoformer.html`'s nav/footer): `<div class="beam">` → `<nav class="site-nav">`
(brand mark + Home/HoloDb/#packages/NuGet, identical on every page) → `<header class="hero">`
(breadcrumb, eyebrow, h1, lede, `.facts` chips, `.install`, `.cta-row`) → a sequence of
`<section class="sec">` (What it is / Why it's useful as a `.grid` of `.card`s / Key features /
Get started with a `.snip` code sample / a `.lim` caveat note) → `footer.site` → the copy-button +
year script (identical). New product pages should copy this shape exactly, not invent new layout —
that IS the cohesion mechanism (§0 of the agent charter).

**Footer link set (standardised 2026-08-26, every page)**: `footer.site .mono` carries `© <year>`
plus 3-4 internal links so every page has a second, bottom-of-page path back into the site graph —
not just the top nav. Plain product pages + `index.html` + `holoformer.html`: `Home · Packages
(/#packages) · HoloDb · NuGet`. The three HoloDb pages use a page-appropriate subset (hub:
`Home · Packages · Docs · Benchmarks · NuGet`; benchmarks: `Home · HoloDb · Manual · NuGet`;
manual: `Home · HoloDb · Packages · NuGet`). Keep this pattern when adding a page.

**SEO tags (every page)**: unique `<title>` + `<meta name="description">` + `<link rel="canonical">`
+ `og:type`/`og:title`/`og:description`/`og:url` + `twitter:card`, all already present on every
page (verified 2026-08-26; `holodb.html` was missing OG/Twitter until this pass — fixed). JSON-LD
(`<script type="application/ld+json">`, plain object or `@graph`, placed just before `</head>`):
`index.html` carries `Organization` + `WebSite`; each of the 11 package pages carries a
`SoftwareApplication` (name/description/version/url/downloadUrl/offer price 0 — matches the
nothing-license-gated stance, never invent a version or a claim not in the page's own content); the
3 explainer/reference pages (`holoformer.html`, `holodb.html`, `holodb/manual/index.html`) carry a
`TechArticle` with an `about` pointing at the relevant `SoftwareApplication`. Keep versions in sync
with the `<Version>` ground truth (see below) when a package bumps — the JSON-LD `softwareVersion`
will go stale exactly like the hero `.facts` chip does.

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

## Reconciliations done 2026-08-26 (first full 11-page render)

- **`evalapp.html` fully rewritten.** The previous page carried an elaborate SVG-charted
  benchmark suite (a "2.7× leaner than MediatR+Polly" claim, an 8,000-request soak scorecard,
  per-architecture allocation/throughput tables) with **no provenance in any EvalApp-owned doc** —
  `EvalApp/docs/about-us-evalapp.md` explicitly says *"I'm not going to quote a benchmark number
  here that I can't currently stand behind."* Per the charter's honesty rule, ALL of those specific
  figures were dropped, not caveated (there was nothing sourced to caveat against). The page was
  rebuilt on the shared template from `docs/site.md` + `about-us-evalapp.md` (voice/origin story,
  including the real "None of this is invented from nothing" lineage table, which IS the owner's
  own content) + `PACKAGE.md`. It now also runs on `site.css` instead of a duplicated local
  stylesheet, closing a cohesion gap. **Flagged for evalapp-owner**: if real, reproducible
  benchmark numbers are ever derived and written into an owned doc, re-render this page to include
  them — the shape (a "what you'd otherwise assemble" comparison) is ready for it.
- **HoloDb version synced**: `holodb/index.html`'s hero eyebrow said v1.7.4; ground truth
  (csproj) is v1.7.7. Fixed. Also added cross-links from the hub's "Networked server + typed
  client" card to the new `holodb-client.html`/`holodb-protocol.html` pages.
- **AlgFormer/HoloFormer positioning decided**: `AlgFormer/docs/site.md` now covers BOTH cores
  (softmax `AlgFormer` + holographic `HoloFormer`), so `algformer.html` is the primary package
  page (nav + index card both point here). The existing `holoformer.html` deep-dive article is
  KEPT (it's good, specific, accurate content about the holographic core) but demoted to a linked
  explainer off `algformer.html` — breadcrumb and closing CTA updated to point back to
  `/algformer.html` instead of floating standalone off `/#packages`.
  No redirect page was created; both URLs stay live and cross-link.
- **Nothing license-gated, everywhere.** Every new/rewritten page states plainly that all
  capabilities are free today and a license key (where one is mentioned at all) is reserved for a
  possible future tier, gating nothing now — matching the intentional product stance. No page
  implies a paid/Pro tier.
- **`index.html`** package count/version numbers fixed (was "9 packages" / stale per-card
  versions from an earlier snapshot), all 11 packages now in the gallery across 4 categories, and
  the "how it fits together" flow diagram extended to name the 4 packages that build on the
  original 6 (HoloDb.Client/Protocol, AlgFormer.Gpu, EvalApp.Neural, Prose).
- **HoloVoxel imagery**: `MonoRepo/HoloVoxel/render-samples/before_dated.png` +
  `after_holoform.png` (near-view shading before/after) were copied to
  `site/assets/holovoxel/{before,after}.png` and used as a before/after comparison on
  `holovoxel.html`, clearly captioned as a reference/proof-of-concept shading pass, NOT something
  the package ships (matches `HoloVoxel/CLAUDE.md`'s own framing). The `far_before_pointsampled`/
  `far_after_smoothed` pair was deliberately NOT used — visual inspection showed the "after" image
  reading as more artifacted/checkered than the "before", the opposite of what the filenames claim
  (likely a mislabeled or unrelated capture) — using it would have been a cohesion/honesty risk.
  Flagged for holovoxel-owner if a correct far-LOD before/after pair is wanted later.

## SEO + navigation pass (2026-08-26)

Holistic audit of all 16 live pages: no orphans found (every page reachable from `index.html`'s
gallery or a cross-link within 2 clicks; `sitemap.xml` already listed all 16 + the 2 live tool
routes). Fixes made: `holodb.html` was missing all `og:*`/`twitter:card` tags — added. Standardised
the footer link set site-wide (see Design system, above) so every page has a second internal-link
path, not just the top nav. Added forward cross-links base→extension that only existed
extension→base before: `algformer.html` → `algformer-gpu.html`, `evalapp.html` →
`evalapp-neural.html`. Added JSON-LD to every page (`Organization`+`WebSite` on the index,
`SoftwareApplication` on all 11 package pages, `TechArticle` on the 3 explainer/reference pages).
Added `/tools/` (the Showroom root, not just `/tools/analyst`/`/tools/creature`) to `sitemap.xml`.
**Flagged, not fixed (out of this agent's remit — Blazor app internals, not `site/` presentation):**
`Showroom/Home.razor` still tags The Creature `soon`/"In the workshop" with no link, while
`site/index.html` already lists it `live` linking to `/tools/creature` (and
`Showroom/Pages/Creature.razor` exists) — one of the two is stale; resolve
by whoever owns the Showroom tools app before the next deploy, so the site doesn't advertise a dead
route or hide a live one.

## Deploy

`.github/workflows/deploy.yml`, triggered on push to `main` (Pages Source must be "GitHub
Actions", one-time repo setting). Steps: `dotnet publish Showroom/Showroom.csproj` →
`_site/ = site/* + published wwwroot under _site/tools/` → upload-pages-artifact → deploy. You
(website-owner) never run this or commit/push — leave changes in the working tree; the
coordinator batch-commits and the user pushes to publish.

## Gotchas

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
  existing plain product page (e.g. `phasor.html`), add it to `index.html`'s gallery (pick a
  category colour), add the nav crumb pattern, add it to `sitemap.xml`, and update the package
  count in `index.html`'s hero facts + this file's Site map section.

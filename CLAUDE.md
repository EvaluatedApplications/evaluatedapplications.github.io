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
`.nav-toggle` checkbox hack) + `.nav-drop` (the Packages mega-menu, see Navigation below),
`.hero`/`.eyebrow`/`.lede`/`.facts`/`.fact`, `.sec`/`.sec-head`,
`.grid`/`.card` (the package-card pattern, `--cat` custom prop sets the left accent bar,
`.card-link` stretched-link overlay makes the whole card clickable — see Navigation),
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
(brand mark + Home/HoloDb/Packages mega-menu/NuGet, identical on every page, plus
`<script src="/assets/nav.js" defer>` in `<head>`) → `<header class="hero">`
(breadcrumb, eyebrow, h1, lede, `.facts` chips, `.install`, `.cta-row`) → a sequence of
`<section class="sec">` (What it is / Why it's useful as a `.grid` of `.card`s / Key features /
Get started with a `.snip` code sample / a `.lim` caveat note) → `footer.site` → the copy-button +
year script (identical). New product pages should copy this shape exactly, not invent new layout —
that IS the cohesion mechanism (§0 of the agent charter).

**Footer link set** (every page): `footer.site .mono` carries `© <year>` + 3-4 internal links, a
second bottom-of-page path into the graph beyond the top nav. Plain product pages + `index.html` +
`holoformer.html`: `Home · Packages(/#packages) · HoloDb · NuGet`. The 3 HoloDb pages use a
page-appropriate subset (hub: `Home · Packages · Docs · Benchmarks · NuGet`; benchmarks: `Home ·
HoloDb · Manual · NuGet`; manual: `Home · HoloDb · Packages · NuGet`).

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

## Navigation — the reachability contract (owned by this agent, not the coordinator)

**The invariant**: every page must be reachable from the NAV MENU on every OTHER page, not just from
an on-page anchor, a body cross-link, or a small "Explore →" text nested in a card. This is a
standing design constraint, not a one-off fix — check it every time a page is added or a nav is
touched.

**How it's satisfied**: a "Packages" mega-menu, `<details class="nav-drop"><summary>Packages</summary>
<div class="nav-drop-menu">...</div></details>`, in the `.nav-links` of every one of the 15 content
pages (all except `404.html`, which is intentionally minimal). It's a native disclosure — opens/closes
on click/tap with zero JS — styled by `.nav-drop*` in `site.css`; `/assets/nav.js` (referenced once per
page, `<script src="/assets/nav.js" defer>`) only adds outside-click/Escape-to-close polish, and is a
safe no-op on pages with no `.nav-drop`. Five columns, identical everywhere: **Foundation** (Phasor,
EvalApp) · **Data** (HoloDb, HoloDb.Client, HoloDb.Protocol, Benchmarks → `holodb.html`, Manual →
`holodb/manual/`) · **Machine learning** (AlgFormer, HoloFormer explained → `holoformer.html`,
AlgFormer.Gpu, EvalApp.Neural, Prose) · **Spatial & games** (Tracer, HoloVoxel) · **Tools** (The
Analyst, The Creature, "All packages, one page" → `/#packages`). On mobile (`max-width:640px`) the
menu drops its absolute positioning and renders as an indented inline list inside the already-open
burger column — no separate mobile design needed. Chose a mega-menu over a dedicated `/packages.html`
because the index gallery already IS that full-list page (`/#packages`); the menu just needs to point
at every entry point, not duplicate the gallery.

**Site-wide rules that make it hold**:
- `#packages` must NEVER be a bare same-page anchor except literally inside `index.html` — on any
  other page it's a dead link (no `#packages` section exists there). Always write `/#packages`.
  Audited 2026-08-26: zero bare `href="#packages"` left outside index.html.
- A gallery/index card that links onward must be clickable across its WHOLE area, not just a small
  "Explore →" text — humans don't perceive an `<article>` with one small link as clickable. Pattern:
  keep the card as `<article class="card">` (it nests a second NuGet link, so the card itself can't
  be an `<a>` — invalid nested-anchor markup), add a full-bleed `<a class="card-link" href="..."
  aria-label="...">` as the FIRST child, `.card-link{position:absolute;inset:0;z-index:1}`, and lift
  `.install`/`.links`/`.note` to `z-index:2` so their own inner links/copy-button stay independently
  clickable above the overlay. Applied to all 11 gallery cards on `index.html` (tool cards were
  already whole-card `<a class="card tool">`, unchanged).
- The mobile CSS-only burger (`.nav-toggle` checkbox hack) must actually reveal `.nav-links`, and the
  `.nav-drop` disclosure inside it must still work once the burger column is open — verified in the
  CSS (`.nav-drop-menu` gets `position:static` etc. inside the `max-width:640px` block so it never
  relies on the desktop absolute-positioning math that would misplace it in a column layout).

**§5 VERIFICATION DISCIPLINE for this site — read before claiming "no orphans" again**: checking that
every `href` resolves to a real file is NOT proof of reachability and must never be reported as such.
The proof is a reachability WALK: starting from `index.html`'s nav AND from one deep page's nav
(e.g. `phasor.html`), using ONLY the nav menu + visible on-page links (no address-bar typing), list the
click-path to every other page, and confirm every page is ≤2 clicks away. Do this walk (and record it
in the task's return message) any time the nav, a page set, or a card grid changes — that's what "done"
means for navigation now, not an href-audit. The 2026-08-26 SEO pass reported "no orphans" from an
href-audit alone; that was the wrong check and is the failure this section exists to prevent.

**SEO/nav facts still true from the 2026-08-26 pass** (kept, the audit method above it was retired):
`sitemap.xml` lists all 16 pages + the 2 live tool routes + `/tools/`. Every page's `footer.site`
carries a second internal-link path (Home/Packages/HoloDb/NuGet or a page-appropriate subset) so
there's a route back into the graph beyond the top nav. JSON-LD on every page (`Organization`+`WebSite`
on the index, `SoftwareApplication` on all 11 package pages, `TechArticle` on the 3 explainer pages).
Forward cross-links exist base→extension (`algformer.html`→`algformer-gpu.html`,
`evalapp.html`→`evalapp-neural.html`) as well as extension→base.
**Flagged, not fixed (Blazor app internals, not `site/` presentation)**: `Showroom/Home.razor` may
still tag The Creature `soon` while `site/index.html` links it `live` at `/tools/creature` — re-check
before the next deploy; whoever owns `Showroom/` should reconcile it.

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
  existing plain product page (e.g. `phasor.html`) INCLUDING its `.nav-drop` mega-menu block and
  the `/assets/nav.js` script tag, add it to `index.html`'s gallery (pick a category colour, add
  the `.card-link` overlay), add it as a new `<a>` inside the matching `.nav-drop-col` in the
  mega-menu on ALL 15 content pages (not just index — this is the step that's easy to miss and
  silently reopens the orphan bug), add the nav crumb pattern, add it to `sitemap.xml`, and update
  the package count in `index.html`'s hero facts + this file's Site map section. Re-run the
  reachability walk (Navigation section, above) before calling it done.

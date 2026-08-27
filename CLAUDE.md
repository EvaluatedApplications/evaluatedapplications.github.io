# AboutUs — CLAUDE.md (website-owner)

Public site repo for `evaluatedapplications.github.io`. Static HTML content (indexable, instant)
+ a Blazor WebAssembly tools app under `/tools` (The Analyst, The Creature). You (website-owner)
own PRESENTATION — design, cohesion, rendering, nav, deploy. Package owners own CONTENT, authored
in `MonoRepo/<Pkg>/docs/site.md`. You never edit package source or `docs/site.md` — flag stale
content to the owner instead. You never commit/push (coordinator commits, user pushes → Pages).

## Site map (what exists, 2026-08-26)

Root static pages (`site/*.html`), all built on the shared design system:
- `index.html` — landing page: company pitch, tool gallery (Analyst/Creature/Forecaster/Prism), then the full
  package gallery in 4 categories (Foundation / Data / Machine learning / Spatial & games), then
  a "how it fits together" flow diagram.
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
- `404.html` — SPA-fallback bounce for `/tools/*` deep links + a friendly not-found page.
- `sitemap.xml`, `robots.txt`, `.nojekyll` — kept in sync with the page set above.

Non-content: `Showroom/` (Blazor WASM app, publishes to `/tools`) — a SEPARATE concern from the
static content pages; don't fold tool code into `site/`. `.github/workflows/deploy.yml` builds
`Showroom` and copies `site/` + the published `wwwroot` into one `_site/` artifact for Pages.

**All 11 current MonoRepo packages have a page**: Phasor, EvalApp, EvalApp.Neural, AlgFormer
(+HoloFormer deep-dive), AlgFormer.Gpu, HoloDb (+benchmarks), HoloDb.Protocol, HoloDb.Client,
HoloVoxel, Prose, Tracer.

**Unlisted client page (not part of the routable site — do not "fix" this by adding it anywhere)**:
`site/recycledao-preview.html` — a private progress preview for the RecycleDAO client PoC
(`C:\Users\dongy\RecycleDAO`, a separate repo outside AboutUs, owned by `recycledao-owner`), built for
the user to share with the client (Antonio) by direct link only. Deliberately NOT in `index.html`'s
gallery, NOT in `sitemap.xml`, NOT in any nav/footer, and carries `<meta name="robots"
content="noindex,nofollow">`. Standalone minimal header (brand only, no nav-links) rather than the
usual site nav, since it isn't an EA product page. Content sourced from RecycleDAO's `CLAUDE.md`
(no `docs/status-brief.md` existed there yet at render time — re-render from that file instead if/when
it appears). Re-render on request when the PoC's milestone status changes; never link it from anywhere.

## Design system

**One stylesheet**: `site/assets/site.css`. Dark-first (`:root`), light palette under
`prefers-color-scheme:light`. Design tokens: `--bg/--bg-2/--surface/--surface-2`,
`--border/--border-2`, `--ink/--ink-soft/--ink-faint`, `--accent/--accent-ink`, `--spectrum`
(brand gradient), 4 category colours `--c-foundation` (purple, Phasor/EvalApp) /
`--c-data` (blue, HoloDb family) / `--c-ml` (pink, AlgFormer family/EvalApp.Neural/Prose) /
`--c-spatial` (green, Tracer/HoloVoxel), `--ok/--warn/--bad`, `--radius`, `--wrap` (1080px),
`--font`/`--mono`. Reusable components: `.site-nav` (sticky, CSS-only mobile burger via
`.nav-toggle` checkbox hack — lean, always a flat list of 4-6 plain links, see Navigation below)
+ `.related` (compact contextual cross-link pills in the hero, see Navigation),
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
(brand mark + Home/HoloDb/Packages(→`/#packages`)/NuGet, identical on every page — lean, no
dropdown/JS) → `<header class="hero">`
(breadcrumb, eyebrow, h1, lede, `.facts` chips, `.install`, `.cta-row`, then a `.related` pills row
— see Navigation) → a sequence of
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

**The invariant**: every page reachable in ≤2 clicks from every OTHER page's nav, without a global
mega-menu bloating every page (tried a 5-column/17-item `<details>` dropdown 2026-08-26, user
feedback: too dense, too large on mobile, and it duplicated what the homepage already does — see
below for the revised shape).

**The shape (revised 2026-08-26, second pass)**: three layers, each doing ONE job.
1. **Lean top nav, identical shape everywhere**: `Home · HoloDb · Packages(→/#packages) · NuGet`,
   plain text links, no dropdown, no JS. A few pages add 1-2 page-appropriate items (the HoloDb hub:
   `Home · Benchmarks · Docs · Packages · NuGet`; the manual: adds `Manual`+`The Analyst`) but NEVER
   more than ~6 items — that's the compactness bar, checked on a narrow viewport (mobile burger
   reveals a short flat list, nothing nested/wide).
2. **The homepage IS the routable index.** `index.html`'s `#packages` gallery already lists every
   package with a fully clickable card (`.card-link` stretched-link overlay, not just a small
   "Explore →" — see below); nav's "Packages" link just sends you there. This is why the nav doesn't
   need to enumerate all 11+ pages itself: `Home`/`Packages` (1 click) → any card (1 click) = every
   page ≤2 clicks from anywhere, guaranteed by this single path alone.
3. **`.related` pills for contextual 1-click jumps.** A small pill row (2-4 sibling links + "All
   packages →" to `/#packages`) in the hero of every product/reference page (15 of 16, all but
   `index.html` itself — it doesn't need to link to itself). Curated per page by what's actually
   relevant, e.g. `phasor.html` → EvalApp/AlgFormer/HoloDb/HoloVoxel; `holodb-protocol.html` →
   HoloDb/HoloDb.Client; `holoformer.html` → AlgFormer/AlgFormer.Gpu/Phasor. This is what makes
   closely-related pages 1 click apart instead of always routing back through the homepage.
   CSS: `.related` in `site.css`, always paired with a `.related-label` and a `.related-all` pill.

**Site-wide rules that make it hold**:
- `#packages` must NEVER be a bare same-page anchor except literally inside `index.html` — on any
  other page it's a dead link (no `#packages` section exists there). Always write `/#packages`.
- A gallery/index card that links onward must be clickable across its WHOLE area, not just a small
  "Explore →" text. Pattern (all 11 cards on `index.html`): keep the card as `<article class="card">`
  (it nests a second NuGet link, so the card itself can't be an `<a>` — invalid nested-anchor markup),
  add a full-bleed `<a class="card-link" href="..." aria-label="...">` as the FIRST child,
  `.card-link{position:absolute;inset:0;z-index:1}`, and lift `.install`/`.links`/`.note` to
  `z-index:2` so their own inner links/copy-button stay independently clickable above the overlay.
  Tool cards were already whole-card `<a class="card tool">`, unchanged.
- Don't reach for a global mega-menu again for "make X reachable" — reach for (a) the homepage
  card grid staying exhaustive and fully clickable, and (b) a `.related` pill on the relevant pages.
  A dropdown only earns its keep for something genuinely global and rarely-changing (there wasn't
  one here); it was cut once it turned out to duplicate the homepage and hurt mobile.

**§5 VERIFICATION DISCIPLINE for this site**: checking that every `href` resolves to a real file is
NOT proof of reachability. The proof is a reachability WALK: starting from `index.html`'s nav AND
from one deep page's nav (e.g. `phasor.html`), using ONLY the nav + visible on-page links (no
address-bar typing), list the click-path to every other page, confirm ≤2 clicks, AND separately
check the nav is compact on a narrow (≤640px) viewport — that was the failing case both times this
got re-litigated. Do this walk any time the nav, page set, or card grid changes; record it in the
task's return message. An href-audit alone is not this check (that was the 2026-08-26 mistake).

**SEO/nav facts still true**: `sitemap.xml` lists all 16 pages + the 4 live tool routes + `/tools/`.
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
  existing plain product page (e.g. `phasor.html`) INCLUDING its lean nav (`Home · HoloDb ·
  Packages · NuGet`) and its `.related` pills row, add it to `index.html`'s gallery (pick a category
  colour, add the `.card-link` overlay — this alone makes it reachable in ≤2 clicks from every page
  via Home/Packages → the gallery, so it's the step that actually matters), then add it into the
  `.related` pills of its closest 1-2 siblings (not every page — contextual, not exhaustive), add the
  nav crumb pattern, add it to `sitemap.xml`, and update the package count in `index.html`'s hero
  facts + this file's Site map section. Re-run the reachability walk (Navigation section, above)
  before calling it done, and check the nav is still compact on a narrow viewport.

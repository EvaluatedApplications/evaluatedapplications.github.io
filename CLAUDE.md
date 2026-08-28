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

**One stylesheet**: `site/assets/site.css`. Dark, UNCONDITIONALLY — this is a branded visual
identity (Dark Side of the Moon), not a neutral utility UI, so it never defers to the visitor's
system/browser colour-scheme. **Changed 2026-08-28**: a `prefers-color-scheme:light` palette
override used to exist (`:root` block + a `.prism-beam` opacity tweak) and was REMOVED, direct user
instruction after real-phone testing showed it firing and washing the brand out to white/pastel on
a phone in light mode ("get rid of light pallets then dark always"). Don't reintroduce a light
palette without an explicit, separate request — and if one's ever wanted, gate it behind an opt-in
control, not automatic OS detection. Design tokens: `--bg/--bg-2/--surface/--surface-2`,
`--border/--border-2`, `--ink/--ink-soft/--ink-faint`, `--accent/--accent-ink`, `--spectrum`
(brand gradient), 4 category colours `--c-foundation` (purple, Phasor/EvalApp) /
`--c-data` (blue, HoloDb family) / `--c-ml` (pink, AlgFormer family/EvalApp.Neural/Prose) /
`--c-spatial` (green, Tracer/HoloVoxel), `--ok/--warn/--bad`, `--radius`, `--wrap` (1080px),
`--font`/`--mono`. This same file also styles the Blazor tools shell's loading/error UI
(`#app:has(.loading-progress)`, `#blazor-error-ui`) via the shared tokens, but that's the ONLY
reach into `Showroom/`'s presentation from here — its own component styles are `showroom-owner`'s
territory (see "Prism motif" below for a live coordination flag on this boundary). Reusable components: `.site-nav` (sticky, CSS-only mobile burger via
`.nav-toggle` checkbox hack — lean, always a flat list of 4-6 plain links, see Navigation below)
+ `.related` (compact contextual cross-link pills in the hero, see Navigation),
`.hero`/`.eyebrow`/`.lede`/`.facts`/`.fact`, `.sec`/`.sec-head`,
`.grid`/`.card` (the package-card pattern, `--cat` custom prop sets the left accent bar,
`.card-link` stretched-link overlay makes the whole card clickable — see Navigation),
`.install` (copy-button code chip), `.btn`/`.btn-primary`/`.btn-ghost`, `.crumb` (breadcrumb),
`.stack` (callout box, `.flow` for pipeline diagrams), `.prose`/`.toc` (manual/docs pages),
`.snip`/`.lim` (code-sample box / caveat note — added 2026-08-26 for the product-page rollout,
reused by 9+ pages), `footer.site`. **`.hero-bar`/`.hero-body`/`.hero-content`/`.win-dots` + the
`body.os-chrome` opt-in system** (window-panel chrome, taskbar/dock nav, mobile icon grid — added
2026-08-28, live on 3 pages only so far, see the dedicated "OS chrome" section below for the full
component list and the sweep recipe). A handful of legacy pages (`evalapp.html` pre-rewrite,
`holodb/index.html`, `holodb.html`) used to duplicate these tokens in a local `<style>` block;
`evalapp.html` was migrated onto `site.css` in the 2026-08-26 rewrite (see Reconciliations). The
two HoloDb pages still carry a local `<style>` (bespoke charts/race-demo/table markup that isn't
reused elsewhere) but declare the SAME token values, so they read as one brand, not a fork — if a
token in `site.css` ever changes, grep those two files' `<style>` blocks too.

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
- **`.prism-beam` (NEW component, hero graphic — NOT swept everywhere)**: a small decorative inline
  SVG (white beam → triangle outline → 7-line ROYGBIV fan), CSS-positioned absolute behind the hero
  text (`.hero` now `position:relative;overflow:hidden`, `.hero>.wrap` lifted to `z-index:1`),
  right-aligned, capped `min(40vw,520px)` wide, `opacity:.65`, hidden below 900px so it can't collide
  with hero copy once it wraps to fewer chars/line on tablet. Currently on exactly **2 pages**:
  `index.html` (the flagship/DSOTM-analogue hero) and `algformer.html` (the literal "Prism" tool is
  linked from that page's hero CTA + Try-it-live grid, so the visual motif and the product name
  finally point at the same place). This was a deliberate scope call, not an oversight — the task
  asked for a reviewable before/after on representative pages rather than a silent 16-page sweep;
  extending `.prism-beam` to more hero pages is a fast follow (same markup block, paste into any
  `<header class="hero">`) whenever that's wanted, see Queued/Flagged in the task return.
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

## Navigation — the reachability contract (owned by this agent, not the coordinator)

**The invariant**: every page reachable in ≤2 clicks from every OTHER page's nav, without a global
mega-menu bloating every page (tried a 5-column/17-item `<details>` dropdown 2026-08-26, user
feedback: too dense, too large on mobile, and it duplicated what the homepage already does — see
below for the revised shape).

**The shape (revised 2026-08-28, third pass — see "OS chrome" below for why this changed)**: three
layers, each doing ONE job.
1. **Lean top nav, identical shape everywhere**: `Home · Packages(→/#packages) · Tools(→/#tools) ·
   NuGet`, plain text links, no dropdown, no JS. **Changed 2026-08-28**: this used to pin `HoloDb` as
   a permanent 2nd slot — a historical artifact (it was the first/richest page built), not a
   principled choice, and it gave HoloDb a 1-click universal link that the other 10 packages didn't
   get (user flag: "top level ones should be relevant not arbitrary eg. HoloDb is pinned only").
   Fixed by swapping the HoloDb-specific slot for `Tools`, which — like `Packages` — routes to a hub
   section (`/#tools`) rather than to any one product, so nothing is singled out. HoloDb is still
   ≤2 clicks from everywhere via `Packages` → its card, exactly like the other 10 packages, and is
   still 1 click from most pages via its `.related` pill (see layer 3). A few pages add 1-2
   page-appropriate items on top of the 4 universal ones (the HoloDb hub: `Home · Packages · Tools ·
   Benchmarks · Docs · NuGet`, 6 items — at the compactness ceiling; the manual: adds `Manual`+`The
   Analyst`) but NEVER more than ~6 items total — that's the compactness bar, checked on a narrow
   viewport.
   **Rollout status**: only the 3 pages listed under "OS chrome" below carry this new nav shape as of
   2026-08-28. The other 14 `site/**/*.html` files still carry the OLD `Home · HoloDb · Packages ·
   NuGet` shape — a real, temporary cross-page inconsistency, open until the sweep (tracked in "OS
   chrome" below, same sweep as the chrome rollout since both touch the same nav markup in the same
   pass). Of those 14, 12 are ordinary content pages with the standard nav; the other 2
   (`404.html`, a brand-only bounce page with no `.nav-links` at all, and the unlisted
   `recycledao-preview.html`, which deliberately uses a standalone non-EA header per its own
   CLAUDE.md entry) never carried the pinned-HoloDb pattern and don't need this particular fix.
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

## OS chrome (in review, 2026-08-28 — NOT yet swept past 3 pages)

Desktop/wide viewports get a window-panel treatment (bordered "window" frame around `.hero` and
every `.sec` that carries a `.sec-head`, a titlebar with decorative traffic-light dots, the nav
restyled as a floating taskbar/dock); narrow viewports get a phone-home-screen treatment (nav
collapses to a fixed bottom icon dock, the `#packages`/`#tools` galleries become an app-icon grid).
**Chrome only** — no JS, no draggable/resizable windows, static HTML/CSS unchanged in cost; every
element it touches is real, already-linked markup (nav `<a>`, `.card-link`, `a.tool`) — narrowing a
mobile package card to an icon hides its description text, never its link.

**Opt-in via `<body class="os-chrome">`.** Currently on exactly 3 pages, deliberately not swept
further yet (user asked for a reviewable subset first): `index.html` (flagship — also exercises the
`.prism-beam`/`.hero-content` z-index interplay, see below), `phasor.html` (a plain product page —
the lean-template stress test), `holodb/index.html` (the richest page — 6 windowed sections, tables,
a bespoke SVG diagram, and the widest nav, all inside the same chrome system). The other 14 pages
(including `algformer.html`, which also carries `.prism-beam`) are untouched and still render the
pre-chrome design — this is an **intentional, temporary split**, not a regression; sweeping it is
the next step once this subset is reviewed.

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
`os-chrome`, currently just `index.html`)**: once `.hero > .wrap` gets an opaque window-panel
background, a `.prism-beam` positioned as a *sibling* of `.wrap` (the pre-chrome markup) would sit
fully behind that new opaque panel and vanish. Fixed by moving `.prism-beam` to be the first child
*inside* `.hero-body` instead, with the real hero text wrapped in one more div, `.hero-content`
(`position:relative;z-index:1`), so the beam (`position:absolute;z-index:0`, unchanged CSS) paints
behind the text but on top of the now-opaque panel background — and its bleed (`right:-40px`) gets
tastefully clipped by the panel's `overflow:hidden` instead of hanging off the page. `algformer.html`
also carries `.prism-beam` but is NOT yet on `os-chrome`, so it's unaffected today; **when
`algformer.html` is swept, its hero markup needs the same `.hero-content` wrapper**, or its beam will
silently disappear behind the new window panel — this is the one page in the remaining 14 where the
"zero-markup" claim above doesn't hold and an extra wrapper is required.

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

**Sweep recipe for the remaining 14 pages** (once this subset is approved): add `class="os-chrome"`
to `<body>`; add nav-links `Tools` (`/#tools`) in place of the old `HoloDb` slot, keeping any
page-specific extra items after it; wrap the hero's real content in `<div class="hero-bar"
aria-hidden="true"><span class="win-dots">...</span><span class="hero-bar-title">NAME.app</span>
</div><div class="hero-body">...</div>`; if the page also carries `.prism-beam` (only
`algformer.html` today), additionally wrap the real text in `.hero-content` per the gotcha above.
Everything else (`.sec` window framing, taskbar styling) needs no further HTML changes — it's already
live in `site.css` and activates the moment `os-chrome` is on the page.

**Recommendation, not acted on (Showroom is out of scope for this agent)**: the same chrome system
(taskbar-style nav, window-panel framing) would read as a natural extension into `Showroom/`'s own
UI for a cohesive OS feel across the whole site+tools experience — flagged for the coordinator to
route to `showroom-owner` if wanted, not something to reach into from here.

## Deploy

`.github/workflows/deploy.yml`, triggered on push to `main` (Pages Source must be "GitHub
Actions", one-time repo setting). Steps: `dotnet publish Showroom/Showroom.csproj` →
`_site/ = site/* + published wwwroot under _site/tools/` → upload-pages-artifact → deploy. You
(website-owner) never run this or commit/push — leave changes in the working tree; the
coordinator batch-commits and the user pushes to publish.

**HARD COUPLING (structural, 2026-08-28)**: `dotnet publish Showroom` is step 1 of the single
`build` job. If Showroom fails to compile, the job aborts *before* `upload-pages-artifact`, so
**nothing deploys — including all 17 purely-static content pages**, which have no dependency on
Showroom whatsoever. A compile error in one tool page takes the whole public site's updates offline.
Before assuming a content change is live, check Showroom actually builds
(`dotnet build Showroom/Showroom.csproj -c Release`). Decoupling this is a design item in the
platform initiative below. *(Noted after a build failure that turned out to be a transient mid-edit
snapshot of another owner's concurrent work, not a real defect — the coupling is real regardless of
what triggers it, but don't record such a collision as a bug. Reading another repo's files mid-flight
can catch a half-landed edit; re-check before reporting.)*

## Platform initiative (in flight, 2026-08-28) — verified facts

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
- **Motion hooks already exist** in `site.css`: both `@media (prefers-reduced-motion:no-preference)`
  and `@media (prefers-reduced-motion:reduce)` blocks are present, so the requested scroll-tied depth
  system has a correct accessibility opt-out to hang off from day one — it must be gated there, and
  the effect must be a pure function of scroll position (CSS `animation-timeline: view()`), never a
  JS scroll handler, or it will fight the mobile performance budget.

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

**Self-maintenance note**: this file is ~360 lines, well over the ~200-line guide. Deliberately not
compacted yet — the initiative will obsolete large parts of the Site map / Design system sections, so
the compaction pass should happen when the architecture lands, not before.

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
  existing plain product page (e.g. `phasor.html`) INCLUDING its lean nav (`Home · Packages · Tools ·
  NuGet` — see Navigation section; NOT the older HoloDb-pinned shape some of the 14 not-yet-swept
  pages still carry) and its `.related` pills row, add it to `index.html`'s gallery (pick a category
  colour, add the `.card-link` overlay — this alone makes it reachable in ≤2 clicks from every page
  via Home/Packages → the gallery, so it's the step that actually matters), then add it into the
  `.related` pills of its closest 1-2 siblings (not every page — contextual, not exhaustive), add the
  nav crumb pattern, add it to `sitemap.xml`, and update the package count in `index.html`'s hero
  facts + this file's Site map section. Re-run the reachability walk (Navigation section, above)
  before calling it done, and check the nav is still compact on a narrow viewport.

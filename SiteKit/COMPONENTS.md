# SiteKit component inventory (spec, not code — Phase 0)

One entry per reusable pattern. For each: what it is, its exact markup shape today (elided where
long), which custom props/classes it depends on, and its current independent implementations —
the static-HTML shape (`site/**/*.html`, hand-typed per page, ~17 copies) and, where one exists,
Showroom's own shape (`.razor`/`.razor.css`, hand-typed once, different syntax, same intent).
This is the transcription source for Phase 1's real Razor components (see
`AboutUs/docs/platform-architecture.md`) — build each component FROM its entry here, not by
re-reading `site.css` and `MainLayout.razor` side by side again.

Full CSS rules for everything below live in `site/assets/site.css` (search the class name); this
file documents the *contract* (markup shape + props), not a second copy of every declaration.

---

## 1. `<BrandMark>` — the prism triangle
**Values**: see `tokens/brand-ea.css`'s trailing comment (viewBox, path, gradient stops).
**Current shape**: an inline `<svg class="mark">` (nav) repeated as a `data:image/svg+xml` favicon
`<link>` (per `<head>`), both literal, both in every one of 17 static pages. Showroom repeats a
third copy in `Layout/MainLayout.razor` and a fourth in `wwwroot/index.html`'s favicon. **4
independent hand-typed copies of the same 3 lines of SVG is the exact failure this toolkit
exists to fix** — a real `<BrandMark size="nav|favicon" />` component (Phase 1) renders both
call shapes from one source; until then, a sweep needs to touch 4 places, not 1.

## 2. Site nav — `.site-nav` / `.wrap` / `.brand` / `.nav-toggle` (checkbox hack) / `.nav-links`
**Markup** (static site, verbatim from `phasor.html`):
```html
<nav class="site-nav"><div class="wrap">
  <a class="brand" href="/"><svg class="mark">...</svg> Evaluated Applications</a>
  <input type="checkbox" id="navtoggle" class="nav-toggle">
  <label for="navtoggle" class="nav-burger" aria-label="Toggle menu"><span></span><span></span><span></span></label>
  <div class="nav-links">
    <a href="/">Home</a><a href="/packages.html">Packages</a>
    <a href="https://www.nuget.org/...">NuGet</a>
    <!-- 0-3 extra page-appropriate items, never past ~6 total -->
  </div>
</div></nav>
```
CSS-only mobile disclosure (no JS): `input:checked ~ .nav-links{display:flex}`. **This IS the
reusable core's "responsive nav" answer** — brand-agnostic, structural, belongs in `core.css`'s
component layer (not `brand-ea.css`).
**Showroom's shape**: `Layout/MainLayout.razor`'s own header, hand-typed Razor with the SAME
class names (`site.css` is directly `<link>`ed into `Showroom/wwwroot/index.html`, so the CSS is
already shared today — only the MARKUP that emits those classes is duplicated). Known drift:
Showroom's nav carries 7 items vs. the static site's lean 3-6 (flagged, unresolved, in
`AboutUs/CLAUDE.md`'s Platform initiative section) — exactly the kind of thing a single shared
`<SiteNav Items="@navItems" />` component would make structurally impossible to diverge on.

## 3. Footer — `footer.site .mono`
`© <year>` + 3-5 internal links, page-appropriate subset. Plain text, no component complexity —
still worth a shared `<SiteFooter Links="@links" />` in Phase 1 purely so the year-stamp script
and link-set pattern live in one place instead of copy-pasted into every page's closing script tag.

## 4. Hero — `.hero` / `.hero-bar` / `.win-dots` / `.hero-body` / `.hero-content` / `.crumb` /
`.eyebrow` / `.lede` / `.facts`/`.fact` / `.install` / `.cta-row` / `.related`
The page-opening block. `.hero-bar` (the `os-chrome` window titlebar: 3 dots + a fake "app name"
title) is optional chrome layered on top of a plain `.hero` — see the OS-chrome entry below for
when that layer is present vs. not. `.related` is the contextual cross-link pill row (2-4 sibling
pages + an "All packages →" pill) that makes the site's ≤2-click reachability invariant hold
without a mega-menu — this pattern (curated related-content pills, not a nav item) is genuinely
reusable for any content site with an inter-linked catalogue, brand-agnostic.
**Showroom's shape**: `.room-head` (crumb + h1 + lede + badges) plays the same role for a tool
page, deliberately NOT identical markup (a tool page has no `.facts`/`.install`/`.cta-row` — see
`platform-architecture.md`'s content/app boundary discussion for why that's correct, not a gap).

## 5. Card / grid — `.grid` / `.card` / `.card-link` (stretched-link overlay) / `--cat` / `--cat-root`
The package-card and tool-card pattern. `--cat` is a per-card CSS custom prop holding either a
solid colour OR a gradient (both valid `background` values); `--cat-root` is a SOLID-only sibling
for the few call sites (`color-mix()`, the OS-chrome mobile icon-tile gradient) that can't accept a
gradient as an input. **The "chord" pattern** (a card whose subject spans 2 real dependency
domains, e.g. `prose.html`, or The Creature tool card) sets `--cat` to a hard-edged 2-stop
`linear-gradient(90deg, var(--c-X) 0%, var(--c-X) 50%, var(--c-Y) 50%, var(--c-Y) 100%)` rather
than blending in RGB space (blending distant hues muddies; hard edges read as two distinct notes
sounding together — the "chord" framing is literal, not a metaphor). `.card-link` is a full-bleed
`<a class="card-link" href position:absolute;inset:0;z-index:1>` as the FIRST child of an
`<article class="card">` (never a bare `<a class="card">` once the card nests a second link, e.g.
a NuGet link or a "Powered by" pill — invalid nested-anchor markup otherwise); everything the card
nests that must stay independently clickable is lifted to `z-index:2`.
`@media(max-width:640px){.grid{grid-template-columns:1fr}}` is a real, load-bearing fix (a 2026-08
mobile-overflow bug) — any reusable grid component must ship this from day one, not rediscover it.

## 6. OS-chrome shell — `body.os-chrome` opt-in: `.hero-bar`/`.win-dots` (titlebar), a taskbar/dock
nav, mobile icon-grid variants of `#packages`/`#tools` `.grid`. Desktop/wide viewports get
window-panel framing (`.sec:has(.sec-head)` grows a fake app-window chrome) — a purely decorative,
opt-in visual treatment layered on top of the plain component set above via one class on `<body>`,
not a parallel page structure. **Rollout status (as of this doc): 3 of 16 static pages carry it**
(`index.html`, `phasor.html`, `holodb/index.html`) — a deliberately paused sweep, not abandoned;
see `AboutUs/CLAUDE.md`'s Navigation section. Showroom has no equivalent today (`Home.razor`'s own
`.hero` reuses the plain `.hero` class, no window-chrome layer) — an open question for whether
Showroom's shell should ever opt in, noted, not decided, in `platform-architecture.md`.

## 7. Scroll-tied parallax depth + spotlight glow — 3-tier `animation-timeline:scroll()/view()`
system, zero JS, gated behind `prefers-reduced-motion:no-preference`. Reads `--glow-near`/
`--glow-mid` (brand-file tokens, per-page-tinted via `body[data-cat="..."]` — see
`brand-ea.css`). **This mechanism itself (the 3-tier structure, the scalar-locked shadow-angle
math, the opacity-multiplier gotcha it documents) is brand-agnostic and belongs in a shared CSS
module** (`core.css`'s eventual companion, not written yet — today it's inline in `site.css`,
search "SCROLL-TIED PARALLAX DEPTH"); only the `--glow-*` VALUES it reads are brand-specific.
Showroom independently re-derived the same concept for its own page shape in
`wwwroot/css/depth.css` (different selectors — no `.hero`/`.sec` on a tool page — same tier
structure, same magnitude-tuning history) rather than sharing a module, because no shared module
existed to reuse. This is the single clearest case in the whole system for "one shared CSS
component, parameterised per page shape" instead of two independent re-derivations that must be
kept in sync by hand whenever the magnitude gets retuned again.

## 8. `.stack` / `.snip` / `.lim` / `.prose` / `.toc` / `.crumb` / `.btn`/`.btn-primary`/`.btn-ghost`
Smaller reusable atoms: callout box, code-sample box, caveat note, long-form article typography +
table-of-contents, breadcrumb, buttons. All brand-agnostic, all straightforward Phase-1 component
candidates (or, for the simplest ones, plain CSS classes a client site just uses directly without
needing a Razor wrapper at all — not every reusable thing needs to become a component).

## 9. `.pkg-strip`/`.pkg-chips`/`.pkg-chip`, `.powered`/`.powered-label`
The lightweight "acknowledge related things exist without a full card" pattern — smaller than
`.related`, no install/NuGet link, just a name + colour dot. Generalises cleanly to any client
site that wants a "built with / powered by" strip without AboutUs's specific package semantics.

---

## What's genuinely AboutUs-specific (does NOT belong in the reusable core)
- The prism-triangle brand mark's literal geometry, the ROYGBIV spectrum values, the per-package
  hue table, the "chord" domain-weighting formula's DOMAIN LIST (Foundation/Data/ML/Spatial) —
  all in `brand-ea.css`, all swappable as one file.
- The actual page CONTENT (package pitches, feature lists, the 11-package catalogue, article
  prose) — already correctly separated today via `docs/site.md` (see `platform-architecture.md`'s
  content-pipeline section for how this generalises to "any client's content docs").
- The specific tool set (Analyst/Creature/Forecaster/Prism) and their domain logic — obviously
  AboutUs/EA-only; a client site's "islands" would be their own interactive features.

## What's genuinely reusable core (the toolkit)
Sections 2, 3, 4 (structure, not `.related`'s curated content), 5 (mechanism, not the hex values),
6 (the opt-in chrome mechanism), 7 (the mechanism, not the glow colours), 8, 9. Roughly: every
component's STRUCTURE and BEHAVIOUR is core; every component's COLOUR and COPY is brand/content.

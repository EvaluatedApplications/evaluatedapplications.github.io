# Brand identity — tools-first pivot + prism colour system

Planning document only. Nothing in `site/**` or `site/assets/site.css` was touched to produce this.
Written by website-owner for the user to review before any implementation pass. Ground truth checked
directly against the real repos before writing anything below (package `.csproj` files in `MonoRepo/`,
`Showroom/Showroom.csproj` and `Showroom/Pages/*.razor`, `site/index.html`, `site/assets/site.css`,
`AboutUs/CLAUDE.md`) — nothing here is asserted from memory or assumption.

Three flagged decisions need your confirmation before this becomes an implementation task — they're
marked **DECISION** inline and summarised at the very end.

---

## 0. Executive summary

- **Homepage becomes tools-only at the top level.** The 11-package gallery moves off `index.html`
  entirely, onto its own new page (`site/packages.html`). Reachability survives unchanged: nav's
  `Packages` link still reaches it in 1 click from anywhere, any package card in 1 more — same
  ≤2-click guarantee the site already enforces, just re-pointed at a real URL instead of a same-page
  anchor.
- **White = Phasor + EvalApp (Foundation).** Reinterpreted literally as "the undispersed beam" —
  concretely, `--c-foundation` becomes the site's existing `--spectrum` gradient (not a new colour),
  with a flat `#ffffff` companion token for contexts that can't render a gradient. This directly
  reuses infrastructure already in `site.css`, doesn't fork it.
- **Every package domain gets a real ROYGBIV band, not an arbitrary colour.** Data (HoloDb family)
  and Spatial (Tracer/HoloVoxel) already sit on real spectrum hues (Blue, Green) and are unchanged.
  ML (AlgFormer family) currently sits on an **off-spectrum pink** (`#e879c8`, not one of the site's
  7 ROYGBIV stops) — **DECISION 1**: recommend moving it onto Indigo (`--spectrum-6`, `#7d7dff`), a
  real spectrum band, so every domain — and therefore every tool chord built from those domains — is
  actually "visible light," which is the premise you asked for.
- **A tool's colour is a reproducible chord, not a hand-pick.** Verified each live tool's real
  dependencies against its actual `.csproj`/source (not assumed): The Analyst = HoloDb only (pure
  Data note). The Creature = AlgFormer + Tracer (ML/Spatial two-note chord — the one genuine blend
  among the 4 live tools today). The Forecaster and Prism both = AlgFormer only (pure ML note). A
  concrete, mechanical formula derives this from any tool's dependency list — spec'd in full in §2,
  reproducible for a 5th tool without a new design decision.
- **Six of 11 packages power no tool today** (verified against `Showroom.csproj`, not assumed):
  HoloDb.Protocol, HoloDb.Client, HoloVoxel, EvalApp.Neural, AlgFormer.Gpu, **and Prose** — that
  last one isn't on your own list but I checked and it's also unreferenced by any tool. **DECISION
  2**: my recommendation is below in §1 (they stay fully reachable via the relocated `packages.html`,
  which is still the real ≤2-click guarantee; framed there as "not yet in a tool" rather than pretending
  a drill-down path exists that doesn't).

---

## 1. The IA pivot

### What "top level" means now
`index.html`'s hero → `#tools` grid is the entire top-level content. No package gallery, no "how it
fits together" flow diagram competes with it on the homepage. Those two blocks (today's `#packages`
section + the closing `.stack` flow diagram) move, close to verbatim, onto a **new page**,
`site/packages.html` — same 4-category layout (Foundation/Data/ML/Spatial), same cards, same
`.card-link` whole-card-clickable pattern, standard page template (hero/nav/footer) around it instead
of living inline on the homepage.

### The homepage, concretely (implementation-ready shape, not yet built)
1. **Nav** — `Home · Packages · NuGet`. **Recommend dropping the `Tools` nav item**: once tools *are*
   Home's entire top-level content, a nav item that scrolls to `/#tools` is redundant with `Home`
   itself. Net effect: nav shrinks from 4 items to 3, comfortably under the site's own ~6-item
   compactness ceiling. (`#tools` keeps its `id` for deep-linking even without a nav entry pointing at
   it — a tool page's own `.related` pills or an external link can still land there directly.)
2. **Hero** — same `.prism-beam`/`.hero-content` shape, copy re-weighted from "11 packages" to
   "4 free tools, live in your browser" as the lead fact chip; package count demoted to a smaller
   supporting fact, not removed.
3. **`#tools` section** — unchanged 4-card grid, `live` tags unchanged. Two additions per card:
   (a) each card's `--cat` custom prop becomes that tool's **chord** (§2) instead of a borrowed
   category colour; (b) a small "Powered by" pill row under the description, one pill per constituent
   package, each linking straight to that package's own page (`/holodb/`, `/algformer.html`,
   `/tracer.html`) — this pill row **is** the "further drilling down into the packages that power
   them" the pivot asked for, reachable with zero extra clicks past what's already on Home.
4. **NEW: a slim "Powered by" strip**, directly below the tools grid — package name chips (name +
   small colour dot, no description/install/NuGet link, deliberately much lighter than a `.card`) for
   a compact acknowledgement that packages exist, plus one `See all 11 packages →` link to
   `/packages.html`. This is the *only* packages-related content left on the homepage, and it's
   explicitly secondary (smaller type, no cards, no grid) so it can't read as a second top-level
   gallery competing with `#tools`.
5. **Footer** — same shape, `Packages` link now points at `/packages.html` instead of `/#packages`.

### `packages.html` (new page, relocated content)
Standard page template. Hero carries the "11 packages, one shared foundation" framing that used to
open `index.html`'s package section. Body = the 4-category `.grid` of `.card`s (unchanged markup,
literally lifted), then the "how it fits together" flow diagram (also lifted, unchanged). Every card
keeps its `.card-link` whole-card overlay, install snippet, NuGet link — nothing about an individual
package page changes.

**Recommend one addition while it's being built**: a one-line "Powers: The Analyst, The Creature" /
"Not yet in a tool" annotation per card (see Decision 2 below) — cheap, and turns the relocated
gallery into a genuine catalogue rather than a page that quietly demotes 6 of 11 packages with no
explanation.

### Reachability contract — verified unchanged
The site's hard invariant is every page ≤2 clicks from every other page. Walk, using only nav +
on-page links, from a package page (e.g. `phasor.html`) that isn't otherwise linked from Home:
`phasor.html` nav → `Packages` (1 click) → `/packages.html` → any of the other 10 cards (1 click) =
**2 clicks**, identical guarantee to today (today it's `Packages` → `/#packages`; tomorrow it's
`Packages` → `/packages.html`, same click count, just a real URL instead of a same-page anchor). From
Home itself: tools are 0 clicks (already on the page) to 1 click (via the "Powered by" pill row) to
reach a package page — **shorter** than today for the 5 tool-connected packages, since previously a
visitor had to scroll past the tools grid to a separate `#packages` section on the same page; now a
relevant package is reachable from inside its tool's own card.

Nothing in this pivot removes a link — it relocates a section and adds pills. The reachability-walk
discipline in `CLAUDE.md` §"Navigation" still applies at implementation time (re-run it on the actual
markup, on a narrow viewport too, before calling that pass done).

### DECISION 2 — the 6 orphan packages (HoloDb.Protocol, HoloDb.Client, HoloVoxel, EvalApp.Neural,
AlgFormer.Gpu, Prose)
None of these power a tool, so none get a "Powered by" pill or an inline drill-down from Home. They
remain **fully clickable within the same ≤2-click invariant** via `packages.html` — this is not a
broken link, just no shortcut from a tool card. My recommendation, flagged for confirmation rather
than assumed:
- Keep them on `packages.html` exactly where they already sit (grouped by domain), and add the
  one-line "Powers: …" / "Not yet in a tool" annotation above so a visitor understands why some cards
  don't have an "Open live →" sibling nearby, instead of it silently reading as an inconsistency.
- Longer-term, the more satisfying fix for at least one of them is a genuine product decision, not a
  brand-identity one: a 5th tool that demos HoloVoxel (a small in-browser voxel-world view) would be
  the most visually compelling of the six to promote out of "catalogue-only" status. That's a
  Showroom/product-roadmap call for the coordinator, not something this document decides — noting it
  here only so it isn't lost.
- Alternative I considered and am **not** recommending: forcing a synthetic "powered by" link from an
  existing tool to one of these packages (e.g. claiming The Analyst is "powered by" HoloDb.Client)
  would be dishonest — HoloDb.Client is the *remote* client, The Analyst runs HoloDb in-process,
  verified in `Analyst.razor` (`@using HoloDb`, `Database.Open(null)` — no client/server split). Don't
  paper over a real gap with an inaccurate dependency claim.

---

## 2. The colour system

### The existing tokens (verified in `site/assets/site.css`, unchanged unless noted)
```
--spectrum: linear-gradient(90deg, #f0796a 0%, #f0a15a 16.6%, #e6c450 33.3%, #7bd86a 50%,
                             #4aa3ff 66.6%, #7d7dff 83.3%, #c07dff 100%)
--spectrum-1 #f0796a (R)   --spectrum-2 #f0a15a (O)   --spectrum-3 #e6c450 (Y)
--spectrum-4 #7bd86a (G)   --spectrum-5 #4aa3ff (B)   --spectrum-6 #7d7dff (I)   --spectrum-7 #c07dff (V)
--c-foundation #8b7dff   --c-data #4aa3ff   --c-ml #e879c8   --c-spatial #7bd86a
--accent #8b7dff (same value as today's --c-foundation)
```
Today's category colours already partly overlap the spectrum (`--c-data` == `--spectrum-5`,
`--c-spatial` == `--spectrum-4`), but `--c-foundation` is a distinct violet (not a spectrum stop) and
`--c-ml` is an **off-spectrum magenta** that isn't any of the 7 ROYGBIV values at all. That's the
concrete gap between what exists and what you're asking for: not every domain colour is actually "in
the visible spectrum" yet.

### Real dependency graph (verified against every relevant `.csproj`, not assumed)
```
Phasor         -> (no deps)                         FOUNDATION
EvalApp        -> (no deps)                         FOUNDATION
HoloDb         -> EvalApp, Phasor                    DATA
HoloDb.Client  -> (HoloDb.Protocol; not audited further, same domain)   DATA
HoloDb.Protocol-> (network codec; same domain)       DATA
AlgFormer      -> EvalApp, Phasor                    ML
AlgFormer.Gpu  -> AlgFormer (same domain)            ML
EvalApp.Neural -> EvalApp (same domain)               ML
Prose          -> HoloDb + AlgFormer (cross-domain; classed ML on index.html today, left as-is)
Tracer         -> EvalApp only (NOT Phasor — verified, real asymmetry)   SPATIAL
HoloVoxel      -> Phasor (same domain)                SPATIAL

Showroom (the tools app) -> HoloDb 1.4.0, AlgFormer 1.5.0, Tracer 1.1.0 (direct PackageReferences,
  verified in Showroom.csproj) + EvalApp transitively (AlgFormer's own dependency) + HoloKernel (an
  in-repo RCL that is itself NuGet-only against AlgFormer 1.5.0 — so a tool using HoloKernel touches
  ML even without its own direct AlgFormer PackageReference).

The Analyst   (Analyst.razor)   -> @using HoloDb                          -> {Data}
The Creature  (Creature.razor)  -> @using HoloKernel, @using Tracer.Helpers -> {ML, Spatial}
The Forecaster(Forecaster.razor)-> @using HoloKernel                       -> {ML}
Prism         (Prism.razor)     -> @using HoloKernel                       -> {ML}
```
This confirms your own read exactly (Creature = AlgFormer+Tracer chord, Analyst = a pure HoloDb note,
Forecaster and Prism both lean on AlgFormer alone) — verified against the real files rather than
taken on trust.

### DECISION 1 — reassign ML off the off-spectrum pink, onto Indigo
`--c-ml` changes from `#e879c8` to `--spectrum-6` (`#7d7dff`, Indigo). Reasoning: the whole premise of
"tools sit in the visible spectrum too" only holds if every domain a tool's chord can draw from is
actually a spectrum colour. Today 3 of the 4 live tool cards (Creature/Forecaster/Prism) already use
`--c-ml` as their `--cat` — so this single token change is *also* what fixes their off-spectrum look,
not a separate cosmetic pass. Concretely this re-tints, sitewide: the ML category dot/cards on the new
`packages.html` (AlgFormer, AlgFormer.Gpu, EvalApp.Neural, Prose), and 3 of 4 tool cards on the
homepage, plus anywhere those pages' OS-chrome mobile icon tiles or nav taskbar tiles reference
`--c-ml` (`index.html`, `phasor.html`, `holodb/index.html` — the 3 pages currently on `os-chrome`).
This is a real, visible, sitewide re-tint — not something to slip in silently, hence flagged as its
own decision rather than folded into "the pivot."

If you'd rather keep the ML pink and treat it as a deliberate house exception to the spectrum
metaphor, say so and §2 below still works — just substitute `#e879c8` back in everywhere `--c-ml` is
used as a chord ingredient; the chord *math* doesn't depend on which hex ML resolves to.

### Foundation = the beam, not a violet
`--c-foundation` is redefined from a flat violet to the literal `--spectrum` gradient — reusing the
gradient already defined and already painting the page-top `.beam` strip, no new asset. A flat
companion token, `--c-foundation-solid:#ffffff`, is added for the few call sites that structurally
can't take a gradient (see the CSS gotcha below). This is a genuine two-tier pattern (paintable value
+ solid fallback), and — important for internal consistency — it's the exact same two-tier shape the
tool-chord system needs anyway (§ below), so it isn't a one-off special case, it's the same mechanism
used twice.

Foundation's cards (Phasor, EvalApp) get the brightest accent on the whole site: pure white against
the deep-black `--bg` reads as literally the highest-contrast, most "glowing" element on the page —
appropriate for "the beam everything else is refracted from," and a nice side effect that needs no
extra design work, it falls out of using `#ffffff` honestly.

### The chord formula (mechanical, reproducible for any tool present or future)
1. List the tool's real non-framework dependencies: direct `PackageReference`s in its own `.csproj`,
   **plus** anything reached only via `HoloKernel` (which is itself pinned to AlgFormer — check both,
   as verified above, since a future tool could depend on HoloKernel without its own redundant direct
   AlgFormer reference).
2. Drop Phasor and EvalApp from that list — Foundation/white is assumed present in every tool and
   never contributes a hue to the chord (if it did, every chord would wash toward white equally and
   stop differentiating anything).
3. Map each remaining package to its domain (Data / ML / Spatial / any future domain) using the same
   table `packages.html` already groups packages by.
4. **Weight per domain**, not per package: `weight(domain) = 1 / (number of distinct domains touched)`.
   A tool touching two packages in the same domain (e.g. a future tool using both HoloDb and
   HoloDb.Client) still contributes one domain-weight, not two — the chord is about which *domains*
   sound, not how many packages happen to sit in one.
5. **Order** the domains left-to-right by the site's own canonical category order: Foundation, Data,
   ML, Spatial, then any future domain in the order it's first added to `packages.html`.
6. **Chord gradient** — build a hard-edged `linear-gradient(90deg, …)` by walking the ordered, weighted
   domain list and accumulating each domain's weight into a contiguous percentage range (see worked
   examples below). Hard edges, not a soft blend — averaging distant hues in RGB tends to muddy
   (blue+green mixed straight would read as a dull teal-grey, not "a database note and a pathfinding
   note sounding together"). A tool's chord should look like distinct notes shown together, matching
   the "chord" framing you asked for, not one indistinct in-between colour.
7. **Root/solid colour** — the domain with the largest weight; ties broken by the same canonical
   order (earlier domain wins). This is the fallback value used anywhere a gradient can't render
   (§ CSS mechanics below).
8. A tool that (hypothetically) touched *zero* non-foundation domains degrades to the Foundation
   value itself (the full spectrum / white) — "pure light," the most foundational chord possible, by
   construction rather than a special case.

### Worked chords for the 4 live tools (exact values)
| Tool | Dependencies (verified) | Domains : weight | Chord gradient | Root/solid |
|---|---|---|---|---|
| The Analyst | HoloDb | Data : 1.0 | `var(--c-data)` (single stop, no visible gradient) | `#4aa3ff` |
| The Creature | AlgFormer + Tracer | ML : 0.5, Spatial : 0.5 | `linear-gradient(90deg, var(--c-ml) 0%, var(--c-ml) 50%, var(--c-spatial) 50%, var(--c-spatial) 100%)` | `#7d7dff` (ML wins the canonical-order tie) |
| The Forecaster | AlgFormer | ML : 1.0 | `var(--c-ml)` | `#7d7dff` |
| Prism | AlgFormer | ML : 1.0 | `var(--c-ml)` | `#7d7dff` |

Only The Creature is a genuine multi-note chord today — the other three resolve to a single domain's
flat colour, which is a correct, honest output of the formula (a tool with one real dependency domain
*should* look like one note), not a limitation of it. This also means today's Analyst card needs **no
colour change at all** (it already uses `--c-data` as `--cat`); Forecaster/Prism only change insofar as
`--c-ml`'s hex value itself moves (Decision 1); Creature is the only card whose *shape* changes, from
one flat colour to a two-tone split.

### CSS mechanics (implementation-ready, not applied)
Reuse the existing `--cat` custom property rather than inventing a parallel `--chord` name — every
place that already reads `var(--cat, var(--accent))` already accepts either a solid colour or a
gradient, since `background` is a shorthand that takes either. Add one sibling property, `--cat-root`,
for the few places that need a real solid colour and can't take a gradient. Per-card `style` attributes
become, e.g.:
```html
<!-- Foundation card -->
<article class="card" style="--cat:var(--c-foundation); --cat-root:var(--c-foundation-solid)">

<!-- The Creature tool card (the one real chord) -->
<a class="card tool" style="--cat:linear-gradient(90deg, var(--c-ml) 0%, var(--c-ml) 50%,
                                     var(--c-spatial) 50%, var(--c-spatial) 100%);
                             --cat-root:var(--c-ml)">

<!-- everything else (single-domain): unchanged shape, just style="--cat:var(--c-data)" etc. -->
```
**One real CSS gotcha, found by reading the actual rules, not guessed**: `site.css`'s OS-chrome
mobile icon-tile rule (`body.os-chrome #packages .grid > .card::before, body.os-chrome #tools .grid >
.card::before`, the block that paints the 56px icon tile on narrow viewports) currently does:
```css
background:linear-gradient(155deg, var(--cat,var(--accent)), color-mix(in srgb,var(--cat,var(--accent)) 45%, var(--bg-2)));
box-shadow:0 10px 18px -10px color-mix(in srgb,var(--cat,var(--accent)) 65%,transparent), ...;
```
`color-mix()` requires a real `<color>`, and a gradient can't be nested as a colour-stop inside
another `linear-gradient()`. Both `var(--cat,...)` references inside that one rule need to fall back
to `var(--cat-root, var(--cat, var(--accent)))` instead — this is the **only** existing call site in
`site.css` that breaks once `--cat` can hold a gradient (verified by grepping every `var(--cat` /
`var(--c-foundation` occurrence in the file — the plain `.card::before{background:var(--cat,...)}`
accent-bar rule at line 128 is fine as-is, `background` alone accepts a gradient). Flagging this
precisely so the implementation pass doesn't have to rediscover it by trial and error.

### Reserved spectrum bands
Red (`--spectrum-1`), Orange (`--spectrum-2`), Yellow (`--spectrum-3`), and Violet (`--spectrum-7`)
are not claimed by any current domain — headroom for a future domain category (e.g. if a "Platform /
API" domain is ever carved out for the metered-analytics-API work) without reshuffling anything that
already shipped.

---

## 3. What changes on the homepage (implementation summary)

In order, top to bottom:
1. Nav: `Home · Packages · NuGet` (3 items — `Tools` dropped as redundant with Home itself).
2. Hero: unchanged shape/graphic (`.prism-beam` + `.hero-content`), copy re-weighted toward "4 free
   tools, live now" as the lead fact.
3. `#tools` grid: unchanged 4-card layout; each card's accent becomes its real chord (§2) instead of
   a borrowed category colour, plus a new "Powered by" pill row linking to constituent package pages.
4. NEW slim "Powered by" strip: package-name chips + one "See all 11 packages →" link to
   `/packages.html`. The only packages content left on the homepage, deliberately lightweight.
5. Footer: unchanged shape, `Packages` link re-pointed at `/packages.html`.

Everything currently in `index.html`'s `#packages` section and the closing "how it fits together"
`.stack`/`.flow` moves to the new `site/packages.html`, near-verbatim, wrapped in the standard page
template.

---

## 4. Other brand-identity implications

- **`sitemap.xml` / dead-anchor rule gets simpler, not harder.** `CLAUDE.md` currently carries a
  standing gotcha: "`#packages` must NEVER be a bare same-page anchor except literally inside
  `index.html`... always write `/#packages`." Once packages live at a real URL, every page's link
  becomes a normal `/packages.html` href — the special-casing this rule exists to prevent goes away
  entirely. A genuine, incidental win from this pivot, worth noting so it doesn't get lost as "just an
  IA change."
- **OS-chrome (3 pages, in review) and this pivot touch the same markup.** `index.html` is both the
  page being restructured here AND one of the 3 pages carrying `os-chrome`. Recommend sequencing, not
  bundling: land the IA + colour pivot on `index.html`/new `packages.html` first, verify it in
  isolation, *then* resume the chrome sweep to the other 14 pages — mixing the two makes it hard to
  attribute a regression to either change. `phasor.html` and `holodb/index.html` (the other 2
  os-chrome pages) are untouched by the IA pivot but **do** inherit the `--c-ml` re-tint (Decision 1)
  if that's confirmed, since both reference ML-domain cards/nav tiles.
- **`.prism-beam` gets more load-bearing, not less.** It already visually depicts a white line
  hitting a triangle and fanning into 7 coloured lines — exactly the metaphor this document formalises
  into a real token system. No markup change needed; the surrounding copy now literally describes what
  it's already showing.
- **SEO/JSON-LD**: the new `packages.html` needs its own `<title>`/description/canonical/OG tags (not
  currently present anywhere, since the content used to live inline on the indexed homepage) and
  should add to `sitemap.xml`. Each individual package page's own `SoftwareApplication` JSON-LD is
  unaffected. This is an implementation detail, not a brand-identity decision, noted so it isn't
  dropped when this becomes a real task.
- **Not addressed here, deliberately out of scope**: any change to `Showroom/`'s own UI (tool pages
  themselves, `MainLayout.razor`'s nav/brand) — that's `showroom-owner`'s territory per the existing
  ownership boundary. The Analyst's live crumb (`<a href="/holodb/">HoloDb</a> / The Analyst`, found
  in `Analyst.razor`) already does a version of "drill down to the powering package" *inside* the tool
  itself — worth flagging to the coordinator as a pattern the other 3 tools could adopt too, but it's a
  Showroom-side change, not something this document or this agent can act on.

---

## Decisions — CONFIRMED by the user (2026-08-28)

1. **`--c-ml` reassigned to Indigo** (`--spectrum-6`, `#7d7dff`), off the old off-spectrum pink
   (`#e879c8`). Re-tints AlgFormer/AlgFormer.Gpu/EvalApp.Neural/Prose cards and 3 of 4 tool cards
   sitewide — implement this change everywhere `--c-ml` is referenced, not just on the pivoted pages.
2. **The 6 orphan packages stay UNANNOTATED** on the relocated `packages.html` — no "Powers: …" /
   "Not yet in a tool" line. Same card shape as today, just relocated. (The longer-term 5th-tool idea
   for HoloVoxel remains a separate, not-decided-here product-roadmap note for the coordinator.)
3. **`Tools` nav item dropped.** Nav becomes `Home · Packages · NuGet` (3 items) everywhere it
   currently reads `Home · Packages · Tools · NuGet`. `#tools` keeps its `id` for deep-linking even
   without a nav entry pointing at it.

Implementation may now proceed per §1-§4 above with these three values locked in.

# SiteKit — the reusable core

**Status (2026-09-02, Phase 2 in progress): `tokens/` is now LIVE, not inert** —
`site/assets/site.css` `@import`s `tokens/core.css` + `tokens/brand-ea.css` instead of restating
their values (Phase 1's remaining open item, closed this pass). `deploy.yml` ships
`SiteKit/tokens/` alongside `site/` so the relative `@import` resolves in production too (see
"The rewire" below). `Showroom/wwwroot/**` is untouched — still `showroom-owner`'s half of this
hand-off, not started.

`SiteKit.Spec`/`SiteKit.Render`/`SiteKit.Render.PoC` are real, building, running code — the
declarative `PageSpec` + fluent builder, the corrected EvalApp-native render pipeline, and now
**3 real pages ported and proven byte-identical** (Phasor from Phase 1, Prose + Tracer added this
pass). `site/**/*.html` is still 100% hand-authored and load-bearing — the render pipeline's
output still lands only in a gitignored `bin/**/out/` folder, never into `site/`, and `deploy.yml`
still doesn't build these projects. Read `AboutUs/docs/platform-architecture.md` §3-§4.5 and §9 for
the architecture and the full verification record.

## The rewire (Phase 1's last open item, closed 2026-09-02)

`site/assets/site.css`'s `:root` token block + `body[data-cat="..."]` glow-tint rules are gone,
replaced by two `@import url("../../SiteKit/tokens/core.css")`/`brand-ea.css` lines right after the
file's top comment (before any other rule, per the CSS spec's `@import`-must-come-first rule).
`tokens/core.css`+`brand-ea.css` are now the actual source of truth, not an inert copy.
**A real drift was caught and fixed doing this**: `brand-ea.css`'s chord rule
(`body[data-cat="holodb-algformer"]`) carried an extra `--glow-near:var(--c-holodb)` declaration
the live `site.css` never had (the original explicitly documented `--glow-near` as untouched for
chord pages, since no chord page carries `.prism-beam`) — removed, then the whole rewire was
verified by parsing BOTH the pre-edit `:root`/`body[data-cat]` text and the post-edit
`core.css`+`brand-ea.css` into selector→{prop=value} maps and diffing them programmatically: **11/11
selectors, identical property sets**, not just eyeballed. `deploy.yml`'s "Assemble site" step now
also does `cp -r SiteKit/tokens/. _site/SiteKit/tokens/` — the `@import`'s relative path
(`../../SiteKit/tokens/...` from `/assets/site.css`) resolves to `/SiteKit/tokens/...` once served
from the Pages artifact root, so without this the import would 404 live and blank every custom
property on the deployed site. This one file is the SiteKit token layer's only production
dependency today.

## The code (Phase 1 core + Phase 2, 3 pages proven)

- **`SiteKit.Spec/`** — `PageSpec.cs` (the record types: `PageSpec`/`SeoSpec`/`HeroSpec`/
  `SectionSpec`/`CardSpec`/`SnippetSpec`/`FooterSpec`/`SiteSpec`/`NavSpec`/`BrandTokens`) +
  `SiteBuilder.cs` (the fluent builder, styled on EvalApp's own chain shape:
  `Site.Define(...).Page(slug, title, category, catVar, p => p.Seo(...).Hero(h => ...)
  .Section(...)).Build(out SiteSpec)`). Zero dependencies. **`CardSpec` gained `CatRootOverride`
  this pass** (the `--cat-root` companion prop a two-tone chord card needs alongside `CatOverride`
  — Phasor's cards never exercised this, Prose's do on every card).
- **`SiteKit.Render/`** — `Jobs.cs` (the render-in-progress records), `Composers.cs` (plain
  string-builder fragment composers, no Razor yet), `WriteStaticFileStep.cs` (the one real side
  effect, `SideEffectStep<PageRenderJob>` declaring `ResourceKind.DiskIO`), `SiteKitPipeline.cs`
  (the real `Eval.App(...)` chain: nested `ForEach<SiteRenderJob>` → `ForEach<PageRenderJob>`, one
  compiled tree, fixed `Tunable.ForCpu()`/`Tunable.Between(1,8,4)` bounds, no `.WithTuning()`).
  `PackageReference EvaluatedApplications.EvalApp 1.7.0` — NuGet only, same boundary `HoloKernel`
  already established for AlgFormer. **`HeroComposer` had a real bug fixed this pass**: it emitted
  the `.hero-content` z-index wrapper unconditionally, but the live site only nests it when a page
  ALSO carries `.prism-beam` (`site.css`'s own comment on the rule says so) — Phasor alone couldn't
  catch this since it's one of the 5 beam pages; Prose (no beam) surfaced it immediately as a real
  diff. Now conditional on `HeroSpec.ShowPrismBeam`, verified correct on both a beam page (Phasor,
  still identical after the fix) and two non-beam pages (Prose, Tracer).
- **`SiteKit.Render.PoC/`** — `PhasorPageSpec.cs`, `ProsePageSpec.cs`, `TracerPageSpec.cs` (three
  real pages ported verbatim into `PageSpec` values — see each file's own header comment for why
  that specific page was picked, what structural shape it exercises that the others don't),
  `Program.cs` (builds all three into one `SiteSpec`, runs the real pipeline once, diffs each
  output against its `site/*.html` original), `StructuralDiff.cs` (the tag-boundary-tokenizing line
  diff, unchanged, reused as-is across all three pages — no page-specific diff logic needed). Run
  it: `dotnet run -c Release` from this folder. Verified result, all three: generated output and
  the hand-authored original are identical once pure whitespace/line-wrap is normalized away, AND
  under an independent all-whitespace-stripped byte compare — see `platform-architecture.md` §9/§10
  for the full record and what that claim does and doesn't cover.

## Why this exists

Two real, admitted pains drove this:
1. **The brand-mark sweep that stopped at the AboutUs/Showroom ownership boundary** (2026-08-28):
   the prism-triangle mark was swept by hand across all 17 static pages, then had to be
   *separately* hand-applied inside `Showroom/Layout/MainLayout.razor` — two authors, two
   syntaxes, one motif, no shared source.
2. **The per-package hex table hand-duplicated with no shared token file**: `AboutUs/CLAUDE.md`'s
   "Per-package palette" table is the source of truth in prose; `site/assets/site.css`'s `:root`
   carries the live values; `Showroom/wwwroot/css/depth.css` carries a SECOND hand-typed copy of
   the same hexes as CSS fallback literals (`var(--c-holodb, #66c1aa)`), because it was safer for
   the showroom-owner author to inline a fallback than trust an implicit cross-file contract.
   Three copies of eight numbers, by hand.

Both are symptoms of the same root cause: there is no single artifact — CSS or component — that
IS the design system. There's a discipline (this repo's `CLAUDE.md`) that describes it accurately,
and two independent hand-authored implementations that (mostly) agree with the discipline. That
scales to one site maintained by one very careful agent. It does not scale to a second client site.

## The split

**`tokens/core.css`** — brand-AGNOSTIC. The *shape* of the system: a dark UI shell, a baseline
rhythm grid, radius/wrap/font-stack, semantic status colours. A future client site keeps this file
byte-for-byte.

**`tokens/brand-ea.css`** — brand-SPECIFIC. Evaluated Applications' actual palette: the ROYGBIV
`--spectrum`, the 8-stop per-package hue table, the accent, the glow defaults. A future client
site replaces this ONE file with their own and inherits every structural rule in `core.css` for
free.

Both files ARE the live token values now (`site/assets/site.css` `@import`s them, see "The rewire"
above) — no longer a separate copy to keep in sync. Any future token change (new package, retint,
a new semantic colour) is made HERE, not in `site.css`.

## Component inventory (markup + class contract, not yet a component library)

This is prose-documented in `COMPONENTS.md`, one entry per reusable pattern, each noting: what it
is, its markup shape, which custom props it reads, and its two current independent
implementations (the static-HTML shape in `site/**/*.html` and, where one exists, Showroom's own
`.razor`/`.razor.css` equivalent). This is the spec Phase 1's real Razor components get built
from — writing it down first means the RCL can be built by literally transcribing this file
component-by-component, not by re-reverse-engineering `site.css` and `MainLayout.razor` side by
side again.

## What this is NOT (yet)

- Not a NuGet package. Not referenced by any `.csproj`. No build step touches `SiteKit.Spec`/
  `SiteKit.Render`/`SiteKit.Render.PoC` from the live site's own build — `deploy.yml` only copies
  `tokens/` as static files (see "The rewire" above), it does not `dotnet build` this folder.
- `tokens/` IS wired into `site/assets/site.css` now (see "The rewire"). `Showroom/wwwroot/css/
  depth.css` is NOT — still a separate hand-typed copy, still `showroom-owner`'s open hand-off item.
- `site/**/*.html` is still 100% hand-authored — the render pipeline (`SiteKit.Render.PoC`) proves
  pages CAN be generated correctly but nothing writes into `site/` yet; that's a later, explicit
  cutover decision, not implied by either the tokens rewire or the Phase 2 page count.
- Not a Razor Class Library yet. `SiteKit.Render`'s composers are plain string builders; Razor/
  `HtmlRenderer` is a planned later upgrade (`platform-architecture.md` §4), not needed for the
  proof so far.

See `AboutUs/docs/platform-architecture.md` for the full plan (why this shape, what Phase 1/2/3
build on top of it, and the concrete showroom-owner hand-off).

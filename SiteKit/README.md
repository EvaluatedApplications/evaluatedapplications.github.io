# SiteKit — the reusable core (extraction seed, Phase 0)

**Status: inert extraction, not yet consumed anywhere.** Nothing in `site/assets/site.css` or
`Showroom/wwwroot/**` has been changed to point at these files. This folder exists so the
brand-agnostic/brand-specific split and the component inventory have a real, versioned home
*before* the real consumer gets built. **That consumer is now planned as a declarative
`PageSpec` + an EvalApp-native rendering pipeline (Phase 1, needs `evalapp-owner` design sign-off
first — Phase 0.5), NOT a generic Razor-Class-Library-plus-static-generator** — read
`AboutUs/docs/platform-architecture.md` §3-§4.5 for the corrected architecture and why; this
README/`COMPONENTS.md` stay accurate regardless (the component markup/prop contract and the token
split are orthogonal to which engine renders them).

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

Both files are **verbatim value-preserving extractions** of what's live today in
`site/assets/site.css`'s `:root` block — no values were changed, only reorganised. Diff them
against `site.css` any time `site.css` changes, until Phase 1 makes `site.css` `@import` these
files instead of restating them (see the architecture doc).

## Component inventory (markup + class contract, not yet a component library)

This is prose-documented in `COMPONENTS.md`, one entry per reusable pattern, each noting: what it
is, its markup shape, which custom props it reads, and its two current independent
implementations (the static-HTML shape in `site/**/*.html` and, where one exists, Showroom's own
`.razor`/`.razor.css` equivalent). This is the spec Phase 1's real Razor components get built
from — writing it down first means the RCL can be built by literally transcribing this file
component-by-component, not by re-reverse-engineering `site.css` and `MainLayout.razor` side by
side again.

## What this is NOT (yet)

- Not a NuGet package. Not referenced by any `.csproj`. No build step touches this folder.
- Not wired into `site/assets/site.css` (still the live, load-bearing file) or
  `Showroom/wwwroot/css/depth.css` (still the live, load-bearing file).
- Not a Razor Class Library — that's Phase 1. This folder is CSS text + markdown only, on purpose,
  so it can be extracted and reviewed with zero build risk before any real code depends on it.

See `AboutUs/docs/platform-architecture.md` for the full plan (why this shape, what Phase 1/2/3
build on top of it, and the concrete showroom-owner hand-off).

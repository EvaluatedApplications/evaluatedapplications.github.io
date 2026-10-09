# AboutUs — CLAUDE.md (website-owner)

**Last verified:** 2026-10-09. Compacted from ~2,300 lines; the full dated history (SiteKit batch logs, palette and
os-chrome passes, per-pass verification notes, reconciliations, 2026-10-02 licence sweep, 2026-09-29 agent-discoverability
pass) is in `CLAUDE-archive.md` (same folder). Grep it before redoing anything: "why is it like this" lives there.

Public site repo for `evaluatedapplications.github.io`: static HTML content (indexable, instant) plus the Showroom tools
under `/tools/`. You (website-owner) own PRESENTATION (design, cohesion, rendering, nav, deploy). Package owners own CONTENT,
authored in `MonoRepo/<Pkg>/docs/site.md`. Never edit package source or `docs/site.md` (flag stale content to the owner).
Never commit/push (coordinator commits, user pushes, Pages publishes).

## Standing rules
- Licence is commercial: NEVER write "free to use", "MIT", "open source", "Pricing: Free"; no `offers:{price:0}` in JSON-LD.
  Canonical wording: `MonoRepo/Editorial/STYLE.md`; the single License section is `packages.html#license`, other pages link to it.
- No em dashes in new copy. No hard-coded versions or parameter counts on pages (they go stale; link NuGet / say "check the
  live listing"). No internal roadmap announcements in public copy.
- Versions: the package `.csproj <Version>` is the only source that cannot be stale.
- Edit via Read/Edit/Write or UTF-8-safe .NET I/O (`UTF8Encoding($false)`); never `Get/Set-Content` on these files.
- Dark theme ALWAYS (user directive 2026-08-28): no `prefers-color-scheme:light` palette, ever, unless explicitly requested
  and opt-in.
- No HTML/XML comment may contain `--`.
- Verify HTML by tag-balance count (div/a/p/section/ul/main/script...) on every edited file; there is no compiler.
- Tool links are TRAILING-SLASH form (`/tools/<slug>/`) everywhere (pages, llms*.txt, sitemap): Pages 301s the bare form.

## Site map (`site/`)
- `index.html` tools-first homepage: pitch, `#tools` gallery (Prism, Nano Stories, Cartographer, Creature, Forecaster, Prose,
  Analyst), `#build-with-us` partnership offer (primary CTA, homepage nav only), `#for-ai`, slim `.pkg-strip` to packages.
- `packages.html` the 12-package gallery in 4 categories + `#license`. Every package card is a `.card-link` overlay.
- Package pages: `phasor`, `evalapp`, `evalapp-neural`, `algformer`, `algformer-gpu`, `holoformer`, `holodb` (benchmarks
  sub-page; hub is `holodb/index.html`), `holodb-client`, `holodb-protocol`, `holovoxel`, `prose`, `tracer`,
  `prism-package` (NuGet Prism, a different thing from the `/tools/prism/` demo; the collision is called out in llms files).
- Manuals: `holodb/manual/`, `evalapp/manual/` (`.prose`/`.toc` template).
- `articles.html` + `articles/*.html` (`_example.html` is the template; not in sitemap).
- `recycledao-preview.html` unlisted client preview. `404.html` (also a legacy SPA deep-link bounce under `/tools/`).
- `council-live/` (index.html + main.js copied byte-identical from the virtual-customer agent; never edit main.js here;
  wording changes go back to that agent). In sitemap, deliberately NOT linked from other pages and deploys separately once the
  user clears a blocker. Leave it and its sitemap entry alone until told.
- `sitemap.xml`, `robots.txt` (explicit AI-crawler Allow blocks), `llms.txt`, `llms-full.txt` (concatenated package
  `docs/site.md`; regenerate from owners' sources), `.nojekyll`, `assets/site.css`.
- Tools (`/tools/`, `/tools/<slug>/`: analyst, cartographer, council-spending, creature, forecaster, prism, prose, stories)
  come from `Showroom/dist`, not `site/`. Sitemap lists the slash forms (council-spending is Showroom's, check before adding).

## Design system
- ONE stylesheet `site/assets/site.css`; tokens live in `SiteKit/tokens/{core,brand-ea}.css` (imported by relative path;
  edit the token files, not site.css, for palette/token changes). `deploy.yml` copies `SiteKit/tokens/` to
  `/SiteKit/tokens/` so the import resolves; keep copy step and `@import` paths in sync.
- Tokens: `--bg/--bg-2/--surface/--surface-2`, `--border/--border-2`, `--ink/--ink-soft/--ink-faint`, `--accent/--accent-ink`,
  `--spectrum`, `--ok/--warn/--bad`, `--radius`, `--wrap` (1080px), `--font`/`--mono`.
- Per-package palette = the `--spectrum` gradient sampled at 8 stops: Tracer `#f0796a`, HoloVoxel `#f09b5c`, HoloDb.Client
  `#e9ba53`, HoloDb.Protocol `#a9cf5f`, HoloDb `#66c1aa`, AlgFormer `#5998ff`, AlgFormer.Gpu `#877dff`, EvalApp.Neural `#c07dff`;
  Phasor/EvalApp use the foundation gradient; Prose is a composite (HoloDb+AlgFormer chord, no own hex); holoformer and
  prism-package reuse `--c-algformer`. Pages set `data-cat`. Composite colouring uses chord glow (see archive).
- Components documented in `SiteKit/COMPONENTS.md` (check it BEFORE a cross-page sweep; update it if the contract changes).
  Architecture plan: `docs/platform-architecture.md`.
- Page-local `<style>` exists on `holodb/index.html`, `holodb.html`, `holoformer.html` (bespoke charts); check them on any
  global token change. `evalapp.html` has an inline-styled table (extract a shared class if a third page wants one).

## Navigation contract
Every page reachable in at most 2 clicks. Top nav is lean: `Home · Packages(/packages.html) · NuGet` (max ~6 items; HoloDb hub
and manuals add page-specific items). Each product/reference page has a `.related` pill row (2-4 siblings + "All packages").
`packages.html` is the package index, `index.html#tools` the tool index. Open follow-up: `/#build-with-us` nav item is
homepage-only.

## Content docs
Sources: `MonoRepo/<Pkg>/docs/site.md` for Phasor, EvalApp, EvalApp.Neural, AlgFormer, AlgFormer.Gpu, HoloDb, HoloDb.Protocol,
HoloDb.Client, HoloVoxel, Prose, Tracer, Prism. Re-render from them; never patch prose independently.
Open flags for owners: several `docs/site.md` historically carried "free to use" wording that `llms-full.txt` concatenates
(fixed in the rendered file; recurs on regeneration unless owners fix source); Phasor's said "source-available";
AlgFormer.Gpu and HoloVoxel lack a minimal code sample (no quick-start section on their pages); inline JSON-LD
`softwareVersion` on several pages drifted (a version-refresh sweep, or remove them as done for pill badges, is pending).

## Adding a package
Read its `docs/site.md` (+ PACKAGE.md/csproj). Copy a plain product page (e.g. `phasor.html`) with its lean nav and `.related`
row; add to `packages.html` (category + `.card-link`), 1-2 sibling `.related` rows, `sitemap.xml`, `llms.txt`/`llms-full.txt`,
package-count facts on `index.html`/`packages.html`, `index.html` `.pkg-strip` and JSON-LD graph, and this file. If it powers a
tool, add a `.powered` pill on that tool's card. Re-check reachability and narrow-viewport nav.

## Deploy
- Two repos, same origin. `C:\Users\dongy\website-data` (GitHub `EvaluatedApplications/website-data`, own Pages at
  `/website-data/`, `.gitattributes` `* -text -diff`) holds `council-web/`, `prism/` (checkpoint + sidecars) and `_framework/`
  (the Blazor runtime: AOT wasm, assemblies, ICU). This repo's `Showroom/dist/` is small (~1 MB).
- Tools pages are Blazier static shells: `EvaluatedApplications.Blazier` 1.0.0 with `<BlazierScaffold>` in `Showroom.csproj`
  renders every `[BlazierPage]` to real HTML as the last step of `dotnet publish`. `Showroom/dist/` holds one `index.html` per
  tool path (gallery `index.html`, plus `analyst/ cartographer/ council-spending/ creature/ forecaster/ prism/ prose/ stories/
  recycledao-demo/`), css, `blazor.webassembly.*.js`, `coi-serviceworker.js`, `data/forecaster-history.json`. The runtime
  downloads only when a visitor starts a tool. `scripts/finish-shells.mjs` adds per-page meta/canonical/OG (noindex for the
  preview). Details: `Showroom/CLAUDE.md`.
- `Showroom/scripts/publish-site.ps1`: publishes, rebuilds `dist/` (no `.br`/`.gz` copied: Pages never serves them), writes the
  runtime into `website-data/_framework`. Run with `-Prune` AFTER the site push is live to delete unreferenced runtime files
  there (it keeps only files the fresh publish contains).
- **Push order: website-data first, then this repo** (the shells fetch hashed runtime files from `/website-data/_framework/`).
  Rolling back the site alone is safe until `-Prune` has run. A checkpoint or council-data refresh is website-data only, no
  site commit, no publish.
- `.github/workflows/deploy.yml` (push to `main`, Pages Source = "GitHub Actions"): does NOT run `dotnet publish`. It copies
  `site/` to the root, `Showroom/dist/` to `/tools`, `SiteKit/tokens/` to `/SiteKit/tokens/`, then uploads and deploys.
  DANGER: CI never builds Showroom, so a stale `dist/` deploys silently. Rebuild `dist/` in the same commit as any Showroom
  source change and confirm the local build succeeded.
- `.gitattributes` `Showroom/dist/** -text -diff` is load-bearing: CRLF conversion once broke SRI hashes and killed the app.
  Heed any "LF will be replaced by CRLF" warning under `dist/` or website-data. Data base: `window.EA_DATA_BASE`; local dev
  needs `Showroom/scripts/link-data.ps1` once.
- Still in this repo: `forecaster-history.json` (refreshed by `refresh-forecaster-data.yml`) and `content.wal`.
- Hand-off each task: leave changes in the working tree; coordinator commits, user pushes. A push is not a published site:
  fetch the live URL.

## Gotchas
- Tools are a separate concern from static pages; their component styles belong to showroom-owner (site.css only styles the
  loading/error UI shell).
- `404.html` still bounces `/tools/*` misses to `/tools/?/<rest>` (legacy SPA fallback). With per-tool static shells this only
  fires for unknown paths; whether to simplify it is a showroom-owner call.
- `index.html` closing text says tools are "free to try"; borderline against the licence rule, left as-is, flag to editorial.
- Naming collision: tool "Prism" vs NuGet `EvaluatedApplications.Prism` (rename is not this agent's call).

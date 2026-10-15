# Blazier follow-ups (opened 2026-10-08, after the switch to static shells)

Items for the coordinator / other owners / later passes. Each says what, why, and who.

1. **Trailing slashes (website-owner).** Pages answers `/tools/prism` with a 301 to `/tools/prism/`. On the phone profile that hop costs
   about 230 ms of first paint (prose shell: 1224 ms without the slash, 996 ms with). Home.razor's cards now link with the slash; the static
   site's own cards/links to `/tools/<slug>` (index.html, algformer.html, prose.html, holodb pages, llms.txt) and `site/sitemap.xml` still use
   the bare form. The canonical URL the shells declare is the slash form, so the sitemap should list it too.
2. **Blazier asks (blazier-owner), none blocking.** (a) A per-page meta description / extra head tags on `[BlazierPage]` (today
   `scripts/finish-shells.mjs` patches them after publish). (b) A `Start=Click` that arms only a chosen control instead of the whole island
   (`touchstart` anywhere would boot the runtime, so every Showroom page is `Static=true` and starts through its own button via
   `Blazier.mount`). (c) `ScaffoldTemplate.IsGenerated` matches the quoted text `name="blazier-scaffold"` anywhere in index.html, so a script
   that merely queries that meta made the first publish fail with BLZ900; match the `<meta` tag instead.
3. **Council slugs have no shell.** `/tools/council-spending/{slug}` goes Pages 404 -> 404.html -> `/tools/?/path` -> gallery shell -> boot
   (one extra hop, as before). If a slug page should be readable before the runtime starts, the 404.html bounce (website-owner's file) could
   target `/tools/council-spending/?/<slug>` for that path, and the hub shell would need to start itself when bounced (it already does).
4. **Shell content is thin by design** (header, lede, start panel, the council glossary). Richer static text per tool (what it shows, the
   package it demos) would help search and link previews; the words would come from the page markup, not a second copy.
5. **Footer year is baked at publish** (`DateTime.Now.Year` in MainLayout runs at build time for the shell): a New Year page shows last
   year's date until the next publish. Harmless, but a publish in January fixes it.
6. **Words that say "free"** (Analyst title, lede and badge; Prose badge) are now also static, indexable HTML: editorial-owner's call, not
   changed here (no behaviour or copy change was in scope).
7. **Shared inline script** (about 12 KB of tone-synth and helpers) is repeated in every shell. Moving it to one cached file would trim each
   shell by about 4 KB gzip; only Prism and Stories use it.

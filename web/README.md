# web: site generator

Builds the staging tree `site-next/` from `data/`, `src/` and `SiteKit/tokens/`. No dependencies beyond Node 20+.

```
node web/scripts/run.mjs          build, voice lint, site check (or: cd web; npm run site)
node web/scripts/build.mjs --versions=auto|csproj|nuget|committed
node web/scripts/lint-voice.mjs   voice rules A to H plus licence wording
node web/scripts/check-site.mjs   links, anchors, nav and footer identical, click depth, sitemap, diagrams
node web/scripts/verify-diagram.mjs   browser checks (needs Edge or Chrome): keyboard, reduced motion, phone
node web/scripts/shots.mjs [outDir] [paths...]   screenshots at 1280 and 390
node web/scripts/prove-snippets.mjs  compile and run every code sample against the published packages, write the outputs
node web/scripts/derive-evidence.mjs  write the batch 4 evidence records from the generator data
node web/scripts/audit-site.mjs  whole-site audit: static crawl plus Edge at 390 and 1280 on every page
node web/scripts/cutover-dry-run.mjs  assemble the final deploy tree in a temp folder and check the old site against it
node web/scripts/check-cutover.mjs   which live files site-next does not cover yet (see web/CUTOVER.md)
```

Layout: `data/` (products, tools, evidence, research, pages, redirects, generated/), `src/` (layout, partials, templates, styles, assets), `scripts/`, `generators/` (C# projects that run the published packages to produce diagram data and compile the page samples).

Versions are read from `MonoRepo/<Pkg>/<Pkg>.csproj` when it exists and written to `data/generated/versions.json`. CI never sees MonoRepo, so commit that file with the output, or build with `--versions=nuget`.

Regenerate the Pleroma replay and capacity data: `dotnet run -c Release --project web/generators/pleroma-demos -- web/data/diagrams/pleroma-superposition.json web/data/diagrams/pleroma-capacity.json`. The Phasor pyramid, superposition scene, coverage meter and ladder table are copied from the Phasor owner's `pleroma-map.json`, `superposition-coverage.json` and `site.md` by `scripts/migrate/extract-superposition.mjs` (nothing is typed). The sizzle-first pattern for product pages, with the Phasor copy, is in `data/copy/phasor-sizzle.md`. The home scene data is written by the build (`data/generated/stack-map.json`).

Regenerate the HoloDb diagram data: `dotnet run -c Release --project web/generators/holodb-demo -- web/data/diagrams/holodb-store.json`. The HoloDb evidence records and the race, explainer and decision-helper data are generated in order by `scripts/migrate/extract-holodb-scaling.mjs`, `extract-holodb-race.mjs`, `extract-holodb-story.mjs` and `extract-holodb-evidence.mjs` (see CLAUDE.md). `verify-labels.mjs` checks label collisions.

Regenerate the Tracer diagram: `dotnet run -c Release --project web/generators/tracer-field -- web/data/diagrams/tracer-field.json`. Re-run the page sample: `dotnet run -c Release --project web/generators/tracer-sample`, then save its output to `data/snippets/tracer-quickstart.out.txt`.

Regenerate the batch 4 diagram data (each runs the published package; all are timings or outputs, so re-run on a quiet machine):
- EvalApp gate: `dotnet run -c Release --project web/generators/evalapp-gate -- web/data/diagrams/evalapp-gate.json` (simulated downloads through a Network gate at seven bounds).
- AlgFormer cost: `dotnet run -c Release --project web/generators/algformer-cost -- web/data/diagrams/algformer-cost.json` (both cores at one shape; medians of seven runs on one thread).
- HoloVoxel: `dotnet run -c Release --project web/generators/holovoxel-lod -- web/data/diagrams/holovoxel-lod.json [seed firstChunkX chunkZ]` (a strip of nine chunks at three levels, and the accuracy, size and decode-cost ladder).
- Prose: `dotnet run -c Release --project web/generators/prose-pipeline -- web/data/diagrams/prose-pipeline.json <folder of alice.txt, holmes.txt, pride.txt>`. The folder holds Project Gutenberg ebooks 11, 1661 and 1342 (public domain, not committed); the generator strips the Gutenberg header and footer itself.
- Then `node web/scripts/derive-evidence.mjs` rewrites the four evidence records (`evalapp-gate`, `algformer-cost`, `holovoxel-lod`, `prose-gate`) from that data, so no evidence number is typed.

Design rules: `docs/design-language.md`.
- HoloGenome: scores are typed once in `data/diagrams/hologenome-scores.json` (from the owner's results tables); `dotnet run -c Release --project web/generators/hologenome-helix -- web/data/diagrams/hologenome-scores.json` adds the `helix` block and package version from the published package; then `derive-evidence`, `prove-snippets hologenome-quickstart`, `run.mjs`, `verify-diagram`.

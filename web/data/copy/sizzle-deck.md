# Sizzle copy deck: every page where a spec-sheet voice is wrong

Pattern and rules: `docs/design-language.md` section 8, worked example `phasor-sizzle.md`. This deck is the copy. The
generator data (`data/products/*.json` `sizzle` blocks, `data/pages/*.json`, `data/site.json`, `data/products.json`) carries
exactly these words; edit both together. Spec-voice pages are untouched: docs, evidence (with costs), client and manual pages,
licence, the AI summary and the five research articles.

Every figure below is sourced. `cite` in the data names the record each number is checked against by `scripts/check-sizzle.mjs`.
Tiles that read straight from an evidence record carry no typed number at all.

Order on a product page: eyebrow (name and category), one promise H1, plain sub-line, install line and "See it run", the live
tool or demo directly under it, number tiles with "See the numbers", "What you can build", "Run it yourself", then the mechanism,
the full feature list, limits, evidence, related.

## Home /

- Eyebrow: .NET libraries and in-browser tools
- H1: A database that learns from its workload. Small models that talk through one shared wave.
- Sub: We build .NET libraries for data, models and games, and tools that run them live in your browser. Nothing is uploaded.
- CTA: [Try it in your browser] (primary), [Browse the products]
- Tiles (read from the products' own tiles): HoloDb 8.5 to 10 times; AlgFormer 2.99 against 3.38; Phasor 200 of 200; HoloVoxel 341 times
- Meta: An embedded SQL database that learns from its queries, and small language models that talk through one shared wave. .NET libraries and tools to try now.

## /products/

- Eyebrow: Products
- H1: Drop a database, a language model or a crowd pathfinder into your app with one command.
- Sub: NuGet packages for .NET. Most build on the same two foundations, so they fit together.
- CTA: [Try them in your browser]
- Meta: Add a SQL database, a language-model engine, a crowd pathfinder or a pipeline runtime to your .NET app with one command. Every library is on NuGet.

## /tools/

- Eyebrow: Tools
- H1: Run a real database in this browser tab.
- Sub: Our libraries, live. Nothing is uploaded: the work happens on your machine.
- CTA: [Start with Umbrarium]
- Meta: Run a SQL database, profile your own data and play a game in your browser tab. Nothing is uploaded; the work happens on your machine.

## /research/

- Eyebrow: Research
- H1: Findings and arguments, with their sources.
- Sub: Each article leads with its finding and names what would refute it.
- CTA: [Start with the first finding]
- Meta: Research articles: how one exact store answers fifty questions in a single pass, and an argument about collapsed civilisations and the design effect.

## /partner/

- Eyebrow: Partner with us
- H1: Build something with our stack, and we will pay you for it.
- Sub: Libraries that run in the browser mean no server, no GPU bill and no user data leaving the device. Our libraries are the stack for that.
- CTA: [Email us] (primary), [See what is on offer]
- Meta: We pay developers to build products on our .NET libraries, and we help you get them working. Fees are negotiable; write to us with what you have in mind.

## /holodb/

- Eyebrow: HoloDb, an embedded SQL database for .NET
- H1: Ask the same question twice and the second time reads almost nothing.
- Sub: An embedded SQL database for .NET that learns from the queries it serves and keeps only what pays. Every SQL statement is answered exactly, with ACID and no index to design.
- Under the hero: the recorded explainers (learns, rent or buy, crash and come back)
- Tiles (typed, each checked against the owner's site.md): 8.5 to 10 times / 61 times / 21 to 24 times / 0 of 46
- Meta: An embedded SQL database for .NET that learns from the queries it serves and keeps only what pays. Every answer is exact, with ACID and no index to design.

## /algformer/

- Eyebrow: AlgFormer, small language models in one shared wave, for .NET
- H1: Many small models, one shared wave.
- Sub: A population of small holographic language models that talk through a shared wave, in pure managed code. They never pass wires to each other. They write to the medium and read the medium.
- Under the hero: the pond toy (computed in the page, no model)
- Tiles (typed, each checked against the owner's site.md, and one from the cost record): 2.99 against 3.38 / 3.47 against 3.84 / 261 launches / 26 times
- Meta: A population of small holographic language models that talk through a shared wave, in pure managed code for .NET. They write to the medium and read the medium.

## /evalapp/

- Eyebrow: EvalApp, a self-tuning async pipeline runtime for .NET
- H1: Run it all at once without overloading anything.
- Sub: Describe a process as plain steps. EvalApp gates every database, disk and network call at a bound you set, undoes failed work in reverse order, and can tune the bound for you. No broker, no services to stand up.
- Under the hero: the recorded gate replay
- Tiles (from the evidence record): 7 of 7 / 1,113 ms to 139 ms / 2.9 times
- Meta: Run every step at once without overloading a database, disk or network. EvalApp gates each resource, undoes failures and can tune itself. No broker needed.

## /prose/

- Eyebrow: Prose, a grammar-driven corpus generator for .NET
- H1: Three novels in. Nearly two thousand checked new sentences out.
- Sub: Prose reads your text through the rules of grammar, keeps what it learns in HoloDb and recombines it into new sentences and question and answer pairs. Every sentence is re-parsed before you get it.
- Tool under the hero: Prose
- Tiles: 8,232 (sentences mined from three public-domain novels) / 1,966 (different sentences from one call that asked for 2,000) / 1,966 of 1,966 (passed the grammar gate on a fresh parse) / 1,966 of 1,966 (used only words the corpus contains)
- Meta: Mine the grammar of your text into HoloDb and recombine it into new sentences and question and answer pairs, each one re-parsed before you get it.

## /tracer/

- Eyebrow: Tracer, pathfinding and game AI for .NET
- H1: Send a whole crowd to one exit with a single search.
- Sub: Bake a navmesh, route single agents or whole crowds across it, close a doorway mid-game and watch the crowd find another way. Line of sight, fog of war and combat are in the same package.
- Tool under the hero: Doctrine
- Tiles (generated from the recorded level): paths found in the crowd tick / paths found with the doorway closed / agents that took a new route / goals for the whole crowd
- Meta: Send a whole crowd to one exit with a single search. Navmesh pathfinding for .NET with dynamic obstacles, line of sight and combat.

## /holovoxel/

- Eyebrow: HoloVoxel, a level-of-detail-native voxel engine for .NET
- H1: Crisp up close, cheap far away, and nothing ever pops.
- Sub: Store each chunk of a voxel world once. Read it with a big budget near the viewer and a tiny one in the distance, and the detail fades smoothly. No mesh tiers to build, store or stitch.
- Under the hero: the rotatable terrain strip
- Tiles (from the evidence record): 98.4% / 341 times / 189 times
- Meta: A voxel engine where level of detail is how a read works: crisp near, soft far, no mesh tiers and no popping. Engine only, for any renderer.

## /algformer/holoformer/

- Eyebrow: How HoloFormer works
- H1: Attention that stays fast as the context grows.
- Sub: HoloFormer folds everything it has read into one running memory per layer. Reading a prompt grows with its length instead of its square, and each new word costs the same however long the context is.
- Tiles (from the AlgFormer evidence record): 26 times / 50 times / 663 times
- Meta: HoloFormer folds a whole context into one running memory, so reading a prompt grows with its length, not its square. Here is how it works, in plain terms.

## /blazier/

- Eyebrow: Blazier, lazy Blazor WebAssembly for static hosts
- H1: Show the page now. Download .NET when they press Start.
- Sub: Blazier turns your Blazor WebAssembly pages into real HTML at publish time. Visitors read at once, and the .NET runtime downloads only when someone presses the button that needs it. Long C# jobs run in a Web Worker behind an ordinary interface.
- Under the hero: a pointer band to /tools/ ("Every tool on this site is a Blazier page", runtime size read from `data/generated/facts.json`), then the `blazier-boot` bars
- Tiles (from the evidence record `blazier-boot`, derived from `data/diagrams/blazier-boot.json`): 6.8 KB / 416 to 484 ms / 14 to 19 ms / 0.49 to 0.57 ms. Sample-app figures only; no Showroom shell figure is recorded
- Sections after the proven program: How it works, Everything in the package, What exists today (typed Web Worker bridge built and measured; EvalApp host seam not built), When another tool fits better, Know before you start
- Meta: Blazor WebAssembly pages that read at once on a static host and download .NET only when a visitor presses Start, plus a typed Web Worker bridge.

## /hologenome/

- Eyebrow: HoloGenome, DNA as a hologram of measured helix physics
- H1: Turn DNA into a vector that winds like the real helix.
- Sub: HoloGenome reads a stretch of A, C, G and T and gives you one fixed-width vector. Its phase turns by the measured twist of each base pair step, so the order along the helix is in the vector. Then it is graded against laboratory measurements it never saw, with the losses published beside the wins.
- CTA: install line, "See how it scores" (the `hologenome-scores` chart, id demo), "Get the code". No Showroom tool exists, so no tool band.
- Tiles (evidence record `hologenome-scores`, derived from `data/diagrams/hologenome-scores.json`): 10.48 to 10.51 bases per turn / 0.529 vs 0.483 / 0.600 / 0.28 to 0.39 behind DNAcycP (a loss tile on purpose)
- Sections: See the numbers, Where it did not win (`doc.losses`), What you can build, Run it yourself (proven program), How it works, Everything in the package, When another tool fits better, Know before you start (graded scores are not in the package), Data and sources (`doc.sources`), Evidence
- Meta: Encode DNA as a fixed-width vector that follows the measured twist of the helix, graded against held-out laboratory measurements, with the losses published.
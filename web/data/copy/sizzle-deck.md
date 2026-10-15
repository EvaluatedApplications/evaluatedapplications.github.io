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
- H1: Data that answers in microseconds. Models that run in a browser tab.
- Sub: We build .NET libraries for data, models and games, and tools that run them live in your browser. Nothing is uploaded.
- CTA: [Try it in your browser] (primary), [Browse the products]
- Tiles (read from the products' own tiles): HoloDb 0.02 ms; AlgFormer 26 times; Phasor 200 of 200; HoloVoxel 341 times
- Meta: Data that answers in microseconds and language models that run in a browser tab. .NET libraries you add with one command, and tools to try now.

## /products/

- Eyebrow: Products
- H1: Drop a database, a language model or a crowd pathfinder into your app with one command.
- Sub: NuGet packages for .NET. Most build on the same two foundations, so they fit together.
- CTA: [Try them in your browser]
- Meta: Add a SQL database, a language-model engine, a crowd pathfinder or a pipeline runtime to your .NET app with one command. Every library is on NuGet.

## /tools/

- Eyebrow: Tools
- H1: Run a real database and train a real model in this browser tab.
- Sub: Our libraries, live. Nothing is uploaded: the work happens on your machine.
- CTA: [Start with The Analyst]
- Meta: Run a SQL database, train a language model and raise a learning creature in your browser tab. Nothing is uploaded; the work happens on your machine.

## /research/

- Eyebrow: Research
- H1: What happens inside a tiny language model, measured.
- Sub: Each article leads with its finding and names what would refute it.
- CTA: [Start with the first finding]
- Meta: Measured findings on tiny language models: how much context they use, how they grow, which layers do the work, and how small they can be.

## /partner/

- Eyebrow: Partner with us
- H1: Build something with our stack, and we will pay you for it.
- Sub: Small models that run in the browser mean no server, no GPU bill and no user data leaving the device. Our libraries are the stack for that.
- CTA: [Email us] (primary), [See what is on offer]
- Meta: We pay developers to build products on our .NET libraries, and we help you get them working. Fees are negotiable; write to us with what you have in mind.

## /holodb/

- Eyebrow: HoloDb, an embeddable SQL database for .NET
- H1: Ten million rows. Dashboard answers in microseconds.
- Sub: Put a SQL database inside your app that rolls up a ledger in a blink, ranks rows by resemblance and keeps ACID transactions. Tables can be bigger than memory.
- Tool under the hero: The Analyst
- Tiles: 0.02 ms (a ten-million-invoice rollup; DuckDB 41.63 ms) / 1,600 times and more (faster than DuckDB on that rollup) / 3.3 times (faster than DuckDB on a join to 200,000 customers) / 16 of 16 (queries returning identical results to SQLite and DuckDB)
- Meta: Put a SQL database inside your .NET app that totals ten million invoices in 0.02 ms, ranks rows by resemblance and survives a crash.

## /algformer/

- Eyebrow: AlgFormer, a transformer engine for .NET
- H1: Train a language model in plain C#. Nothing else to install.
- Sub: Define, train and run transformer models with no Python, no GPU and no native runtime. Pick classic attention, or the holographic core that reads long prompts in a fraction of the time.
- Tool under the hero: The Forecaster (also The Creature, Prism, Nano Stories, The Cartographer)
- Tiles (from the evidence record): 26 times / 50 times / 663 times
- Meta: Train and run language models in plain C# with no Python, GPU or native runtime. The holographic core reads long prompts in a fraction of the time.

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
- Sub: Bake a navmesh, route single agents or whole swarms across it, close a doorway mid-game and watch the crowd find another way. Line of sight, fog of war and combat are in the same package.
- Tool under the hero: The Creature
- Tiles (generated from the recorded level): paths found in the swarm tick / paths found with the doorway closed / agents that took a new route / goals for the whole crowd
- Meta: Send a whole crowd to one exit with a single search. Navmesh pathfinding for .NET with dynamic obstacles, line of sight and combat.

## /holovoxel/

- Eyebrow: HoloVoxel, a level-of-detail-native voxel engine for .NET
- H1: Crisp up close, cheap far away, and nothing ever pops.
- Sub: Store each chunk of a voxel world once. Read it with a big budget near the viewer and a tiny one in the distance, and the detail fades smoothly. No mesh tiers to build, store or stitch.
- Under the hero: the rotatable terrain strip
- Tiles (from the evidence record): 98.4% / 341 times / 189 times
- Meta: A voxel engine where level of detail is how a read works: crisp near, soft far, no mesh tiers and no popping. Engine only, for any renderer.

## /prism/

- Eyebrow: Prism, decode gate, lineage journal and growth for AlgFormer models
- H1: A model that remembers how it grew.
- Sub: Prism picks each next word by how confident the model is, records every time you grow the model, and works out how it stood at any past round. It is the code behind the Prism chat in your browser.
- Tool under the hero: Prism
- Tiles: 30,400 (parity checks of the gate against the code it replaced, zero mismatches) / 19.5 ms (to open a journal of 50,000 events)
- Meta: Grow a language model and keep a record of every change. Prism is a .NET decode gate and lineage journal, and the code behind the Prism chat tool.

## /holodb/pleroma/

- Eyebrow: HoloDb, built on Pleroma
- H1: Ask a table a question without reading the table.
- Sub: Switch on the plane for a table and HoloDb answers filters, group-bys, distinct counts and joins from a small exact summary instead of scanning rows. It is optional, off until you ask, and every answer matches the engine's.
- Tiles: About 600 times (an overdue-invoices rollup on ten million rows, 5.93 ms to 0.01 ms) / 121 times (median on nine ad-hoc filters over a ten-million-event dashboard) / 12 times (a dashboard panel while data keeps loading)
- Meta: Switch on the plane and HoloDb answers filters, group-bys and joins from a small exact summary instead of scanning rows. Optional, and every answer matches.

## /algformer/holoformer/

- Eyebrow: How HoloFormer works
- H1: Attention that stays fast as the context grows.
- Sub: HoloFormer folds everything it has read into one running memory per layer. Reading a prompt grows with its length instead of its square, and each new word costs the same however long the context is.
- Tiles (from the AlgFormer evidence record): 26 times / 50 times / 663 times
- Meta: HoloFormer folds a whole context into one running memory, so reading a prompt grows with its length, not its square. Here is how it works, in plain terms.

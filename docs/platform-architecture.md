# Platform architecture — the intentional version

Written by website-owner, 2026-09-02, on direct strategic instruction: this site (and its
relationship to Showroom) is becoming a real product — "Evaluated Applications' website hosting
project," a reusable toolkit for building future client sites, not a bespoke AboutUs homepage.
This doc supersedes the "Platform initiative" section of `AboutUs/CLAUDE.md` as the place
architectural reasoning lives; `CLAUDE.md` keeps a condensed pointer + the day-to-day operational
facts (site map, deploy steps, gotchas). Ground truth checked directly against the real repo
before writing anything below (`site/**`, `Showroom/**`, `HoloKernel/HoloKernel.csproj`,
`.github/workflows/deploy.yml`, `site/assets/site.css`, `Showroom/CLAUDE.md`, `docs/brand-identity.md`)
— nothing here is asserted from memory.

**Scope of this pass**: produce the real plan, and execute the one safe/foundational first slice
(the token + component extraction into `AboutUs/SiteKit/`, done alongside this doc — see
`SiteKit/README.md`). Not a rewrite. Nothing in `site/` or `Showroom/` was changed to produce this
— both continue rendering exactly as they did before this task.

**Revised mid-session, 2026-09-02, on direct strategic correction — read this before §3/§4 below.**
A first pass of this doc proposed a generic "Razor Class Library + console-app static generator"
shape, styled loosely on ordinary Blazor patterns. **Explicitly rejected.** The user's real
direction: every EA product is declarative-first by design — EvalApp is a composed-`Step`
pipeline described as data, not hand-orchestrated imperative code; HoloDb is SQL, describe what
you want; a `PrismSpec` is a declared shape a shared engine builds and grows from. Every page on
this site being a hand-authored HTML file, individually tweaked, is the one part of this company's
own stack still imperative — and that's the actual "haphazard, not intentional" problem, not
(only) the CSS/markup duplication §2 below still correctly diagnoses. The corrected direction,
worked through in §3/§4: a **declarative page/site spec** (a real C# record type, not JSON/YAML/a
templating DSL) authored via a **fluent builder in the same style EvalApp's own** (`Eval.App(...)
.DefineDomain(...).DefineTask<T>(...).AddStep(...).Run(out pipeline).Build()`), consumed by a
**rendering engine built ON TOP OF EvalApp itself** — each render concern (metadata, nav, hero,
sections, static-HTML emission, island-placeholder emission) as a real EvalApp `Step`, composed
through EvalApp's own pipeline machinery, so rendering a whole site's pages (or many client sites'
pages at once) is a resource-gated, tuned fan-out EvalApp already knows how to do — not a second
bespoke execution engine invented just for this. §4.5 below flags exactly which parts of this
design need `evalapp-owner`'s sign-off before Phase 1 starts; this doc does not decide those
unilaterally.

---

## 0. The one fact that must drive every decision below

Measured, not estimated (`AboutUs/CLAUDE.md`'s Platform-initiative section, kept verified here):
publishing Showroom's real package set as a Blazor WASM app is **28.0 MB raw / 10.8 MB gzip /
8.3 MB brotli across 213 files**. A static content page on this site is **12-36 KB HTML + ~83 KB
shared CSS** (site.css has grown since the original 15 KB measurement but is still two orders of
magnitude smaller). That's a **~250-800x gap** depending on which CSS number you use — either way,
the single number that decides this architecture. **A content page must never be made to pay the
app's boot cost, under any refactor.** Every recommendation below is checked against this
invariant before anything else.

## 1. The content/app boundary — is it drawn right?

**The formal test** (not "how many pages exist," which is an accident of today's catalogue size):
a page is CONTENT if everything on it can be expressed with zero required JavaScript — CSS-only
interactivity (the checkbox-hack mobile nav, `<details>`, hover/scroll-driven CSS animation) is
fine, a `<script>` that only wires a copy-button or a footer year-stamp is fine, but the page's
*value* is readable text a search engine should index and a visitor should be able to read with
JS disabled. A page/experience is APP if it requires genuinely stateful client compute that
cannot be meaningfully pre-rendered — a trained model doing live inference, a real SQL engine
running over data the visitor just uploaded, a training loop with a mutable weight tensor.

Applying that test to the real 2026-09-02 catalogue: **every one of the 17 static pages
(16 product/reference/index pages + `articles.html` + the unlisted `recycledao-preview.html`)
passes as pure content — none of them do anything a search engine or a no-JS browser can't already
read.** All 4 Showroom tools genuinely fail the test (a live HoloFormer session, a live HoloDb
query engine) — they cannot be meaningfully static. **Conclusion: the boundary — which artifacts
are static HTML vs. which are the WASM app — is correctly drawn today.** Nothing needs to move
from one side to the other. This is worth stating with confidence rather than re-litigating,
because it means the coming work is entirely about *how each side is authored and shared*, not
about redrawing which pages belong where.

**Where the boundary is genuinely interesting going forward — islands.** A content page linking
out to a tool (e.g. `algformer.html`'s CTA to `/tools/prism`) is today a dead-end click: the
visitor leaves the content page entirely and pays the full 28 MB app-shell cost to see 3 seconds
of a live demo embedded in an otherwise text-heavy page. The `JSComponents`/
`RootComponentMappingCollection` research already done (§3.3 in `CLAUDE.md`'s old Platform section,
carried forward below) is first-party on the installed SDK specifically for this case: mounting
ONE interactive Razor component into an arbitrary static page, **on demand** (click-to-activate,
never eager-loaded on page paint — the 250x gap means an island must never auto-boot). This is a
real, compelling use case (the reverted "you be the judge" section on `algformer.html` was
content-shaped exactly for this — see `CLAUDE.md`'s Reconciliations entry) but is explicitly
**Phase 3+ scope, not this pass**: it needs the shared component/generator infrastructure below to
exist first, or it's just one more hand-built one-off. Flagged as the concrete payoff that
justifies building that infrastructure, not attempted here.

## 2. Why the current split, though boundary-correct, isn't "intentional"

The boundary between WHAT ships as static vs. app is right. The boundary between WHO/HOW
authors each side is where the real problem lives, and it's the thing the two admitted pains
(brand-mark sweep stopping at the repo-boundary; the per-package hex table hand-duplicated three
times — see `SiteKit/README.md` for both, checked against the real files, not just quoted from the
brief) both trace back to:

- **`site/**/*.html`**: 17 files, each a hand-typed, hand-copied instantiation of "the page
  template" (`CLAUDE.md`'s own words: "New product pages should copy this shape exactly, not
  invent new layout"). The template is a *convention*, enforced by a human/agent re-reading a
  1500+ line CLAUDE.md and being careful — not a structural guarantee. Every sweep (brand mark,
  per-package palette, vertical rhythm, os-chrome rollout) has been a scripted-or-manual pass over
  N files, verified after the fact by brace-counting and grep, because there's no compiler for
  "did every page actually stay identical in the ways that matter."
- **`Showroom/Layout/MainLayout.razor`**: one hand-typed Razor file implementing the *same design
  intent* (nav, brand mark, footer chrome) in a different language, by a different author-agent,
  reconciled only by both sides separately reading the same CSS class names out of the one shared
  `site.css` file `Showroom/wwwroot/index.html` happens to `<link>` directly. CSS values ARE
  already shared (this is better than the brief's framing suggested — see §2.1 below); MARKUP is
  not, at all.
- **The generation step itself is a human/agent, not a build tool.** Every page render this repo
  has ever done (per `CLAUDE.md`'s own Content-doc-sources section) is: read `docs/site.md`, copy
  an existing page's HTML by hand, edit the prose, verify by re-reading. That's a template-render
  operation being performed manually, indefinitely, by whoever holds this repo's context — which
  is exactly why a `CLAUDE.md` this repo needs is 1500+ lines: the "compiler" for consistency is a
  human reading a design doc every single time.

### 2.1 A correction to the brief's framing, checked against the real files

The brief characterises the repo split as having "no shared token file across the AboutUs/Showroom
repo boundary." That's **half true and worth being precise about**, because it changes what Phase 1
actually needs to build: `AboutUs` is a **single git repository** (`Showroom/` is a folder inside
it, not a git submodule or a second repo — verified: `Showroom/.git` does not exist,
`AboutUs/.git` does). `Showroom/wwwroot/index.html` **already `<link>`s `/assets/site.css`
directly** (`Showroom/CLAUDE.md`, "Site plumbing": "Showroom borrows the one design system") — at
deploy time both `site/` and `Showroom/dist/` land under the same origin (`deploy.yml`: `site/`
copied to `_site/`, `Showroom/dist/` copied to `_site/tools/`), so `/assets/site.css` genuinely IS
one physical file both consume at runtime, right now, today. The "no shared token file" pain is
real but narrower than it reads: it's specifically that (a) some CSS call sites (SVG favicon data
URIs, `.razor.css` isolated-scope files that can't reliably assume load order) re-type hex
FALLBACK literals defensively rather than trust the shared `var()`, and (b) **markup** — the HTML/
Razor that actually emits classes referencing those tokens — has zero sharing at all. The "two
repos" framing in the strategic brief is really about the *ownership/ agent boundary*
(website-owner vs. showroom-owner), which is real and matters for workflow, not a technical git
boundary that blocks a shared artifact today. This distinction matters directly for scoping Phase
1: **the shared CSS token layer barely needs building** (it already exists as one file; Phase 1's
job is mostly reorganising it into the core/brand split `SiteKit/tokens/` seeds, then pointing
`site.css` at it via `@import` instead of restating values) — **the shared component/markup layer
is the real, unbuilt thing.**

## 3. The reusable starter-kit: what it actually consists of, EvalApp-native

**Recommendation, decisive: the toolkit is not a stylesheet or a template repo — it's a
declarative page/site spec (data) plus a rendering engine built as a real EvalApp pipeline
(execution).** This mirrors EvalApp's own stated design philosophy exactly ("design first, code
second... decide the data record before touching the builder" — `EvalApp/docs/site.md`'s own
words) — applied here to what a *page* is, not what an order or a job is. Four parts, developed
inside this repo first as a sibling to `HoloKernel/` (the already-proven in-repo-RCL,
NuGet-only-against-published-packages, `ProjectReference`'d-by-Showroom pattern), graduating to a
real MonoRepo package with its own owner agent (`sitekit-owner`?) and a published
`EvaluatedApplications.SiteKit` NuGet package once there's a SECOND real consumer — not before,
same "don't publish until proven" discipline the rest of the house's packages followed:

1. **`tokens/`** — unchanged from the first pass of this plan: `core.css` (brand-agnostic) +
   `brand-ea.css` (this brand's values). Already extracted, verbatim, in Phase 0 — see
   `SiteKit/README.md`. Pure data, this part of the plan doesn't change.
2. **`SiteKit.Spec`** — the declarative record types + their fluent builder. The record shape
   (sketched in §3.1 below, not final) is a `SiteSpec` containing `PageSpec`s, each carrying its
   metadata/hero/sections/related-links/optional-islands as plain immutable data — no HTML, no
   rendering logic, nothing imperative. The fluent builder that PRODUCES this data is styled
   directly on EvalApp's own chain shape (`Site.Define(...).Page(slug, p => p.Title(...).Hero(...)
   .Section(...)).Build(out ISiteSpec spec)`) so authoring a page reads the same way authoring an
   EvalApp pipeline does — because, per EvalApp's own stated value ("the builder chain is the
   architecture... it can't drift out of sync the way a diagram does"), the same property should
   hold for a page: reading the spec top to bottom should read like the page.
3. **`SiteKit.Render`** — the rendering ENGINE, and the actual EvalApp-native part: an
   `Eval.App(...)` pipeline whose steps consume a `PageSpec` (or a job wrapping one) and produce
   either a written static `.html` file or an island-mount placeholder + registration metadata.
   Sketched in full in §3.2. Individual steps may internally use Razor components +
   `HtmlRenderer` to produce a fragment's markup (the earlier `HtmlRenderer` spike — see §4 — is
   still real, still used, just demoted from "the top-level driver" to "how one step renders its
   fragment," which is the correct level for it). `NuGet`-only against `EvalApp` (+
   `Microsoft.AspNetCore.Components.Web` for the Razor-step internals) — same dependency shape
   `HoloKernel` already established for AlgFormer.
4. **`SiteKit.Components`** — the shared component/markup contract from `SiteKit/COMPONENTS.md`,
   now the thing `SiteKit.Render`'s steps actually emit (as Razor components under `HtmlRenderer`,
   or as plain string composers for the simplest atoms) AND the thing Showroom's own chrome
   consumes directly (via `ProjectReference`, same as today's `HoloKernel` pattern) — this is
   still what closes the brand-mark-sweep / hex-triplication pains, unchanged from the first pass
   of this plan, just now positioned as a dependency OF the render engine rather than a parallel
   RCL with its own separate generator.

### 3.1 The spec record shape — a sketch, not final API

Per EvalApp's own "design the record first" discipline, applied to a page:

```csharp
public sealed record PageSpec(
    string Slug,                              // "phasor" -> site/phasor.html
    SeoSpec Seo,                              // title/description/canonical/OG/JSON-LD
    string? Category,                         // per-package data-cat, or null
    HeroSpec Hero,
    IReadOnlyList<SectionSpec> Sections,
    IReadOnlyList<string> RelatedSlugs,
    IReadOnlyList<IslandRef> Islands           // usually empty — see §3.3
);

public sealed record HeroSpec(string Eyebrow, string Headline, string Lede,
    IReadOnlyList<FactChip> Facts, string? InstallCommand, IReadOnlyList<CtaLink> Ctas);

public sealed record SectionSpec(SectionKind Kind, string? Heading,
    IReadOnlyList<CardSpec>? Cards, string? Body);   // Kind picks the shared component: CardGrid/Prose/Stack/Snip...

public sealed record IslandRef(string ComponentId, string MountSelector, IslandActivation Activation);
// IslandActivation: OnClick | OnVisible  — never Eager; see §3.3, the 250x gap still applies
```

A `SiteSpec` is `IReadOnlyList<PageSpec>` plus site-level facts (brand, nav item list, footer link
set). This is genuinely brand/content-agnostic shape-wise — a future client site produces a
different `SiteSpec` value, never a different record TYPE.

### 3.2 The render engine — an EvalApp pipeline, CORRECTED and now real, compiling, verified code

**Superseded 2026-09-02.** The original sketch here (a two-pipeline composition: a standalone
`ICompiledPipeline<PageRenderJob>` invoked from inside an outer pipeline's `ForEach` lambda) was
wrong per `evalapp-owner`'s review — see §4.5 for exactly what was wrong and why. It has been
replaced with real, building, running code, not a further sketch:

- `AboutUs/SiteKit/SiteKit.Spec/` — `PageSpec.cs` (the record types) + `SiteBuilder.cs` (the
  fluent builder, `Site.Define(...).Page(slug, title, category, catVar, p => p.Seo(...).Hero(h =>
  ...).Section(...)).Build(out SiteSpec)`). Zero dependencies, no EvalApp reference.
- `AboutUs/SiteKit/SiteKit.Render/` — `Jobs.cs` (`PageRenderJob`/`SiteRenderJob`/
  `MultiSiteBuildJob`, the accumulating records), `Composers.cs` (plain string-builder fragment
  composers — `HeadComposer`/`NavComposer`/`HeroComposer`/`SectionComposer`/`FooterComposer`/
  `HtmlComposer`; no Razor/`HtmlRenderer` yet, see §4's note on where that still fits),
  `WriteStaticFileStep.cs` (the one real side effect), `SiteKitPipeline.cs` (`SiteKitPipeline
  .Build()` — the actual `Eval.App(...)` chain, ONE compiled tree, `ForEach<SiteRenderJob>` nested
  inside `ForEach<PageRenderJob>`, no `WithTuning()`, `Tunable.ForCpu()` for both fan-outs,
  `Tunable.Between(1, 8, 4)` for the shared `DiskIO` gate). `PackageReference
  EvaluatedApplications.EvalApp 1.7.0` (NuGet only, never a MonoRepo `ProjectReference` — same
  boundary `HoloKernel` already established for AlgFormer), verified this exact version is both
  published (`api.nuget.org` query) and locally restorable before pinning it.
- `AboutUs/SiteKit/SiteKit.Render.PoC/` — the Phase 1 proof itself: `PhasorPageSpec.cs` (the real
  `phasor.html` page transcribed verbatim into a `PageSpec` value via the builder), `Program.cs`
  (builds the spec, runs `SiteKitPipeline.Build()`, diffs the output against the live
  `site/phasor.html`), `StructuralDiff.cs` (a small LCS line-diff that tokenizes on tag boundaries
  so hand-word-wrapped prose and one-line-packed tags don't read as false content differences —
  see §9 for why that normalization was needed and how it was verified honest, not just
  convenient).

All three projects build clean (`dotnet build`, 0 errors/0 warnings) against the real, published
EvalApp package — this is no longer illustrative. The actual pipeline chain, reproduced here
because it's now truth rather than a sketch (see `SiteKitPipeline.cs` for the authoritative,
commented version):

```csharp
Eval.App("SiteKit.BuildSites")
    .WithResource(ResourceKind.DiskIO, Tunable.Between(1, 8, 4))
    .DefineDomain("Sites")
        .DefineTask<MultiSiteBuildJob>("BuildSites")
            .ForEach<SiteRenderJob>(
                select: job => job.Sites.Select(s => new SiteRenderJob(s)),
                merge: (job, results) => job with { WrittenFiles = results.SelectMany(r => r.WrittenFiles ?? Array.Empty<string>()).ToList() },
                collectionName: "sites", parallelism: Tunable.ForCpu(),
                configure: site => site
                    .ForEach<PageRenderJob>(
                        select: s => s.Spec.Pages.Select(p => new PageRenderJob(p, s.Spec.Brand, s.Spec.Nav, s.Spec.OutputRoot)),
                        merge: (s, results) => s with { WrittenFiles = results.Select(r => r.OutputPath!).ToList() },
                        collectionName: "pages", parallelism: Tunable.ForCpu(),
                        configure: page => page
                            .AddStep("RenderHead",     job => job with { HeadHtml = HeadComposer.Compose(job.Spec, job.Brand) })
                            .AddStep("RenderNav",      job => job with { NavHtml = NavComposer.Compose(job.Spec, job.Nav, job.Brand) })
                            .AddStep("RenderHero",     job => job with { BodyFragments = new List<string> { HeroComposer.Compose(job.Spec, job.Brand) } })
                            .AddStep("RenderSections", job => job with { BodyFragments = (job.BodyFragments ?? Array.Empty<string>()).Concat(SectionComposer.ComposeAll(job.Spec)).ToList() })
                            .AddStep("RenderFooter",   job => job with { FooterHtml = FooterComposer.Compose(job.Spec.Footer, job.Brand.CompanyName) })
                            .AddStep("ComposeHtml",    job => job with { FinalHtml = HtmlComposer.Compose(job), OutputPath = Path.Combine(job.OutputRoot, job.Spec.Slug + ".html") })
                            .AddStep<WriteStaticFileStep>("WriteFile")
                    )
            )
        .Run(out ICompiledPipeline<MultiSiteBuildJob> pipeline)
    .Build();
```

Note the `BodyFragments` shape: a list accumulated through `RenderHero`/`RenderSections`, joined
exactly once in `HtmlComposer.Compose` — the direct fix for the O(n²) string-concat bug flagged in
§4.5, not a cosmetic rename.

### 3.3 Static emission vs. island mounting — genuinely different pipeline shapes, not the same
step type wearing two hats

Important to be honest about, since the coordinator's brief asked this directly: **static-HTML
emission and island-mounting are not symmetric halves of one mechanism.** `WriteStaticFileStep`
above is real work EvalApp's pipeline does, end to end, at BUILD TIME — read the spec, render
fragments, write a file, done, verifiable, gated, tuned. **Mounting a Blazor island happens at
RUNTIME, in the visitor's browser, at a moment no EvalApp pipeline (a server/build-time-invoked
.NET library) has any presence at all.** What the render engine's `RenderSections` step DOES own,
correctly, for a page with an `IslandRef`: emitting the static placeholder markup
(`<div id="prism-demo-mount" data-island="prism-demo">`) + a small inline bootstrap script that
calls `Blazor.rootComponents.add(...)`/registers via `JSComponents` **only once the visitor
performs the gesture `IslandActivation` names** (a click, an intersection-observer visibility
trigger) — never on page paint, which is the one invariant (§0's 250x gap) that must survive this
architecture unconditionally. The pipeline's job stops at "did we correctly emit the placeholder +
registration metadata" — whether the island actually mounts live in a browser is a runtime fact,
verified the same disclosed way every other live-browser claim in this repo's CLAUDE.md files is
(not provable by the pipeline itself, needs a real device check).

## 4. Where the earlier HtmlRenderer/JSComponents research fits now

Both pieces of research are still real and still used — demoted from "the architecture" to "how
individual render steps are implemented," which is the correct level for them once the actual
orchestration is EvalApp:

- **`HtmlRenderer`** (`Microsoft.AspNetCore.Components.Web.HtmlRenderer`, spiked and verified
  working on this SDK — 10.0.400, no Kestrel, no crawl step, correct escaping, encoding-proof
  non-ASCII output) is how a step like `HeroRenderer.Render(...)` or `SectionsRenderer.Render(...)`
  in §3.2 turns a spec fragment + a `SiteKit.Components` Razor component into an HTML string — the
  IMPLEMENTATION inside one `AddStep` lambda, not a parallel top-level generator loop. This keeps
  the earlier finding's real value (a compiler-enforced component render instead of hand-typed
  HTML) while putting the actual page-to-page, site-to-site FAN-OUT where it belongs: EvalApp.
- **`JSComponents`/`RootComponentMappingCollection`** (confirmed first-party, present on the
  installed SDK by reflection over the real DLLs) is the client-side half of the island mechanism
  §3.3 describes — the piece that actually mounts a component into the placeholder markup a render
  step emitted, once a visitor's gesture fires it. Still real, still the right primitive, still
  genuinely outside any pipeline's reach once execution moves to the browser.

## 4.5 `evalapp-owner` sign-off — RECEIVED 2026-09-02, composition corrected and proven

`evalapp-owner`'s verdict: EvalApp is a good, correct fit for this workload — but the original
§3.2 code sketch below (now replaced, see the corrected version further down) was WRONG and would
not have compiled. Recorded here so the mistake and the fix are both on the record, not just the
fix:

- **`ForEach` has no per-item execution-delegate overload.** Every real overload's last parameter
  is a build-time step-DSL callback (`Action<ISubTaskBuilder<TItem>>`), compiled once into a
  reused inner tree — not a `Parallel.ForEachAsync`-style per-item lambda. Verified directly
  against `EvalApp/Consumer/Fluent/ITaskBuilder.cs`/`ISubTaskBuilder.cs`.
- **A separately-built `ICompiledPipeline<T>` cannot be plugged in as a step.**
  `Consumer.ICompiledPipeline<T>` does not implement `IStep<T>`. Calling
  `otherPipeline.RunAsync(...)` from inside a step body *works* (nothing stops it) but buys
  nothing and breaks the outer pipeline's own debug/breadcrumb tracing — so it's a real anti-
  pattern, not just a compile error to route around.
- **The correct shape**: ONE compiled tree using nested `ForEach`-in-`ForEach` — sites → pages —
  all builder-authored in a single `Eval.App(...)` chain, no separate pipeline object for
  per-page rendering. If a standalone single-page pipeline is ever wanted too (e.g. a live-
  preview/watch mode), share the step *code* (extract render steps as reusable methods/classes
  used by both trees), never share a pre-compiled pipeline instance.
- **`Gate(ResourceKind.DiskIO, ...)` genuinely is constructed once and shared** across every page
  write, site-wide, under this shape — confirmed by `evalapp-owner` and re-confirmed independently
  below (§9) by reading `Fluent/Builders.cs`: `WithResource(kind, tunable)` on the app builder
  registers one shared semaphore per `ResourceKind`; every `Gate(kind, ...)`/auto-gated
  `AddStep<TStep>()` call with that same kind draws from the same pool. This is exactly the
  resource-gating benefit the whole design exists for, and it holds.
- **A real, separate perf bug was in the sketch too, unrelated to the pipeline choice**:
  `RenderSections`'s `job with { BodyHtml = job.BodyHtml + ... }` re-concatenated the whole growing
  body string every section — O(n²) string-concat-in-a-loop, ordinary C# hygiene, not an EvalApp
  issue. Fixed by accumulating fragments in a list through the steps and joining once in
  `ComposeHtml` (see `SiteKit.Render/SiteKitPipeline.cs` — `BodyFragments` is a list the whole way
  through, `HtmlComposer.Compose` does the one join).
- **`SideEffectStep<T>` + `AddStep<WriteStaticFileStep>()` was already correct as sketched** — no
  change needed. Confirmed it auto-gates on the declared `ResourceKind.DiskIO` and works fine
  nested inside the per-page `ForEach`.
- **No `.WithTuning()` on this pipeline** — `Tunable.ForCpu()` for the render-step `ForEach`
  fan-outs, a small fixed `Tunable.Between(1, 8, 4)` for the `DiskIO` write-gate bound. Mirrors an
  existing HoloDb precedent (a workload whose optimum is already obvious doesn't benefit from
  per-run adaptive tuning) and sidesteps the separately-logged EvalApp gotcha: no sanctioned
  non-file-persisting tuning store for CI/ephemeral contexts.

All four of §4.5's original open questions are answered by the above: (1) nested `ForEach`, not
`AddSubTaskFor` and not a plugged-in `ICompiledPipeline`; (2) the O(n²) risk was real but was in
the sketch's string handling, not EvalApp's record-copy cost, and is fixed; (3) `AddStep<TStep>()`
auto-gating is correct as-is; (4) fixed bounds, no `WithTuning()`.

§3.2's code is no longer a sketch — see the corrected, real version there, backed by the building/
running code in `SiteKit.Spec`/`SiteKit.Render`/`SiteKit.Render.PoC` and the verification record
in §9.

## 5. AboutUs-specific vs. reusable core — the concrete split

Already tabulated in full in `SiteKit/COMPONENTS.md`'s closing section; the short version:
**reusable core = every component's STRUCTURE and BEHAVIOUR** (the nav's responsive mechanism, the
card grid's stretched-link pattern, the OS-chrome opt-in shell, the parallax-depth 3-tier
mechanism, the generator/HtmlRenderer pipeline itself, the `docs/site.md`-as-content-source
convention generalised to "any client's content docs"). **AboutUs-specific = every component's
COLOUR and COPY** (the prism triangle, the ROYGBIV per-package palette and its dependency-derived
placement rule, the actual product pitches, the 11-package catalogue, the article essays). The
chord-domain-WEIGHTING FORMULA (§2 of `docs/brand-identity.md`: "weight per domain, not per
package") is a genuinely reusable ALGORITHM even though its inputs (the domain list) are
AboutUs-specific — worth carrying into the RCL as a small parameterised helper rather than
re-deriving per client.

## 6. Phased plan

**Phase 0 — DONE this pass.** Extract tokens (value-preserving, verbatim) into
`AboutUs/SiteKit/tokens/{core,brand-ea}.css`; document the full component inventory in
`AboutUs/SiteKit/COMPONENTS.md`. Nothing wired in; `site/assets/site.css` and
`Showroom/wwwroot/css/*` remain the live, load-bearing files, unchanged. Zero rendering risk —
verified by NOT touching either.

**Phase 0.5 — evalapp-owner design review — DONE 2026-09-02.** Verdict: EvalApp is a good, correct
fit; the original §3.2 sketch's composition was wrong (see §4.5 for exactly what and why) and has
been corrected. `WriteStaticFileStep`'s auto-gating was confirmed correct as originally sketched.

**Phase 1 — `SiteKit.Spec` + `SiteKit.Render`, real code — CORE DONE 2026-09-02, one page proven.**
Built: the record types + fluent builder (`SiteKit.Spec`, zero dependencies), the corrected
EvalApp-based render pipeline (`SiteKit.Render`, `NuGet`-only against `EvaluatedApplications
.EvalApp` 1.7.0 — no `Microsoft.AspNetCore.Components.Web` dependency yet, since Phase 1's fragment
composers are plain string builders, not Razor; that upgrade is still available later without
changing the pipeline shape, see §4). Ported `phasor.html` (small, canonical, exercises hero
facts/install/CTAs/related pills, prose section, two card-grid sections (one with a `.lim`
caveat), a six-card grid, a two-snippet code section, and a closing `.stack` CTA section — a good
cross-section of the component inventory) through the real pipeline in
`SiteKit.Render.PoC/PhasorPageSpec.cs` + `Program.cs`, and diffed the output against the live
`site/phasor.html`. **Result: after normalizing away pure whitespace/line-wrap differences (the
hand-authored file hand-wraps long prose across physical lines and packs some tags on one line;
the generator always emits one logical element per line), the generated page and the hand-authored
original are IDENTICAL — 492 normalized tokens each, 0 additions, 0 removals.** A second,
independent check (strip ALL whitespace from both raw files, byte-compare) also returned equal:
12,606 non-whitespace characters, identical. See §9 for the full verification record, including
what the normalization does and doesn't paper over (documented, not asserted). **This is real
proof the composition works and reproduces a real page exactly** — not a toy example. **Fully
closed 2026-09-02**: `site/assets/site.css` now `@import`s `SiteKit/tokens/{core,brand-ea}.css`
instead of restating their values (mechanical, low-risk, done independently of the render-engine
work as planned) — see §10 for the rewire + its own verification record, including a real drift it
caught. `Showroom/Layout/MainLayout.razor` has still not been touched (that's the
`showroom-owner` half of Phase 1, still not dispatched — unaffected by the rewire, since
`Showroom/wwwroot/index.html` already `<link>`s the same physical `/assets/site.css` that now
resolves its tokens from `SiteKit/tokens/` instead of inline — no Showroom-side change needed for
that half to keep working).

**Phase 2 — full catalogue migration, IN PROGRESS (started 2026-09-02, 3 of 17 pages proven).**
`phasor.html` round-tripped correctly through the real pipeline in Phase 1 (§9). This pass added
two more pages, deliberately chosen to be structurally DIFFERENT from Phasor (per this phase's own
mandate: broaden the proof, don't re-confirm the same path) — both ported, run through the SAME
compiled pipeline as Phasor in one call, and verified against their live originals the same
rigorous way (tag-boundary structural diff + whitespace-stripped byte compare):
- **`prose.html`** — exercises `CardSpec.CatOverride` as a real two-tone chord (paired with the
  new `CatRootOverride` field, `--cat-root`), a page where `Category` (the `data-cat` attribute,
  `"holodb-algformer"`) and `CategoryDotVar` (the `.sec-head .dot` colour, `var(--c-algformer)`)
  are genuinely different values, no `.prism-beam`, and no closing `ClosingStack` section.
- **`tracer.html`** — a Snippets section with exactly one snippet and no `LimHtml`/`DescAfterHtml`
  at all, a `CardGrid` section that also carries a trailing `.lim` note in the same section, and a
  second, independent non-beam page (re-confirming the bug fix below wasn't a Prose-only fix).

**Two real bugs were found and fixed in `SiteKit.Render`/`SiteKit.Spec` doing this, not just
content transcribed** — see §10 for the full record: (1) `HeroComposer` emitted the
`.hero-content` z-index wrapper unconditionally; the live site only nests it on the 5 pages that
also carry `.prism-beam` (`site.css`'s own comment on the rule says so) — Phasor alone couldn't
have caught this since it's one of those 5 pages. Fixed to be conditional on
`HeroSpec.ShowPrismBeam`, re-verified Phasor still matches after the fix. (2) A content
transcription slip in the first `ProsePageSpec.cs` draft (copied Phasor's snippet-2 lead-in
sentence instead of Prose's own) — caught by the same diff tooling, not missed silently. Remaining
14 pages (holodb hub/benchmarks/manual, algformer.html's `.card.tool` gallery shape, holoformer.html
and holovoxel.html's bespoke local-`<style>` pages, the remaining plain product pages, `index.html`/
`packages.html`/`articles.html`) not started — the composer set proven so far covers Prose/CardGrid/
Snippets/ClosingStack sections and the plain `os-chrome` template; `.card.tool` galleries and any
page with a genuinely bespoke local `<style>` block will need new composer support before they can
be ported the same way, not just a new `PageSpec` value. Nothing in `site/` itself changes until a
future phase actually cuts over — Phase 1/2's output still lives only in
`SiteKit.Render.PoC/bin/**/out/`, gitignored, never written into `site/`.

**Phase 3 — islands (not started, needs Phase 2).** Pick ONE real, justified island (the
`algformer.html`-to-Prism "you be the judge" content, already written once and reverted for
placement reasons, not technical ones — see `CLAUDE.md`'s Reconciliations entry) and wire it via
`JSComponents`, deferred behind a click, its placeholder emitted by the render pipeline per §3.3.
Prove the pattern once, in the open, before treating it as a general capability.

**Phase 4 — extraction to a standalone repo/NuGet package (not started, needs a second real
consumer).** Only once there's an actual second site (a real client, or a deliberately built
second internal example) does `SiteKit` earn moving out of `AboutUs` into its own MonoRepo package
(with its own owner agent, following the house's `<Pkg>/CLAUDE.md` + advisory-desk pattern) and
publishing to NuGet through the same `release.yml` pattern the rest of the house's packages use.
Designing Phase 1's `PageSpec`/component API cleanly (props in, no AboutUs-specific data baked into
C#, only ever passed in as parameters/content docs) is what makes this extraction mechanical later
instead of a second rewrite.

None of Phases 0.5-4 are started in this pass, per the task's own scope instruction ("figure out
what's up and propose the real architecture... not execute a multi-week rewrite unsupervised").

## 7. What changes for website-owner (this agent), starting now

- New CLAUDE.md gotcha (added in the same pass as this doc): any future sitewide sweep (a new
  brand pass, a new token, a new component) should ask "does this belong in `SiteKit/tokens/` or
  `SiteKit/COMPONENTS.md` first, before it's typed into 17 files again" — even before Phase 1's
  RCL exists, the DOCUMENTATION should stay the source of truth the hand-sweep is checked against,
  not `site.css` read cold.
- No change to today's actual render workflow (still reading `docs/site.md`, still hand-copying
  the page template) until Phase 2 exists — this doc is a plan, not a new day-to-day process yet.

## 8. The showroom-owner hand-off (write-up for a fresh agent with no memory of this conversation)

**Context you need**: `AboutUs` is one git repo; `Showroom/` is a folder in it (not a separate
git repo), owned by you per the existing agent-ownership boundary (website-owner owns `site/`,
you own `Showroom/`). A new sibling folder, `AboutUs/SiteKit/`, now exists — Phase 0 of a plan to
turn this site's design system into a genuinely shared, reusable toolkit (full reasoning:
`AboutUs/docs/platform-architecture.md`, the doc you're reading a summary of; the inventory:
`AboutUs/SiteKit/COMPONENTS.md`; the extracted tokens: `AboutUs/SiteKit/tokens/*.css`).

**What's already true and unchanged**: `Showroom/wwwroot/index.html` already `<link>`s
`/assets/site.css` directly — your CSS token VALUES are already shared with the static site today,
nothing about that changed. `SiteKit/tokens/{core,brand-ea}.css` are currently INERT
value-preserving copies of what's already in `site.css`'s `:root` — they don't yet do anything,
don't import them, don't change `depth.css` or `MainLayout.razor` because of this hand-off alone.

**What's asked of you, when this becomes a real task (not yet dispatched as one — read
`platform-architecture.md` §6 "Phase 1" first if/when the coordinator assigns this)**:
1. Review `SiteKit/COMPONENTS.md`'s entries for the components YOUR side currently hand-duplicates
   independently (`.site-nav`'s Showroom equivalent in `MainLayout.razor`, `depth.css`'s
   independent 3-tier parallax re-derivation, the brand-mark copies in `MainLayout.razor` +
   `wwwroot/index.html`'s favicon) — confirm or correct the entry against your own files (it was
   written from your `CLAUDE.md` + a direct read of your `.razor`/`.css` files, but you're the
   ground truth for your own repo, verify before trusting it).
2. **Do NOT start building `SiteKit.Components`/`SiteKit.Render` independently, and note the
   render engine itself is now planned as a real EvalApp pipeline** (§3-§4.5 of
   `platform-architecture.md` — not a plain Razor generator, and it needs `evalapp-owner`'s design
   sign-off before implementation starts, separately from your review). Phase 1 (building the
   actual component/render code) is a joint piece of work between you and website-owner — you are
   the only one who knows what Showroom's OWN chrome actually needs from a shared `<SiteNav>`/
   `<BrandMark>` component API (e.g. your nav's real 7-item list vs. the static site's 3-6 — that
   drift needs to be resolved as a real design decision, not silently averaged by whoever builds
   the component first). Flag design requirements/constraints back rather than picking an API
   unilaterally.
3. **Standing per-package-palette retint flag is still open and independent of all this** (from
   `AboutUs/CLAUDE.md`'s existing Reconciliations section, restated here so it's not lost in this
   hand-off): Prism/Analyst/Creature/Forecaster cards should retint to the real per-package hex
   table in `SiteKit/tokens/brand-ea.css` (same values as `AboutUs/CLAUDE.md`'s "Per-package
   palette" table) — Prism → `--c-algformer` `#5998ff`, Analyst → `--c-holodb` `#66c1aa`, Creature
   → the AlgFormer+Tracer chord, Forecaster → `--c-algformer` `#5998ff`. This is a SMALL, safe,
   do-anytime task, independent of the bigger SiteKit initiative — worth doing regardless of when/
   whether Phase 1 gets scheduled, since `Showroom/CLAUDE.md` already documents this exact
   retint as done via `depth.css`'s `[data-cat]` selectors, but the plain `--cat` accent colours on
   `Home.razor`'s own tool cards should be double-checked against the CURRENT table (the source
   table has been re-verified accurate against `.csproj` dependency graphs multiple times since
   this flag was first raised — trust `brand-ea.css`'s copy as current).
4. When you next touch `depth.css` or `MainLayout.razor` for any reason, cross-check the change
   against `SiteKit/COMPONENTS.md`'s relevant entry and flag back to website-owner/coordinator if
   you find the entry is stale or wrong — this doc is only useful if it stays true.

**What NOT to do**: don't fold Showroom's tool pages into `site/`, don't make any content page
eager-load the Blazor runtime, don't treat this hand-off as authorization to start a big rewrite —
Phase 1 needs explicit coordinator dispatch with both agents' scope agreed, per this repo's
existing cross-cutting-change discipline.

## 9. Verification record — the Phase 1 proof, in full

Run 2026-09-02, from `AboutUs/SiteKit/SiteKit.Render.PoC`:

```
dotnet build -c Release      # SiteKit.Spec, SiteKit.Render, SiteKit.Render.PoC — 0 warnings, 0 errors
dotnet run -c Release
```

Output:
```
Pipeline succeeded. Wrote 1 file(s):
  ...\SiteKit.Render.PoC\bin\Release\net10.0\out\phasor.html

=== Structural diff (whitespace-normalized: trim each line, drop blank lines) ===

IDENTICAL after whitespace normalization (492 non-blank lines each). No content or structural
differences found.
```

A second, independent, cruder check (not via the diff tool at all — raw PowerShell, both files'
ENTIRE text with all whitespace runs collapsed to nothing, then a case-sensitive string compare):
`orig stripped length: 12606`, `gen stripped length: 12606`, `equal: True`. Two different
comparison methods, same conclusion — not relying on the diff tool being honest with itself.

**What "IDENTICAL after whitespace normalization" actually means, stated precisely so it isn't
oversold**: the `StructuralDiff` normalizer tokenizes both files on tag boundaries (splits right
before `<` and right after `>`), then collapses internal whitespace (including embedded newlines)
to a single space per token and drops empty tokens. This intentionally erases exactly two classes
of difference and no others: (1) which physical line a hand-typed author chose to word-wrap long
prose onto, and (2) whether several short tags were packed onto one physical source line or given
one line each. It does NOT erase attribute differences (a changed `href`, a missing `style=`, a
different class), tag differences, tag order, or text content differences — those would all show
up as a token substitution and be reported as `-`/`+` lines, because each token is either one
complete tag (with all its attributes) or one text run, so any change inside either is a changed
token. The all-whitespace-stripped byte comparison is the belt-and-braces check that would catch
anything the tokenizer-based diff might in principle miss (e.g. a text difference that happened to
still normalize to the same token count) — it found none. Between the two, the honest claim is:
**the generated `phasor.html` carries the exact same tags, attributes, and text content, in the
exact same order, as the hand-authored original.** The only real difference between the two files,
found and named rather than hidden, is presentational whitespace an author typed by hand and a
generator does not reproduce byte-for-byte — which is expected and fine, not a defect.

**What this does and does not prove about the wider plan**: it proves the corrected `SiteKit.Spec`
record shape can faithfully hold one real page's content, and the corrected `SiteKit.Render`
EvalApp pipeline can faithfully turn that data back into the exact markup a human previously hand-
typed — for one page, exercising most (not all) of the current component inventory (no
`RelatedAllHref` override was exercised since Phasor uses the default; no per-card `CatOverride`
was exercised since Phasor's cards are all one category; islands are untouched, correctly, since
Phasor doesn't have one — see §3.3). It does NOT yet prove the pipeline scales cleanly to the
other 16 pages' component combinations (the HoloDb hub's race-demo/benchmark tables, the `.prose`/
`.toc` manual template, the OS-chrome taskbar/dock on non-plain pages) — that's exactly Phase 2's
job, one page/batch at a time, each checked the same way.

## 10. Verification record — the token rewire + Phase 2's two-page batch, 2026-09-02

**The token rewire.** Before editing `site/assets/site.css`, its pre-edit `:root` block +
`body[data-cat="..."]` rules were saved verbatim. After rewiring to the two `@import`s, both the
saved original text and the concatenated `SiteKit/tokens/core.css`+`brand-ea.css` were parsed
independently (strip comments, match `:root{...}`/`body[data-cat="..."]{...}` blocks, extract every
`--prop:value;` declaration, normalize internal whitespace) into `selector → {prop=value}` maps and
diffed. First run found a real mismatch: `brand-ea.css`'s chord rule carried
`--glow-near:var(--c-holodb)`, which the live original never set for that selector. Fixed
`brand-ea.css` (removed the stray declaration, documented why in a comment), re-ran the same
diff: **11/11 selectors (`:root` + 10 `body[data-cat="..."]` rules), identical property=value sets,
zero mismatches.** A brace-balance check on the edited `site.css` also passed (242 open, 242
close). `deploy.yml` was updated in the same pass to copy `SiteKit/tokens/` into the Pages artifact
at the exact relative depth the `@import`'s path expects (`../../SiteKit/tokens/...` from
`/assets/site.css` resolves to `/SiteKit/tokens/...` once served from the artifact root) — without
this the import would have silently 404'd live and blanked every custom property sitewide; this was
reasoned through and fixed proactively, not discovered by trial.

**Phase 2's `prose.html` + `tracer.html`.** Both ported into `PageSpec` values
(`ProsePageSpec.cs`/`TracerPageSpec.cs`), both run through the same `SiteKitPipeline.Build()` call
as `phasor.html` in one `Program.cs` invocation (`dotnet build -c Release` — 0 warnings/0 errors
across `SiteKit.Spec`/`SiteKit.Render`/`SiteKit.Render.PoC` — then `dotnet run -c Release`):

```
Pipeline succeeded. Wrote 3 file(s): phasor.html, prose.html, tracer.html

=== phasor.html ===
IDENTICAL after whitespace normalization (492 non-blank lines each).
All-whitespace-stripped byte compare: orig=12606 chars, gen=12606 chars, equal=True

=== prose.html ===
IDENTICAL after whitespace normalization (409 non-blank lines each).
All-whitespace-stripped byte compare: orig=12573 chars, gen=12573 chars, equal=True

=== tracer.html ===
IDENTICAL after whitespace normalization (412 non-blank lines each).
All-whitespace-stripped byte compare: orig=10902 chars, gen=10902 chars, equal=True

ALL PAGES VERIFIED IDENTICAL.
```

This wasn't clean on the first attempt for `prose.html`, which is the point of doing this
page-by-page instead of trusting the composers by inspection: the first run reported 7
generated-only lines (`<div class="hero-content">`/`</div>` plus a wrong DescAfterHtml sentence
copied from Phasor) and 1 original-only line (Prose's actual "Parse a single sentence directly..."
lead-in). Both were real: the `.hero-content` gap was a genuine composer bug, now fixed in
`HeroComposer` for every page (conditional on `HeroSpec.ShowPrismBeam`), not special-cased for
Prose — see §6's Phase 2 writeup above for the full description. The wrong sentence was a
transcription slip in `ProsePageSpec.cs` (fixed in the spec, not the composer — that half was
content, not code). `tracer.html` then passed cleanly on its first run, which is itself part of the
proof: it independently re-exercises the non-beam `HeroComposer` path (different page, unrelated
content) and found nothing further wrong, some evidence the fix generalizes rather than papering
over one page's symptom.

---

## 11. Verification record — Phase 2's second batch (6 more pages), 2026-09-02

Run from `AboutUs/SiteKit/SiteKit.Render.PoC` after the nav-item-count question above was
resolved (this batch was dispatched as the follow-on task in the same session):

```
dotnet build -c Release      # SiteKit.Spec, SiteKit.Render, SiteKit.Render.PoC — 0 warnings, 0 errors
dotnet run -c Release
```

`evalapp.html`, `holovoxel.html`, `holodb-client.html`, `holodb-protocol.html`,
`evalapp-neural.html`, `algformer-gpu.html` were each transcribed verbatim into a `PageSpec`
(`EvalAppPageSpec.cs`, `HoloVoxelPageSpec.cs`, `HoloDbClientPageSpec.cs`,
`HoloDbProtocolPageSpec.cs`, `EvalAppNeuralPageSpec.cs`, `AlgFormerGpuPageSpec.cs`), added to the
same `Program.cs` site build alongside Phase 1/2's first 3 pages, and run through one
`SiteKitPipeline.Build()` call together — 9 pages, 1 pipeline, 1 run. All 9 verified IDENTICAL
under both checks (tag-boundary structural diff AND the independent all-whitespace-stripped byte
compare):

```
=== evalapp.html === IDENTICAL (639 lines). Byte compare: 16628=16628, equal=True
=== holovoxel.html === IDENTICAL (457 lines). Byte compare: 13180=13180, equal=True
=== holodb-client.html === IDENTICAL (423 lines). Byte compare: 10512=10512, equal=True
=== holodb-protocol.html === IDENTICAL (399 lines). Byte compare: 10427=10427, equal=True
=== evalapp-neural.html === IDENTICAL (420 lines). Byte compare: 10295=10295, equal=True
=== algformer-gpu.html === IDENTICAL (386 lines). Byte compare: 9785=9785, equal=True
ALL PAGES VERIFIED IDENTICAL. (all 9, including the 3 from the prior batch)
```

**This batch was deliberately picked to surface every remaining composer gap in one pass rather
than one new feature per page** (per the dispatching task's own instruction). Real, additive
(non-breaking) additions to `SiteKit.Spec`/`SiteKit.Render`, each proven necessary by an actual
page rather than speculated:
- **`HeroSpec.LimHtml`** (+ `IHeroBuilder.Lim(...)`) — a `.lim` caveat paragraph between the CTA
  row and the Related pills. First needed by `evalapp.html` and `holodb-protocol.html`.
- **`SectionSpec.StackFlow`** (new `SectionKind`) — a `.sec-head`-titled section whose body is a
  `.stack` holding one prose paragraph plus a `.flow` diagram row. Needed by exactly one section,
  `evalapp.html`'s "What you'd otherwise assemble" (`SemaphoreSlim + MediatR + ... = EvalApp`).
  Deliberately kept distinct from `ClosingStack` (no `.sec-head`, always the page's final CTA).
- **`SectionSpec.Raw`** (new `SectionKind`, + `RawBodyHtml`) — an escape hatch that still gets
  standard `.sec`/`.wrap`/`.sec-head` framing but drops a raw HTML blob as the body. Needed by
  exactly one section site-wide so far, `evalapp.html`'s "None of this is invented from nothing"
  `<table>` (idea-provenance vs. feature). Deliberately NOT a typed table spec — one real use
  doesn't earn its own schema yet; revisit if a second table-shaped section ever turns up.
- **`SectionSpec.ExtraHtml`** (on the existing `Prose` kind) — raw HTML inserted after a Prose
  section's own `<p class="desc">` and before its optional `.lim`. Needed by `holovoxel.html`'s
  "What the same engine output can look like" section (the before/after `.shots` figure grid).
- **`SnippetSpec.DescBeforeHtml`** — a description paragraph emitted immediately BEFORE its own
  snippet, as opposed to the pre-existing `DescAfterHtml` (emitted after, reading as an intro to
  the NEXT snippet even though attached to the previous one). Needed by `holodb-protocol.html`'s
  "A minimal example," where every snippet — including the first — gets its own lead-in line, a
  shape `DescAfterHtml` alone can't express (there's no prior snippet for the first lead-in to
  attach "after"). `holodb-client.html`/`evalapp-neural.html` both re-confirmed the pre-existing
  `DescAfterHtml` shape still holds unchanged on 2 more pages, no regression.
- **`PageSpec.PageStyleHtml`** (+ `IPageBuilder.PageStyle(...)`) — a verbatim page-local
  `<style>...</style>` block, emitted in `<head>` right after the shared stylesheet `<link>` and
  before the JSON-LD `<script>`. Needed by `holovoxel.html`'s `.shots` grid CSS (one of only 3
  pages site-wide that still carries page-local CSS — the other 2, `holodb.html`/`holodb/manual/
  index.html`, are `.prose`-template pages not yet attempted through this pipeline).
- **`HeroSpec.InstallMaxWidthPx`** (default `520`, + `IHeroBuilder.Install(cmd, maxWidthPx:)`) —
  the ONE real bug this batch's diff caught, not a speculative addition: `HeroComposer` hardcoded
  `.install`'s `max-width` to `520px`, but `holodb-protocol.html`/`algformer-gpu.html` (both with
  longer NuGet package names) actually use `560px` on the live site so the chip doesn't wrap. The
  first run of this batch caught it as a genuine 1-line diff on both pages (`orig=10427 chars,
  gen=10427, equal=False` etc.) — not silently accepted, not special-cased per page in the
  composer; fixed as a proper parameter, re-verified clean on the second run. This is exactly the
  kind of thing page-by-page verification against the real files exists to catch, as opposed to
  trusting the composer by inspection (same lesson `prose.html` already taught Phase 2's first
  batch with the `.hero-content` gap).

**`holodb-client.html`, `evalapp-neural.html`, `algformer-gpu.html` needed ZERO new composer
capability** — deliberately included as controls proving the by-then-larger composer surface
already generalizes to a 4th/5th/6th/7th/8th/9th page, not just re-exercising known-good paths on
pages picked to need nothing new. `algformer-gpu.html` in particular is the first page site-wide
with zero Prose/Snippets/StackFlow/Raw sections at all — three plain `CardGrid`s only — proving
that shape is valid too.

**Status after this batch, since superseded by §12 below**: 9 of 17 routable pages proven; the
HoloDb hub/benchmarks/manual, `algformer.html`'s `.card.tool` gallery shape, `holoformer.html`'s
bespoke concept-card layout, and `articles.html`/`articles/_example.html`'s `.prose`/`.toc`
template all still needed new composer support before a `PageSpec` could even be attempted for
them — see §12 for that work, done the same day.

## 12. Verification record — Phase 2's third batch (the last 7 flagged pages), 2026-09-02

Run from `AboutUs/SiteKit/SiteKit.Render.PoC`, same session, dispatched as "continue Phase 2" with
the 7 pages §11 flagged as needing new composer support first:

```
dotnet build -c Release      # SiteKit.Spec, SiteKit.Render, SiteKit.Render.PoC — 0 errors
dotnet run -c Release
```

`algformer.html`, `holoformer.html`, `articles.html`, `articles/_example.html`, `holodb.html`
(benchmarks), `holodb/manual/index.html`, `holodb/index.html` (the hub) were each transcribed
verbatim into a `PageSpec` (`AlgFormerPageSpec.cs`, `HoloFormerPageSpec.cs`, `ArticlesPageSpec.cs`,
`ArticleExamplePageSpec.cs`, `HoloDbBenchmarksPageSpec.cs`, `HoloDbManualPageSpec.cs`,
`HoloDbHubPageSpec.cs`), added to the same `Program.cs` site build alongside all 9 prior pages —
16 pages, 1 pipeline, 1 run. All 16 verified IDENTICAL under both checks:

```
=== algformer.html === IDENTICAL (560 lines). Byte compare: 15185=15185, equal=True
=== holoformer.html === IDENTICAL (673 lines). Byte compare: 18963=18963, equal=True
=== articles.html === IDENTICAL (218 lines). Byte compare: 6461=6461, equal=True
=== articles/_example.html === IDENTICAL (244 lines). Byte compare: 7174=7174, equal=True
=== holodb.html === IDENTICAL (804 lines). Byte compare: 14044=14044, equal=True
=== holodb/manual/index.html === IDENTICAL (937 lines). Byte compare: 16640=16640, equal=True
=== holodb/index.html === IDENTICAL (1620 lines). Byte compare: 32370=32370, equal=True
ALL PAGES VERIFIED IDENTICAL. (all 16, including the 9 from the prior two batches)
```

Every one of the 7 pages needed at least one real capability that didn't exist before this batch
(unlike the second batch's 3 "zero new capability" controls) — this was the batch explicitly
chosen to be structurally hard, not a confirming pass. Additions (all additive/backward-compatible
— new optional fields/params with defaults, or new enum cases; none of the prior 9 pages' specs
needed to change):

- **4 new `SectionKind`s**: `ToolGrid` (`ToolCardSpec`, the `.card.tool` gallery shape —
  `algformer.html`'s 3-card `.grid` version and the HoloDb hub's grid-less 1-card version, via the
  new `omitGridWrapper` flag, a real live-markup difference caught by the diff, not assumed),
  `Compare` (`algformer.html`'s 2-card `.cmp` shape, reusing `CardSpec` verbatim), `ConceptArticle`
  (`ConceptCardSpec`/`ConceptCompareCardSpec` — `holoformer.html`'s whole bespoke 7-concept-card
  `<main class="sec">` body, typed because the glyph+kick+h2+paragraphs+anchor shape genuinely
  repeats 7 times on one page), `ProseArticle` (`ProseArticleSpec` — the `<main class="wrap">
  <article class="prose">` shell shared by `articles/_example.html`/`holodb.html`/`holodb/manual/
  index.html`, paired with **`PageSpec.Hero` becoming nullable** so the pipeline can skip
  `HeroComposer` entirely for a page with no `<header class="hero">` at all).
- **`HeroSpec.RawBodyHtml`** — a full hero-body escape hatch for the HoloDb hub's hero (no crumb,
  no `.facts`, a `.race` widget, `.install` AFTER `.cta-row` — every typed field would have gone
  unused while fighting the ones that don't fit). `HeroSpec.CrumbHtml`/`ExtraBodyHtml` are the
  lighter option used where the standard shape mostly fits (`holoformer.html`, `articles.html`).
- **`SectionSpec.SectionId`** (`id="..."` for same-page anchors) and **`SectionSpec`/`HeroSpec`
  `LeadingCommentHtml`** (raw HTML, in practice an `<!-- HOW IT WORKS -->`-style marker, before a
  section/hero tag — the HoloDb hub carries one before all 8 of its top-level blocks; the
  structural-diff tokenizer treats comment text as real content, so these had to be reproduced
  verbatim once discovered as a real diff, not dropped as "just a comment").
- **`CardSpec.LimHtml`/`PreBodyHtml`/`OmitCatStyle`**, **`SectionSpec.IntroHtml`/`LimStyleAttr`**,
  **`SectionSpec.ClosingStackWithInstall`** — all first exercised by the HoloDb hub's
  "Capabilities"/"Deploy"/"Get started" sections (a second `.lim` inside one card; a `.snip` before
  a card's `.desc`; a card with no `--cat` at all; a lead-in paragraph before a `.grid`; an inline
  style on a trailing `.lim`; an `.install` chip inside a ClosingStack).
- **`SeoSpec.OgType`/`RobotsMeta`**, **`PageSpec.TailScriptHtml`/`LeadingHtml`/`MetaCharset`/
  `NavBurgerAriaLabel`/`NavItemsOverride`**, **`HeroSpec.ExtraClass`**, **`RelatedLink.CssClass`**
  — a cluster of small, REAL, page-level literal divergences the byte-identity check surfaced one
  at a time (an `og:type` hardcoded to `"website"` in HeadComposer was a genuine unhandled gap,
  not a hypothetical one — `holoformer.html`'s first diff run failed on exactly this line). Each
  fixed as a typed override, not a special case threaded through the composer.

**No page in this batch needed zero new capability** — a deliberate contrast with the second
batch's 3 controls, confirming the composer surface still had real gaps left to find right up to
the last page (the HoloDb hub alone needed roughly a dozen of the additions above).

**Still unattempted**: `index.html`, `packages.html` — neither was named in the task that dispatched
this batch (both are plain package/tool gallery grids, structurally close to the already-proven
CardGrid/ToolGrid shapes), so they're the one remaining gap in the 17-page count, not assumed done.
`deploy.yml` is still untouched, no `site/**/*.html` PAGE markup was touched, and generated output
still lands only in a gitignored `bin/**/out/` folder — this batch, like the two before it, proves
the pipeline CAN reproduce these pages, it does not cut any of them over.

## Open questions, genuinely unresolved (flagged, not decided here)

- Should Showroom's OS-chrome-equivalent (a taskbar/dock shell for tool pages) exist at all, or is
  the plain `.room` column intentionally distinct chrome for "you're now in an app, not a content
  page"? Leaning toward "deliberately distinct is fine" (reinforces the content/app boundary
  visually) but this is a real design call, not decided by this doc.
- Whether `docs/site.md` should become the generalised "any client's content doc" format
  verbatim, or whether client content needs a richer schema (sections, ordering, feature lists as
  structured data rather than prose the renderer re-parses). Phase 2's actual generator design
  should answer this with real code, not guessed here.
- **RESOLVED 2026-09-02 — the nav-item-count/redundant-`Tools`-link question, direct user
  decision**: "I prefer the HTML versions, as the Blazor-only pages are for apps, not sharing
  info." This hardens the existing content/app split (16 static pages + 4 Blazor tool routes)
  from an observation into a firm principle: the static HTML site is the canonical home for ALL
  informational/shareable content; Showroom exists ONLY for interactive tool apps, never for
  restating info content. Practical implication: **do not build a shared `<SiteNav Items="..."/>`
  component that lists the same items in both places.** The two navs are independently scoped and
  don't need matching item counts:
  - The static site's nav stays exactly what it already is (`Home · Packages · NuGet` + 0-3
    page-appropriate extras, see the Navigation section of `AboutUs/CLAUDE.md`) — `Home` already
    IS the tools front door post-pivot, so there's nothing to add here on the user's account of
    "a link out to Tools."
  - Showroom's own nav (currently `Home · Packages · Tools · NuGet`, 4 items, per
    `SiteKit/COMPONENTS.md` entry 2) is `showroom-owner`'s own call to resolve — including the
    already-flagged `Tools` link redundancy (it points at `Home.razor`, which already IS the tool
    gallery and is already the first nav item) — on Showroom's own terms, not by forcing parity
    with the static site's count. The coordinator is messaging showroom-owner the same resolution
    separately so both sides can close independently.
  This was a real blocker specifically for a SHARED component's single API; it is not a blocker
  for either site's own nav shape, and neither nav needed to change as a result of resolving it.
  `SiteKit/COMPONENTS.md` entry 2 updated to record this (see that file for the closing note) —
  no unified `<SiteNav>` component is planned; each repo keeps rendering its own nav markup
  (Showroom hand-typed Razor, the static site via `SiteKitPipeline`'s `NavComposer` once/if a
  page is cut over) from its own `NavSpec`-shaped item list.

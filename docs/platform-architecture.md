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

### 3.2 The render engine — an EvalApp pipeline, sketched

Two pipeline levels, matching "render one page" and "render a whole site's pages," the second a
resource-gated fan-out over the first — this is the part that's genuinely, non-contrivedly EvalApp-
shaped, not a stretch:

```csharp
// Level 1 — one page. The record threaded through is the render-in-progress job, not the raw
// spec (mirrors EvalApp's own pattern of an accumulating data record moving step to step).
public sealed record PageRenderJob(PageSpec Spec, BrandTokens Brand,
    string? HeadHtml = null, string? NavHtml = null, string? BodyHtml = null,
    string? FooterHtml = null, string? FinalHtml = null, string? OutputPath = null,
    IReadOnlyList<string> Diagnostics = null!);

Eval.App("SiteKit.RenderPage")
    .DefineDomain("Pages")
        .DefineTask<PageRenderJob>("RenderPage")
            .AddStep("RenderHead",   job => job with { HeadHtml   = HeadRenderer.Render(job.Spec.Seo, job.Brand) })
            .AddStep("RenderNav",    job => job with { NavHtml    = NavRenderer.Render(job.Spec, job.Brand) })
            .AddStep("RenderHero",   job => job with { BodyHtml   = HeroRenderer.Render(job.Spec.Hero, job.Brand) })
            .AddStep("RenderSections", job => job with { BodyHtml = job.BodyHtml + SectionsRenderer.Render(job.Spec.Sections, job.Spec.Islands, job.Brand) })
            .AddStep("RenderFooter", job => job with { FooterHtml = FooterRenderer.Render(job.Spec, job.Brand) })
            .AddStep("ComposeHtml",  job => job with { FinalHtml  = HtmlComposer.Compose(job) })
            .AddStep<WriteStaticFileStep>()   // the ONE real side effect — declares ResourceKind.DiskIO,
        .Run(out ICompiledPipeline<PageRenderJob> renderPage)  // auto-gated via AddStep<TStep>()'s existing mechanism
    .Build();

// Level 2 — a whole site. Resource-gated fan-out over pages; this is the "render N pages (or many
// client sites at once) for free" payoff the coordinator asked this design to deliver.
Eval.App("SiteKit.BuildSite")
    .WithResource(ResourceKind.Cpu, Tunable.ForCpu())
    .WithResource(ResourceKind.DiskIO, Tunable.Between(1, 8, 4))
    .DefineDomain("Sites")
        .DefineTask<SiteBuildJob>("BuildSite")
            .ForEach(job => job.Spec.Pages, Tunable.ForItems(),
                async (page, ct) => await renderPage.RunAsync(new PageRenderJob(page, job.Brand), ct))
        .Run(out ICompiledPipeline<SiteBuildJob> buildSite)
    .Build();
```

**This is illustrative, not verified-compiling code** — I have not confirmed `ForEach`'s exact
signature supports invoking a nested `ICompiledPipeline<T>.RunAsync` from inside its lambda, nor
whether that's the idiomatic composition EvalApp expects for "a task made of sub-pipeline-shaped
work" vs. `AddSubTaskFor`/nested `DefineDomain`. **This exact question is the first line item in
§4.5's evalapp-owner sign-off list** — flagged, not assumed.

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

## 4.5 Needs `evalapp-owner` sign-off before Phase 1 starts — not decided unilaterally here

This design uses EvalApp for something outside its documented consumer set (AlgFormer/HoloDb/
Tracer/EvalApp.Neural today) — a build-time, batch, page-rendering workload rather than a
request-driven or long-running data pipeline. That's a real, good-faith engineering fit (§3.2's
closing paragraph makes the honest case), but it is `evalapp-owner`'s call whether the specific
composition below is sound, not this agent's to assert unilaterally. Concrete open questions,
worth a real design conversation before any of Phase 1 is implemented:

1. **Nested-pipeline composition**: is invoking `renderPage.RunAsync(...)` from inside the outer
   `BuildSite` pipeline's `ForEach` lambda (site → pages, sketched in §3.2) a sound, supported
   pattern — or does EvalApp expect hierarchical work like this expressed via `AddSubTaskFor`/
   nested `DefineDomain` instead, with "one page" as a genuine nested task rather than a
   separately-built pipeline invoked from a closure?
2. **Record-copy overhead at page-render scale**: `PageRenderJob` above threads through ~6-7
   sequential `with`-copies per page. Fine at today's scale (17 pages); is it still the idiomatic
   EvalApp shape at "many client sites, hundreds of pages," or does a lighter accumulate-fragments-
   then-compose-once shape fit EvalApp's own performance model better at that scale?
3. **Does `SideEffectStep<T>` + `AddStep<TStep>()` auto-gating fit a static-site-generation file
   write cleanly**, or is an explicit `.Gate(ResourceKind.DiskIO, ...)`-wrapped lambda the more
   appropriate shape for "write this one file" specifically?
4. **Does the tuner's model transfer to a one-shot batch build?** EvalApp's adaptive tuner is
   described (its own docs) as discovering a steady-state operating point over ongoing traffic; a
   site build is short-lived and invoked fresh per CI run. Is `Tunable.ForItems()`'s tuning still
   the right choice here, or does a build-time batch workload want a fixed `Tunable.ForCpu()`
   bound instead (no adaptive-learning value across a single short run, no persisted-state benefit
   worth the file I/O)?

Until these are answered, §3.2's code is a sketch establishing the SHAPE of the recommendation
(spec-in, EvalApp-orchestrated-render-out, resource-gated per-page fan-out), not an implementation
plan ready to execute.

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

**Phase 0.5 — evalapp-owner design review (not started, blocks Phase 1).** Coordinator dispatches
§4.5's four open questions to `evalapp-owner` as a real design conversation, not a rubber stamp.
Output: a confirmed (or corrected) shape for `PageRenderJob`/the two pipeline levels/the
`WriteStaticFileStep` gating choice/the tuning-vs-fixed-bound call for a build-time batch workload.
Nothing below is implemented until this lands — a wrong composition pattern baked into Phase 1
would be expensive to unwind later.

**Phase 1 — `SiteKit.Spec` + `SiteKit.Render`, real code (not started).** Build the record types +
fluent builder (`SiteKit.Spec`, no EvalApp dependency — pure data + a builder, same shape as any
other C# fluent API) and the EvalApp-based render pipeline (`SiteKit.Render`, `NuGet`-only against
`EvalApp` + `Microsoft.AspNetCore.Components.Web`, per Phase 0.5's confirmed shape). Port ONE
existing page (recommend `phasor.html` — small, canonical, exercises every component) through the
new pipeline and diff its output against the hand-written original before porting further pages.
Rewire `site/assets/site.css` to `@import` `SiteKit/tokens/core.css` + `brand-ea.css` instead of
restating the values (mechanical, low-risk, independent of the render-engine work, can land early).
Port `Showroom/Layout/MainLayout.razor` onto the shared `SiteKit.Components` (`<SiteNav>`/
`<SiteFooter>`/`<BrandMark>`) via `ProjectReference` (the exact pattern `HoloKernel` already proved
works for Showroom). This is the pass that actually closes the brand-mark-sweep and
hex-triplication pains for good, structurally, not by discipline — and replaces "website-owner
hand-copies an existing page's HTML" with "website-owner authors/maintains `PageSpec` values and
verifies pipeline output."

**Phase 2 — full catalogue migration (not started, needs Phase 1 parity proof).** Once `phasor.html`
round-trips correctly through the real pipeline, migrate the rest of the 17-page catalogue from
hand-written HTML to `PageSpec` values + generated output, one page (or a small batch) at a time,
each verified against the hand-written original before moving on.

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

---

## Open questions, genuinely unresolved (flagged, not decided here)

- Should Showroom's OS-chrome-equivalent (a taskbar/dock shell for tool pages) exist at all, or is
  the plain `.room` column intentionally distinct chrome for "you're now in an app, not a content
  page"? Leaning toward "deliberately distinct is fine" (reinforces the content/app boundary
  visually) but this is a real design call, not decided by this doc.
- Whether `docs/site.md` should become the generalised "any client's content doc" format
  verbatim, or whether client content needs a richer schema (sections, ordering, feature lists as
  structured data rather than prose the renderer re-parses). Phase 2's actual generator design
  should answer this with real code, not guessed here.
- The nav-item-count drift (Showroom 7 vs. static site 3-6) flagged above needs an actual decision
  before Phase 1's `<SiteNav>` component can ship an honest single API — noted, not resolved.

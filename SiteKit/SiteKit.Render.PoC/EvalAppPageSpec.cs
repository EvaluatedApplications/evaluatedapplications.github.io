using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 1: site/evalapp.html transcribed verbatim into a PageSpec.
/// The structurally richest page of this batch — exercises three brand-new composer additions,
/// none of which existed before this pass:
///   - HeroSpec.LimHtml — a `.lim` aside between the CTA row and the Related pills.
///   - SectionSpec.StackFlow — the "What you'd otherwise assemble" section: a `.sec-head`
///     titled section whose body is a `.stack` holding one prose paragraph plus a `.flow`
///     diagram row ("SemaphoreSlim + MediatR + ... = EvalApp"). Distinct from ClosingStack
///     (no `.sec-head`, always the page's final CTA block) — this one sits mid-page.
///   - SectionSpec.Raw — the "None of this is invented from nothing" section is a genuinely
///     bespoke `&lt;table&gt;` (idea provenance vs. what EvalApp does), which doesn't fit
///     Prose/CardGrid/Snippets/StackFlow. Modeled as a raw HTML escape hatch rather than a new
///     typed table spec, since this is the only page site-wide that needs one.
/// </summary>
public static class EvalAppPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "EvalApp — a self-tuning async pipeline runtime for .NET",
            Description: "EvalApp is a resource-gated, self-tuning async pipeline runtime for .NET: describe a process as data and steps, and stop hand-writing concurrency coordination. One in-process dependency in place of a DI container plus MediatR plus Polly plus TPL Dataflow.",
            Canonical: "https://evaluatedapplications.github.io/evalapp.html",
            OgTitle: "EvalApp — a self-tuning async pipeline runtime for .NET",
            OgDescription: "Describe a process as data and steps; EvalApp compiles it into one pipeline, gates every resource it touches, and tunes its own concurrency to the hardware it's running on.",
            OgUrl: "https://evaluatedapplications.github.io/evalapp.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.EvalApp","description":"EvalApp is a resource-gated, self-tuning async pipeline runtime for .NET: sagas with compensation, middleware, adaptive concurrency tuning, and a fluent builder — one in-process dependency in place of a DI container plus MediatR plus Polly plus TPL Dataflow.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.6.1","url":"https://evaluatedapplications.github.io/evalapp.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.EvalApp","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("EvalApp · foundation")
            .Headline("Describe a process as data and steps. Stop hand-writing the coordination.")
            .Lede("EvalApp replaces the plumbing every real async .NET codebase ends up writing by hand: a <code>SemaphoreSlim</code> to stop a flood of requests hammering a database, a guessed constant for \"how many things run at once,\" a <code>Task.WhenAll</code> wrapped in <code>try/catch</code> to aggregate failures, a rollback path bolted on after something breaks halfway through. You describe a process as a plain data record and a sequence of steps; EvalApp compiles that into a single, reusable execution path, gates every resource it touches at a real bound, and — when you turn tuning on — discovers the concurrency that actually works on the hardware it's running on, instead of a number someone picked once and never revisited.")
            .Fact("<b>v1.7.0</b>")
            .Fact("net<b>8.0</b>")
            .Fact("zero dependencies")
            .Fact("AOT &amp; trim-safe")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.EvalApp")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.EvalApp", CtaStyle.Ghost, externalNewTab: true)
            .Lim("It's the shared runtime under every Evaluated Applications product, and it works standalone: no broker, no external service, no config store — add the package reference and it works from the first run.")
            .Related("Phasor", "/phasor.html")
            .Related("EvalApp.Neural", "/evalapp-neural.html")
            .BarTitle("evalapp.app"))
        .Section(SectionSpec.CardGrid(
            "Why it's useful", "one dependency instead of a stack",
            new[]
            {
                new CardSpec("One dependency instead of a stack", "Where you'd otherwise reach for a DI container plus MediatR plus Polly plus TPL Dataflow, EvalApp is a single compiled pipeline that covers fan-out, resource gating, retries, and compensation together."),
                new CardSpec("The tuner is proactive, not reactive", "A circuit breaker waits for something to break, then responds. EvalApp's adaptive tuner is continuously probing for the operating point that keeps things from breaking in the first place — independently, per instance, no coordination required. A Bayesian mode persists what it learned across restarts."),
                new CardSpec("Whole categories of bugs don't compile", "A saga you open has to be closed before the pipeline builds. A side-effecting step can't be added without declaring the resource it touches — there is no path to an ungated side effect."),
                new CardSpec("The builder chain is the architecture", "Reading the fluent declaration top to bottom reads like the process it describes, because it isn't documentation about the code — it is the code, so it can't drift out of sync the way a diagram does."),
                new CardSpec("Nothing to stand up first", "It's a project reference, not infrastructure. Most pipeline libraries want a broker or a database before you've processed your first item; EvalApp doesn't."),
            }))
        .Section(SectionSpec.StackFlow(
            "What you'd otherwise assemble", "the plumbing this replaces",
            "A <code>SemaphoreSlim</code> to stop a flood hitting a resource; MediatR for dispatch and pipeline behaviours; Polly for retry, timeout and circuit-breaking; TPL Dataflow for staged parallelism; a DI container to wire it together; and a hand-rolled rollback path for when something fails halfway through. Each is a solved problem in isolation — getting all of them right together, and keeping them right as a codebase grows, is the part that never stays solved. EvalApp is one compiled pipeline that covers fan-out, resource gating, retries, sagas with compensation, and adaptive tuning together.",
            "<span>SemaphoreSlim</span><em>+</em><span>MediatR</span><em>+</em><span>Polly</span><em>+</em><span>TPL Dataflow</span><em>+</em><span>DI container</span><em>=</em><span style=\"color:var(--accent-ink); font-weight:650\">EvalApp</span>"))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("Fluent pipeline builder", "<code>Eval.App(...)</code> → domains → tasks → steps, compiled once into an immutable, reusable <code>ICompiledPipeline&lt;T&gt;</code>."),
                new CardSpec("Resource-gated concurrency", "Declare <code>Network</code> / <code>DiskIO</code> / <code>Cpu</code> / <code>Database</code> (or a named custom pool) and every step that touches it contends for the same bound."),
                new CardSpec("Adaptive and Bayesian tuning", "A hill-climbing tuner that rediscovers the optimum each run, or a Bayesian tuner with a performance model that survives restarts. Both opt-in, both tune within the bounds you declare."),
                new CardSpec("Parallel ForEach and branches", "Bounded per-item fan-out over a collection (<code>Tunable.ForItems()</code> lets the tuner pick the count) and fixed concurrent branches with configurable merge strategies."),
                new CardSpec("Sagas with compensation", "Chain steps with an undo; when a later step fails, every prior step's compensation runs in reverse (LIFO) order."),
                new CardSpec("Middleware", "Retry, timeout, circuit breaker, timing, audit, and validation, composable around any step."),
                new CardSpec("Results as data, not exceptions", "<code>PipelineResult&lt;T&gt;</code> is <code>Success</code> / <code>Failure</code> / <code>Skipped</code>, pattern-matched instead of caught."),
                new CardSpec("Offline licensing facade", "The same cross-product license check every Evaluated Applications package embeds, entirely offline (ECDsa P-256, no network call, ever)."),
            }))
        .Section(SectionSpec.Raw(
            "None of this is invented from nothing", "where the ideas come from",
            """
            <div class="tbl-wrap" style="overflow-x:auto; border:1px solid var(--border); border-radius:var(--radius)">
                  <table style="border-collapse:collapse; width:100%; font-size:.92rem; min-width:560px">
                    <thead><tr>
                      <th style="text-align:left; padding:11px 16px; border-bottom:1px solid var(--border-2); font-family:var(--mono); font-size:.76rem; text-transform:uppercase; letter-spacing:.05em; color:var(--ink-faint); font-weight:500">What EvalApp does</th>
                      <th style="text-align:left; padding:11px 16px; border-bottom:1px solid var(--border-2); font-family:var(--mono); font-size:.76rem; text-transform:uppercase; letter-spacing:.05em; color:var(--ink-faint); font-weight:500">Where the idea comes from</th>
                    </tr></thead>
                    <tbody style="color:var(--ink-soft)">
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">All state is a plain typed record; steps transform it, never own it</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Data-driven design, out of game and simulation architecture</td></tr>
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">A step returns success or failure and the pipeline short-circuits without throwing</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Railway-Oriented Programming — Scott Wlaschin, the F# community</td></tr>
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Compensating actions run in reverse when something fails partway through</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">The Saga pattern — Garcia-Molina &amp; Salem, 1987</td></tr>
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">The builder's return type changes with every call, so invalid orderings don't compile</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">The .NET fluent-builder convention plus the type-state pattern</td></tr>
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Probe a neighbouring value, measure, keep it or revert</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Hill-climbing / adaptive concurrency, out of the auto-tuning literature</td></tr>
                      <tr><td style="padding:10px 16px; border-bottom:1px solid var(--border)">A persistent belief about what works best, updated as evidence comes in, that survives a restart</td><td style="padding:10px 16px; border-bottom:1px solid var(--border)">Bayesian bandits / Thompson sampling, out of the multi-armed-bandit literature</td></tr>
                      <tr><td style="padding:10px 16px">Domains map to bounded contexts; naming follows how the business actually talks</td><td style="padding:10px 16px">Domain-Driven Design — Eric Evans, 2003</td></tr>
                    </tbody>
                  </table>
                </div>
            """))
        .Section(SectionSpec.CardGrid(
            "Where it fits, and where it doesn't", "honest scope",
            new[]
            {
                new CardSpec("Not a durable-execution system", "Long-running workflows that must survive a process restart over hours or days are Temporal's job, not EvalApp's."),
                new CardSpec("Not a reactive-resilience library", "Purely reactive resilience — detect a break, respond to it — is what Polly is for. EvalApp is proactive: it tunes toward the operating point that avoids the break."),
                new CardSpec("The case in between", "No infrastructure to stand up, throughput that matters, and concurrency that tunes itself instead of getting hand-picked once — that's what EvalApp is for."),
            }))
        .Section(SectionSpec.SnippetList(
            "Get started", "minimal example",
            new[]
            {
                new SnippetSpec(
                    """
                    using EvalApp.Consumer;

                    public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");

                    Eval.App("Orders")
                        .DefineDomain("Orders")
                            .DefineTask&lt;OrderData&gt;("ProcessOrder")
                                .AddStep("Validate", d =&gt; d with { Status = "Validated" })
                                .AddStep("ApplyDiscount", d =&gt; d with { Total = d.Total * 0.9m })
                                .AddStep("Complete", d =&gt; d with { Status = "Complete" })
                            .Run(out ICompiledPipeline&lt;OrderData&gt; pipeline)
                        .Build();   // no license key needed — nothing is license-gated today

                    var result = await pipeline.RunAsync(new OrderData("Alice", 99.99m));
                    """,
                    DescAfterHtml: "Build the pipeline once (e.g. at startup); call <code>RunAsync</code> many times."),
            },
            limHtml: "<b>Everything in EvalApp is free to use today.</b> Nothing is license-gated; a license key changes no runtime behavior right now. The license system exists as shared, ready-to-use wiring for a possible future advanced tier, but it caps nothing today. <b>Where it doesn't fit:</b> long-running workflows that must survive a process restart over hours or days are Temporal's job, not EvalApp's; purely reactive resilience is what Polly is for. Want the concurrency tuner itself to keep learning live instead of using the built-in heuristic? See <a href=\"/evalapp-neural.html\">EvalApp.Neural</a> →."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

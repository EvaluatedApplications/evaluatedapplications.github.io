using EvalApp.Consumer;
using SiteKit.Spec;

namespace SiteKit.Render;

/// <summary>
/// The real, corrected EvalApp render pipeline — replaces the wrong two-pipeline sketch in
/// platform-architecture.md §3.2 (a separately-built ICompiledPipeline&lt;PageRenderJob&gt;
/// plugged in as a step doesn't compile: Consumer.ICompiledPipeline&lt;T&gt; doesn't implement
/// IStep&lt;T&gt;). Per evalapp-owner's review: ONE compiled tree, sites -> pages via nested
/// ForEach-in-ForEach, all builder-authored in a single Eval.App(...) chain. No WithTuning()
/// (fixed Tunable.ForCpu()/Tunable.Between bounds instead — a build-time batch workload doesn't
/// benefit from adaptive per-run tuning, and there's no sanctioned non-file-persisting tuning
/// store for CI/ephemeral contexts). Build() compiles once; RunAsync can be called many times
/// (many CI runs, or many client sites in one call by passing more SiteSpecs).
/// </summary>
public static class SiteKitPipeline
{
    public static ICompiledPipeline<MultiSiteBuildJob> Build()
    {
        Eval.App("SiteKit.BuildSites")
            // The one gate every page write across every site shares — verified by evalapp-owner
            // as constructed once and genuinely shared, the actual resource-gating payoff this
            // whole design exists for. Fixed bound, not tuned: DiskIO's optimum for "write a
            // static file" is already obvious (a handful of concurrent writers), so there is
            // nothing for a per-run tuner to discover here.
            .WithResource(ResourceKind.DiskIO, Tunable.Between(1, 8, 4))
            .DefineDomain("Sites")
                .DefineTask<MultiSiteBuildJob>("BuildSites")
                    .ForEach<SiteRenderJob>(
                        select: job => job.Sites.Select(s => new SiteRenderJob(s)),
                        merge: (job, results) => job with
                        {
                            WrittenFiles = results.SelectMany(r => r.WrittenFiles ?? Array.Empty<string>()).ToList()
                        },
                        collectionName: "sites",
                        parallelism: Tunable.ForCpu(),
                        configure: site => site
                            .ForEach<PageRenderJob>(
                                select: s => s.Spec.Pages.Select(p =>
                                    new PageRenderJob(p, s.Spec.Brand, s.Spec.Nav, s.Spec.OutputRoot)),
                                merge: (s, results) => s with
                                {
                                    WrittenFiles = results.Select(r => r.OutputPath!).ToList()
                                },
                                collectionName: "pages",
                                parallelism: Tunable.ForCpu(),
                                configure: page => page
                                    .AddStep("RenderHead", job => job with
                                    {
                                        HeadHtml = HeadComposer.Compose(job.Spec, job.Brand)
                                    })
                                    .AddStep("RenderNav", job => job with
                                    {
                                        NavHtml = NavComposer.Compose(job.Spec, job.Nav, job.Brand)
                                    })
                                    .AddStep("RenderHero", job => job with
                                    {
                                        // First body fragment. Accumulation, not concatenation —
                                        // see RenderSections below + evalapp-owner's O(n^2) flag.
                                        BodyFragments = new List<string> { HeroComposer.Compose(job.Spec, job.Brand) }
                                    })
                                    .AddStep("RenderSections", job => job with
                                    {
                                        // Append each section's own fragment to the list; joined
                                        // exactly once in ComposeHtml. NOT `BodyHtml + section`
                                        // per section (that was the real O(n^2) bug in the
                                        // original §3.2 sketch, unrelated to the pipeline choice
                                        // itself but fixed in the same pass per the review).
                                        BodyFragments = (job.BodyFragments ?? Array.Empty<string>())
                                            .Concat(SectionComposer.ComposeAll(job.Spec))
                                            .ToList()
                                    })
                                    .AddStep("RenderFooter", job => job with
                                    {
                                        FooterHtml = FooterComposer.Compose(job.Spec.Footer, job.Brand.CompanyName)
                                    })
                                    .AddStep("ComposeHtml", job => job with
                                    {
                                        FinalHtml = HtmlComposer.Compose(job),
                                        OutputPath = Path.Combine(job.OutputRoot, job.Spec.Slug + ".html")
                                    })
                                    // The ONE real side effect. SideEffectStep<T> declaring
                                    // ResourceKind + AddStep<TStep>() auto-gates on the DiskIO
                                    // pool declared above — confirmed correct as originally
                                    // sketched, no change needed here per the review.
                                    .AddStep<WriteStaticFileStep>("WriteFile")
                            )
                    )
                .Run(out ICompiledPipeline<MultiSiteBuildJob> pipeline)
            .Build();

        return pipeline;
    }
}

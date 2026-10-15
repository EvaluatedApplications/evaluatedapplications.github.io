using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 5: site/evalapp-neural.html transcribed verbatim into a PageSpec.
/// Needed NO new composer capability — a 3-snippet section where the first two snippets each
/// carry a DescAfterHtml introducing the next one and the third has none (the same chained-intro
/// shape Phasor's 2-snippet section already exercised, just one snippet longer), plus three plain
/// CardGrids (one with a closing LimHtml). Ported as a second control page for this batch.
/// </summary>
public static class EvalAppNeuralPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "EvalApp.Neural — real-time neural tuning for EvalApp",
            Description: "EvalApp.Neural plugs a tiny, always-learning neural policy into EvalApp in place of the built-in heuristic tuner. Ships warm, keeps learning live, and adapts to workloads that shift or couple multiple resources — where a fixed heuristic falls flat.",
            Canonical: "https://evaluatedapplications.github.io/evalapp-neural.html",
            OgTitle: "EvalApp.Neural — real-time neural tuning for EvalApp",
            OgDescription: "A tiny, always-learning neural policy that plugs into EvalApp's concurrency tuner and keeps adapting live.",
            OgUrl: "https://evaluatedapplications.github.io/evalapp-neural.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.EvalApp.Neural","description":"EvalApp.Neural plugs a tiny, always-learning neural policy into EvalApp in place of the built-in heuristic tuner. Ships warm, keeps learning live, and adapts to workloads that shift or couple multiple resources.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.0.1","url":"https://evaluatedapplications.github.io/evalapp-neural.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.EvalApp.Neural","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("EvalApp.Neural · machine learning")
            .Headline("A tuner that keeps learning while your pipeline runs.")
            .Lede("EvalApp's built-in concurrency tuner works well on clean, predictable workloads but falls flat when conditions shift or resources couple — a database racing CPU racing disk. EvalApp.Neural plugs a tiny, always-learning neural policy into EvalApp in its place. It trains on a real workload's own experience, so it adapts live as conditions change, ships warm (pre-trained on diverse scenarios), and keeps learning from every decision.")
            .Fact("<b>v1.0.1</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("<b>~0.4ms</b> per decision")
            .Fact("d48/L1 holographic model")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.EvalApp.Neural")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.EvalApp.Neural", CtaStyle.Ghost, externalNewTab: true)
            .Cta("See EvalApp →", "/evalapp.html", CtaStyle.Ghost)
            .Related("EvalApp", "/evalapp.html")
            .Related("AlgFormer", "/algformer.html")
            .BarTitle("evalapp-neural.app"))
        .Section(SectionSpec.CardGrid(
            "Why it works", "online, not frozen",
            new[]
            {
                new CardSpec("Online learning", "Unlike a frozen prior, it reacts to real-time shifts in contention, load, or workload shape. The built-in heuristic, tuned on static assumptions, can't do this."),
                new CardSpec("Proven on real workloads", "Matches the built-in heuristic on lean, single-gate pipelines (no edge there — the optimum is obvious), but beats it by roughly <b>25%</b> on EvalApp.Neural.Train's Sisyphus benchmark, a coupled DB + CPU + disk workload deliberately built to fight itself."),
                new CardSpec("Opt-in, no refactor", "Add <code>.WithNeuralTuning()</code> to your app builder; nothing else about the pipeline changes."),
            }))
        .Section(SectionSpec.SnippetList(
            "How to use it", "one line, or process-wide",
            new[]
            {
                new SnippetSpec(
                    """
                    var app = Eval.App(...)
                        .WithResource(...)
                        .WithNeuralTuning()
                        .DefineDomain(...);
                    """,
                    DescAfterHtml: "Install it globally for every pipeline in the process (useful if you're running multiple):"),
                new SnippetSpec(
                    """
                    using var _ = NeuralTuning.UseGlobally(NeuralTuning.NewModel());
                    """,
                    DescAfterHtml: "Or train on your own workload by pointing a shared model file at every runner — the model grows as they train:"),
                new SnippetSpec(
                    """
                    using var _ = NeuralTuning.UseGloballyPersisted("model.bin");
                    """),
            }))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("Warm-start", "Ships pre-trained on diverse workloads; nothing to configure before it's useful."),
                new CardSpec("Always learning", "Real-time adaptation to non-stationary conditions — no manual retuning needed."),
                new CardSpec("Tiny footprint", "A d48/L1 holographic model (HoloFormer, via AlgFormer) at roughly 0.4ms per decision."),
                new CardSpec("No license gate", "Runs at full effect regardless of license tier — free, period."),
                new CardSpec("Production ready", "Coordinate descent handles multi-gate coupling cleanly; deterministic under a repeated seed."),
            }))
        .Section(SectionSpec.CardGrid(
            "What you need to know", "good to know",
            new[]
            {
                new CardSpec("Best on coupled, noisy workloads", "Clean single-gate CPU-bound pipelines won't see much gain — the optimum there is already obvious. Real multi-gate scenarios are where it shines."),
                new CardSpec("Online by default", "Always learning from its own decisions. You can freeze it for reproducibility, but production paths always enable learning."),
                new CardSpec("Dependencies", "Depends on <a href=\"/evalapp.html\">EvalApp</a> and <a href=\"/algformer.html\">AlgFormer</a> — AlgFormer ships built into the DLL, no separate install needed."),
            },
            limHtml: "<b>Growing the warm-start:</b> the shipped model is retrained periodically on a growing set of realistic, coupled multi-resource workloads, and the improved weights ship in each release — you always start from the best warm-start we've measured, and it keeps learning live from your own workload on top of that. <b>Compatible:</b> .NET 8.0+, Windows/Linux. <b>License:</b> proprietary, same terms as every EA product."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 6: site/algformer-gpu.html transcribed verbatim into a PageSpec.
/// Needed NO new composer capability — the simplest structural shape in this whole batch: three
/// CardGrid sections only (no Prose, no Snippets, no StackFlow/Raw), the last carrying a closing
/// LimHtml. Ported as a third control page, and the first page site-wide with zero code-snippet
/// sections at all, exercising that a page can validly have none.
/// </summary>
public static class AlgFormerGpuPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "AlgFormer.Gpu — optional CUDA acceleration for HoloFormer",
            Description: "AlgFormer.Gpu adds ILGPU/CUDA-backed training and inference for AlgFormer's HoloFormer engine: same model, same weights, just faster, with automatic CPU fallback when no GPU is present.",
            Canonical: "https://evaluatedapplications.github.io/algformer-gpu.html",
            OgTitle: "AlgFormer.Gpu — optional CUDA acceleration for HoloFormer",
            OgDescription: "Same model, same weights, just faster. GPU-batched training and inference for AlgFormer's HoloFormer engine, with automatic CPU fallback.",
            OgUrl: "https://evaluatedapplications.github.io/algformer-gpu.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.AlgFormer.Gpu","description":"AlgFormer.Gpu adds ILGPU/CUDA-backed training and inference for AlgFormer's HoloFormer engine: same model, same weights, just faster, with automatic CPU fallback when no GPU is present.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+ (CUDA optional)","softwareVersion":"1.3.0","url":"https://evaluatedapplications.github.io/algformer-gpu.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.AlgFormer.Gpu","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("AlgFormer.Gpu · machine learning")
            .Headline("Same model, same weights. Just faster.")
            .Lede("AlgFormer.Gpu adds ILGPU/CUDA-backed training and inference for the HoloFormer (holographic attention) engine from the core <a href=\"/algformer.html\">AlgFormer</a> package. It builds GPU kernels straight from a CPU HoloFormer's serialized weights — no separate model format, no retraining to switch devices. Backward passes, a fused on-device Adam optimizer step, and batched forward inference all run on the GPU when one is present, with the CPU engine automatically taking over when it isn't.")
            .Fact("<b>v1.3.0</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("ILGPU / CUDA")
            .Fact("automatic CPU fallback")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.AlgFormer.Gpu", maxWidthPx: 560)
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.AlgFormer.Gpu", CtaStyle.Ghost, externalNewTab: true)
            .Cta("See AlgFormer →", "/algformer.html", CtaStyle.Ghost)
            .Related("AlgFormer", "/algformer.html")
            .Related("Phasor", "/phasor.html")
            .BarTitle("algformer-gpu.app"))
        .Section(SectionSpec.CardGrid(
            "Why it's useful", "drop-in, not a rewrite",
            new[]
            {
                new CardSpec("Drop-in, not a rewrite", "Point it at an existing CPU <code>HoloFormer</code>; it trains and serves against the same serialized weights."),
                new CardSpec("Automatic fallback", "Runtime device detection means the same build works on a machine with a CUDA GPU and one without — no separate code paths for callers to maintain."),
                new CardSpec("Correctness-checked against the CPU engine", "Every GPU kernel is gradient-checked against the CPU HoloFormer (which does the math in double precision) as the reference oracle. GPU runs in float32 for speed, so results are float-close to the CPU reference, not bit-identical."),
                new CardSpec("Fused on-device Adam", "The optimizer step runs on the GPU too, alongside the forward/backward kernels, avoiding a round trip to the CPU on every training batch."),
            }))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("GPU-batched training", "Forward and backward for HoloFormer, including per-layer weight-tied iterative refinement (\"StackIter\") and all-positions loss."),
                new CardSpec("Checkpoint/resume", "Fused on-device Adam optimizer state can be checkpointed and resumed alongside the GPU-resident training loop."),
                new CardSpec("Device detection", "<code>GpuDevice.HasGpu</code> with silent, safe CPU fallback when no supported CUDA device is present."),
                new CardSpec("Standalone kernel verification", "Utilities to confirm a given GPU matches the CPU reference before trusting it in production."),
            }))
        .Section(SectionSpec.CardGrid(
            "Good to know", "honest limits",
            new[]
            {
                new CardSpec("Requires a CUDA device to use the GPU path", "Without an ILGPU-supported CUDA device at runtime, the package falls back to the CPU <code>HoloFormer</code> engine automatically — the same build works either way."),
                new CardSpec("Optional", "The core <code>AlgFormer</code> package has no GPU dependency at all and stays dependency-free and AOT/trim-safe. Add this package only if you want GPU-accelerated training or inference."),
                new CardSpec("The older softmax path is deprecated", "The package also includes GPU support for AlgFormer's earlier (non-HoloFormer) softmax-attention model. That path still builds and runs, but all current development — including the fused Adam step — targets HoloFormer only. New projects should use the HoloFormer path."),
            },
            limHtml: "Install from NuGet: <code>EvaluatedApplications.AlgFormer.Gpu</code>. Requires the core <code>EvaluatedApplications.AlgFormer</code> package alongside it. <b>License:</b> proprietary; every capability here is free to use today."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

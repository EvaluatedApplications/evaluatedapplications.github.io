using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 1: site/algformer.html transcribed verbatim. Chosen to exercise two
/// genuinely new composer capabilities in one page, both promoted to typed SectionKinds (not
/// SectionSpec.Raw, per the "type what recurs" rule): SectionKind.ToolGrid (the "Try it live"
/// `.card.tool` gallery — a `&lt;a class="card tool"&gt;` link with its own `.tag`/`.ver`/`.go-in`
/// shape, distinct from CardGrid's `&lt;article&gt;` cards) and SectionKind.Compare (the "Two
/// cores, same shape" `.cmp` two-card side-by-side, reusing the exact same CardSpec shape as
/// CardGrid — just a different wrapper class — followed by one closing prose paragraph).
/// </summary>
public static class AlgFormerPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "AlgFormer — a transformer engine with two attention cores, for .NET",
            Description: "AlgFormer defines, trains, and runs transformer-style language models in pure managed .NET — no GPU, no Python, no native runtime required. Two interchangeable attention cores: classic softmax, and a holographic bind/bundle/unbind core that scales linearly instead of quadratically.",
            Canonical: "https://evaluatedapplications.github.io/algformer.html",
            OgTitle: "AlgFormer — a transformer engine with two attention cores, for .NET",
            OgDescription: "A real, gradient-trained transformer, written entirely in C#. Two param-for-param comparable attention cores: classic softmax, and a holographic core that plays context instead of scoring it.",
            OgUrl: "https://evaluatedapplications.github.io/algformer.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.AlgFormer","description":"AlgFormer defines, trains, and runs transformer-style language models in pure managed .NET — no GPU, no Python, no native runtime required. Two interchangeable attention cores: classic softmax, and a holographic bind/bundle/unbind core that scales linearly instead of quadratically.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.5.0","url":"https://evaluatedapplications.github.io/algformer.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.AlgFormer","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("AlgFormer · machine learning")
            .Headline("A transformer engine with two attention cores. One classic, one holographic.")
            .Lede("AlgFormer defines, trains, and runs transformer-style language models in pure managed .NET —\n          <code>double[]</code> arrays with SIMD, no ONNX, no libtorch, no CUDA toolkit, no Python interop. Give it a\n          vocabulary and a shape, train it on your own token sequences through a data-parallel pipeline, and run\n          inference — single prediction, full logits, or streaming generation with a KV cache. It ships two\n          interchangeable attention cores so you can compare what \"attention\" costs.")
            .Fact("<b>v1.5.0</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("no external ML runtime")
            .Fact("no GPU required")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.AlgFormer")
            .Cta("How the holographic core works, in plain English →", "/holoformer.html", CtaStyle.Primary)
            .Cta("Talk to a trained checkpoint, live →", "/tools/prism", CtaStyle.Ghost)
            .Cta("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.AlgFormer", CtaStyle.Ghost, externalNewTab: true)
            .Related("HoloFormer, explained", "/holoformer.html")
            .Related("AlgFormer.Gpu", "/algformer-gpu.html")
            .Related("Phasor", "/phasor.html")
            .BarTitle("algformer.app")
            .PrismBeam())
        .Section(SectionSpec.ToolGrid(
            "Try it live", "AlgFormer/HoloFormer, running in your browser right now",
            new[]
            {
                new ToolCardSpec("Prism", "/tools/prism", "live", "AlgFormer",
                    "Chat with a real, point-in-time copy of our trained checkpoint — then look under the hood\n          at every layer and pass it resonated through, live, per character it writes back.",
                    "Open Prism →", "var(--c-algformer)"),
                new ToolCardSpec("The Creature", "/tools/creature", "live", "AlgFormer · Tracer",
                    "Raise a small creature that learns to navigate a world you draw. A holographic transformer\n          for its brain, our pathfinder for its feet — it starts clueless and gets better as you watch.",
                    "Open The Creature →",
                    "linear-gradient(90deg, var(--c-algformer) 0%, var(--c-algformer) 50%, var(--c-tracer) 50%, var(--c-tracer) 100%)",
                    "var(--c-algformer)"),
                new ToolCardSpec("The Forecaster", "/tools/forecaster", "live", "AlgFormer",
                    "Watch a holographic transformer learn to call the next move on a real hourly stock price\n          tape — direction and rough size, training live in your tab as it watches more history go by.",
                    "Open The Forecaster →", "var(--c-algformer)"),
            }))
        .Section(SectionSpec.Prose(
            "The problem it solves", "a real transformer, no native runtime",
            "Most transformer tooling means either binding into a native runtime (ONNX, PyTorch/libtorch, CUDA) or\n      hand-rolling matrix code with no attention research behind it. AlgFormer sits in between: a real,\n      gradient-trained transformer architecture, written entirely in C#, that runs anywhere .NET runs.\n      It also lets you compare two different ideas of what attention should cost. Standard softmax attention\n      scores every token against every other token — quadratic in sequence length. AlgFormer ships a second,\n      holographic attention core alongside it that composes context through bind/bundle/unbind operations\n      instead of pairwise scoring — linear in sequence length, with constant-time-per-token serving. Both cores\n      are parameter-for-parameter comparable, so you can train the same shape on the same data with either and\n      see the difference directly."))
        .Section(SectionSpec.Compare(
            "Two cores, same shape", "swap the core, not the workflow",
            new[]
            {
                new CardSpec("AlgFormer (softmax)",
                    "Classic dot-product attention — the well-understood baseline. Every dense map is an\n          algebraic relation-bank cell (<code>S·d</code> params, not <code>d²</code>), so it stays compact even\n          in pure managed code.",
                    "var(--c-algformer-gpu)"),
                new CardSpec("HoloFormer (holographic)",
                    "Attention is bind(k,v) → causal-bundle → unbind(q) — holographic resonance instead of\n          softmax·V. O(sequence length × model width) to train, effectively O(1) per token to serve via an\n          incremental KV cache, versus O(sequence length²) for standard attention.",
                    "var(--c-algformer)"),
            },
            "<code>AlgFormer</code> and <code>HoloFormer</code> share the same model shape, training loop, checkpoint format,\n      and serving API — construct one instead of the other with matching dimensions, and train/serve it through\n      the same code."))
        .Section(SectionSpec.CardGrid(
            "Why it's useful", "everything included to actually use it",
            new[]
            {
                new CardSpec("No external ML runtime", "Everything is plain <code>double[]</code> math with SIMD, compiled straight into your app. No ONNX Runtime, no libtorch, no CUDA toolkit, no Python interop."),
                new CardSpec("Attention that scales differently", "HoloFormer's holographic core is useful when context windows get long — training cost grows linearly, serving cost per token stays effectively constant."),
                new CardSpec("Everything to actually use it", "A subword tokenizer, a data-parallel trainer, streaming generation with a KV cache, checkpoint save/load with format versioning, and helpers to grow a trained model to a longer context or bigger vocabulary without retraining from scratch."),
                new CardSpec("Built for distributed and swarm training", "Relay components let multiple machines train a shared model cooperatively over MQTT, exchanging either full batches or lightweight position manifests."),
            }))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("Pure managed code", "<code>double[]</code> + SIMD — no external ML runtime, no native dependency, no GPU required."),
                new CardSpec("Data-parallel training", "Resource-gated, adaptively tuned, for full epochs over your dataset."),
                new CardSpec("Streaming inference", "An incremental KV cache for low-latency, constant-work-per-token generation."),
                new CardSpec("Versioned checkpoints", "Backward-compatible format versioning, so old checkpoints keep loading as the format evolves."),
                new CardSpec("In-place model growth", "Extend context length, attention shifts, or vocabulary on an already-trained model — <code>HoloShape</code> sizes it with measured, not guessed, tradeoffs."),
                new CardSpec("Gradient-check oracles", "Built in, so the training math itself can be verified, not just assumed correct."),
            }))
        .Section(SectionSpec.SnippetList(
            "Get started", "minimal example",
            new[]
            {
                new SnippetSpec(
                    """
                    using PrismFormer;

                    // Define a model (tokens are ints — bring your own tokenizer/encoding)
                    var model = new AlgFormer(vocab: 16, shifts: 4, layers: 2, maxContext: 6, dModel: 32, frozenPrefix: 0);

                    // Train — data is a list of (context tokens, target token) pairs
                    var trainer = new PrismTrainer(model);                 // data-parallel via EvalApp
                    double loss = trainer.TrainEpoch(data, batchSize: 64, lr: 5e-2, shuffleSeed: 1);

                    // Run inference
                    int next      = model.Predict(context);                 // argmax next token
                    double[] lg   = model.LogitsFor(context);                // full logits
                    int[] sample  = model.Generate(prompt, maxNewTokens: 20, temperature: 0.8);
                    """,
                    DescAfterHtml: "Swapping in the holographic core is the same shape — construct a <code>HoloFormer</code> instead of an <code>AlgFormer</code> with matching dimensions, and train/serve it through the same API."),
            },
            limHtml: "<b>Compatibility:</b> .NET 8.0+, pure managed code, runs anywhere .NET runs. Depends on the <a href=\"/phasor.html\">Phasor</a> codec (model geometry defaults) and the <a href=\"/evalapp.html\">EvalApp</a> pipeline runtime (data-parallel training); swarm relay components use MQTTnet. Want GPU-accelerated training/inference for the holographic core? See <a href=\"/algformer-gpu.html\">AlgFormer.Gpu</a> →. <b>License:</b> proprietary, compiled library only — every capability is free to use today."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

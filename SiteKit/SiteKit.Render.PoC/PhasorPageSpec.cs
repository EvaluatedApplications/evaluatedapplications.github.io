using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// The Phase-1 proof-of-concept port: site/phasor.html transcribed verbatim (words, links,
/// facts, code samples) into a real PageSpec value via the SiteKit.Spec fluent builder.
/// Every string below is copied from the live file, not re-derived — this is a RENDER port,
/// not a rewrite of the page's content.
/// </summary>
public static class PhasorPageSpec
{
    public static BrandTokens Brand() => new(
        CompanyName: "Evaluated Applications",
        FaviconDataUri: "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Cdefs%3E%3ClinearGradient id='g' x1='0' y1='0' x2='1' y2='1'%3E%3Cstop offset='0' stop-color='%23f0796a'/%3E%3Cstop offset='.166' stop-color='%23f0a15a'/%3E%3Cstop offset='.333' stop-color='%23e6c450'/%3E%3Cstop offset='.5' stop-color='%237bd86a'/%3E%3Cstop offset='.666' stop-color='%234aa3ff'/%3E%3Cstop offset='.833' stop-color='%237d7dff'/%3E%3Cstop offset='1' stop-color='%23c07dff'/%3E%3C/linearGradient%3E%3C/defs%3E%3Crect width='32' height='32' rx='7' fill='%23050608'/%3E%3Cpath d='M16 5L27 26H5Z' fill='none' stroke='url(%23g)' stroke-width='2' stroke-linejoin='round'/%3E%3C/svg%3E",
        MarkGradientSvgDefs: "<defs><linearGradient id=\"m\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#f0796a\"/><stop offset=\".166\" stop-color=\"#f0a15a\"/><stop offset=\".333\" stop-color=\"#e6c450\"/><stop offset=\".5\" stop-color=\"#7bd86a\"/><stop offset=\".666\" stop-color=\"#4aa3ff\"/><stop offset=\".833\" stop-color=\"#7d7dff\"/><stop offset=\"1\" stop-color=\"#c07dff\"/></linearGradient></defs>",
        PrismBeamSvg: """
        <svg viewBox="0 0 480 320" preserveAspectRatio="xMidYMid meet">
          <line x1="0" y1="150" x2="150" y2="150" stroke="var(--ink-faint)" stroke-width="2" stroke-linecap="round" opacity=".55"/>
          <path d="M150 78L150 222L262 150Z" fill="none" stroke="var(--ink-faint)" stroke-width="1.5" stroke-linejoin="round" opacity=".5"/>
          <g stroke-width="1.6" stroke-linecap="round" opacity=".85">
            <line x1="258" y1="150" x2="478" y2="56" stroke="var(--spectrum-1)"/>
            <line x1="258" y1="150" x2="478" y2="92" stroke="var(--spectrum-2)"/>
            <line x1="258" y1="150" x2="478" y2="128" stroke="var(--spectrum-3)"/>
            <line x1="258" y1="150" x2="478" y2="164" stroke="var(--spectrum-4)"/>
            <line x1="258" y1="150" x2="478" y2="200" stroke="var(--spectrum-5)"/>
            <line x1="258" y1="150" x2="478" y2="236" stroke="var(--spectrum-6)"/>
            <line x1="258" y1="150" x2="478" y2="272" stroke="var(--spectrum-7)"/>
          </g>
        </svg>
        """);

    public static NavSpec Nav() => new(new[]
    {
        new RelatedLink("Home", "/"),
        new RelatedLink("Packages", "/packages.html"),
        new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
    });

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "Phasor — a vector-symbolic codec for .NET",
            Description: "Phasor encodes numbers and symbols as phasor faces and composes them with one algebra — bind, unbind, bundle, correlate. Arithmetic is encoding, not calculation. Zero dependencies, AOT/trim-safe, the foundation the rest of Evaluated Applications is built on.",
            Canonical: "https://evaluatedapplications.github.io/phasor.html",
            OgTitle: "Phasor — a vector-symbolic codec for .NET",
            OgDescription: "Encode numbers and symbols as phasor faces, then compose them with one algebra instead of a calculator. Zero dependencies, AOT/trim-safe.",
            OgUrl: "https://evaluatedapplications.github.io/phasor.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.Phasor","description":"Phasor encodes numbers and symbols as phasor faces and composes them with one algebra — bind, unbind, bundle, correlate. Zero dependencies, AOT/trim-safe, the foundation the rest of Evaluated Applications is built on.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.0.3","url":"https://evaluatedapplications.github.io/phasor.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.Phasor","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("Phasor · foundation")
            .Headline("Encode numbers and symbols as phasor faces. Compose them with one algebra.")
            .Lede("Phasor is a Vector Symbolic Architecture (VSA) codec for .NET. Tokens — numbers and text — become bundles of unit complex numbers called phasors. Bind, unbind, bundle, and correlate form the complete algebra: arithmetic is encoding, not calculation. It's the base package the rest of the Evaluated Applications stack (AlgFormer, HoloDb, HoloVoxel) is built on.")
            .Fact("<b>v1.0.3</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("zero dependencies")
            .Fact("AOT &amp; trim-safe")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.Phasor")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.Phasor", CtaStyle.Ghost, externalNewTab: true)
            .Cta("See it power a transformer →", "/algformer.html", CtaStyle.Ghost)
            .Related("EvalApp", "/evalapp.html")
            .Related("AlgFormer", "/algformer.html")
            .Related("HoloDb", "/holodb/")
            .Related("HoloVoxel", "/holovoxel.html")
            .BarTitle("phasor.app")
            .PrismBeam())
        .Section(SectionSpec.Prose(
            "The problem it solves", "numbers and symbols, one algebra",
            "Symbolic and numeric data are usually kept separate: numbers flow through arithmetic circuits, symbols through lookup tables. Phasor bridges them. Bind two numbers on the linear band and you add them; bind on the log band and you multiply them. A symbol encodes to a fixed random phasor. Now both live in the same algebra — you can store them together, mix them, and read values back by correlation. This is the substrate for exact storage (no floating-point error in bundle/delete), similarity search at scale, and models that reason over both values and symbols with the same composition operators."))
        .Section(SectionSpec.CardGrid(
            "How it works", "two operations",
            new[]
            {
                new CardSpec("Encode", "A number becomes a face with its value encoded as phase — value in, phase out, deterministic. A symbol gets a salted hash-based random signature."),
                new CardSpec("Compose", "<b>Bind</b> (complex multiply per component) builds relationships; <b>bundle</b> (sum) collects sets or superpositions; <b>unbind</b> (conjugate) extracts parts exactly; <b>readout</b> by correlation gives a dot-product score. On the linear band, bind is addition; on the log band, it's multiplication — there is no separate calculator, arithmetic <em>is</em> the composition."),
            }))
        .Section(SectionSpec.CardGrid(
            "Two profiles", "pick the one that matches your workload",
            new[]
            {
                new CardSpec("PhasorCodec", "Fixed-width (256 reals) with a frozen identity prefix and a learned orbital tail. Built for neural models: the frozen part keeps values exact while the tail learns meaning downstream."),
                new CardSpec("HoloCodec", "Configurable width, SIMD (<code>Vector&lt;double&gt;</code>), a planar layout (reals, then imags) with a memoized number cache. Built for holographic storage and similarity search at scale."),
            },
            limHtml: "Do not mix faces between the two profiles — they use different layouts and are not interchangeable inputs to the same op."))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("One algebra, no special cases", "Bind, unbind, bundle and readout work the same way on numbers and symbols."),
                new CardSpec("Exact composition", "Bundle and unbind are exact; subtraction removes a contribution with no floating-point error."),
                new CardSpec("No dependencies", "Only .NET <code>System</code> and <code>System.Numerics</code> — no external libraries."),
                new CardSpec("AOT and trim compatible", "Works with ahead-of-time compilation and tree-shaking, no reflection tricks."),
                new CardSpec("Deterministic hashing", "Symbols encode to repeatable, salted phasor signatures — reproducible, not cryptographic."),
                new CardSpec("Memoized decode cache", "<code>HoloCodec</code> caches number faces to speed up repeated decodes."),
            }))
        .Section(SectionSpec.SnippetList(
            "Get started", "minimal example",
            new[]
            {
                new SnippetSpec(
                    """
                    using Phasor;

                    // Encode a number and a symbol
                    double[] five     = PhasorCodec.Encode("5");
                    double[] greeting = PhasorCodec.Encode("hello");

                    // Bind two numbers: adds them on the linear band
                    double[] fivePlusThree = PhasorCodec.Bind(five, PhasorCodec.Encode("3"));
                    int sum = PhasorCodec.DecodeSum(fivePlusThree, max: 10);   // sum == 8

                    // Bundle (superpose) items to build a set, then unbind to extract one
                    double[] pair = PhasorCodec.Bundle(greeting, five);
                    double[] extracted = PhasorCodec.Unbind(pair, greeting);   // extracted ~= five
                    """,
                    DescAfterHtml: "For larger workloads or similarity search, use <code>HoloCodec</code> (SIMD, configurable width):"),
                new SnippetSpec(
                    """
                    using Phasor;

                    var codec = new HoloCodec(dim: 1024);   // 1024-dimensional, ~256 in the number band
                    double[] encoded = codec.Encode("hello");
                    double similarity = codec.Dot(encoded, another);
                    """),
            },
            limHtml: "<b>Compatibility:</b> .NET 8.0+, Windows/Linux/macOS, no platform-specific code, no dependency to bring your own model or storage layer. <b>License:</b> proprietary — every capability is free to use today; a license key is reserved for possible future advanced features, none gated currently."))
        .Section(SectionSpec.ClosingStack(
            "Where Phasor shows up",
            "It's the base package: <a href=\"/algformer.html\">AlgFormer</a>'s frozen input/output codec, <a href=\"/holodb/\">HoloDb</a>'s row holograms, and <a href=\"/holovoxel.html\">HoloVoxel</a>'s chunk storage all build directly on it.",
            new[]
            {
                new CtaLink("Get it on NuGet", "https://www.nuget.org/packages/EvaluatedApplications.Phasor", CtaStyle.Primary, ExternalNewTab: true),
                new CtaLink("See all packages", "/packages.html", CtaStyle.Ghost),
            }))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

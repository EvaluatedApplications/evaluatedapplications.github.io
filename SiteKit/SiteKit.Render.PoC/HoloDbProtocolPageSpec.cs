using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 4: site/holodb-protocol.html transcribed verbatim into a PageSpec.
/// Exercises SnippetSpec.DescBeforeHtml for the first time: "A minimal example" has a lead-in
/// paragraph before EVERY snippet ("Client-side (using...)" / "Server-side (using...)"),
/// structurally different from Phasor/Tracer/HoloDb.Client's "desc between two snippets reads as
/// introducing the next one" shape (DescAfterHtml) — here each snippet gets its own genuine
/// lead-in immediately before it, including the very first one, which DescAfterHtml alone cannot
/// express (there's no prior snippet for the first lead-in to attach "after"). Also a second
/// hero LimHtml instance (after evalapp.html), re-confirming that composer addition on a page
/// with a Related list that follows it, unrelated content.
/// </summary>
public static class HoloDbProtocolPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloDb.Protocol — the HoloDb wire contract",
            Description: "HoloDb.Protocol defines how the HoloDb server and client communicate: length-prefixed TCP framing and a columnar-preserving binary codec, so columnar results stay columnar all the way to the client.",
            Canonical: "https://evaluatedapplications.github.io/holodb-protocol.html",
            OgTitle: "HoloDb.Protocol — the HoloDb wire contract",
            OgDescription: "Length-prefixed TCP framing and a columnar-preserving codec — the contract between HoloDb server and client.",
            OgUrl: "https://evaluatedapplications.github.io/holodb-protocol.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.HoloDb.Protocol","description":"HoloDb.Protocol defines how the HoloDb server and client communicate: length-prefixed TCP framing and a columnar-preserving binary codec, so columnar results stay columnar all the way to the client.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.0.2","url":"https://evaluatedapplications.github.io/holodb-protocol.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.HoloDb.Protocol","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("HoloDb.Protocol · data")
            .Headline("Columns stay columns, all the way to the client.")
            .Lede("HoloDb.Protocol defines how the HoloDb server and client communicate. Instead of sending results as JSON or row-by-row data structures, it preserves the columnar shape: a <code>long[]</code>, <code>double[]</code>, or <code>string[]</code> travels as a typed array over the wire, not as individual objects — keeping <a href=\"/holodb/\">HoloDb</a>'s columnar advantage intact when data moves between processes.")
            .Fact("<b>v1.0.2</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("length-prefixed TCP framing")
            .Fact("transport-agnostic (TLS-ready)")
            .Install("dotnet add package EvaluatedApplications.HoloDb.Protocol", maxWidthPx: 560)
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb.Protocol", CtaStyle.Ghost, externalNewTab: true)
            .Cta("See HoloDb.Client →", "/holodb-client.html", CtaStyle.Ghost)
            .Lim("Most consumers should depend on <a href=\"/holodb-client.html\">HoloDb.Client</a> instead, which pulls this package in transitively. Depend on it directly only if you're implementing a server.")
            .Related("HoloDb", "/holodb/")
            .Related("HoloDb.Client", "/holodb-client.html")
            .BarTitle("holodb-protocol.app"))
        .Section(SectionSpec.Prose(
            "Why it matters", "the win doesn't stop at the wire",
            "Most database protocols trade columnar performance for JSON or ORM convenience. If you run a columnar query — the common case — and the server has to serialize it back to rows, materialize it into objects, and send JSON, you've lost the performance win that made the columnar query fast in the first place. This protocol keeps that win alive. It also encodes the wire format explicitly — no framework assumptions about object graphs or reflection — which makes it predictable (you know exactly what bytes cross the network), versionable (clients and servers from different builds detect incompatibility and fail cleanly), and testable (every message shape can be validated bit-for-bit)."))
        .Section(SectionSpec.CardGrid(
            "What it does", "features",
            new[]
            {
                new CardSpec("Columnar results", "<code>long[]</code>, <code>double[]</code>, <code>string[]</code> delivered directly, not wrapped in row objects."),
                new CardSpec("Materialized rows", "For non-columnar queries (JOINs, GROUP BY), results are packed into a compact binary row format."),
                new CardSpec("Length-prefixed frames", "Message boundaries stay safe even over unreliable transports or when connection buffering breaks alignment."),
                new CardSpec("Transport-agnostic", "Works over TCP, TLS, or any duplex <code>Stream</code> — this package doesn't provide encryption; TLS is the transport layer's concern."),
                new CardSpec("Security hardening", "Frame size limits and count-bounds checks protect against hostile or malformed length prefixes forcing huge allocations."),
                new CardSpec("Version negotiation", "Client and server compatibility is checked before executing queries, and error semantics distinguish protocol errors from query errors so callers know whether to retry."),
            }))
        .Section(SectionSpec.SnippetList(
            "A minimal example", "client and server side",
            new[]
            {
                new SnippetSpec(
                    """
                    using var client = new HoloDbClient("localhost", 5433, token: "mytoken");
                    await client.ConnectAsync();

                    // Result comes back as a columnar QueryResult if the query was columnar.
                    var result = await client.ExecuteAsync("SELECT * FROM prices WHERE date > ?", new[] { arg });

                    if (result.IsColumnar &amp;&amp; result.Columns["price"] is double[] prices)
                    {
                        foreach (var p in prices)
                            Console.WriteLine($"Price: {p}");
                    }
                    """,
                    DescBeforeHtml: "Client-side (using <a href=\"/holodb-client.html\">HoloDb.Client</a>, which wraps this protocol):"),
                new SnippetSpec(
                    """
                    var payload = await Wire.ReadFrameAsync(networkStream);
                    var request = HoloProtocol.DecodeRequest(payload);

                    if (request.Op == WireOp.Exec)
                    {
                        var result = await engine.ExecuteAsync(request.Sql);
                        var response = HoloProtocol.Result(result);
                        await Wire.WriteFrameAsync(networkStream, response);
                    }
                    """,
                    DescBeforeHtml: "Server-side (using this package to build a server):"),
            }))
        .Section(SectionSpec.Prose(
            "Compatibility &amp; wire stability", "frozen on purpose",
            "The wire format is frozen: opcode byte values never change, and message field order and types are stable. Old clients can talk to new servers (version negotiation detects incompatibility and fails cleanly), and changes to the protocol are rare and coordinated — both client and server must be rebuilt together.",
            limHtml: "<b>Depends on:</b> <a href=\"/holodb/\">HoloDb</a> (for the <code>QueryResult</code> type that crosses the wire in both directions). <b>Auth tokens</b> travel as plain UTF-8 in the <code>Hello</code> handshake — TLS is assumed by the deployment, not provided by this package. <b>License:</b> proprietary; every capability is free to use today."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

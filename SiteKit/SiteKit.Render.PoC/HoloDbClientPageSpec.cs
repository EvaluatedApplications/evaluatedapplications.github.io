using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 3: site/holodb-client.html transcribed verbatim into a PageSpec.
/// Needed NO new composer capability — every shape here (a Prose section with no LimHtml, a
/// CardGrid, a Snippets section using DescAfterHtml where a description between two snippets
/// introduces the next one, a closing CardGrid with a LimHtml) already existed after Phase 1/the
/// first Phase-2 batch. Ported specifically as a control: proof the existing composer surface
/// already generalizes to a 4th/5th/6th page without every page needing a new feature.
/// </summary>
public static class HoloDbClientPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloDb.Client — the .NET client for a remote HoloDb server",
            Description: "HoloDb.Client connects your .NET app to a HoloDb server over TLS. Same API as the embedded engine, columnar results stay columnar on the wire, and opt-in resilience: exponential backoff, a circuit breaker, and per-operation deadlines.",
            Canonical: "https://evaluatedapplications.github.io/holodb-client.html",
            OgTitle: "HoloDb.Client — the .NET client for a remote HoloDb server",
            OgDescription: "Connect to a HoloDb server over TLS with the same API as the embedded engine — swap embedded for networked by changing only how you get the handle.",
            OgUrl: "https://evaluatedapplications.github.io/holodb-client.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.HoloDb.Client","description":"HoloDb.Client connects your .NET app to a HoloDb server over TLS. Same API as the embedded engine, columnar results stay columnar on the wire, and opt-in resilience: exponential backoff, a circuit breaker, and per-operation deadlines.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.4.0","url":"https://evaluatedapplications.github.io/holodb-client.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.HoloDb.Client","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("HoloDb.Client · data")
            .Headline("Embedded or networked. Same code either way.")
            .Lede("HoloDb.Client connects your .NET app to a <a href=\"/holodb/\">HoloDb</a> server over TCP with mandatory TLS encryption and token authentication. Run SQL queries and bulk-load data using the same API as the embedded in-process <code>HoloDbService</code>, so you can swap from embedded to networked by changing only how you create the handle. Columnar results stay columnar on the wire — no row buffering or JSON serialization overhead.")
            .Fact("<b>v1.4.0</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("TLS by default")
            .Fact("opt-in resilience")
            .Install("dotnet add package EvaluatedApplications.HoloDb.Client")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb.Client", CtaStyle.Ghost, externalNewTab: true)
            .Cta("See HoloDb →", "/holodb/", CtaStyle.Ghost)
            .Related("HoloDb", "/holodb/")
            .Related("HoloDb.Protocol", "/holodb-protocol.html")
            .BarTitle("holodb-client.app"))
        .Section(SectionSpec.Prose(
            "The problem", "speed vs scalability, without two APIs",
            "Building analytics or database-driven apps means choosing between speed (embed the database in-process) and scalability (run it on its own server). Embedding locks you to one machine; networking usually means two separate APIs that behave differently. HoloDb.Client lets you have both: one code path works with either the embedded engine or a remote server."))
        .Section(SectionSpec.CardGrid(
            "Why HoloDb.Client", "what you get",
            new[]
            {
                new CardSpec("Same API, embedded or networked", "<code>Execute</code>, <code>ExecuteAsync</code>, <code>BulkLoadAsync</code>, <code>GetStatsAsync</code>, <code>PingAsync</code> — identical surface whether you talk to in-process <code>HoloDbService</code> or a remote server."),
                new CardSpec("Columnar over the wire", "Results are typed column arrays, matching the in-process engine's <code>QueryResult</code> structure — no serialization tax."),
                new CardSpec("Secure by default", "TLS is on. Trust a self-signed server with certificate pinning (thumbprint), or skip it in dev only."),
                new CardSpec("Built-in resilience", "Optional exponential backoff with full jitter, a circuit breaker, and a per-operation deadline budget — opt in where you need it, transparent where you don't."),
                new CardSpec("Designed for concurrency", "One client per concurrent worker, or pool clients upfront with <code>HoloDbClientPool</code>."),
            }))
        .Section(SectionSpec.SnippetList(
            "How to use it", "basic, resilient, pinned",
            new[]
            {
                new SnippetSpec(
                    """
                    using HoloDb.Client;

                    // Connect to server
                    var client = await HoloDbClient.ConnectAsync("db.example.com", 5433);
                    try {
                        var result = await client.ExecuteAsync("SELECT id, value FROM data LIMIT 10");
                        foreach (var row in result.Rows)
                            Console.WriteLine($"id={row[0]}, value={row[1]}");
                    } finally {
                        await client.DisposeAsync();
                    }
                    """,
                    DescAfterHtml: "Resilient client with retry, backoff, and a circuit breaker:"),
                new SnippetSpec(
                    """
                    var resilient = await ResilientHoloDbClient.ConnectAsync(
                        "db.example.com", 5433,
                        retryDelay: TimeSpan.FromMilliseconds(200),
                        maxAttempts: 3,
                        breakerFailureThreshold: 5
                    );

                    // Automatically retries transient transport failures
                    var result = await resilient.ExecuteAsync("SELECT COUNT(*) FROM data");
                    """,
                    DescAfterHtml: "Pinned certificate (recommended for self-hosted):"),
                new SnippetSpec(
                    """
                    var options = new HoloDbClientOptions {
                        AuthToken = "your-token",
                        UseTls = true,
                        PinnedThumbprint = "ABC123DEF456..." // SHA-1 thumbprint
                    };

                    var client = await HoloDbClient.ConnectAsync("localhost", 5433, options);
                    """),
            }))
        .Section(SectionSpec.CardGrid(
            "Key notes", "good to know",
            new[]
            {
                new CardSpec("One client = one session", "Requests serialize through an internal queue; concurrent callers share the same TCP connection. Use one client per worker, or pool clients with <code>HoloDbClientPool</code>."),
                new CardSpec("No automatic reconnect", "A dropped socket raises an exception; reconnect explicitly, or opt into <code>ResilientHoloDbClient</code> which handles this transparently."),
                new CardSpec("Transient vs application errors", "<code>HoloRemoteException</code> (SQL errors, auth failures) is never retried; <code>ResilientHoloDbClient</code> retries only transport-level failures."),
            },
            limHtml: "<b>Version history:</b> v1.2.0 added exponential backoff, v1.3.0 added the circuit breaker, v1.4.0 (current) adds operation deadline budgets across retries. <b>Requires:</b> .NET 8.0+, a HoloDb server v1.4.0+ (wire-protocol compatible), network connectivity with TLS support. <b>License:</b> proprietary, part of Evaluated Applications."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

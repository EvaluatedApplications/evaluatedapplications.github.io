using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 6: site/holodb/manual/index.html transcribed verbatim. The third
/// and richest real use of SectionKind.ProseArticle — confirms the typed shell (crumb/h1/lede/
/// related/toc) generalizes across all three prose-template pages now proven
/// (`articles/_example.html`, `holodb.html`, this one), each with a genuinely different one-off
/// BodyHtml. Also the first live use of RelatedLink.CssClass (the nav's own "Manual" link carries
/// `class="active"`).
/// </summary>
public static class HoloDbManualPageSpec
{
    private const string Body = """
    <h2 id="install">Install</h2>
      <p>Add the package from NuGet:</p>
      <pre><code>dotnet add package EvaluatedApplications.HoloDb</code></pre>
      <p>It targets .NET 8+ and is dependency-light. The package ships the compiled library only, and every capability is free
        to use.</p>

      <h2 id="quickstart">Quick start</h2>
      <p>Create a service, run some SQL, read the rows back. This is the whole surface for most apps:</p>
      <pre><code>using HoloDb;

    var db = new HoloDbService(new HoloDbOptions());   // in-memory

    db.Execute("CREATE TABLE users (id INT PRIMARY KEY, age INT, name TEXT)");
    db.Execute("INSERT INTO users VALUES (1, 30, 'Ada'), (2, 24, 'Linus')");

    var result = db.Execute("SELECT name FROM users WHERE age >= 18 ORDER BY age DESC");
    foreach (var row in result.Rows)
        Console.WriteLine(row["name"]);</code></pre>
      <p>Everything runs synchronously through <code>Execute</code>. There is also <code>ExecuteAsync(sql, ct)</code>, which
        offloads the work to the thread pool so a request thread isn't blocked:</p>
      <pre><code>var result = await db.ExecuteAsync("SELECT COUNT(*) FROM users");</code></pre>

      <h2 id="durability">In-memory or durable</h2>
      <p>By default the database lives in memory and is gone when the process ends. To make writes durable, give it a
        write-ahead log path:</p>
      <pre><code>var db = new HoloDbService(new HoloDbOptions { WalPath = "app.wal" });</code></pre>
      <p>Durable state is a compact <strong>snapshot of the current data</strong> plus a short log of the writes since the last
        checkpoint. Recovery is bounded by how much data you hold, not by how long the database has been running — reopening a
        long-lived database stays fast because it loads a snapshot and replays only the recent tail, never the entire history.</p>
      <p>The log is folded into a fresh snapshot automatically once it passes a threshold (<code>CheckpointThresholdBytes</code>,
        default 8&nbsp;MB), and you can force one — for example before a clean shutdown:</p>
      <pre><code>db.Checkpoint();   // fold the log into a snapshot and truncate it</code></pre>
      <blockquote>In WebAssembly (a browser tab) there is no file system, so leave <code>WalPath</code> null and use the
        in-memory engine — which is exactly what the <a href="/tools/analyst">Analyst demo</a> does.</blockquote>
      <p>If you want the raw engine directly (no async wrapper, no licensing surface), open it yourself:</p>
      <pre><code>Database engine = Database.Open("app.wal");   // or Database.Open(null) for in-memory
    engine.Execute("...");</code></pre>
      <p>The <code>HoloDbService.Database</code> property exposes the same engine underneath the service.</p>

      <h2 id="paged">Durable &amp; larger-than-memory</h2>
      <p>The default engine keeps everything in RAM (durable via the snapshot + WAL above). When a table needs to
        <strong>outgrow memory</strong>, create it as a <em>paged</em> table: it lives on an 8&nbsp;KB buffer-pool page store
        with a redo-only WAL, so cold pages spill to disk and the table can exceed RAM — while the constant-time aggregates
        stay maintained <em>on disk</em>.</p>
      <pre><code>// create a durable, larger-than-memory table backed by a directory
    db.CreatePaged("CREATE TABLE events (id INT PRIMARY KEY, kind TEXT, value INT)", dir: "data/events");

    db.PagedBulkLoad("events", rows);              // stream rows in; commits every N (default 4096)
    db.Execute("SELECT kind, COUNT(*), SUM(value) FROM events GROUP BY kind");

    // reopen it later — crash recovery is bounded, a torn tail is dropped
    db.OpenPaged("data/events");</code></pre>
      <p>The buffer-pool size (<code>CreatePaged</code>'s <code>bufferFrames</code>, default 8192 &times; 8&nbsp;KB) bounds how
        much stays resident. The one structure that scales with row count is the primary-key index, which stays in RAM
        (~16&nbsp;bytes/row) — the limiting resource for tables far larger than memory.</p>

      <h2 id="tables">Tables &amp; types</h2>
      <p>Three storage types, each with the usual SQL aliases:</p>
      <table>
        <thead><tr><th>Type</th><th>Aliases</th><th>.NET</th></tr></thead>
        <tbody>
          <tr><td><code>INT</code></td><td>INTEGER</td><td>long</td></tr>
          <tr><td><code>REAL</code></td><td>FLOAT, DOUBLE, DECIMAL, NUMERIC</td><td>double</td></tr>
          <tr><td><code>TEXT</code></td><td>VARCHAR, STRING</td><td>string</td></tr>
        </tbody>
      </table>
      <pre><code>CREATE TABLE sales (
      id     INT PRIMARY KEY,
      region TEXT,
      amount REAL
    )</code></pre>
      <p>One column may be <code>PRIMARY KEY</code>; it is enforced unique.</p>
      <blockquote><code>DECIMAL</code>/<code>NUMERIC</code> map to <code>REAL</code> (a 64-bit float), so they are <strong>not</strong>
        exact decimals. For money, store integer minor units — cents as an <code>INT</code> — which is exact and also lets the
        constant-time integer aggregates serve your revenue totals.</blockquote>

      <h2 id="inserting">Inserting data</h2>
      <p>Single or batched rows, with or without a column list:</p>
      <pre><code>db.Execute("INSERT INTO sales VALUES (1, 'EU', 42.50)");
    db.Execute("INSERT INTO sales (id, region, amount) VALUES (2, 'US', 17.00), (3, 'EU', 61.10)");</code></pre>
      <p>Strings use single quotes; escape a quote by doubling it (<code>'O''Neil'</code>). <code>UPDATE</code> and
        <code>DELETE</code> with a <code>WHERE</code> clause work as expected:</p>
      <pre><code>db.Execute("UPDATE sales SET amount = 0 WHERE region = 'US'");
    db.Execute("DELETE FROM sales WHERE amount = 0");</code></pre>

      <h2 id="bulkload">Bulk loading</h2>
      <p>For large loads, skip the SQL parser entirely and hand the engine typed column arrays. This is the fast path — one
        array per column (<code>long[]</code>, <code>double[]</code>, or <code>string[]</code>):</p>
      <pre><code>var ids     = new long[]   { 1, 2, 3 };
    var regions = new string[] { "EU", "US", "EU" };
    var amounts = new double[] { 42.50, 17.00, 61.10 };

    db.BulkLoad("sales", new[] { "id", "region", "amount" },
                new Array[] { ids, regions, amounts }, count: 3);</code></pre>
      <p>It is atomic and validates primary-key uniqueness before committing. Load in chunks (e.g. 100k rows) for very large
        datasets — that is how the Analyst ingests a big paste without stalling the browser.</p>

      <h2 id="querying">Querying (the dialect)</h2>
      <p>HoloDb speaks a substantial SQL dialect. A <code>SELECT</code> supports:</p>
      <ul>
        <li>Aggregates: <code>COUNT</code>, <code>SUM</code>, <code>AVG</code>, <code>MIN</code>, <code>MAX</code></li>
        <li>A full boolean <code>WHERE</code>: <code>AND</code> / <code>OR</code> / <code>NOT</code> / parentheses,
          the comparisons <code>= != &lt; &gt; &lt;= &gt;=</code>, plus <code>LIKE</code>, <code>IN (…)</code>,
          <code>IN (SELECT …)</code>, and <code>BETWEEN … AND …</code></li>
        <li><code>GROUP BY</code> with <code>HAVING</code></li>
        <li><code>ORDER BY</code> (multiple keys, <code>ASC</code> / <code>DESC</code>)</li>
        <li><code>LIMIT</code> and <code>DISTINCT</code></li>
        <li><code>INNER JOIN … ON …</code></li>
      </ul>
      <pre><code>SELECT region, COUNT(*), SUM(amount), AVG(amount)
    FROM sales
    WHERE amount BETWEEN 10 AND 1000
    GROUP BY region
    HAVING SUM(amount) > 50
    ORDER BY sum(amount) DESC
    LIMIT 10</code></pre>
      <p>A join across two tables:</p>
      <pre><code>SELECT o.id, c.name, o.total
    FROM orders o
    INNER JOIN customers c ON o.customer_id = c.id
    WHERE o.total > 100
    ORDER BY o.total DESC</code></pre>

      <h2 id="results">Reading results</h2>
      <p><code>Execute</code> returns a <code>QueryResult</code>. The simplest way to read it is row-by-row as dictionaries
        keyed by column name:</p>
      <pre><code>var r = db.Execute("SELECT region, COUNT(*) FROM sales GROUP BY region");
    foreach (var row in r.Rows)
        Console.WriteLine($"{row["region"]}: {row["count(*)"]}");</code></pre>
      <p>Aggregate columns are named by the function applied: <code>count(*)</code>, <code>sum(amount)</code>,
        <code>avg(amount)</code>, <code>min(amount)</code>, <code>max(amount)</code>. Use <code>r.Columns</code> to enumerate
        the result's column names in order (handy for building a table generically), and for a write statement
        <code>r.Affected</code> is the number of rows changed.</p>
      <blockquote>For hot paths, <code>QueryResult</code> also keeps results columnar and exposes direct accessors so you can
        read values without materialising a dictionary per row.</blockquote>

      <h2 id="transactions">Transactions</h2>
      <p>ACID transactions with explicit control. Wrap statements in <code>BEGIN</code> … <code>COMMIT</code>, or discard with
        <code>ROLLBACK</code>:</p>
      <pre><code>db.Execute("BEGIN");
    try
    {
        db.Execute("UPDATE accounts SET balance = balance - 100 WHERE id = 1");
        db.Execute("UPDATE accounts SET balance = balance + 100 WHERE id = 2");
        db.Execute("COMMIT");
    }
    catch
    {
        db.Execute("ROLLBACK");
        throw;
    }</code></pre>
      <p>With a <code>WalPath</code> set, a committed transaction is durable — it survives a crash. On restart the engine loads
        the last snapshot and replays only the committed writes logged since it, so recovery cost tracks live data, not history.</p>

      <h2 id="similarity">Similarity search</h2>
      <p>Because rows are stored holographically, HoloDb can do content-addressable retrieval: rank rows by similarity to a
        partial key, no separate vector index to build. Use the <code>NEAREST</code> clause with the fields you know and a
        <code>LIMIT</code> for how many neighbours you want:</p>
      <pre><code>SELECT id, title
    FROM articles
    NEAREST (title = 'holographic database')
    LIMIT 5</code></pre>
      <p>This returns the five rows whose content is closest to the query, ordered by similarity — SQL and vector-style
        retrieval from the same engine, over the same rows.</p>
      <blockquote>Similarity is exact below a few thousand rows and uses a sublinear index above that (in-memory engine).
        <code>NEAREST</code> can't yet be combined with <code>WHERE</code>, <code>GROUP BY</code>, or a join.</blockquote>

      <h2 id="indexes">Indexes &amp; why aggregates are instant</h2>
      <p>Add a sorted index on a column to speed range and lookup queries:</p>
      <pre><code>CREATE INDEX ON sales (region)</code></pre>
      <p>You rarely need indexes for counting, though: <code>COUNT(*)</code> and <code>SUM</code> over integer columns are
        served from maintained accumulators in constant time, independent of how many rows the table holds. That is the source
        of HoloDb's benchmark wins — it doesn't scan to count.</p>

      <h2 id="di">Dependency injection</h2>
      <p>Register HoloDb in a .NET host and inject <code>HoloDbService</code> wherever you need it:</p>
      <pre><code>builder.Services.AddHoloDb(options =>
    {
        options.WalPath = "app.wal";
    });

    // then, in a controller / service:
    public class ReportService(HoloDbService db)
    {
        public Task&lt;QueryResult&gt; Totals() =>
            db.ExecuteAsync("SELECT region, SUM(amount) FROM sales GROUP BY region");
    }</code></pre>

      <h2 id="evalapp">EvalApp pipelines</h2>
      <p>HoloDb runs inside an <strong>EvalApp</strong> harness, so heavy queries execute as compiled, resource-gated,
        self-tuning pipelines. Attach a service to a pipeline app and query it from any step:</p>
      <pre><code>var app = AppBuilder.Create()
        .WithHoloDb(db)
        .Build();</code></pre>

      <h2 id="browser">Running in the browser</h2>
      <p>HoloDb is pure managed .NET, so it compiles to WebAssembly and runs client-side with no server. Open the in-memory
        engine, load data, and query it — nothing leaves the device. The <a href="/tools/analyst">Analyst tool</a> on this site
        is exactly that: a real HoloDb, in your browser tab, profiling and querying whatever you paste in.</p>

      <h2 id="networked">Networked (client/server)</h2>
      <p>The same engine runs as a server. Pull the image and run it, with a data volume for durability and a token for auth:</p>
      <pre><code>docker run -p 5432:5432 -v holodb-data:/data \
      ghcr.io/evaluatedapplications/holodb:latest --token s3cret</code></pre>
      <p>Then talk to it from .NET with the client package — connect over TLS with a token and run the same SQL and bulk-load
        surface as the in-process engine, so moving from embedded to networked changes only how you get the handle:</p>
      <pre><code>dotnet add package EvaluatedApplications.HoloDb.Client</code></pre>

      <h2 id="limits">Current limits</h2>
      <p>Kept here so nothing surprises you in production:</p>
      <ul>
        <li>Exactly one <code>PRIMARY KEY</code> per table (<code>INT</code> or <code>TEXT</code>, not <code>REAL</code>).</li>
        <li>Joins are <code>INNER</code> only — no <code>OUTER</code> joins yet, and no <code>ALTER TABLE</code>.</li>
        <li>Only integer <code>SUM</code>/<code>COUNT</code> are constant-time; real-number sums use a fast SIMD scan.</li>
        <li><code>NEAREST</code> doesn't yet combine with <code>WHERE</code> / <code>GROUP BY</code> / joins.</li>
        <li>The paged engine doesn't do multi-way joins or <code>GROUP BY</code> over a join.</li>
      </ul>

      <p style="margin-top:2.5rem"><a href="/holodb/">← Back to HoloDb</a> &nbsp;·&nbsp; <a href="/tools/analyst">Try the Analyst →</a></p>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloDb manual — how to use it",
            Description: "How to use HoloDb: install, open a database, create tables, insert and bulk-load, query with the SQL dialect (aggregates, joins, WHERE, GROUP BY, ORDER BY, similarity search), transactions, durable and larger-than-memory paged storage, DI, and running it in the browser.",
            Canonical: "https://evaluatedapplications.github.io/holodb/manual/",
            OgTitle: "HoloDb manual — how to use it",
            OgDescription: "A practical guide to HoloDb: open a database, create tables, insert and bulk-load, query, transactions, similarity search, DI, and running it in the browser.",
            OgUrl: "https://evaluatedapplications.github.io/holodb/manual/",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"TechArticle","headline":"HoloDb manual — how to use it","description":"How to use HoloDb: install, open a database, create tables, insert and bulk-load, query with the SQL dialect, transactions, durable and larger-than-memory paged storage, DI, and running it in the browser.","url":"https://evaluatedapplications.github.io/holodb/manual/","author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"publisher":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"about":{"@type":"SoftwareApplication","name":"EvaluatedApplications.HoloDb","url":"https://evaluatedapplications.github.io/holodb/"}}
            """,
            OgType: "article"))
        .TailScript("<script>document.getElementById('yr').textContent = new Date().getFullYear();</script>")
        .NavItems(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("Manual", "/holodb/manual/", CssClass: "active"),
            new RelatedLink("The Analyst", "/tools/analyst"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb", ExternalNewTab: true),
        })
        .Section(SectionSpec.ProseArticle(new ProseArticleSpec(
            CrumbHtml: "<a href=\"/holodb/\">HoloDb</a> <span>/</span> Manual",
            H1: "HoloDb manual",
            LedeHtml: "An embeddable holographic database for .NET. It stores data as one associative store — queried\n    by key with SQL, by content with similarity search, and by aggregate — is transactional (ACID with a write-ahead log),\n    durable and larger-than-memory when you need it, and small enough to run in a browser tab. This page is the practical\n    guide: install it, put data in, get answers out.",
            Related: new[]
            {
                new RelatedLink("HoloDb", "/holodb/"),
                new RelatedLink("Benchmarks", "/holodb.html"),
                new RelatedLink("HoloDb.Client", "/holodb-client.html"),
            },
            RelatedAllText: "All packages →",
            RelatedAllHref: "/packages.html",
            TocItems: new[]
            {
                new RelatedLink("Install", "#install"),
                new RelatedLink("Quick start", "#quickstart"),
                new RelatedLink("In-memory or durable", "#durability"),
                new RelatedLink("Durable &amp; larger-than-memory", "#paged"),
                new RelatedLink("Tables &amp; types", "#tables"),
                new RelatedLink("Inserting data", "#inserting"),
                new RelatedLink("Bulk loading", "#bulkload"),
                new RelatedLink("Querying (the dialect)", "#querying"),
                new RelatedLink("Reading results", "#results"),
                new RelatedLink("Transactions", "#transactions"),
                new RelatedLink("Similarity search", "#similarity"),
                new RelatedLink("Indexes", "#indexes"),
                new RelatedLink("Dependency injection", "#di"),
                new RelatedLink("EvalApp pipelines", "#evalapp"),
                new RelatedLink("Running in the browser", "#browser"),
                new RelatedLink("Networked (client/server)", "#networked"),
                new RelatedLink("Current limits", "#limits"),
            },
            BodyHtml: Body)))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb", ExternalNewTab: true),
        }));
}

using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 5: site/holodb.html (the benchmark-methodology sub-page)
/// transcribed verbatim. The second real use of SectionKind.ProseArticle (after
/// `articles/_example.html`) — confirms the typed shell (crumb/h1/lede/related, no toc here)
/// generalizes to a genuinely different page with its own one-off tables/snip block as raw
/// BodyHtml. Also the first page needing PageSpec.MetaCharset ("UTF-8", uppercase — a real,
/// harmless, pre-existing inconsistency on the live page, reproduced rather than silently fixed).
/// </summary>
public static class HoloDbBenchmarksPageSpec
{
    private const string PageStyle = """
    <style>
      table.cmp{width:100%;border-collapse:collapse;font-size:.88rem;margin:8px 0 4px;display:block;overflow-x:auto}
      table.cmp th{text-align:right;color:var(--ink);border-bottom:1px solid var(--border-2);padding:8px 11px;font-family:var(--mono);font-size:.76rem;text-transform:uppercase;letter-spacing:.03em;white-space:nowrap}
      table.cmp th:first-child,table.cmp td:first-child{text-align:left}
      table.cmp td{border-bottom:1px solid var(--border);padding:8px 11px;color:var(--ink-soft);font-variant-numeric:tabular-nums;white-space:nowrap}
      table.cmp td.holo{color:var(--accent-ink);font-weight:700}
      table.cmp tr:hover td{color:var(--ink)}
      .meth{list-style:none;padding:0;margin:0;display:grid;gap:10px}
      .meth li{background:var(--surface);border:1px solid var(--border);border-radius:10px;padding:12px 16px;color:var(--ink-soft);font-size:.94rem;line-height:1.5}
      .meth b{color:var(--ink)}
    </style>
    """;

    private const string Body = """
    <h2>The workload</h2>
      <div class="snip" style="background:var(--bg-2);border:1px solid var(--border);border-radius:10px;padding:14px 16px;margin:14px 0;overflow-x:auto"><code style="font-family:var(--mono);font-size:.84rem;line-height:21px;color:var(--ink);white-space:pre">CREATE TABLE customers (
      customer_id  INT  PRIMARY KEY,   -- 200,000 customers
      name         TEXT,
      segment      TEXT,              -- smb / mid / enterprise / strategic
      tier         TEXT,              -- gold / silver / bronze
      signup_month INT
    );
    CREATE TABLE invoices (
      invoice_id   INT  PRIMARY KEY,   -- 10,000,000 invoices
      customer_id  INT,               -- FK → customers, ~50 invoices each
      month        INT,               -- yyyymm, 202301 … 202412 (24 months)
      region       TEXT,              -- US 40% / EU 30% / APAC 20% / LATAM 10%
      status       TEXT,              -- paid 78 / pending 10 / overdue 7 / refunded 3 / failed 2 (%)
      amount_cents INT                -- money as integer minor units (cents), long-tail $20–$50,000
    );</code></div>

      <h2>How it's run</h2>
      <ul class="meth">
        <li><b>Same data, same SQL.</b> The <code>customers</code> and <code>invoices</code> tables above, generated once with a fixed seed and loaded into every engine through its own idiomatic bulk path.</li>
        <li><b>Real billing queries.</b> The sixteen queries are the ones a subscription business actually runs: revenue by status / month / region, the running total and invoice count, a single-invoice lookup, overdue outstanding, big-ticket count, largest invoices, top customers, a region×status breakdown — and five cross-table <b>JOINs</b> to the customer dimension: revenue by segment, enterprise revenue, revenue by signup cohort, top named customers, and segment×status.</li>
        <li><b>Verified identical.</b> Before timing, each query's full result set is normalized and compared across all engines — if any engine disagrees, the run is flagged, not published. Every number below passed.</li>
        <li><b>Timed fairly.</b> Median of many runs of execute + consume-all-rows through each engine's own typed client API (no boxing), on a 16-core machine.</li>
        <li><b>DuckDB</b> runs in-process, in memory (its fastest mode).</li>
        <li><b>SQL Server</b> is the durable, on-disk peer: SQL&nbsp;Server 2022 (LocalDB), reading its on-disk table and paying a client/server round-trip the embedded engines don't.</li>
        <li><b>HoloDb</b> runs both embedded (in-process) and as a networked TLS server (the fair peer to SQL Server's round-trip).</li>
        <li><b>Why cents.</b> Money is stored as integer minor units — the model every ledger uses. Integer <code>SUM</code>/<code>COUNT</code> are exact and constant-time from HoloDb's maintained accumulators; the queries it loses are the ones it has to <em>scan</em> for (top-K, filtered sums, high-cardinality and multi-column groups).</li>
      </ul>

      <h2>In-process, in memory <span style="color:var(--ink-faint);font-size:.7em;font-weight:400">— HoloDb (embedded) vs DuckDB (in-process), 10M invoices ⋈ 200k customers, median ms</span></h2>
      <table class="cmp">
        <thead><tr><th>billing query</th><th>HoloDb</th><th>DuckDB</th><th>faster</th></tr></thead>
        <tbody>
          <tr><td>revenue by region</td><td class="holo">0.004</td><td>48.6</td><td>HoloDb ~12,000×</td></tr>
          <tr><td>invoices by status</td><td class="holo">0.006</td><td>59.0</td><td>HoloDb ~9,800×</td></tr>
          <tr><td>total invoiced</td><td class="holo">0.002</td><td>2.95</td><td>HoloDb ~1,500×</td></tr>
          <tr><td>invoice count</td><td class="holo">0.001</td><td>0.98</td><td>HoloDb ~980×</td></tr>
          <tr><td>revenue by month</td><td class="holo">0.011</td><td>7.11</td><td>HoloDb ~650×</td></tr>
          <tr><td>look up one invoice</td><td class="holo">0.005</td><td>0.61</td><td>HoloDb ~120×</td></tr>
          <tr><td>big-ticket invoices (&gt;$5k)</td><td>6.40</td><td class="holo">3.85</td><td>DuckDB ~1.7×</td></tr>
          <tr><td>revenue by region &amp; status</td><td>129.7</td><td class="holo">76.8</td><td>DuckDB ~1.7×</td></tr>
          <tr><td>top customers by revenue</td><td>206.9</td><td class="holo">162.0</td><td>DuckDB ~1.3×</td></tr>
          <tr><td>largest invoices</td><td>11.8</td><td class="holo">4.48</td><td>DuckDB ~2.6×</td></tr>
          <tr><td>overdue outstanding</td><td>82.7</td><td class="holo">14.1</td><td>DuckDB ~5.8×</td></tr>
          <tr><td>revenue by segment (JOIN)</td><td class="holo">56.7</td><td>65.6</td><td>HoloDb ~1.2×</td></tr>
          <tr><td>revenue by segment &amp; status (JOIN)</td><td>135</td><td class="holo">94.9</td><td>DuckDB ~1.4×</td></tr>
          <tr><td>enterprise revenue (JOIN)</td><td>34.3</td><td class="holo">10.3</td><td>DuckDB ~3.3×</td></tr>
          <tr><td>revenue by signup cohort (JOIN)</td><td>44.0</td><td class="holo">15.3</td><td>DuckDB ~2.9×</td></tr>
          <tr><td>top named customers (JOIN)</td><td>808</td><td class="holo">322</td><td>DuckDB ~2.5×</td></tr>
        </tbody>
      </table>
      <p style="color:var(--ink-faint);font-size:.86rem">The <span style="color:var(--accent-ink)">purple</span> cell is the faster engine. Bulk load, rows/sec: DuckDB 916k · HoloDb 359k. The six rollups and the lookup resolve from maintained accumulators and the PK index (no scan, hence microseconds); DuckDB's columnar engine keeps a modest lead on the single-table scans. The five cross-table JOINs now fan out in parallel across all cores: HoloDb <strong>wins revenue by segment</strong> and lands within ~1.4–3.3× of DuckDB on the rest (down from 15–92× before), all verified identical, and beats SQLite and SQL&nbsp;Server outright.</p>

      <h2>Networked service <span style="color:var(--ink-faint);font-size:.7em;font-weight:400">— HoloDb server vs SQL&nbsp;Server 2022 (LocalDB), 10M invoices ⋈ 200k customers, median ms</span></h2>
      <table class="cmp">
        <thead><tr><th>billing query</th><th>HoloDb&nbsp;server</th><th>SQL&nbsp;Server</th><th>faster</th></tr></thead>
        <tbody>
          <tr><td>invoices by status</td><td class="holo">0.124</td><td>4,032</td><td>HoloDb ~32,000×</td></tr>
          <tr><td>revenue by month</td><td class="holo">0.153</td><td>2,965</td><td>HoloDb ~19,000×</td></tr>
          <tr><td>revenue by region</td><td class="holo">0.134</td><td>2,575</td><td>HoloDb ~19,000×</td></tr>
          <tr><td>total invoiced</td><td class="holo">0.129</td><td>1,156</td><td>HoloDb ~9,000×</td></tr>
          <tr><td>invoice count</td><td class="holo">0.138</td><td>589</td><td>HoloDb ~4,300×</td></tr>
          <tr><td>largest invoices</td><td class="holo">7.42</td><td>3,001</td><td>HoloDb ~405×</td></tr>
          <tr><td>big-ticket invoices (&gt;$5k)</td><td class="holo">4.20</td><td>689</td><td>HoloDb ~164×</td></tr>
          <tr><td>revenue by region &amp; status</td><td class="holo">131.7</td><td>3,260</td><td>HoloDb ~25×</td></tr>
          <tr><td>overdue outstanding</td><td class="holo">83.2</td><td>1,004</td><td>HoloDb ~12×</td></tr>
          <tr><td>top customers by revenue</td><td class="holo">221.0</td><td>3,277</td><td>HoloDb ~15×</td></tr>
          <tr><td>enterprise revenue (JOIN)</td><td class="holo">39.7</td><td>1,792</td><td>HoloDb ~45×</td></tr>
          <tr><td>revenue by segment &amp; status (JOIN)</td><td class="holo">140</td><td>6,556</td><td>HoloDb ~47×</td></tr>
          <tr><td>revenue by segment (JOIN)</td><td class="holo">57.5</td><td>2,946</td><td>HoloDb ~51×</td></tr>
          <tr><td>revenue by signup cohort (JOIN)</td><td class="holo">67.6</td><td>2,853</td><td>HoloDb ~42×</td></tr>
          <tr><td>top named customers (JOIN)</td><td class="holo">806</td><td>4,173</td><td>HoloDb ~5.2×</td></tr>
          <tr><td>look up one invoice</td><td>0.20</td><td>0.20</td><td>tie (~0.2 ms)</td></tr>
        </tbody>
      </table>
      <p style="color:var(--ink-faint);font-size:.86rem">Both are networked services paying a client/server round-trip. HoloDb serves the working set from RAM with the same maintained accumulators; SQL&nbsp;Server 2022 (LocalDB) reads its on-disk table. Bulk load, rows/sec: HoloDb server 697k · SQL&nbsp;Server 349k. HoloDb wins <strong>15 of the 16</strong>, including <strong>all five JOINs by 5× to 51×</strong> now that they fan out in parallel across all cores (the heaviest, the name-grouped join, lands at 806&nbsp;ms versus SQL&nbsp;Server's 4,173&nbsp;ms) — with the 16th, a sub-millisecond point lookup, a tie.</p>

      <h2>What the numbers say</h2>
      <p>The <strong>maintained aggregates</strong> — the whole-ledger total and count, and the single-column <code>GROUP BY</code> rollups (revenue by status, month, region) — resolve in <strong>microseconds and are constant-time</strong>: a kept tally is read instead of scanning, so response holds as the ledger grows. The single-invoice lookup resolves from the primary-key index. In-process, HoloDb leads on the six rollups and the lookup; as a networked service against SQL&nbsp;Server it leads on <strong>15 of the 16</strong>, including all five joins by 5× to 51× (the name-grouped join finishes in 806&nbsp;ms, well under SQL&nbsp;Server's 4.2&nbsp;s).</p>
      <p>The five cross-table <strong>JOINs</strong> fan out in parallel across every core (through the same EvalApp-gated pool the scans use): HoloDb <strong>wins revenue by segment</strong> outright and lands within ~1.4–3.3× of DuckDB on the rest — down from 15–92× before — all verified identical. DuckDB's pure-columnar engine keeps a modest lead on the single-table scans (a filtered sum, a top-K, a multi-column group). HoloDb targets the <strong>mixed</strong> transactional, analytics and vector workload, durable and larger-than-memory, in a single embeddable dependency.</p>

      <p style="margin-top:2rem"><a href="/holodb/">← Back to HoloDb</a> &nbsp;·&nbsp; <a href="/tools/analyst">Run it in the browser →</a></p>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloDb benchmarks — full methodology &amp; results",
            Description: "HoloDb vs DuckDB (in-process) and HoloDb server vs SQL Server (LocalDB): the same SQL over the same 10M invoices joined to 200k customers — single-table rollups and cross-table JOINs — results verified byte-for-byte identical, two apples-to-apples comparisons. Full method and every number, wins and losses.",
            Canonical: "https://evaluatedapplications.github.io/holodb.html",
            OgTitle: "HoloDb benchmarks — full methodology &amp; results",
            OgDescription: "HoloDb vs DuckDB in-process, HoloDb server vs SQL Server networked: same SQL, same 10M-row data, byte-for-byte verified. Full method and every number.",
            OgUrl: "https://evaluatedapplications.github.io/holodb.html",
            TwitterCard: "summary_large_image",
            JsonLd: """
            {"@context":"https://schema.org","@type":"TechArticle","headline":"HoloDb benchmarks — full methodology & results","description":"HoloDb vs DuckDB in-process, HoloDb server vs SQL Server networked: same SQL, same 10M-row data, byte-for-byte verified. Full method and every number.","url":"https://evaluatedapplications.github.io/holodb.html","author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"publisher":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"about":{"@type":"SoftwareApplication","name":"EvaluatedApplications.HoloDb","url":"https://evaluatedapplications.github.io/holodb/"}}
            """,
            OgType: "article"))
        .MetaCharset("UTF-8")
        .NavBurgerAriaLabel("Menu")
        .PageStyle(PageStyle)
        .TailScript("<script>document.getElementById('yr').textContent=new Date().getFullYear();</script>")
        .NavItems(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("Docs", "/holodb/manual/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb", ExternalNewTab: true),
        })
        .Section(SectionSpec.ProseArticle(new ProseArticleSpec(
            CrumbHtml: "<a href=\"/holodb/\">HoloDb</a> <span>/</span> Benchmarks",
            H1: "Benchmarks — the full method and every number",
            LedeHtml: "One realistic workload — a billing SaaS's <strong>invoice ledger</strong> — run as <strong>two separate apples-to-apples comparisons</strong>: the in-process, in-memory engines (HoloDb embedded and DuckDB), and the networked services (HoloDb server and a durable on-disk SQL&nbsp;Server). Every result — single-table rollups and cross-table joins alike — is verified byte-for-byte identical across every engine before it is timed, including the queries where HoloDb trails.",
            Related: new[]
            {
                new RelatedLink("HoloDb", "/holodb/"),
                new RelatedLink("HoloDb.Client", "/holodb-client.html"),
                new RelatedLink("Manual", "/holodb/manual/"),
            },
            RelatedAllText: "All packages →",
            RelatedAllHref: "/packages.html",
            TocItems: null,
            BodyHtml: Body)))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("Manual", "/holodb/manual/"),
            new RelatedLink("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.HoloDb", ExternalNewTab: true),
        }));
}

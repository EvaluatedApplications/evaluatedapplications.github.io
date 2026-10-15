using System.Diagnostics;
using HoloDb;

namespace Showroom.Services;

/// <summary>One step of the content-DB spike pipeline (fetch / write / open / query), reported
/// honestly for the diagnostic page — success or the real exception, never a generic message.</summary>
public sealed record ContentDbStep(string Name, bool Success, string Detail, long ElapsedMs,
    string? ExceptionType = null, string? ExceptionMessage = null);

public sealed record ContentDbRow(string Slug, string Title, string Body);

/// <summary>The whole spike run's result: every step attempted, in order, plus the rows read back
/// if the pipeline made it all the way through. <see cref="Success"/> is true only if every step
/// succeeded.</summary>
public sealed record ContentDbDiagnostics(IReadOnlyList<ContentDbStep> Steps, IReadOnlyList<ContentDbRow>? Rows = null)
{
    public bool Success => Steps.Count > 0 && Steps.All(s => s.Success);
}

/// <summary>
/// SPIKE: proves that a HoloDb legacy-mode database, built at desktop build time (see
/// AboutUs/ContentDbBuilder) and shipped as a static asset, can be fetched, written into the WASM
/// virtual filesystem, opened, and queried entirely inside the browser -- with every step of that
/// chain reported honestly to a visible diagnostic page. This is NOT a content system: the table it
/// opens holds a handful of spike rows, nothing in the app reads from it for real content yet.
///
/// LAZY BY DESIGN, never runs at app boot. Registered as a Blazor WASM singleton
/// (`builder.Services.AddSingleton&lt;ContentDbHost&gt;()`) so every component that asks shares the
/// same loaded Database instance once it exists -- but the actual fetch/open pipeline only runs the
/// first time something calls <see cref="GetOrLoadAsync"/>, which today is only
/// Pages/ContentDbSpike.razor's own OnInitializedAsync. Opening that one page is what triggers the
/// load; nothing else in the app does. This mirrors HoloKernel/SessionHost.cs's own
/// factory-passed-at-call-time pattern deliberately, for the same reason: a singleton must never
/// itself hold a scoped HttpClient, so the caller passes one in instead of it being constructor-injected.
///
/// WHY LAZY MATTERS HERE SPECIFICALLY: this app already had one real outage from a silent boot-time
/// hang (see Showroom/CLAUDE.md, "THE ACTUAL ROOT CAUSE OF THE 2026-09-06 OUTAGE") -- a
/// fetch-and-open on the startup path is exactly that shape. If this pipeline ever throws or hangs,
/// it can only do so once a visitor has navigated to the diagnostic page; the rest of the app,
/// including every real tool, is completely unaffected either way.
/// </summary>
public sealed class ContentDbHost
{
    // The WASM virtual filesystem path this spike writes into. A dedicated top-level directory
    // (distinct from anything the Blazor framework itself uses), created explicitly before the
    // write -- Mono's WASM File.WriteAllBytes has the same "directory must already exist" contract
    // as real desktop .NET, even though the whole tree is in-memory and ephemeral (reset every page
    // load). "content.wal" is the exact file name HoloDb.Database.Open(walPath) expects; a sibling
    // "content.wal.snap" (walPath + ".snap", HoloDb's own fixed naming convention) is written too
    // IF the build produced one -- see ContentDbBuilder's own note on why it might not have.
    const string VirtualDir = "/holodb-content";
    const string WalFileName = "content.wal";

    Lazy<Task<ContentDbDiagnostics>>? _lazy;

    /// <summary>The opened database, once a successful load has completed -- null until then, or if
    /// the load failed. Available so any future component can run its own query against the SAME
    /// shared instance without re-running the whole pipeline.</summary>
    public Database? Database { get; private set; }

    /// <summary>Run the pipeline once, sharing the same in-flight/completed task with every caller
    /// (including a caller that arrives while the first load is still running). Never throws --
    /// every failure is captured as a step and returned, not raised, so a caller never needs its own
    /// try/catch around this.</summary>
    public Task<ContentDbDiagnostics> GetOrLoadAsync(HttpClient http)
    {
        _lazy ??= new Lazy<Task<ContentDbDiagnostics>>(() => RunAsync(http), LazyThreadSafetyMode.ExecutionAndPublication);
        return _lazy.Value;
    }

    async Task<ContentDbDiagnostics> RunAsync(HttpClient http)
    {
        var steps = new List<ContentDbStep>();
        var virtualWalPath = $"{VirtualDir}/{WalFileName}";

        // ---- STEP 1: fetch ----
        byte[] walBytes;
        byte[]? snapBytes;
        {
            var sw = Stopwatch.StartNew();
            try
            {
                walBytes = await http.GetByteArrayAsync($"data/{WalFileName}");
                // The .snap sidecar is OPTIONAL at this data volume (see ContentDbBuilder's own
                // finding: this HoloDb version didn't produce one for 5 rows) -- a 404 on it is not
                // a pipeline failure, only a missing-WAL fetch is.
                try { snapBytes = await http.GetByteArrayAsync($"data/{WalFileName}.snap"); }
                catch (HttpRequestException) { snapBytes = null; }
                steps.Add(new ContentDbStep("Fetch", true,
                    snapBytes is null
                        ? $"{walBytes.Length} bytes (content.wal), no .snap sidecar present"
                        : $"{walBytes.Length} + {snapBytes.Length} bytes (content.wal + content.wal.snap)",
                    sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                steps.Add(Fail("Fetch", ex, sw.ElapsedMilliseconds));
                return new ContentDbDiagnostics(steps);
            }
        }

        // ---- STEP 2: write into the WASM virtual filesystem ----
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(VirtualDir);
                await File.WriteAllBytesAsync(virtualWalPath, walBytes);
                if (snapBytes is not null) await File.WriteAllBytesAsync(virtualWalPath + ".snap", snapBytes);
                steps.Add(new ContentDbStep("WriteVfs", true, virtualWalPath, sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                steps.Add(Fail("WriteVfs", ex, sw.ElapsedMilliseconds));
                return new ContentDbDiagnostics(steps);
            }
        }

        // ---- STEP 3: open ----
        Database db;
        {
            var sw = Stopwatch.StartNew();
            try
            {
                db = HoloDb.Database.Open(virtualWalPath);
                Database = db;
                steps.Add(new ContentDbStep("Open", true, "Database.Open succeeded (legacy mode)", sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                steps.Add(Fail("Open", ex, sw.ElapsedMilliseconds));
                return new ContentDbDiagnostics(steps);
            }
        }

        // ---- STEP 4: query ----
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = db.Execute("SELECT Slug, Title, Body FROM PageContent ORDER BY Slug");
                var rows = new List<ContentDbRow>(result.RowCount);
                for (var r = 0; r < result.RowCount; r++)
                    rows.Add(new ContentDbRow(result.GetText(r, 0), result.GetText(r, 1), result.GetText(r, 2)));
                steps.Add(new ContentDbStep("Query", true, $"{rows.Count} row(s) read back", sw.ElapsedMilliseconds));
                return new ContentDbDiagnostics(steps, rows);
            }
            catch (Exception ex)
            {
                steps.Add(Fail("Query", ex, sw.ElapsedMilliseconds));
                return new ContentDbDiagnostics(steps);
            }
        }
    }

    static ContentDbStep Fail(string name, Exception ex, long elapsedMs) =>
        new(name, false, "failed", elapsedMs, ex.GetType().FullName, ex.Message);
}

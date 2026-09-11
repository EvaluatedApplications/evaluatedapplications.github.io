// Build-time tool: produces the legacy-mode HoloDb content artifact Showroom ships as a static
// asset and opens at runtime in the browser (see Showroom/Services/ContentDbHost.cs and
// Showroom/Pages/ContentDbSpike.razor). Run it any time the spike table's seed rows change:
//
//     dotnet run --project C:\Users\dongy\AboutUs\ContentDbBuilder\ContentDbBuilder.csproj
//
// It writes straight into Showroom's wwwroot/data folder, computed relative to this source file
// (not the current working directory or the build output layout, both of which vary) via
// [CallerFilePath] — robust to being invoked from anywhere.
//
// LEGACY MODE, not paged: Database.Open(walPath). REAL API SURFACE OF THE PINNED 1.4.0 PACKAGE,
// checked by reflection before writing this (not assumed from a newer HoloDb's docs): this version's
// Open() takes ONLY walPath -- no checkpointThresholdBytes parameter, no paged parameter, and there
// is no public Checkpoint() method at all. So this tool cannot force an eager fold-into-snapshot the
// way a newer HoloDb could; whatever file(s) legacy mode actually produces at this data volume (a
// WAL, and a ".snap" snapshot only if some internal automatic threshold happens to fire) is exactly
// what gets shipped and exactly what the runtime path must be able to open. That is fine for this
// spike: Showroom itself is pinned to the SAME 1.4.0, so the artifact and the reader are guaranteed
// to agree on format either way.
//
// SPIKE SCOPE: this table exists to prove the fetch -> virtual-FS write -> open -> query mechanism
// works in the real WASM runtime. It intentionally holds only a HANDFUL of rows, sourced from real,
// already-published site copy (the <meta name="description"> tags on the live product pages) so the
// content is genuine, not lorem-ipsum placeholder text — but this is NOT a content migration. No
// other page's real copy is being moved here yet.

using System.Runtime.CompilerServices;
using HoloDb;

static string RepoRoot([CallerFilePath] string here = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));

var outputDir = Path.Combine(RepoRoot(), "Showroom", "wwwroot", "data");
Directory.CreateDirectory(outputDir);

var buildDir = Path.Combine(Path.GetTempPath(), "contentdb-build-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(buildDir);
var walPath = Path.Combine(buildDir, "content.wal");

Console.WriteLine($"[ContentDbBuilder] building at {walPath}");

var db = Database.Open(walPath); // legacy mode -- this package version has no other Open overload

db.Execute("CREATE TABLE PageContent (Slug TEXT PRIMARY KEY, Title TEXT, Body TEXT)");

// Real copy, verbatim from each product page's live <meta name="description"> (site/*.html) as of
// 2026-09-11 — trimmed only where noted, never invented. A handful of rows, not a migration.
var rows = new (string Slug, string Title, string Body)[]
{
    ("phasor", "Phasor",
        "Phasor encodes numbers and symbols as phasor faces and composes them with one algebra " +
        "-- bind, unbind, bundle, correlate. Arithmetic is encoding, not calculation."),
    ("holodb", "HoloDb",
        "HoloDb stores data as holograms in one associative store, queried three ways over one " +
        "copy: exact SQL by key, similarity search by content (NEAREST), and constant-time analytics."),
    ("algformer", "AlgFormer",
        "AlgFormer defines, trains, and runs transformer-style language models in pure managed " +
        ".NET -- no GPU, no Python, no native runtime required."),
    ("tracer", "Tracer",
        "Tracer is a pathfinding and game-AI SDK for .NET: navmesh baking, multi-agent " +
        "pathfinding with goal-sharing, dynamic obstacles, line-of-sight, fog-of-war, influence maps, combat."),
    ("evalapp", "EvalApp",
        "EvalApp is a resource-gated, self-tuning async pipeline runtime for .NET: describe a " +
        "process as data and steps, and stop hand-writing concurrency coordination."),
};

foreach (var (slug, title, body) in rows)
{
    var sql = $"INSERT INTO PageContent (Slug, Title, Body) VALUES " +
              $"('{Esc(slug)}', '{Esc(title)}', '{Esc(body)}')";
    db.Execute(sql);
}

// Round-trip verify BEFORE shipping: reopen a fresh Database instance against the same walPath and
// re-run the query, so a format problem is caught here, at build time, not first discovered by the
// browser.
var verify = Database.Open(walPath);
var result = verify.Execute("SELECT Slug, Title, Body FROM PageContent ORDER BY Slug");
Console.WriteLine($"[ContentDbBuilder] round-trip verify: {result.RowCount} row(s)");
for (var r = 0; r < result.RowCount; r++)
    Console.WriteLine($"    {result.GetText(r, 0),-10} {result.GetText(r, 1)}");
if (result.RowCount != rows.Length)
    throw new InvalidOperationException($"expected {rows.Length} rows, read back {result.RowCount}");

Console.WriteLine("[ContentDbBuilder] files present after build:");
foreach (var f in Directory.GetFiles(buildDir))
    Console.WriteLine($"    {f} ({new FileInfo(f).Length} bytes)");

var walBytes = File.ReadAllBytes(walPath);
File.WriteAllBytes(Path.Combine(outputDir, "content.wal"), walBytes);
Console.WriteLine($"[ContentDbBuilder] wrote {outputDir}\\content.wal ({walBytes.Length} bytes)");

var snapPath = walPath + ".snap";
if (File.Exists(snapPath))
{
    var snapBytes = File.ReadAllBytes(snapPath);
    File.WriteAllBytes(Path.Combine(outputDir, "content.wal.snap"), snapBytes);
    Console.WriteLine($"[ContentDbBuilder] wrote {outputDir}\\content.wal.snap ({snapBytes.Length} bytes)");
}
else
{
    // No snapshot exists at this data volume with this HoloDb version's default checkpoint
    // behaviour -- the WAL alone is a complete, replayable legacy database, so this is not an
    // error. Clean up any stale snapshot from a PRIOR run so a leftover file never gets shipped
    // for a table shape that no longer matches the current WAL.
    var stale = Path.Combine(outputDir, "content.wal.snap");
    if (File.Exists(stale)) File.Delete(stale);
    Console.WriteLine("[ContentDbBuilder] no .snap file produced (WAL-only) -- did not write content.wal.snap");
}

Directory.Delete(buildDir, recursive: true);

static string Esc(string s) => s.Replace("'", "''");

using SiteKit.Render;
using SiteKit.Render.PoC;
using SiteKit.Spec;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(AppContext.BaseDirectory, "out");
var handAuthoredPath = Path.Combine(repoRoot, "site", "phasor.html");

Site.Define("aboutus-poc", PhasorPageSpec.Brand(), PhasorPageSpec.Nav(), outputRoot)
    .Page("phasor", "Phasor", "foundation", "var(--c-foundation)", PhasorPageSpec.Configure)
    .Build(out SiteSpec siteSpec);

var pipeline = SiteKitPipeline.Build();
var result = await pipeline.RunAsync(new MultiSiteBuildJob(new[] { siteSpec }));

if (!result.IsSuccess)
{
    Console.WriteLine($"PIPELINE FAILED: {result}");
    return 1;
}

var written = result.GetData().WrittenFiles ?? Array.Empty<string>();
Console.WriteLine($"Pipeline succeeded. Wrote {written.Count} file(s):");
foreach (var f in written) Console.WriteLine("  " + f);

var generatedPath = Path.Combine(outputRoot, "phasor.html");
if (!File.Exists(handAuthoredPath))
{
    Console.WriteLine($"\nCould not find hand-authored original at {handAuthoredPath} — cannot diff.");
    return 1;
}

var generated = await File.ReadAllTextAsync(generatedPath);
var original = await File.ReadAllTextAsync(handAuthoredPath);

Console.WriteLine("\n=== Structural diff (whitespace-normalized: trim each line, drop blank lines) ===\n");
var report = StructuralDiff.Compare(original, generated);
Console.WriteLine(report);

return 0;

static string FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "site")))
        dir = dir.Parent;
    if (dir is null)
        throw new InvalidOperationException($"Could not locate AboutUs repo root (a 'site' folder) above {startDir}.");
    return dir.FullName;
}

using SiteKit.Render;
using SiteKit.Render.PoC;
using SiteKit.Spec;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(AppContext.BaseDirectory, "out");

// Phase 1 proved phasor.html alone. Phase 2 adds prose.html to the SAME site build — deliberately
// chosen for its structural DIFFERENCES from Phasor (per-card CatOverride+CatRootOverride chord,
// Category != CategoryDotVar, no prism-beam, no ClosingStack section) — see ProsePageSpec.cs's own
// header comment for the full list. Both pages run through ONE compiled pipeline tree, proving the
// nested ForEach<SiteRenderJob>/ForEach<PageRenderJob> shape actually fans out over >1 page, not
// just executes a single hardcoded path.
// Phase 2's second batch (2026-09-02): evalapp/holovoxel/holodb-client/holodb-protocol/
// evalapp-neural/algformer-gpu, chosen (per the coordinator's own dispatch) to exercise the
// composer's remaining gaps in one pass rather than one new feature per page: HeroSpec.LimHtml
// (evalapp, holodb-protocol), SectionSpec.StackFlow + SectionSpec.Raw (evalapp only — the
// richest page in this batch), PageSpec.PageStyleHtml + a second live .PrismBeam() page
// (holovoxel), SnippetSpec.DescBeforeHtml (holodb-protocol), and three genuine "no new feature
// needed" controls (holodb-client, evalapp-neural, algformer-gpu) proving the existing surface
// already generalizes. See each PageSpec's own header comment for its specific reasoning.
Site.Define("aboutus-poc", PhasorPageSpec.Brand(), PhasorPageSpec.Nav(), outputRoot)
    .Page("phasor", "Phasor", "foundation", "var(--c-foundation)", PhasorPageSpec.Configure)
    .Page("prose", "Prose", "holodb-algformer", "var(--c-algformer)", ProsePageSpec.Configure)
    .Page("tracer", "Tracer", "tracer", "var(--c-tracer)", TracerPageSpec.Configure)
    .Page("evalapp", "EvalApp", "foundation", "var(--c-foundation)", EvalAppPageSpec.Configure)
    .Page("holovoxel", "HoloVoxel", "holovoxel", "var(--c-holovoxel)", HoloVoxelPageSpec.Configure)
    .Page("holodb-client", "HoloDb.Client", "holodb-client", "var(--c-holodb-client)", HoloDbClientPageSpec.Configure)
    .Page("holodb-protocol", "HoloDb.Protocol", "holodb-protocol", "var(--c-holodb-protocol)", HoloDbProtocolPageSpec.Configure)
    .Page("evalapp-neural", "EvalApp.Neural", "evalapp-neural", "var(--c-evalapp-neural)", EvalAppNeuralPageSpec.Configure)
    .Page("algformer-gpu", "AlgFormer.Gpu", "algformer-gpu", "var(--c-algformer-gpu)", AlgFormerGpuPageSpec.Configure)
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

var pagesToVerify = new[] { "phasor", "prose", "tracer", "evalapp", "holovoxel", "holodb-client", "holodb-protocol", "evalapp-neural", "algformer-gpu" };
var overallOk = true;

foreach (var slug in pagesToVerify)
{
    var handAuthoredPath = Path.Combine(repoRoot, "site", slug + ".html");
    var generatedPath = Path.Combine(outputRoot, slug + ".html");

    Console.WriteLine($"\n=== {slug}.html ===");
    if (!File.Exists(handAuthoredPath))
    {
        Console.WriteLine($"Could not find hand-authored original at {handAuthoredPath} — cannot diff.");
        overallOk = false;
        continue;
    }
    if (!File.Exists(generatedPath))
    {
        Console.WriteLine($"Pipeline did not write {generatedPath} — cannot diff.");
        overallOk = false;
        continue;
    }

    var generated = await File.ReadAllTextAsync(generatedPath);
    var original = await File.ReadAllTextAsync(handAuthoredPath);

    Console.WriteLine("--- Structural diff (tag-boundary tokenized, whitespace-normalized) ---");
    var report = StructuralDiff.Compare(original, generated);
    Console.WriteLine(report);
    if (!report.StartsWith("IDENTICAL")) overallOk = false;

    var strippedOriginal = System.Text.RegularExpressions.Regex.Replace(original, @"\s+", "");
    var strippedGenerated = System.Text.RegularExpressions.Regex.Replace(generated, @"\s+", "");
    var byteEqual = strippedOriginal == strippedGenerated;
    Console.WriteLine($"--- All-whitespace-stripped byte compare: orig={strippedOriginal.Length} chars, gen={strippedGenerated.Length} chars, equal={byteEqual} ---");
    if (!byteEqual) overallOk = false;
}

Console.WriteLine(overallOk ? "\nALL PAGES VERIFIED IDENTICAL." : "\nAT LEAST ONE PAGE DID NOT VERIFY — see above.");
return overallOk ? 0 : 1;

static string FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "site")))
        dir = dir.Parent;
    if (dir is null)
        throw new InvalidOperationException($"Could not locate AboutUs repo root (a 'site' folder) above {startDir}.");
    return dir.FullName;
}

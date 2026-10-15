// Checks for the Helix tool (correctness only; the timings printed are indicative, taken natively on whatever else the machine is doing):
//  1. the package version the page shows equals the one Showroom.csproj and this project reference;
//  2. the parser: FASTA, plain lines, bad bases, partial measurement columns, caps; the demo set round-trips through its text form;
//  3. the helix numbers against an independent sum over the Olson table; the page's shown regions exist and are the file's own text;
//  4. every stage the page runs, on the demo set, plus the instrument's own nulls (permuted measurements must not be "found");
//  5. the console program the page renders compiles and runs against the published package and prints the same numbers.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HelixDemo;
using HoloGenome;

int rows = args.Length > 0 ? int.Parse(args[0]) : 600;
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
int failures = 0;
void Check(string name, bool ok, string detail = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {detail}"); if (!ok) failures++; }

// 1. version single source
string csproj = File.ReadAllText(Path.Combine(root, "Showroom.csproj"));
var m = Regex.Match(csproj, @"EvaluatedApplications\.HoloGenome""\s+Version=""([^""]+)""");
Check("page version equals Showroom.csproj", m.Success && m.Groups[1].Value == HelixCode.PackageVersion, $"({HelixCode.PackageVersion} vs {m.Groups[1].Value})");
string proj = File.ReadAllText(Path.Combine(root, "tools", "HelixProof", "HelixProof.csproj"));
Check("page version equals HelixProof.csproj", proj.Contains($"EvaluatedApplications.HoloGenome\" Version=\"{HelixCode.PackageVersion}\""));

// 2. parser
var fa = HelixAnalysis.Parse(">a 1.5\nACGT\nACGT\n>b\tx 2.5\nacgtn\n>c\nTTTT\n");
Check("FASTA: two good records, N record skipped", fa.Sequences.Count == 2 && fa.Sequences[0] == "ACGTACGT" && fa.Sequences[1] == "TTTT", string.Join("|", fa.Sequences));
Check("FASTA: header measurement read, partial column ignored with a note", fa.Measurements is null && fa.Notes.Any(n => n.Contains("ignored")) && fa.Notes.Any(n => n.Contains("not A, C, G or T")), string.Join("; ", fa.Notes));
var fb = HelixAnalysis.Parse(">a 1.5\nACGT\n>b 2.5\nTTGA\n");
Check("FASTA: full column read", fb.Measurements is { Count: 2 } && fb.Measurements[1] == 2.5);
var ln = HelixAnalysis.Parse("ACGT,0.5\nggcc\t-1e-3\n\nTT 7\n");
Check("lines: comma, tab and space separators, lower case", ln.Sequences.SequenceEqual(["ACGT", "GGCC", "TT"]) && ln.Measurements is { Count: 3 } && ln.Measurements[1] == -0.001);
var lo = HelixAnalysis.Parse("ACGT\nTTAA\n");
Check("lines: no column gives null measurements", lo.Measurements is null && lo.Sequences.Count == 2);
var big = HelixAnalysis.Parse(string.Join("\n", Enumerable.Repeat("ACGTACGT,1", HelixAnalysis.MaxRows + 7)));
Check($"cap: first {HelixAnalysis.MaxRows} rows kept, the rest counted", big.Sequences.Count == HelixAnalysis.MaxRows && big.Notes.Any(n => n.StartsWith("7 records")));
var demo = HelixDemoData.Make(rows);
var back = HelixAnalysis.Parse(HelixDemoData.ToText(demo));
Check("demo round-trips through its text form exactly", back.Sequences.SequenceEqual(demo.Sequences) && back.Measurements!.SequenceEqual(demo.Measurements!));
Check("demo is a prefix of a larger demo", HelixDemoData.Make(rows * 2).Sequences.Take(rows).SequenceEqual(demo.Sequences));

// 3. helix numbers against an independent computation; the shown regions
string seq = "ATGCGTACGTTAGCAAAAATTTTTGCGCGATCGATCGTAGCTAGCTAGCA";
var tr = HelixAnalysis.Track(seq);
double deg = 0; for (int i = 1; i < seq.Length; i++) deg += OlsonTwist.Instance.TwistDegrees(seq[i - 1], seq[i]);
Check("track turns equal the sum of Olson twists / 360", Math.Abs(tr.Turns - deg / 360) < 1e-12, $"({tr.Turns:F4} turns, {tr.HologramReals} reals)");
Check("constant-twist track turns equal 34.3 x steps / 360", Math.Abs(tr.FlatTurns - 34.3 * (seq.Length - 1) / 360) < 1e-12);
Check("Olson period prints as 10.5340", HelicalScore.OlsonPeriodBasesPerTurn.ToString("F4", CultureInfo.InvariantCulture) == "10.5340");
foreach (var r in new[] { "parse", "track", "blocks", "helical", "order-null", "fit", "plan" })
    Check($"region '{r}' extracted and non-empty", HelixCode.Region(r).Split('\n').Length > 2 && !HelixCode.Region(r).Contains("// >>>") && !HelixCode.Region(r).Contains("// <<<"));
Check("full file view has no markers", !HelixCode.Source("HelixAnalysis.cs").Contains("// >>>") && HelixCode.Source("HelixAnalysis.cs").Contains("class HelixAnalysis"));

// 4. every stage on the demo set, with timings, then the nulls
var opt = new RunOptions(BlockMode.Rows, 1, FitRows: 1000, FitIterations: 5000, FitControl: true);
var results = new List<StageResult>();
var total = Stopwatch.StartNew();
foreach (var st in HelixAnalysis.Plan(demo, opt))
{
    var r = st.Run(); results.Add(r);
    Console.WriteLine($"  {r.Ms,8:F0} ms  {r.Label,-32} " + (r.Grade is { } g ? $"r = {g.Pearson:F3} [{g.Low:F3}, {g.High:F3}] N={g.N} blocks={g.Blocks}" : r.Model is { } mo ? $"period {mo.PeriodBasesPerTurn:F4}  loss {mo.LossLog[0]:F5} -> {mo.LossLog[^1]:F5}  ({r.Note})" : ""));
}
Console.WriteLine($"  all stages: {total.Elapsed.TotalSeconds:F1} s (native, single thread)");
StageResult S(string id) => results.First(x => x.Id == id);
Check("seven stages ran", results.Count == 7, $"({results.Count})");
Check("planted: helical interval excludes 0", S("helical").Grade!.Low > 0);
Check("planted: fitted period within 0.2 of the planted 10.4", Math.Abs(S("fit").Model!.PeriodBasesPerTurn - HelixDemoData.PlantedPeriod) < 0.2, $"({S("fit").Model!.PeriodBasesPerTurn:F4})");
Check("planted: held-out interval excludes 0", S("heldout").Grade!.Low > 0);
Check("shuffled-fit period is not the planted one", Math.Abs(S("fit-shuffled").Model!.PeriodBasesPerTurn - HelixDemoData.PlantedPeriod) > 0.2, $"({S("fit-shuffled").Model!.PeriodBasesPerTurn:F4})");
Check("order null: interval contains 0 or is below the headline", S("order-null").Grade!.Low <= 0 || S("order-null").Grade!.High < S("helical").Grade!.Low);
// the instrument must be able to say no: permute the measurement column 100 times, nothing should be found beyond the interval's own error rate
int excludes = 0; var meanR = 0.0;
for (int k = 0; k < 100; k++)
{
    var rng = new Random(1000 + k); var perm = demo.Measurements!.OrderBy(_ => rng.Next()).ToList();
    var gp = HelixAnalysis.Helical("h", "h", demo.Sequences, perm, null, ResampleBlocks.Rows(demo.Sequences.Count)).Grade!;
    meanR += gp.Pearson / 100; if (gp.Low > 0 || gp.High < 0) excludes++;
}
Check("100 permuted measurements: mean r near 0 and the 95% interval excludes 0 in at most 12", Math.Abs(meanR) < 0.01 && excludes <= 12, $"(mean r {meanR:F4}, excluded 0 in {excludes}/100)");// block modes run
foreach (var mode in new[] { BlockMode.Segments, BlockMode.Tiled })
{
    var g = HelixAnalysis.Plan(demo, opt with { Mode = mode, BlockSize = mode == BlockMode.Tiled ? 7 : 10 }).First().Run().Grade!;
    Check($"block mode {mode} runs", g.Blocks > 0 && g.Blocks <= demo.Sequences.Count, $"({g.Blocks} blocks)");
}

// 5. the rendered console program
string dir = Path.Combine(Path.GetTempPath(), "helix-proof-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dir);
string program = HelixCode.MinimalProgram(opt);
File.WriteAllText(Path.Combine(dir, "Program.cs"), program);
File.WriteAllText(Path.Combine(dir, "HelixAnalysis.cs"), HelixCode.Source("HelixAnalysis.cs"));
File.WriteAllText(Path.Combine(dir, "demo.txt"), HelixDemoData.ToText(demo));
File.WriteAllText(Path.Combine(dir, "Minimal.csproj"),
    $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>" +
    $"<ItemGroup><PackageReference Include=\"{HelixCode.Package}\" Version=\"{HelixCode.PackageVersion}\" /></ItemGroup></Project>");
var psi = new ProcessStartInfo("dotnet", "run -c Release -- demo.txt") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
using (var p = Process.Start(psi)!)
{
    string output = p.StandardOutput.ReadToEnd(), err = p.StandardError.ReadToEnd();
    p.WaitForExit();
    Console.WriteLine(output);
    if (err.Length > 0) Console.WriteLine(err);
    Check("rendered console program builds and runs", p.ExitCode == 0 && output.Contains("sequences"));
    var rs = Regex.Matches(output, @"r = (-?[\d.]+) \[(-?[\d.]+), (-?[\d.]+)\]").Select(x => string.Join(",", x.Groups.Cast<Group>().Skip(1).Select(g => g.Value))).ToList();
    var inproc = results.Where(x => x.Grade is not null).Select(x => $"{x.Grade!.Pearson:F3},{x.Grade.Low:F3},{x.Grade.High:F3}").ToList();
    Check("console grades equal the in-process grades (3 dp)", rs.SequenceEqual(inproc), $"(console {rs.Count} lines, in-process {inproc.Count})");
    var ps = Regex.Matches(output, @"period (\d+\.\d+) bases").Select(x => x.Groups[1].Value).ToList();
    var pin = results.Where(x => x.Model is not null).Select(x => x.Model!.PeriodBasesPerTurn.ToString("F4", CultureInfo.InvariantCulture)).ToList();
    Check("console fitted periods equal the in-process periods (4 dp)", ps.SequenceEqual(pin), $"(console {string.Join("/", ps)} in-process {string.Join("/", pin)})");
    Console.WriteLine($"program length: {program.Split('\n').Length} lines; kept at {dir}");
}
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

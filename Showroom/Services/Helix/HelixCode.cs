// Text for the "How this is built" panel. The code shown IS the embedded source of HelixAnalysis.cs / HelixDemoData.cs (the files the page
// runs), cut at the >>> / <<< markers; the console program is rendered from the same constants. tools/HelixProof compiles that program
// against the published package, runs it, and compares its numbers with an in-process run of the same stages.
using System.Reflection;
using System.Text;

namespace HelixDemo;

public static class HelixCode
{
    public const string Package = "EvaluatedApplications.HoloGenome";
    public const string PackageVersion = "0.2.0";     // keep equal to Showroom.csproj; HelixProof fails if it differs

    static readonly Dictionary<string, string> Cache = new();

    /// <summary>The embedded source file with the region markers removed.</summary>
    public static string Source(string name)
    {
        if (Cache.TryGetValue(name, out var c)) return c;
        using var s = typeof(HelixAnalysis).Assembly.GetManifestResourceStream(name);
        if (s is null) return $"// {name} is not embedded in this build.";
        using var rd = new StreamReader(s);
        var lines = rd.ReadToEnd().Replace("\r\n", "\n").Split('\n').Where(l => !IsMarker(l));
        return Cache[name] = string.Join("\n", lines);
    }

    static bool IsMarker(string l) { var t = l.Trim(); return t.StartsWith("// >>> ") || t.StartsWith("// <<< "); }

    /// <summary>The lines of one region of HelixAnalysis.cs, as written, with the common indent removed.</summary>
    public static string Region(string region)
    {
        string key = "region:" + region;
        if (Cache.TryGetValue(key, out var c)) return c;
        using var s = typeof(HelixAnalysis).Assembly.GetManifestResourceStream("HelixAnalysis.cs");
        if (s is null) return "// HelixAnalysis.cs is not embedded in this build.";
        using var rd = new StreamReader(s);
        var all = rd.ReadToEnd().Replace("\r\n", "\n").Split('\n');
        var body = new List<string>(); bool on = false;
        foreach (var l in all)
        {
            var t = l.Trim();
            if (t == "// >>> " + region) { on = true; continue; }
            if (t == "// <<< " + region) break;
            if (on) body.Add(l);
        }
        int indent = body.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return Cache[key] = string.Join("\n", body.Select(l => l.Length >= indent ? l[indent..] : l)).TrimEnd();
    }

    static string Lit(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "_");

    /// <summary>A complete Program.cs for a console app: it reads a file in the page's input format and runs every stage the page runs.</summary>
    public static string MinimalProgram(RunOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// dotnet new console; dotnet add package {Package} --version {PackageVersion}");
        sb.AppendLine("// Put HelixAnalysis.cs (the next tab) beside this file. Usage: dotnet run -c Release -- sequences.txt");
        sb.AppendLine("// The file is FASTA, or one \"SEQUENCE,measurement\" line per row (the page's Download button writes one).");
        sb.AppendLine("using HelixDemo;");
        sb.AppendLine("using HoloGenome;");
        sb.AppendLine();
        sb.AppendLine("Dataset data = HelixAnalysis.Parse(File.ReadAllText(args[0]));");
        sb.AppendLine("Console.WriteLine($\"{data.Sequences.Count} sequences, measurement column: {data.Measurements is not null}\");");
        sb.AppendLine("foreach (string note in data.Notes) Console.WriteLine(\"  note: \" + note);");
        sb.AppendLine();
        sb.AppendLine("TrackResult track = HelixAnalysis.Track(data.Sequences[0]);");
        sb.AppendLine("Console.WriteLine($\"first sequence: {track.Turns:F2} helix turns, {track.BasesPerTurn:F4} bases per turn; \" +");
        sb.AppendLine("                  $\"Olson period {HelicalScore.OlsonPeriodBasesPerTurn:F4}\");");
        sb.AppendLine();
        sb.AppendLine($"var options = new RunOptions(BlockMode.{o.Mode}, {o.BlockSize}, FitRows: {Lit(o.FitRows)}, FitIterations: {Lit(o.FitIterations)}, FitControl: {(o.FitControl ? "true" : "false")});");
        sb.AppendLine("foreach (Stage stage in HelixAnalysis.Plan(data, options))");
        sb.AppendLine("{");
        sb.AppendLine("    StageResult r = stage.Run();");
        sb.AppendLine("    string what = r.Grade is { } g ? $\"r = {g.Pearson:F3} [{g.Low:F3}, {g.High:F3}] over {g.N} rows, {g.Blocks} blocks\"");
        sb.AppendLine("                : r.Model is { } m ? $\"period {m.PeriodBasesPerTurn:F4} bases per turn (started at {HelixAnalysis.FitStartPeriod}); {r.Note}\" : \"\";");
        sb.AppendLine("    Console.WriteLine($\"{r.Ms,9:F0} ms  {r.Label,-32} {what}\");");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

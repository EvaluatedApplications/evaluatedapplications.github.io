using System.IO.Compression;
using System.Text.Json;
using Showroom.Services;
using static Showroom.Services.CouncilTerms;
// Prints what the page would say for every council in <council-web dir>; see CouncilTermsCheck.csproj.
var dataRoot = args[0];
// register every council's facts exactly as the page does when it reads profiles.json
using (var gz = new GZipStream(File.OpenRead(Path.Combine(dataRoot, "profiles.json.gz")), CompressionMode.Decompress))
using (var doc = JsonDocument.Parse(gz))
    foreach (var e in doc.RootElement.EnumerateArray())
        if (e.TryGetProperty("facts", out var f)) CouncilFacts.Set(e.GetProperty("slug").GetString()!, CouncilFacts.FromJson(f));
var only = args.Length > 2 ? args[2].Split(',').ToHashSet() : null;
var slugs = File.ReadAllLines(Path.Combine(dataRoot, "index.csv")).Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[0]).ToList();
var sw = new StringWriter();
foreach (var slug in slugs.OrderBy(s => s, StringComparer.Ordinal))
{
    if (only is not null && !only.Contains(slug)) continue;
    sw.WriteLine("## " + slug);
    var list = CannotCheck(slug);
    sw.WriteLine("LIST " + list.Count);
    foreach (var l in list) sw.WriteLine("  - " + l);
    foreach (var id in new[] { "twins", "withintxn", "other" }) sw.WriteLine("CCR " + id + ": " + (CheckCannotRun(slug, id) ?? "(null)"));
    foreach (var y in File.ReadAllLines(Path.Combine(dataRoot, slug, "years.csv")).Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[0]))
    {
        sw.WriteLine("YEAR " + y + " NOTE: " + (YearNote(slug, y) ?? "(null)"));
        foreach (var sch in new[] { "A", "D" })
        {
            bool na = IsNa(slug, sch);
            var t = new System.Text.StringBuilder();
            if (NumberingNote(slug, sch, y) is { } nn) t.Append("[NUMNOTE] " + nn + " ");
            if (na && NotAvailable(slug, sch, y) is { } naText) t.Append("[NA] " + naText);
            sw.WriteLine("YEAR " + y + " " + sch + " na=" + na + " " + t.ToString().Trim());
        }
    }
}
File.WriteAllText(args[1], sw.ToString().Replace("\r\n", "\n"));
Console.WriteLine("wrote " + args[1]);
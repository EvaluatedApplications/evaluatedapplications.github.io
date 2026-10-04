using System.IO.Compression;
using System.Text;
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
    foreach (var id in new[] { "twins", "withintxn" }) if (CheckNote(slug, id) is { } note) sw.WriteLine("NOTE " + id + ": " + note);
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
if (only is null) sw.WriteLine("COVERAGE twins: " + CheckCoverage("twins", slugs.Select(s => (s, s))));
File.WriteAllText(args[1], sw.ToString().Replace("\r\n", "\n"));
Console.WriteLine("wrote " + args[1]);

// ---- every machine code in the SHIPPED data must have plain words (CouncilCodes); a missing one fails the check (exit 1) ----
var seen = new HashSet<(string Vocab, string Value)>();
void Cross(string file, string column, string vocab)
{
    using var gz = new GZipStream(File.OpenRead(Path.Combine(dataRoot, "cross", file + ".csv.gz")), CompressionMode.Decompress);
    using var rd = new StreamReader(gz, new UTF8Encoding(false));
    var all = new List<List<string>>(); var cur = new List<string>(); var sb = new StringBuilder(); bool q = false;
    int ch;
    while ((ch = rd.Read()) >= 0)
    {
        char c = (char)ch;
        if (q) { if (c == '"') { if (rd.Peek() == '"') { sb.Append('"'); rd.Read(); } else q = false; } else sb.Append(c); }
        else if (c == '"') q = true;
        else if (c == ',') { cur.Add(sb.ToString()); sb.Clear(); }
        else if (c == '\n') { cur.Add(sb.ToString()); sb.Clear(); all.Add(cur); cur = new(); }
        else if (c != '\r') sb.Append(c);
    }
    int col = all[0].IndexOf(column);
    if (col < 0) throw new InvalidDataException($"{file} has no {column} column");
    foreach (var r in all.Skip(1)) if (r.Count > col) seen.Add((vocab, r[col]));
}
Cross("debt_sink", "Flags", CouncilCodes.VocabDebtFlag);
Cross("payment_misfits", "LabelCheck", CouncilCodes.VocabLabelCheck);
Cross("debt_ledger", "DecodeStatus", CouncilCodes.VocabDecodeStatus);
// exception bundles: Schedule,GroupId,TransactionId,SupplierName,Net,Difference,Detail,Classification(7),TransactionGross,ExplainedBy(9),...  (quoted fields may hold commas)
foreach (var dir in Directory.GetDirectories(dataRoot).Where(d => Path.GetFileName(d) != "cross"))
    foreach (var file in Directory.GetFiles(dir, "fy-*.exceptions*.csv.gz"))
    {
        using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var rd = new StreamReader(gz, new UTF8Encoding(false));
        for (string? line = rd.ReadLine(); line is not null; line = rd.ReadLine())
        {
            if (line.Length == 0 || line[0] == '@' || line.StartsWith("Schedule,", StringComparison.Ordinal)) continue;
            var cells = new List<string>(13); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); if (cells.Count == 10) break; }
                else sb.Append(c);
            }
            if (cells.Count >= 10) { seen.Add((CouncilCodes.VocabClassification, cells[7])); seen.Add((CouncilCodes.VocabExplainedBy, cells[9])); }
        }
    }
var unmapped = CouncilCodes.Audit(seen);
foreach (var u in unmapped) Console.WriteLine("UNMAPPED CODE: " + u);
Console.WriteLine(unmapped.Count == 0 ? $"CODES: all {seen.Count} distinct code values in the shipped data have plain words." : $"FAILED: {unmapped.Count} code value(s) have no plain words in CouncilCodes.cs.");
return unmapped.Count == 0 ? 0 : 1;

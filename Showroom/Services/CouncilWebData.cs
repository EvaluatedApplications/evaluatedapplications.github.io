using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Showroom.Services;

/// <summary>One line of web_export/index.csv: what one council's phone-sized export holds.</summary>
public sealed record CouncilIndexRow(string Slug, int Months, int TxRows, int ExceptionRows, long TotalGzipBytes);

/// <summary>One line of a council's months.csv. A month is the month of each ROW'S OWN pay date, not the file tag.</summary>
public sealed record MonthRow(string Month, int TxRows, decimal Net, int ExceptionParts, int ExceptionRows, long ExceptionGzipBytes, int Parts = 1);

/// <summary>One line of a council's years.csv: one FINANCIAL year (April to March, "2023-24"; or "undated"). Its flagged lines are one bundle
/// (<see cref="Parts"/> files, split only when a year is heavy); <see cref="ExceptionGzipBytes"/> is what the visitor downloads for it.</summary>
public sealed record YearRow(string Year, int Months, int Parts, int TxRows, decimal Net, int ExceptionRows, long ExceptionBytes, long ExceptionGzipBytes);

/// <summary>A council's own profile text, written by the virtual-customer and carried unchanged (see CouncilWebBuilder).</summary>
public sealed record CouncilProfile(string Slug, string Name, string Page, string HowTo, IReadOnlyList<string> Quirks,
    string LastChecked, string Verification, string? Foi, int HeldBack);

/// <summary>
/// Reads the phone-sized council data under website-data/council-web (built by CouncilWebBuilder from the virtual-customer's
/// web_export). Rules: never fetch a whole-council file; a month is fetched only when the visitor asks for it; every file is
/// small enough to read in one go (the largest exception slice is 0.1 MB compressed) and is parsed in time slices.
/// Each load logs one "CW-PERF" line to the browser console with what it cost, so the timings in the report can be re-measured.
/// </summary>
public sealed class CouncilWebData
{
    static string Root => DataUrl.For("council-web/");   // website-data repo, see DataUrl
    readonly HttpClient _http;
    readonly IJSRuntime _js;
    readonly Dictionary<string, List<string[]>> _tables = new();
    readonly Interner _interner = new();
    List<CouncilIndexRow>? _index;
    List<CouncilProfile>? _profiles;
    readonly Dictionary<string, List<MonthRow>> _months = new();
    Dictionary<string, string>? _sources;

    public CouncilWebData(HttpClient http, IJSRuntime js) { _http = http; _js = js; }

    /// <summary>The twenty-one councils, in the order the page lists them: the four hosted first, then the cities, then the nine added in Session 38.</summary>
    public static readonly (string Slug, string Name, string Short)[] Councils =
    {
        ("wokingham", "Wokingham Borough Council", "Wokingham"),
        ("reading", "Reading Borough Council", "Reading"),
        ("westberkshire", "West Berkshire Council", "West Berkshire"),
        ("rbwm", "Royal Borough of Windsor and Maidenhead", "RBWM"),
        ("bracknellforest", "Bracknell Forest Council", "Bracknell Forest"),
        ("merton", "London Borough of Merton", "Merton"),
        ("birmingham", "Birmingham City Council", "Birmingham"),
        ("leeds", "Leeds City Council", "Leeds"),
        ("sheffield", "Sheffield City Council", "Sheffield"),
        ("bradford", "Bradford Metropolitan District Council", "Bradford"),
        ("liverpool", "Liverpool City Council", "Liverpool"),
        ("bristol", "Bristol City Council", "Bristol"),
        ("wakefield", "Wakefield Metropolitan District Council", "Wakefield"),
        ("coventry", "Coventry City Council", "Coventry"),
        ("durham", "Durham County Council", "Durham"),
        ("kirklees", "Kirklees Council", "Kirklees"),
        ("leicester", "Leicester City Council", "Leicester"),
        ("cornwall", "Cornwall Council", "Cornwall"),
        ("nottingham", "Nottingham City Council", "Nottingham"),
        ("wirral", "Wirral Metropolitan Borough Council", "Wirral"),
        ("newcastle", "Newcastle City Council", "Newcastle"),
    };

    public static string NameOf(string slug) => Councils.FirstOrDefault(c => c.Slug == slug).Name ?? slug;
    public static string ShortOf(string slug) => Councils.FirstOrDefault(c => c.Slug == slug).Short ?? slug;

    public async Task LogAsync(string what, double ms, string extra = "")
    {
        try { await _js.InvokeVoidAsync("console.info", $"CW-PERF {what} {ms:F0}ms {extra}".TrimEnd()); } catch { }
    }

    /// <summary>The downloaded (still gzipped) bytes of a file. A cancelled load stops the download.</summary>
    public async Task<byte[]> FetchAsync(string path, bool gz, CancellationToken ct = default) =>
        await _http.GetByteArrayAsync(Root + path + (gz ? ".gz" : ""), ct);

    /// <summary>Inflates gzip bytes in 32 KB steps, handing the thread back between steps when a slice is spent (a 1.5 MB file inflates in one go otherwise).</summary>
    public static async Task<byte[]> InflateAsync(byte[] raw, Cooperative co, int expected = 0)
    {
        using var input = new MemoryStream(raw, writable: false);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        var output = new MemoryStream(expected > 0 ? expected : raw.Length * 6);
        var buf = new byte[32768];
        int n;
        while ((n = zip.Read(buf, 0, buf.Length)) > 0)
        {
            output.Write(buf, 0, n);
            if (co.Due) await co.YieldAsync();
        }
        return output.GetBuffer().AsSpan(0, (int)output.Length).ToArray();
    }

    async Task<byte[]> GetBytesAsync(string path, bool gz, Cooperative co)
    {
        var raw = await FetchAsync(path, gz, co.Ct);
        return gz ? await InflateAsync(raw, co) : raw;
    }

    /// <summary>A file's decompressed bytes (not cached): for the month slices, which are scanned straight from bytes.</summary>
    public async Task<byte[]> BytesAsync(string path, bool gz, Cooperative co)
    {
        var sw = Stopwatch.StartNew();
        var b = await GetBytesAsync(path, gz, co);
        await LogAsync("bytes " + path, sw.ElapsedMilliseconds, $"({b.Length / 1024} KB)");
        return b;
    }
    /// <summary>A parsed CSV (header row first), cached by path for the page's life. Small files only.</summary>
    public async Task<List<string[]>> TableAsync(string path, bool gz, Cooperative co, IWorkProgress? progress = null, string? label = null, int internMask = 0)
    {
        if (_tables.TryGetValue(path, out var hit)) return hit;
        var sw = Stopwatch.StartNew();
        var bytes = await GetBytesAsync(path, gz, co);
        long fetched = sw.ElapsedMilliseconds;
        var t = await SlicedCsv.ParseAsync(bytes, co, _interner, internMask, progress, label);
        _tables[path] = t;
        await LogAsync("table " + path, sw.ElapsedMilliseconds, $"(fetch+unzip {fetched}ms, {t.Count - 1} rows, {bytes.Length / 1024} KB)");
        return t;
    }

    /// <summary>Drops a cached table (a month slice is only kept as its grouped result).</summary>
    public void Forget(string path) => _tables.Remove(path);

    public async Task<List<CouncilIndexRow>> IndexAsync(Cooperative co)
    {
        if (_index is not null) return _index;
        var t = await TableAsync("index.csv", false, co);
        // Council,Months,Parts,TxRows,ExceptionRows,UnmatchedExceptionRows,LargestMonth,LargestMonthRaw,LargestMonthGzip,
        // LargestExceptionMonthRaw,LargestExceptionMonthGzip,TotalRawMB,TotalGzipMB,ArchiveMB
        _index = t.Skip(1).Where(r => r.Length >= 13).Select(r => new CouncilIndexRow(r[0], I(r[1]), I(r[3]), I(r[4]),
            (long)(double.Parse(r[12], CultureInfo.InvariantCulture) * 1048576))).ToList();
        return _index;
    }

    public async Task<List<MonthRow>> MonthsAsync(string slug, Cooperative co)
    {
        if (_months.TryGetValue(slug, out var m)) return m;
        var t = await TableAsync($"{slug}/months.csv", false, co);
        // Month,Parts,Rows,Net,Bytes,GzipBytes,ExceptionParts,ExceptionRows,ExceptionBytes,ExceptionGzipBytes
        m = t.Skip(1).Where(r => r.Length >= 10).Select(r => new MonthRow(r[0], I(r[2]), D(r[3]), I(r[6]), I(r[7]), long.Parse(r[9], CultureInfo.InvariantCulture), Math.Max(1, I(r[1])))).ToList();
        _months[slug] = m;
        return m;
    }

    readonly Dictionary<string, List<YearRow>> _years = new();
    public async Task<List<YearRow>> YearsAsync(string slug, Cooperative co)
    {
        if (_years.TryGetValue(slug, out var y)) return y;
        var t = await TableAsync($"{slug}/years.csv", false, co);
        // Year,Months,Parts,TxRows,Net,ExceptionRows,ExceptionBytes,ExceptionGzipBytes
        y = t.Skip(1).Where(r => r.Length >= 8).Select(r => new YearRow(r[0], I(r[1]), I(r[2]), I(r[3]), D(r[4]), I(r[5]), long.Parse(r[6], CultureInfo.InvariantCulture), long.Parse(r[7], CultureInfo.InvariantCulture))).ToList();
        _years[slug] = y;
        return y;
    }

    /// <summary>The financial year (April to March, as the government's declared totals are given) a month belongs to: "2023-04" and "2024-03" are both "2023-24". Anything else is "undated".</summary>
    public static string FinancialYearOf(string month)
    {
        if (month.Length != 7 || !int.TryParse(month.AsSpan(0, 4), out var y) || !int.TryParse(month.AsSpan(5, 2), out var m)) return "undated";
        int s = m >= 4 ? y : y - 1;
        return $"{s}-{(s + 1) % 100:00}";
    }

    // The raw bytes of the last two months' transaction slices, so a second "See the source rows" in the same month costs nothing.
    readonly Dictionary<string, List<byte[]>> _txSlices = new();
    readonly List<string> _txOrder = new();

    /// <summary>Every part of one month's transaction slice (decompressed bytes). Fetched only when a visitor asks for source rows.</summary>
    public async Task<List<byte[]>> TxSliceAsync(string slug, string month, int parts, Cooperative co)
    {
        string key = slug + "/" + month;
        if (_txSlices.TryGetValue(key, out var hit)) return hit;
        var sw = Stopwatch.StartNew();
        var list = new List<byte[]>();
        for (int p = 1; p <= Math.Max(1, parts); p++)
            list.Add(await GetBytesAsync(p == 1 ? $"{slug}/{month}.csv" : $"{slug}/{month}.{p}.csv", true, co));
        _txSlices[key] = list; _txOrder.Add(key);
        while (_txOrder.Count > 2) { _txSlices.Remove(_txOrder[0]); _txOrder.RemoveAt(0); }
        await LogAsync($"tx slice {key}", sw.ElapsedMilliseconds, $"({parts} part(s), {list.Sum(b => b.Length) / 1024} KB)");
        return list;
    }

    /// <summary>The slug of a council from its full name ("Leeds City Council" gives "leeds"); null when it is not one of the twenty-one.</summary>
    public static string? SlugOfName(string fullName) =>
        Councils.FirstOrDefault(c => c.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase)).Slug;

    public async Task<CouncilProfile?> ProfileAsync(string slug)
    {
        if (_profiles is null)
        {
            var sw = Stopwatch.StartNew();
            var bytes = await GetBytesAsync("profiles.json", true, new Cooperative(CancellationToken.None));
            using var doc = JsonDocument.Parse(bytes);
            var list = new List<CouncilProfile>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var quirks = new List<string>();
                if (e.TryGetProperty("quirks", out var q)) foreach (var x in q.EnumerateArray()) quirks.Add(x.GetString() ?? "");
                int held = e.TryGetProperty("heldBack", out var hb) ? hb.GetInt32() : 0;
                list.Add(new CouncilProfile(S("slug"), S("name"), S("page"), S("howTo"), quirks, S("lastChecked"), S("verification"),
                    string.IsNullOrWhiteSpace(S("foi")) ? null : S("foi"), held));
            }
            _profiles = list;
            await LogAsync("profiles.json", sw.ElapsedMilliseconds, $"({bytes.Length / 1024} KB)");
        }
        return _profiles.FirstOrDefault(p => p.Slug == slug);
    }

    /// <summary>Download link for a government outturn dataset named like "MHCLG Revenue Outturn 2023-24 RSX" (from sources.csv).</summary>
    public async Task<string?> SourceUrlAsync(string sourceText, Cooperative co)
    {
        if (_sources is null)
        {
            var t = await TableAsync("cross/sources.csv", false, co);
            _sources = t.Skip(1).Where(r => r.Length >= 2).ToDictionary(r => r[0], r => r[1]);
        }
        var year = System.Text.RegularExpressions.Regex.Match(sourceText, @"\d{4}-\d{2}").Value;
        string? code = sourceText.Contains("COR A1") ? "CORA1" : sourceText.Contains("RSX") ? "RSX" : sourceText.Contains("RO4") ? "RO4"
            : System.Text.RegularExpressions.Regex.IsMatch(sourceText, @"\bRS\b") ? "RS" : null;
        return code is not null && year.Length > 0 && _sources.TryGetValue($"{code}_{year}", out var u) ? u : null;
    }

    static int I(string s) => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public static decimal D(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}

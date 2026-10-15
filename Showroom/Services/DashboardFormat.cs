using System.Globalization;

namespace Showroom.Services;

/// <summary>Number formatting for the dashboard (invariant culture, so a shell and a phone print the same).</summary>
public static class DashboardFormat
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string N(long n) => n.ToString("N0", C);

    public static string Money(long pounds) => Math.Abs(pounds) switch
    {
        >= 1_000_000_000 => "£" + (pounds / 1e9).ToString("0.00", C) + "bn",
        >= 1_000_000 => "£" + (pounds / 1e6).ToString("0.00", C) + "m",
        >= 10_000 => "£" + (pounds / 1e3).ToString("0.0", C) + "k",
        _ => "£" + pounds.ToString("N0", C),
    };

    public static string Ms(double ms) => ms switch
    {
        < 10 => ms.ToString("0.00", C) + " ms",
        < 100 => ms.ToString("0.0", C) + " ms",
        < 2000 => ms.ToString("0", C) + " ms",
        _ => (ms / 1000).ToString("0.0", C) + " s",
    };

    public static string Mb(long bytes) => (bytes / 1048576.0).ToString("N0", C) + " MB";

    /// <summary>"plane" for the Pleroma aggregate plane, otherwise "engine" (a columnar scan or accumulator).</summary>
    public static string RouteBadge(string route) =>
        route.Contains("Pleroma plane", StringComparison.OrdinalIgnoreCase) ? "plane" : route.Length == 0 ? "-" : "engine";
}

/// <summary>The visitor's own interaction timings this session. Nearest-rank percentiles over the last 500.</summary>
public sealed class LatencyLog
{
    const int Cap = 500;
    readonly List<double> _summary = new(), _detail = new();

    public int Count => _summary.Count;

    public void Add(double summaryMs, double detailMs)
    {
        _summary.Add(summaryMs); _detail.Add(detailMs);
        if (_summary.Count > Cap) { _summary.RemoveAt(0); _detail.RemoveAt(0); }
    }

    public (double P50, double P95) Summary => (P(_summary, .5), P(_summary, .95));
    public (double P50, double P95) Detail => (P(_detail, .5), P(_detail, .95));

    static double P(List<double> xs, double q)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        return s[Math.Clamp((int)Math.Ceiling(q * s.Count) - 1, 0, s.Count - 1)];
    }
}

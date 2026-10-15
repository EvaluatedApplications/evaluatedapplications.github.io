using System.Globalization;
using System.Net;
using System.Text;
using HelixDemo;
using HoloGenome;

namespace Showroom.Services;

/// <summary>The two figures on the Helix page, drawn as SVG text from the numbers the run produced (Razor cannot carry an SVG text tag).
/// Nothing here computes science: the track is the encoder's phases, the chart is the graded r and its interval.</summary>
public static class HelixSvg
{
    public const int TrackCap = 300;                 // bases drawn; the numbers beside the figure use the whole sequence
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Top: one cell per base, hue = the helix phase there (one full colour cycle per turn). Bottom: how far the measured twist has
    /// run ahead of (+) or behind (-) a constant 34.3 degree twist, in degrees, along the sequence.</summary>
    public static string Track(string seq, TrackResult t)
    {
        int n = Math.Min(seq.Length, TrackCap);
        const double W = 900, Cell = 30, Gap = 26, Chart = 96;
        double cw = W / n;
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"hx-svg\" viewBox=\"0 0 900 {F(Cell + Gap + Chart + 22)}\" role=\"img\" aria-label=\"Helix phase of {seq.Length} bases: {F(t.Turns)} turns\">");
        for (int i = 0; i < n; i++)
        {
            double turn = t.Phase[i] / (2 * Math.PI), hue = (turn - Math.Floor(turn)) * 360;
            sb.Append($"<rect x=\"{F(i * cw)}\" y=\"0\" width=\"{F(cw + 0.4)}\" height=\"{F(Cell)}\" fill=\"hsl({F(hue)} 65% 52%)\"/>");
            if (n <= 90) sb.Append($"<text x=\"{F(i * cw + cw / 2)}\" y=\"{F(Cell * 0.68)}\" text-anchor=\"middle\" font-size=\"{F(Math.Min(16, cw * 0.7))}\" fill=\"#0b0d12\" font-family=\"monospace\">{seq[i]}</text>");
            if (i > 0 && Math.Floor(turn) > Math.Floor(t.Phase[i - 1] / (2 * Math.PI)))
                sb.Append($"<line x1=\"{F(i * cw)}\" x2=\"{F(i * cw)}\" y1=\"0\" y2=\"{F(Cell + 8)}\" stroke=\"#fff\" stroke-width=\"1.5\"/>");
        }
        double top = Cell + Gap, mid = top + Chart / 2;
        var drift = Enumerable.Range(0, n).Select(i => (t.Phase[i] - t.FlatPhase[i]) * 180 / Math.PI).ToArray();
        double span = Math.Max(1, drift.Max(Math.Abs));
        sb.Append($"<line x1=\"0\" x2=\"900\" y1=\"{F(mid)}\" y2=\"{F(mid)}\" stroke=\"currentColor\" stroke-opacity=\".35\" stroke-dasharray=\"4 4\"/>");
        sb.Append("<polyline fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" points=\"");
        for (int i = 0; i < n; i++) sb.Append($"{F(i * cw + cw / 2)},{F(mid - drift[i] / span * (Chart / 2 - 4))} ");
        sb.Append("\"/>");
        sb.Append($"<text x=\"2\" y=\"{F(top + 11)}\" font-size=\"12\" fill=\"currentColor\" fill-opacity=\".7\">ahead of constant twist: +{F(span)} degrees at most</text>");
        sb.Append($"<text x=\"2\" y=\"{F(top + Chart + 14)}\" font-size=\"12\" fill=\"currentColor\" fill-opacity=\".7\">behind: -{F(span)}. Left to right: base {1} to {n}{(seq.Length > n ? $" of {seq.Length}" : "")}. White ticks: a full turn.</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>One row per graded score: r as a dot, the 95% interval as a bar, a dashed line at zero.</summary>
    public static string Grades(IReadOnlyList<(string Label, ScoreGrade G, bool Headline)> rows)
    {
        double lo = Math.Min(-0.1, rows.Min(r => r.G.Low)) - 0.05, hi = Math.Max(0.1, rows.Max(r => r.G.High)) + 0.05;
        const double Left = 250, Right = 880, RowH = 34;
        double X(double v) => Left + (v - lo) / (hi - lo) * (Right - Left);
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"hx-svg\" viewBox=\"0 0 900 {F(rows.Count * RowH + 34)}\" role=\"img\" aria-label=\"Correlation with the measurement and its 95 percent interval for each score\">");
        sb.Append($"<line x1=\"{F(X(0))}\" x2=\"{F(X(0))}\" y1=\"0\" y2=\"{F(rows.Count * RowH)}\" stroke=\"currentColor\" stroke-opacity=\".5\" stroke-dasharray=\"4 4\"/>");
        for (int i = 0; i < rows.Count; i++)
        {
            var (label, g, head) = rows[i];
            double y = i * RowH + RowH / 2;
            sb.Append($"<text x=\"4\" y=\"{F(y + 4)}\" font-size=\"13\" fill=\"currentColor\" font-weight=\"{(head ? 700 : 400)}\">{WebUtility.HtmlEncode(label)}</text>");
            sb.Append($"<line x1=\"{F(X(g.Low))}\" x2=\"{F(X(g.High))}\" y1=\"{F(y)}\" y2=\"{F(y)}\" stroke=\"{(head ? "var(--accent)" : "currentColor")}\" stroke-opacity=\"{(head ? 1 : .6)}\" stroke-width=\"6\" stroke-linecap=\"round\"/>");
            sb.Append($"<circle cx=\"{F(X(g.Pearson))}\" cy=\"{F(y)}\" r=\"6\" fill=\"var(--ink)\"/>");
        }
        double ay = rows.Count * RowH + 14;
        sb.Append($"<text x=\"{F(X(0))}\" y=\"{F(ay + 12)}\" font-size=\"12\" text-anchor=\"middle\" fill=\"currentColor\" fill-opacity=\".7\">r = 0</text>");
        sb.Append($"<text x=\"{F(Left)}\" y=\"{F(ay + 12)}\" font-size=\"12\" fill=\"currentColor\" fill-opacity=\".7\">{F(lo)}</text>");
        sb.Append($"<text x=\"{F(Right)}\" y=\"{F(ay + 12)}\" font-size=\"12\" text-anchor=\"end\" fill=\"currentColor\" fill-opacity=\".7\">{F(hi)}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}

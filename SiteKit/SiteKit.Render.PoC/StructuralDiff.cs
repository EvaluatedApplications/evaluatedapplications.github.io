using System.Text;

namespace SiteKit.Render.PoC;

/// <summary>
/// A small LCS-based line diff for comparing the generated page against the hand-authored
/// original. Normalizes away pure-whitespace/indentation/line-wrapping differences (a
/// generator will never reproduce hand-typed indentation byte-for-byte, and that's not the
/// thing this proof-of-concept needs to prove) so what's left is real content/structure drift.
/// </summary>
public static class StructuralDiff
{
    public static string Compare(string original, string generated)
    {
        var a = Normalize(original);
        var b = Normalize(generated);

        if (a.SequenceEqual(b))
            return $"IDENTICAL after whitespace normalization ({a.Count} non-blank lines each). " +
                   "No content or structural differences found.";

        var (removed, added, unchanged) = DiffLines(a, b);
        var sb = new StringBuilder();
        sb.AppendLine($"{a.Count} normalized lines in original, {b.Count} in generated, {unchanged} unchanged.");
        sb.AppendLine($"{removed.Count} line(s) only in the ORIGINAL (hand-authored) file:");
        foreach (var line in removed) sb.AppendLine("  - " + line);
        sb.AppendLine($"{added.Count} line(s) only in the GENERATED file:");
        foreach (var line in added) sb.AppendLine("  + " + line);
        return sb.ToString();
    }

    private static List<string> Normalize(string html)
    {
        // Tokenize on TAG boundaries, not physical newlines: the hand-authored file both packs
        // several tags on one physical line (e.g. one-line <article class="card">...</article>
        // blocks) AND hand-word-wraps long prose across several physical lines mid-paragraph.
        // Splitting right before '<' and right after '>' turns both representations into the
        // same token stream — one token per tag, one token per inline text run with its
        // internal whitespace (including embedded newlines) collapsed to a single space — so
        // neither a re-wrap nor a re-packing shows up as a false content diff; only genuine
        // content/attribute/structure differences survive.
        var tokens = System.Text.RegularExpressions.Regex.Split(html, @"(?=<)|(?<=>)");
        return tokens
            .Select(t => System.Text.RegularExpressions.Regex.Replace(t.Trim(), @"\s+", " "))
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static (List<string> removed, List<string> added, int unchanged) DiffLines(List<string> a, List<string> b)
    {
        var n = a.Count; var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var removed = new List<string>();
        var added = new List<string>();
        int ii = 0, jj = 0, unchanged = 0;
        while (ii < n && jj < m)
        {
            if (a[ii] == b[jj]) { unchanged++; ii++; jj++; }
            else if (dp[ii + 1, jj] >= dp[ii, jj + 1]) { removed.Add(a[ii]); ii++; }
            else { added.Add(b[jj]); jj++; }
        }
        while (ii < n) { removed.Add(a[ii]); ii++; }
        while (jj < m) { added.Add(b[jj]); jj++; }
        return (removed, added, unchanged);
    }
}

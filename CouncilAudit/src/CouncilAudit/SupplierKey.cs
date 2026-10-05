using System.Text;

namespace CouncilAudit;

/// <summary>
/// Deterministic, rule-based supplier-name normalisation for cross-council matching (same
/// supplier paid by several councils; a payment seen from both sides). Deliberately NOT
/// built on HoloDb NEAREST: as shipped (2.0.0, the latest published version at the time of
/// writing), NEAREST ranks an exact field match first and everything after it in an order
/// unrelated to string distance (confirmed in FEEDBACK.md F-04 - "BOLT SUPPLIES" ranks
/// above "ACME LTD." for a query of "ACME LTD"). A graded text encoding was requested but
/// is not live, so this key is the real matching mechanism today; if/when graded NEAREST
/// ships, it can ADD recall on top of this key, not replace it.
/// </summary>
public static class SupplierKey
{
    private static readonly string[] Suffixes =
    {
        " LIMITED", " LTD", " PLC", " LLP", " LLC", " CIC", " CIO",
    };

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.ToUpperInvariant().Trim();

        // "T/A"/"TRADING AS" - keep only the part before it (the legal entity), consistently.
        foreach (var marker in new[] { " T/A ", " TRADING AS " })
        {
            int idx = s.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0) s = s[..idx];
        }

        // Strip common company-type suffixes (possibly more than one, e.g. "... LTD LLP").
        bool strippedAny;
        do
        {
            strippedAny = false;
            foreach (var suf in Suffixes)
            {
                if (s.EndsWith(suf, StringComparison.Ordinal))
                {
                    s = s[..^suf.Length].TrimEnd();
                    strippedAny = true;
                }
            }
        } while (strippedAny);

        // Strip punctuation, collapse whitespace.
        var sb = new StringBuilder(s.Length);
        bool lastWasSpace = false;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().Trim();
    }
}

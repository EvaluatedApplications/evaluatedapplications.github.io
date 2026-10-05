using System.Text.RegularExpressions;

namespace CouncilAudit;

/// <summary>
/// Session 32: a containment screen over graded NEAREST proposals. HoloDb's NEAREST score does not separate a right
/// alias from a wrong one (Session 30/31: 121 of 274 rejected proposals scored above the lowest accepted one), so the
/// second pass was a person. This screen looks at what the score cannot: do the entity's distinctive words appear as
/// whole words in the supplier name, and does the supplier name carry other distinctive words of its own?
/// It sorts proposals into three piles; it never replaces the person for the middle pile.
///   Contained : every distinctive word present, no other distinctive word (and, for a council, a council word).
///   NoOverlap : fewer than half the distinctive words present (after prefix and one-letter-typo tolerance).
///   Partial   : everything else; a person decides.
/// </summary>
public static class AliasScreen
{
    public enum Tier { Contained, Partial, NoOverlap }

    public sealed record Result(Tier Tier, double Coverage, int ExtraWords);

    // Words that do not distinguish one body from another. Deliberately generic: no entity-specific entries.
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "COUNCIL","CNCL","CNC","BOROUGH","CITY","OF","LTD","LIMITED","THE","DISTRICT","METROPOLITAN","MET","MDC","BC","CC",
        "FOR","AND","&","LONDON","ROYAL","DC","CO","PLC","LLP","LBC","MBC","B",
    };

    private static readonly HashSet<string> CouncilWords = new(StringComparer.Ordinal)
    { "COUNCIL","CNCL","CNC","BC","CC","MDC","DC","BOROUGH","LBC","MBC" };

    public static string[] Words(string s) =>
        Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9& ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool WordMatches(string keyWord, string coreWord)
    {
        if (keyWord == coreWord) return true;
        if (keyWord.Length >= 3 && coreWord.StartsWith(keyWord, StringComparison.Ordinal)) return true; // "Bracknell Forest BC" style cuts
        if (coreWord.Length >= 6 && keyWord.Length > coreWord.Length && keyWord.StartsWith(coreWord, StringComparison.Ordinal)) return true; // "FUTURESFC" (added after seeing one miss; post hoc)
        if (keyWord.Length >= 6 && coreWord.Length >= 6 && Math.Abs(keyWord.Length - coreWord.Length) <= 1 && Edit1(keyWord, coreWord)) return true;
        return false;
    }

    // true when the words differ by at most one insertion, deletion or substitution
    private static bool Edit1(string a, string b)
    {
        if (a == b) return true;
        if (a.Length == b.Length)
        {
            int d = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i] && ++d > 1) return false;
            return true;
        }
        var (s, l) = a.Length < b.Length ? (a, b) : (b, a);
        int i2 = 0, j = 0, skips = 0;
        while (i2 < s.Length && j < l.Length)
        {
            if (s[i2] == l[j]) { i2++; j++; }
            else { if (++skips > 1) return false; j++; }
        }
        return true;
    }

    /// <summary>Screen one supplier name against one entity (any of its query spellings is enough).</summary>
    public static Result Screen(IEnumerable<string> entityQueries, bool entityIsCouncil, string supplierName)
    {
        var key = Words(supplierName);
        Result? best = null;
        foreach (var q in entityQueries)
        {
            var core = Words(q).Where(w => !Generic.Contains(w)).ToArray();
            if (core.Length == 0) continue;
            int hit = core.Count(c => key.Any(k => WordMatches(k, c)));
            double cov = (double)hit / core.Length;
            int extra = key.Count(k => !Generic.Contains(k) && !core.Any(c => WordMatches(k, c)));
            bool councilWordOk = !entityIsCouncil || key.Any(CouncilWords.Contains);
            var tier = cov >= 1.0 && extra == 0 && councilWordOk ? Tier.Contained
                     : cov < 0.5 ? Tier.NoOverlap : Tier.Partial;
            var r = new Result(tier, cov, extra);
            if (best == null || Rank(r) < Rank(best)) best = r;
        }
        return best ?? new Result(Tier.NoOverlap, 0, key.Length);
    }

    private static int Rank(Result r) => r.Tier == Tier.Contained ? 0 : r.Tier == Tier.Partial ? 1 : 2;
}

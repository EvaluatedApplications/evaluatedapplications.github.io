using System.Globalization;
using System.Text.RegularExpressions;

namespace CouncilAudit;

// Session 53: the pure parts of the error-category checklist (`checklist all`, see CHECKLIST.md). Everything here takes plain strings or
// tables and returns findings, so each rule can be tested on a planted defect. The file reading, the per-council loop and the exit code
// live in CouncilAudit.Cli/Checklist.cs.

/// <summary>C12: first-person, working-note and jargon text in a public profile.</summary>
public static class PrivateText
{
    // The same patterns the website builder uses to drop a sentence (the website builder: InternalSentence, FirstPerson,
    // WorkingNote), plus the protocol jargon it strips silently. The checklist wants the SOURCE clean so the public text never depends on a rescue.
    static readonly Regex Internal = new(@"owner's tip|Session \d+|FIRST PASS|plain bot fetch|\.cs\b|\.md\b|CityFixups|AuditEngine|ColumnMapping|MapRows|KnownQuirks|\bfoi/|scratch|inbox|Schedule R\b|hand-built|ONBOARDING|checklist|BASELINE|FEEDBACK|\bCLI\b|ParseDate|against the BASE|Fixups|this session|handopen|full-engine|MAD-r|XlsxReader|XlsReader|ExcelDataReader|FileDuplication|OtherForRepeat|rawcheck", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex First = new(@"\b(?:I|I'm|I've|I'd|I'll|me|my|myself|mine)\b", RegexOptions.Compiled);
    static readonly Regex Working = new(@"human click-through|not-yet-confirmed|\bnot as a fix\b|Recorded here as|top-?\d+ export|shown live to a visitor|\bseed \d+\b|\bWilson\b|\b95% ?CI\b|\bCI \[", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Jargon = new(@"\bseeded\b|\bn=\d+|\(seed", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Every match with a short stretch of its context, labelled by kind. Empty when the text is clean.</summary>
    public static List<string> Find(string text)
    {
        var found = new List<string>();
        void Scan(Regex rx, string kind)
        {
            foreach (Match m in rx.Matches(text))
            {
                int a = Math.Max(0, m.Index - 40), b = Math.Min(text.Length, m.Index + m.Length + 40);
                found.Add($"{kind} \"{m.Value}\" in: ...{text[a..b].Replace('\n', ' ')}...");
            }
        }
        Scan(First, "first person");
        Scan(Internal, "working-file reference");
        Scan(Working, "working note");
        Scan(Jargon, "protocol jargon");
        return found;
    }
}

/// <summary>C20 (Session 57): no individual is named in public prose. A council's data may name a private landlord, a sole trader or a performer's
/// trading name; our own text describes the pattern ("two private landlords") and never repeats the name. Three tests: a title before a name,
/// a forename (optionally a second forename or initial) followed by a surname-shaped word, an initial-and-surname or forename-and-surname in front of
/// a trade word, and any run of words that hashes to a name the export withheld from a redacted row (RowRedaction.NameHash).</summary>
public static class IndividualNames
{
    static readonly string[] Forenames =
    {
        "Aaron","Abdul","Adam","Adrian","Ahmed","Alan","Albert","Alex","Alexander","Alison","Amanda","Amy","Andrea","Andrew","Angela","Ann","Anna","Anne","Anthony","Antony","Barry","Beverley","Brian","Bruce","Carl","Carol","Caroline","Catherine","Charles","Charlotte","Chris","Christine","Christopher","Claire","Clive","Colin","Craig","Dale","Damian","Daniel","Darren","David","Dean","Deborah","Dennis","Derek","Diane","Donald","Dorothy","Douglas","Duncan","Edward","Elaine","Eleanor","Elizabeth","Emily","Emma","Eric","Fiona","Frances","Frank","Gareth","Gary","Gavin","Geoffrey","George","Gerald","Gillian","Glenn","Gordon","Graham","Gregory","Hannah","Harry","Heather","Helen","Henry","Howard","Hugh","Ian","Jack","Jacqueline","Jacqui","James","Jamie","Jane","Janet","Jason","Jean","Jeffrey","Jennifer","Jeremy","Jessica","Jill","Joan","Joanne","John","Jonathan","Joseph","Joanna","Judith","Julia","Julie","Justin","Karen","Katherine","Kathleen","Keith","Kelly","Kenneth","Kevin","Kim","Kirsty","Laura","Lawrence","Lee","Leslie","Linda","Lisa","Lorraine","Louise","Lucy","Malcolm","Margaret","Maria","Marie","Marilyn","Mark","Martin","Mary","Matthew","Maureen","Melanie","Michael","Michelle","Mohammed","Muhammad","Nicholas","Nicola","Nigel","Norman","Olivia","Pamela","Patricia","Patrick","Paul","Paula","Peter","Philip","Rachel","Ralph","Raymond","Rebecca","Richard","Rita","Robert","Robin","Roger","Ronald","Rosemary","Roy","Russell","Ruth","Samuel","Sandra","Sara","Sarah","Scott","Sean","Sharon","Shirley","Simon","Sophie","Stephanie","Stephen","Steve","Steven","Stuart","Susan","Suzanne","Terence","Teresa","Terry","Thomas","Timothy","Tina","Tony","Tracey","Tracy","Trevor","Valerie","Vanessa","Victoria","Vincent","Wayne","Wendy","William","Yvonne"
    };
    // forename-and-surname shaped names that are really organisations or brands; every entry is a reviewed false positive
    static readonly HashSet<string> NotPeople = new(StringComparer.OrdinalIgnoreCase)
    {
        "John Lewis", "Thomas Cook", "Dennis Eagle"   // Dennis Eagle: a refuse-vehicle maker (a company, found in Wakefield and York profiles, reviewed 2026-10-05)
    };
    static readonly string Name = @"[A-Z][a-z]+(?:'[a-z]+)?(?:-[A-Z][a-z]+)?";
    static readonly Regex Title = new(@"\b(?:Mr|Mrs|Ms|Miss|Dr|Cllr|Councillor|Sir|Lady|Prof)\b\.? " + Name, RegexOptions.Compiled);
    static readonly Regex Couple = new(@"\b(" + Name + @") (?:&|and) (" + Name + @") (" + Name + @")\b", RegexOptions.Compiled);
    static readonly Regex Full = new(@"\b(" + string.Join("|", Forenames) + @")(?: (?:[A-Z]\.?|" + Name + @"))? (" + Name + @")\b", RegexOptions.Compiled);
    static readonly Regex Trade = new(@"\b(?:[A-Z]\.? )?" + Name + @" " + Name + @" (?:Electrical|Plumbing|Roofing|Builders|Building|Joinery|Decorating|Decorators|Carpentry|Plastering|Heating|Gas Services|Photography|Entertainments?|Magic|Music|Landscaping|Gardening|Locksmiths?|Cars|Taxis)\b", RegexOptions.Compiled);
    static readonly Regex Initial = new(@"\b[A-Z]\.? (?:[A-Z]\.? )?(?!(?:Way|Card|Year)\b)" + Name + @" (?:Electrical|Plumbing|Roofing|Builders|Joinery|Decorating|Plastering|Heating|Photography|Taxis)\b", RegexOptions.Compiled);

    static string Plain(string s) => Regex.Replace(Regex.Replace(s, @"[^\p{L}\p{N}' ]+", " "), @"\s+", " ").Trim();

    /// <summary>Every finding, with context. <paramref name="withheld"/> is the set of NameHash values of person-like names the export withheld (may be empty).</summary>
    public static List<string> Find(string text, HashSet<string> withheld)
    {
        var f = new List<string>();
        void Add(string kind, Match m)
        {
            if (NotPeople.Any(n => m.Value.StartsWith(n, StringComparison.OrdinalIgnoreCase))) return;
            int a = Math.Max(0, m.Index - 30), b = Math.Min(text.Length, m.Index + m.Length + 30);
            f.Add($"{kind} \"{m.Value}\" in: ...{text[a..b].Replace('\n', ' ')}...");
        }
        foreach (Match m in Title.Matches(text)) Add("a titled name", m);
        foreach (Match m in Couple.Matches(text)) if (Forenames.Contains(m.Groups[1].Value) || Forenames.Contains(m.Groups[2].Value)) Add("a named couple", m);
        foreach (Match m in Full.Matches(text)) Add("a forename and surname", m);
        foreach (Match m in Trade.Matches(text)) Add("a personal name in front of a trade", m);
        foreach (Match m in Initial.Matches(text)) Add("an initial and surname in front of a trade", m);
        if (withheld.Count > 0)
        {
            var w = Plain(text).Split(' ');
            for (int i = 0; i < w.Length; i++)
                for (int n = 2; n <= 5 && i + n <= w.Length; n++)
                {
                    string run = string.Join(" ", w, i, n);
                    if (withheld.Contains(RowRedaction.NameHash(run))) f.Add($"a name the export withheld from a redacted row: \"{run}\"");
                }
        }
        return f;
    }
}

/// <summary>C11: a council paying itself is not "another body".</summary>
public static class SelfPayment
{
    static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    { "council", "city", "county", "borough", "metropolitan", "district", "london", "royal", "of", "the", "and", "cc", "bc", "mbc", "dc", "lbc", "mdc", "rbc", "bbc", "cbc", "mb", "authority" };

    static string[] Words(string s) => Regex.Split(s.ToLowerInvariant().Replace("&", " "), @"[^a-z0-9]+").Where(w => w.Length > 0).ToArray();

    /// <summary>The distinctive words of a body's name ("Leeds CC" and "Leeds City Council" both give "leeds").</summary>
    public static string Core(string name) => string.Join("", Words(name).Where(w => !Filler.Contains(w)));

    /// <summary>True when <paramref name="entity"/> names the council itself: its distinctive words equal the council's, or equal the slug
    /// (so "RBWM" and "Royal Borough of Windsor and Maidenhead" both match slug rbwm / the full name's core).</summary>
    public static bool IsSelf(string slug, string councilName, string entity)
    {
        string e = Core(entity);
        if (e.Length == 0) return false;
        return e == Core(councilName) || e == slug || e == slug.Replace("-", "");
    }
}

/// <summary>C06: money figures stated in prose, compared with the amounts the export can derive.</summary>
public static class MoneyFigures
{
    static readonly Regex Rx = new(@"(?:GBP|£)\s?(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(\s?(?:m\b|million|bn\b|k\b))?", RegexOptions.Compiled);

    public readonly record struct Stated(string Raw, decimal Value, decimal Tolerance);

    public static List<Stated> Extract(string text)
    {
        var list = new List<Stated>();
        foreach (Match m in Rx.Matches(text))
        {
            string num = m.Groups[1].Value.Replace(",", "");
            if (!decimal.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            int dp = num.Contains('.') ? num.Length - num.IndexOf('.') - 1 : 0;
            decimal unit = 1m;
            string u = m.Groups[2].Value.Trim().ToLowerInvariant();
            if (u == "m" || u == "million") unit = 1_000_000m; else if (u == "bn") unit = 1_000_000_000m; else if (u == "k") unit = 1_000m;
            decimal tol = 0.5m * unit * (decimal)Math.Pow(10, -dp);
            if (unit == 1m && dp == 0) tol = 0.5m;
            list.Add(new Stated(m.Value.Trim(), v * unit, tol + 0.0000001m));
        }
        return list;
    }

    public enum Verdict { Verified, NearMiss, NotDerivable }

    /// <summary>Verified: some derivable amount equals the stated one to its stated precision (the amount, or its absolute value).
    /// NearMiss: no amount does, but one is within 0.5% of it, which reads as a stale or mistyped copy of that amount.
    /// NotDerivable: the export has nothing to compare it with (hand-opened or external figure).</summary>
    public static Verdict Judge(Stated s, IEnumerable<decimal> derivable, out decimal nearest)
    {
        nearest = 0m;
        decimal best = decimal.MaxValue;
        foreach (var d in derivable)
        {
            foreach (var c in new[] { d, Math.Abs(d) })
            {
                decimal diff = Math.Abs(c - s.Value);
                if (diff <= s.Tolerance) { nearest = c; return Verdict.Verified; }
                if (diff < best) { best = diff; nearest = c; }
            }
        }
        // a figure under a million is as likely a hand-opened payment as a copy of a total (York: GBP 228,920 sits within 0.5% of an unrelated total)
        if (best != decimal.MaxValue && Math.Abs(s.Value) >= 1_000_000m && best / Math.Abs(s.Value) <= 0.005m) return Verdict.NearMiss;
        return Verdict.NotDerivable;
    }
}

/// <summary>C02/C03/C04/C05: one row of export/transaction_twins.csv checked against the rule the page states and the sums it prints.</summary>
public static class TwinChecks
{
    /// <summary>Findings for the whole twins table of one council (rows already filtered to the council). Header names are read by name.</summary>
    public static List<string> Findings(List<string[]> table, string council)
    {
        var f = new List<string>();
        if (table.Count == 0) return f;
        var h = table[0];
        int Ix(string n) { int i = Array.IndexOf(h, n); if (i < 0) throw new InvalidOperationException("twins: no column " + n); return i; }
        int iA = Ix("TransactionA"), iB = Ix("TransactionB"), iDa = Ix("DateA"), iDb = Ix("DateB"), iDays = Ix("DaysApart"), iTot = Ix("TotalValue"),
            iGid = Ix("GroupId"), iGs = Ix("GroupSize"), iEx = Ix("ExtraCopyValue"), iSup = Ix("SupplierName"), iYa = Ix("YearA"), iYb = Ix("YearB");
        var byGroup = new Dictionary<string, List<string[]>>();
        foreach (var r in table.Skip(1))
        {
            // C03: both dates exist, and the stated gap is the real gap, within the stated seven days
            if (!DateTime.TryParseExact(r[iDa], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var da) ||
                !DateTime.TryParseExact(r[iDb], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var db))
                f.Add($"C03 {council} twin {r[iA]}/{r[iB]} ({r[iSup]}): a first pay date is missing ('{r[iDa]}', '{r[iDb]}') but the pair is listed as within seven days");
            else
            {
                int real = (int)Math.Abs((db - da).TotalDays);
                if (!int.TryParse(r[iDays], out int stated) || stated < 0 || stated != real)
                    f.Add($"C03 {council} twin {r[iA]}/{r[iB]}: DaysApart is {r[iDays]} but the dates {r[iDa]} and {r[iDb]} are {real} apart");
                if (real > 7) f.Add($"C03 {council} twin {r[iA]}/{r[iB]}: dates {real} days apart, the rule says within seven");
            }
            if (r[iA] == r[iB]) f.Add($"C05 {council} twin {r[iA]}: pairs a transaction with itself");
            if (!byGroup.TryGetValue(r[iGid] + "|" + r[iSup], out var l)) byGroup[r[iGid] + "|" + r[iSup]] = l = new();
            l.Add(r);
        }
        // C04: per group, not per pair. A group of n identical transactions lists n(n-1)/2 pairs but has n-1 extra copies; each transaction
        // after the first is credited once, so the group's ExtraCopyValue column sums to (n-1) x the transaction's value.
        foreach (var (key, rows) in byGroup)
        {
            int n = int.Parse(rows[0][iGs], CultureInfo.InvariantCulture);
            if (n > 3) f.Add($"C05 {council} twin group {key}: size {n}, the stated rule lists lines of that kind seen in at most three transactions");
            // a group is the set of transactions linked by listed pairs: between n-1 pairs (a chain, when two copies are further apart than
            // seven days) and n(n-1)/2 (every pair), and the pairs must connect all n transactions
            int maxPairs = n * (n - 1) / 2;
            if (rows.Count < n - 1 || rows.Count > maxPairs) f.Add($"C04 {council} twin group {key}: {n} transactions should list {n - 1} to {maxPairs} pairs, the file lists {rows.Count}");
            else
            {
                var parent = new Dictionary<string, string>();
                string Find(string x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                foreach (var r in rows)
                {
                    string a = r[iYa] + "|" + r[iA], b = r[iYb] + "|" + r[iB];
                    if (!parent.ContainsKey(a)) parent[a] = a; if (!parent.ContainsKey(b)) parent[b] = b;
                    parent[Find(a)] = Find(b);
                }
                int comps = parent.Keys.Select(Find).Distinct().Count();
                if (parent.Count != n || comps != 1) f.Add($"C04 {council} twin group {key}: size {n}, but its pairs name {parent.Count} transactions in {comps} linked set(s)");
            }
            decimal value = decimal.Parse(rows[0][iTot], NumberStyles.Float, CultureInfo.InvariantCulture);
            decimal extra = rows.Sum(r => decimal.Parse(r[iEx], NumberStyles.Float, CultureInfo.InvariantCulture));
            if (rows.All(r => decimal.Parse(r[iTot], NumberStyles.Float, CultureInfo.InvariantCulture) == value) && Math.Abs(extra - (n - 1) * value) > 0.011m)
                f.Add($"C04 {council} twin group {key}: extra-copy value {extra:N2}, expected {(n - 1) * value:N2} ({n - 1} extra copies of {value:N2})");
        }
        return f;
    }
}

/// <summary>C02: the Schedule A gap, recomputed from net, gross and VAT without the engine's classifier.</summary>
public static class ScheduleAGap
{
    /// <summary>Gross a payment of <paramref name="net"/> should state under a VAT Type label (the labels the council files use).</summary>
    public static decimal ExpectedGross(decimal net, string? vatType)
    {
        string v = (vatType ?? "").Trim().ToUpperInvariant();
        decimal mult = v.StartsWith("STD") ? 1.20m : v.StartsWith("RRTE") ? 1.05m : 1.00m;
        return Math.Round(net * mult, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The stated gap for lines of one supplier in one transaction: stated Gross minus the closer of (each line at its own label) and
    /// (the whole net at the first line's label). Null when there are no lines.</summary>
    public static decimal? Gap(IReadOnlyList<(decimal net, string vatType)> lines, decimal grossStated)
    {
        var g = Gaps(lines, grossStated);
        return g.Count == 0 ? null : g.OrderBy(Math.Abs).First();
    }

    /// <summary>Both readings of the expected gross: each line at its own label (rounded per line), and the whole net at the first line's label.</summary>
    public static List<decimal> Gaps(IReadOnlyList<(decimal net, string vatType)> lines, decimal grossStated)
    {
        if (lines.Count == 0) return new List<decimal>();
        decimal perLine = lines.Sum(l => ExpectedGross(l.net, l.vatType));
        decimal whole = ExpectedGross(lines.Sum(l => l.net), lines[0].vatType);
        return new List<decimal> { grossStated - perLine, grossStated - whole };
    }
}

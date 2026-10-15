using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CouncilAudit;

/// <summary>
/// Session 54 (re-audit N4, PRIVACY): a row is redacted if ANY column carries a redaction marker, not only the column the mapping
/// calls the payee. Bradford's August 2026 file has two payee columns: "SupplierName" reads "REDACTED PERSONAL DATA" on 229
/// childcare-voucher rows while "Supplier Name" holds the person's name on those rows; the scanner took the second column,
/// counted 0 redacted and published the 229 names. The rule here is the one place that decides it:
///  - <see cref="MarkerIn"/>: the first cell of the row (any column, mapped or not) that is a redaction marker;
///  - <see cref="Withhold"/>: when there is one, every cell in a name-keyed column (payee, "Supplier Name (alt)", beneficiary...) that is
///    not itself a marker is replaced by the marker, and any other cell that repeats a withheld name is replaced too.
/// No personal name from a redacted row is kept anywhere in the export, and none can reach a slice or a cross file, because
/// every later step reads the export. Only a salted-free SHA-1 of each withheld name is kept (export/&lt;slug&gt;/withheld_names.csv),
/// so checklist category C14 can prove no withheld name appears in any published file without the workspace holding the names.
/// </summary>
public static class RowRedaction
{
    static readonly Regex NameWord = new(@"supplier|payee|vendor|beneficiar|creditor|claimant|recipient|client|merchant|trader", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NotNameWord = new(@"number|\bno\b|\bnum\b|code|\bid\b|id$|\bref|reference|type|key|categor|date|amount|value|class|status|industry|invoice|group|regist|ledger|\buri\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly HashSet<string> ExactNames = new(StringComparer.Ordinal) { "supplier", "payee", "vendor", "beneficiary", "creditor", "claimant", "recipient", "client", "merchant", "trader", "supplierbeneficiary", "beneficiarysupplier" };

    /// <summary>The scope of the rule. DEFAULT (false): a row is redacted when a NAME-KEYED column (the payee column, "Supplier Name (alt)", beneficiary and similar)
    /// carries a marker. True (COUNCILAUDIT_REDACT_ANYCOLUMN=1): any cell of any column. Measured on the 28 councils before choosing (PREREG_S54_REAUDIT.md
    /// Amendment 1): the any-column reading would also withhold the named company payee on every row whose PURPOSE or ID column is redacted: Wakefield 567,113
    /// rows (the "Supplier ID" column reads Redacted on every row, the company registration number is published beside it), Kirklees 71,682 (school taxi
    /// firms with a Purpose of Spend of REDACTED PERSONAL DATA), Surrey 163,483, Camden about 5,600, Hertfordshire 15,917, Nottingham about 480, Cornwall 19.</summary>
    public static bool AnyColumn => Environment.GetEnvironmentVariable("COUNCILAUDIT_REDACT_ANYCOLUMN") == "1";

    /// <summary>True when a cell is a redaction marker: "REDACTED..." (not the real supplier Redactive), the split "RED12345ACTED"
    /// form, or a short "... personal data" label ("REDACETED PERSONAL DATA", "Redated Personal Data"). A long free-text description
    /// that merely mentions personal data is not a marker.</summary>
    public static bool IsMarkerCell(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return false;
        var s = cell.Trim();
        return AuditEngine.IsRedactedPlaceholder(s) || (s.Length <= 40 && s.Contains("PERSONAL DATA", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when a column header names a person or body (a payee), not a number, code, date, amount or type. "Supplier Name",
    /// "SupplierName", "Supplier Name (alt)", "Beneficiary Name", "Payee" yes; "Supplier Number", "Supplier Invoice No", "Payee Type" no.</summary>
    public static bool IsNameHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || NotNameWord.IsMatch(header)) return false;
        // the bare word ("Supplier", "Payee") or a word with "name" in it ("SupplierName", "Supplier Name (alt)", "Beneficiary Name"); a flag such as "VCSE Supplier"
        // (Calderdale) or "Supplier Group" is not a payee name
        var norm = Regex.Replace(header.ToLowerInvariant(), "[^a-z]", "");
        if (ExactNames.Contains(norm)) return true;
        return NameWord.IsMatch(norm) && norm.Contains("name");
    }

    /// <summary>The first marker cell that makes the row redacted (null when none): a marker in a name-keyed column or the payee column, or, with
    /// <paramref name="anyColumn"/>, in any column.</summary>
    public static string? MarkerIn(string[] header, string[] row, int payeeIdx, bool anyColumn)
    {
        for (int i = 0; i < row.Length; i++)
        {
            if (!IsMarkerCell(row[i])) continue;
            if (anyColumn || i == payeeIdx || (i < header.Length && IsNameHeader(header[i]))) return row[i].Trim();
        }
        return null;
    }

    /// <summary>The text a withheld row shows in place of a name: the payee cell when it is itself a marker, else the first name-keyed cell that is a
    /// marker (Bradford's "SupplierName" = "REDACTED PERSONAL DATA"), else a plain statement naming the column that carries the marker.</summary>
    public static string Label(string[] header, string[] row, int payeeIdx)
    {
        if (payeeIdx >= 0 && payeeIdx < row.Length && IsMarkerCell(row[payeeIdx])) return row[payeeIdx].Trim();
        for (int i = 0; i < row.Length; i++)
            if (i < header.Length && IsNameHeader(header[i]) && IsMarkerCell(row[i])) return row[i].Trim();
        // any-column scope only: the marker sits in a non-name column
        for (int i = 0; i < row.Length; i++)
            if (IsMarkerCell(row[i])) return "REDACTED (withheld: " + (i < header.Length && !string.IsNullOrWhiteSpace(header[i]) ? header[i].Trim() : "column " + (i + 1)) + " is redacted)";
        return "REDACTED";
    }

    static readonly System.Collections.Concurrent.ConcurrentBag<(string Tag, string Hash, bool Person)> WithheldBag = new();

    static readonly HashSet<string> OrgWords = new(StringComparer.Ordinal)
    { "LTD", "LIMITED", "PLC", "LLP", "NURSERY", "NURSERIES", "SCHOOL", "SCHOOLS", "CARE", "COTTAGE", "CLUB", "CENTRE", "CENTER", "PRESCHOOL", "PRE-SCHOOL", "PLAYGROUP", "CHILDMINDING",
      "CHILDMINDER", "CHILDMINDERS", "KIDS", "CHILDREN", "CHILDRENS", "ACADEMY", "TRUST", "HOUSE", "LODGE", "FARM", "PARK", "PLAY", "LEARNING", "FOUNDATION", "SERVICES", "HOMES", "COMMUNITY",
      "NURSING", "MONTESSORI", "COUNCIL", "COLLEGE", "CHURCH", "ASSOCIATION", "GROUP", "HOSPITAL", "CHARITY", "PROJECT", "TABLE", "TENNIS", "BOOTS", "THE", "OF", "AND" };

    /// <summary>True when a withheld text looks like a person: two to five words, each only letters, apostrophes, hyphens or full stops, none an organisation word
    /// (Ltd, Nursery, Club, Childminding...). A conservative heuristic used only to decide which withheld names the checklist insists never appear elsewhere;
    /// an organisation that was withheld may legitimately be published on other rows.</summary>
    public static bool LooksLikePerson(string name)
    {
        var tokens = Regex.Split(name.Trim().ToUpperInvariant(), @"\s+").Where(x => x.Length > 0).ToArray();
        if (tokens.Length < 2 || tokens.Length > 5) return false;
        foreach (var tk in tokens)
        {
            if (!Regex.IsMatch(tk, @"^[A-Z][A-Z'\-\.]*$") || OrgWords.Contains(tk)) return false;
        }
        return true;
    }

    /// <summary>Remembers the hash of every withheld name (never the name) so export can write export/&lt;slug&gt;/withheld_names.csv.</summary>
    public static void Record(string tag, List<string> names)
    {
        foreach (var n in names) WithheldBag.Add((tag, NameHash(n), LooksLikePerson(n)));
    }

    /// <summary>Empties the record (start of a council's export).</summary>
    public static void ClearWithheld() { while (WithheldBag.TryTake(out _)) { } }

    /// <summary>The withheld-name hashes recorded since the last clear, with how many rows, whether the text looks like a person, and which files: "Hash,Rows,PersonLike,Files".</summary>
    public static List<string> WithheldCsv()
    {
        var lines = new List<string> { "Hash,Rows,PersonLike,Files" };
        foreach (var g in WithheldBag.GroupBy(x => x.Hash).OrderBy(g => g.Key, StringComparer.Ordinal))
            lines.Add($"{g.Key},{g.Count()},{(g.First().Person ? 1 : 0)},{string.Join(" ", g.Select(x => x.Tag).Distinct().OrderBy(x => x, StringComparer.Ordinal))}");
        return lines;
    }

    /// <summary>Hash kept for a withheld name (upper case, single spaces).</summary>
    public static string NameHash(string name)
    {
        var norm = Regex.Replace(name.Trim().ToUpperInvariant(), @"\s+", " ");
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(norm)));
    }

    /// <summary>A copy of <paramref name="row"/> with every name-keyed cell that is not a marker replaced by <paramref name="marker"/>
    /// (the payee column <paramref name="payeeIdx"/> always counts as name-keyed), and any other cell that repeats a withheld name
    /// replaced as well. The withheld names are returned for hashing, never stored.</summary>
    public static string[] Withhold(string[] header, string[] row, int payeeIdx, string marker, List<string> withheld)
    {
        var copy = (string[])row.Clone();
        var names = new List<string>();
        for (int i = 0; i < copy.Length; i++)
        {
            var v = copy[i]?.Trim() ?? "";
            if (v.Length == 0 || IsMarkerCell(v)) continue;
            bool nameCol = i == payeeIdx || (i < header.Length && IsNameHeader(header[i]));
            if (!nameCol) continue;
            names.Add(v);
            copy[i] = marker;
        }
        if (names.Count == 0) return copy;
        for (int i = 0; i < copy.Length; i++)
        {
            var v = copy[i]?.Trim() ?? "";
            if (v.Length == 0 || IsMarkerCell(v)) continue;
            if (names.Any(n => n.Equals(v, StringComparison.OrdinalIgnoreCase))) copy[i] = marker;
        }
        withheld.AddRange(names);
        return copy;
    }

    /// <summary>One cell of a published row: the column it sits in (an OtherColumns entry is "OtherColumns:Header") and its text.</summary>
    public readonly record struct Cell(string Column, string Value);

    /// <summary>The cells of a published row, with the "Header=value|Header=value" OtherColumns text expanded.</summary>
    public static List<Cell> Cells(string[] header, string[] row)
    {
        var list = new List<Cell>();
        for (int i = 0; i < header.Length && i < row.Length; i++)
        {
            if (header[i] == "OtherColumns") { foreach (var (k, v) in SplitOther(row[i])) list.Add(new Cell("OtherColumns:" + k, v)); }
            else list.Add(new Cell(header[i], row[i]));
        }
        return list;
    }

    /// <summary>Splits "A=1|B=x|y" (a "|" with no "=" after it continues the previous value).</summary>
    public static List<(string key, string value)> SplitOther(string text)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(text)) return res;
        foreach (var part in text.Split('|'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) res.Add((part.Substring(0, eq), part.Substring(eq + 1)));
            else if (res.Count > 0) res[^1] = (res[^1].Item1, res[^1].Item2 + "|" + part);
            else res.Add(("", part));
        }
        return res;
    }

    static bool IsNameCell(Cell c)
    {
        string col = c.Column.StartsWith("OtherColumns:") ? c.Column.Substring(13) : c.Column;
        return c.Column == "SupplierName" || c.Column == "SupplierKey" || IsNameHeader(col);
    }

    /// <summary>C14 for one published row. Empty = fine. When a name-keyed cell is a marker (or, with <paramref name="anyColumn"/>, any cell) the row is
    /// redacted, and then every name-keyed cell (the payee column, the key derived from the name, OtherColumns entries with a name header) must itself be a
    /// marker or empty.</summary>
    public static List<string> Violations(string[] header, string[] row, bool anyColumn = false)
    {
        var found = new List<string>();
        var cells = Cells(header, row);
        Cell? marker = null;
        foreach (var c in cells) if (IsMarkerCell(c.Value) && (anyColumn || IsNameCell(c))) { marker = c; break; }
        if (marker is null) return found;
        foreach (var c in cells)
        {
            if (!IsNameCell(c) || string.IsNullOrWhiteSpace(c.Value) || IsMarkerCell(c.Value)) continue;
            found.Add($"redacted row (marker in {marker.Value.Column}) still shows a name-keyed value in {c.Column}");
        }
        return found;
    }
}

using System.Globalization;
using System.Text;
using static Showroom.Services.CouncilTerms;

namespace Showroom.Services;

/// <summary>One thing a visitor ticked: what the published numbers show, as plain facts, and which council to ask. Nothing in it is a cause,
/// an explanation or a question that suggests an answer.</summary>
public sealed class FoiItem
{
    public string Key = "", Slug = "", Section = "", Fact = "";
    public List<string> Lines = new();
}

/// <summary>
/// The visitor's request tray: the items ticked anywhere on the Council Spending pages, kept for as long as the page stays open (nothing is
/// stored, nothing is sent anywhere). One letter is built per council from the items that belong to it.
/// </summary>
public sealed class FoiTray
{
    public const int MaxItems = 300;       // the tray as a whole
    public const int MaxPerLetter = 100;   // a letter longer than this is not one a council can answer; the rest is said plainly on the page

    readonly List<FoiItem> _items = new();
    readonly HashSet<string> _keys = new();
    /// <summary>Anything in the tray changed (an item, or the visitor's details): the tray panel redraws.</summary>
    public event Action? Changed;
    /// <summary>An item was added or removed: the lists that show "Add to request" ticks redraw. Typing a name does not raise this.</summary>
    public event Action? ItemsChanged;

    public string Name = "", ReplyEmail = "";
    public bool Electronic = true;
    public int Version { get; private set; }
    public int Count => _items.Count;
    public IReadOnlyList<FoiItem> Items => _items;
    public bool Has(string key) => _keys.Contains(key);

    public bool Add(FoiItem it)
    {
        if (_keys.Contains(it.Key)) return true;
        if (_items.Count >= MaxItems) return false;
        _items.Add(it); _keys.Add(it.Key); Touch(); ItemsChanged?.Invoke();
        return true;
    }
    public void Remove(string key)
    {
        if (!_keys.Remove(key)) return;
        _items.RemoveAll(i => i.Key == key); Touch(); ItemsChanged?.Invoke();
    }
    public void ClearCouncil(string slug)
    {
        foreach (var i in _items.Where(i => i.Slug == slug)) _keys.Remove(i.Key);
        _items.RemoveAll(i => i.Slug == slug); Touch(); ItemsChanged?.Invoke();
    }
    /// <summary>Called when the name or reply address changes, so a built letter is rebuilt.</summary>
    public void Touch() { Version++; Changed?.Invoke(); }
}

/// <summary>Builds the sentences of a request item from the published figures, and the letter from the items.</summary>
public static class FoiFacts
{
    /// <summary>The one line every item ends with. It asks for the records and the reason, and offers neither an explanation nor an answer.</summary>
    public const string Request = "Please provide the records you hold for this item, and the reason for it.";

    static string MonthPhrase(string month) => month == "undated" ? "payments with no readable date" : "payments dated " + MonthName(month);
    // A transaction number is quoted only where the council's file really gives one: the scanner's row numbers and placeholders are never presented as the council's number (CouncilTerms.TxText).
    // A letter asks the council about ITS records, so it is identified by payee, date and amounts when the file has no number.
    static string Txn(string slug, string tx) => TxText(slug, tx);

    /// <summary>The heading a group's items sit under in the letter. Neutral wording: the schedule titles on the page describe what a rule looks for, a letter states only what the file shows.</summary>
    static string LetterSection(string schedule) => schedule switch
    {
        "A" => "Transactions published with a net amount and a gross amount",
        _ => ScheduleTitle(schedule),
    };

    /// <summary>A flagged group of one month, as the visitor sees it on the page.</summary>
    public static FoiItem FromGroup(string slug, string month, ExGroup g)
    {
        var it = new FoiItem { Slug = slug, Section = LetterSection(g.Schedule) };
        var who = g.Lines.Select(l => l.Supplier).Distinct().ToList();
        string supplier = who.Count == 0 ? "a payee" : who.Count == 1 ? who[0] : who[0] + " and " + (who.Count == 2 ? who[1] : $"{who.Count - 1} other payees");
        var tx = g.Lines.Select(l => Txn(slug, l.Tx)).Distinct().ToList();
        string txs = string.Join(", ", tx.Take(6)) + (tx.Count > 6 || (tx.Count > 1 && g.LineCount > g.Lines.Count) ? " and others" : "");
        it.Key = $"g|{slug}|{month}|{g.Schedule}|{g.Class}|{(g.Lines.Count > 0 ? g.Lines[0].Tx : "")}|{who.FirstOrDefault()}|{g.Value}";
        string when = MonthPhrase(month);
        if (g.Schedule == "A" && g.Lines.Count > 0)
        {
            var l = g.Lines[0];
            if (g.Class == "NSquaredListing")
                // facts only: the stated gross and the published rows' total (no class, reading or cause goes in a letter)
                it.Fact = $"In the {when}, transaction {Txn(slug, l.Tx)} ({l.Supplier}) is published as {NumLines(g.LineCount)} totalling {Gbp(l.Net)}, against a stated gross of {Gbp(l.Gross)}.";
            else
                // Facts the file shows directly and nothing computed from them: no difference, no VAT adjustment (the "difference" the scanner holds is not yet a figure a letter should quote)
                it.Fact = $"In the {when}, transaction {Txn(slug, l.Tx)} ({l.Supplier}) is published with a net of {Gbp(l.Net)} and a gross of {Gbp(l.Gross)}.";
            it.Lines.Add($"transaction {Txn(slug, l.Tx)}, {l.Supplier}, {when}, net {Gbp(l.Net)}, gross {Gbp(l.Gross)}");
        }
        else if (g.Schedule == "D")
        {
            it.Fact = $"In the {when}, transaction number {txs} is published against more than one payee or pay date: {NumLines(g.LineCount)}, {Gbp(g.Value)} in all.";
            AddLines(it, g, slug);
        }
        else
        {
            int size = MonthScan.GroupSizeOf(g.Detail);
            string amounts = string.Join(", ", g.Lines.Select(l => Gbp(l.Net)).Distinct().Take(4));
            string of = size > g.LineCount ? $" This month holds {Num(g.LineCount)} of the group's {Num(size)} lines." : "";
            if (g.ExplainedBy == "NSquaredRows" && g.Lines.Count > 0)
                // a payment run the file prints n times: state the stated gross and what each transaction's lines total (the group value is the stated gross, once)
                it.Fact = $"In the {when}, the published file lists {Plural(g.Lines.Select(l => l.Tx).Distinct().Count(), "transaction", "transactions")} ({txs}) for {supplier}, each with a stated gross of {Gbp(g.Lines[0].Gross)} and published lines totalling {Gbp(g.Lines[0].Net)}.";
            else
                it.Fact = $"In the {when}, the published file lists {NumLines(g.LineCount)} for {supplier} with the same amount ({amounts}), description and pay date, under transaction{(tx.Count == 1 ? "" : "s")} {txs}; {Gbp(g.Value)} in all.{of}";
            AddLines(it, g, slug);
        }
        return it;
    }

    static void AddLines(FoiItem it, ExGroup g, string slug)
    {
        foreach (var l in g.Lines) it.Lines.Add($"transaction {Txn(slug, l.Tx)}, {l.Supplier}, net {Gbp(l.Net)}, gross {Gbp(l.Gross)}");
        if (g.LineCount > g.Lines.Count) it.Lines.Add($"and {Plural(g.LineCount - g.Lines.Count, "more line", "more lines")} of the same group in that month");
    }

    /// <summary>The letter to one council, built only from the items ticked for it. <paramref name="omitted"/> is how many items did not fit.</summary>
    public static string Letter(string councilName, string? foiEmail, IReadOnlyList<FoiItem> items, string name, string replyEmail, bool electronic, DateTime today, out int omitted)
    {
        omitted = Math.Max(0, items.Count - FoiTray.MaxPerLetter);
        var sb = new StringBuilder();
        var me = string.IsNullOrWhiteSpace(name) ? "[Your name]" : name.Trim();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(me);
        sb.AppendLine(today.ToString("d MMMM yyyy", inv));
        sb.AppendLine();
        sb.AppendLine("Freedom of Information request");
        sb.AppendLine();
        sb.AppendLine($"To: {councilName}, Freedom of Information team");
        sb.AppendLine(string.IsNullOrWhiteSpace(foiEmail)
            ? "[Add the council's Freedom of Information address from its own website: it has not been verified here.]"
            : foiEmail.Trim());
        sb.AppendLine();
        sb.AppendLine("Dear Sir or Madam,");
        sb.AppendLine();
        sb.AppendLine("Under the Freedom of Information Act 2000, I request the information described below.");
        sb.AppendLine();
        sb.AppendLine($"Each item is a line, or a group of lines, in the payments data that {councilName} publishes under the Local Government Transparency Code. " +
                      "Each item states what the published numbers show and nothing more. For each item I am asking for the records you hold and the reason.");
        sb.AppendLine();
        int n = 0;
        foreach (var sec in items.Take(FoiTray.MaxPerLetter).GroupBy(i => i.Section))
        {
            sb.AppendLine(sec.Key + ":");
            sb.AppendLine();
            foreach (var it in sec)
            {
                n++;
                sb.AppendLine($"{n}. {it.Fact}");
                foreach (var l in it.Lines) sb.AppendLine($"   - {l}");
                sb.AppendLine($"   {Request}");
                sb.AppendLine();
            }
        }
        sb.AppendLine("I understand a response is due within 20 working days of receipt.");
        sb.AppendLine();
        sb.AppendLine(electronic
            ? (string.IsNullOrWhiteSpace(replyEmail) ? "I am happy to receive your response electronically." : $"I am happy to receive your response electronically, to {replyEmail.Trim()}.")
            : "Please send your response by post.");
        sb.AppendLine();
        sb.AppendLine("Yours faithfully,");
        sb.AppendLine();
        sb.AppendLine(me);
        if (!string.IsNullOrWhiteSpace(replyEmail)) sb.AppendLine(replyEmail.Trim());
        return sb.ToString();
    }
}

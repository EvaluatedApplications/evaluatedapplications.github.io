// The ONE place the dashboard's SQL is written. The page executes these strings and the "How this is built" panel prints these
// same strings (they are the same objects), and DashboardCode renders the console program from them. Nothing is retyped elsewhere.
namespace OrdersDemo;

public enum Stage { Summary = 1, Detail = 2 }

/// <summary>One statement the dashboard runs: what it feeds, when it runs, and the exact SQL text.</summary>
public sealed record DashQuery(string Id, string Title, Stage Stage, string Sql);

/// <summary>The visitor's current brushing. An empty set means "all".</summary>
public sealed class OrdersFilter
{
    public readonly HashSet<string> Regions = new(), Categories = new(), Statuses = new();
    public int MonthLo = -1, MonthHi = -1;

    public bool Any => Regions.Count + Categories.Count + Statuses.Count > 0 || MonthLo >= 0;

    public HashSet<string> Set(string dim) => dim switch { "region" => Regions, "category" => Categories, _ => Statuses };

    public void Toggle(string dim, string value)
    {
        var s = Set(dim);
        if (!s.Remove(value)) s.Add(value);
    }

    /// <summary>Tap one month, tap a second to extend to the span between them; tap the lone month again to clear.</summary>
    public void ClickMonth(int m)
    {
        if (MonthLo == m && MonthHi == m) { MonthLo = MonthHi = -1; return; }
        if (MonthLo >= 0 && MonthLo == MonthHi) { (MonthLo, MonthHi) = (Math.Min(MonthLo, m), Math.Max(MonthLo, m)); return; }
        MonthLo = MonthHi = m;
    }

    public void SetMonths(int lo, int hi) { MonthLo = lo; MonthHi = hi; }
    public void Clear() { Regions.Clear(); Categories.Clear(); Statuses.Clear(); MonthLo = MonthHi = -1; }
}

public static class DashboardSql
{
    public const string Schema =
        "CREATE TABLE orders (id INT PRIMARY KEY, customer INT, month INT, region TEXT, category TEXT, status TEXT, amount INT)";

    /// <summary>The Pleroma aggregate plane: four small-domain dimensions and one measure, 6+3+4+3 = 16 joint bits.</summary>
    public const string PlaneIndex =
        "CREATE INDEX by_slice ON orders (month, region, category, status) INCLUDE (amount) USING SUMMARY WITH (max_joint_bits = 16)";

    public static string Where(OrdersFilter f, string? skip = null)
    {
        var p = new List<string>();
        Add(p, "month", skip, MonthPredicate(f));
        Add(p, "region", skip, In("region", f.Regions));
        Add(p, "category", skip, In("category", f.Categories));
        Add(p, "status", skip, In("status", f.Statuses));
        return p.Count == 0 ? "" : " WHERE " + string.Join(" AND ", p);
    }

    /// <summary>Every statement for the current brushing. Each chart leaves its own dimension out of its WHERE, so a chart keeps
    /// showing the alternatives to what you picked in it (the usual cross-filter behaviour).</summary>
    public static IReadOnlyList<DashQuery> Plan(OrdersFilter f) => new[]
    {
        new DashQuery("tiles", "Totals", Stage.Summary,
            $"SELECT count(*), sum(amount) FROM orders{Where(f)}"),
        new DashQuery("month", "Orders and revenue by month", Stage.Summary,
            $"SELECT month, count(*), sum(amount) FROM orders{Where(f, "month")} GROUP BY month ORDER BY month"),
        new DashQuery("region", "By region", Stage.Summary,
            $"SELECT region, count(*), sum(amount) FROM orders{Where(f, "region")} GROUP BY region ORDER BY sum(amount) DESC"),
        new DashQuery("category", "By category", Stage.Summary,
            $"SELECT category, count(*), sum(amount) FROM orders{Where(f, "category")} GROUP BY category ORDER BY sum(amount) DESC"),
        new DashQuery("status", "By status", Stage.Summary,
            $"SELECT status, count(*), sum(amount) FROM orders{Where(f, "status")} GROUP BY status ORDER BY status"),
        new DashQuery("customers", "Top 10 customers by revenue", Stage.Detail,
            $"SELECT customer, count(*), sum(amount) FROM orders{Where(f)} GROUP BY customer ORDER BY sum(amount) DESC LIMIT 10"),
        new DashQuery("largest", "Ten largest orders", Stage.Detail,
            $"SELECT id, month, region, category, status, amount FROM orders{Where(f)} ORDER BY amount DESC, id LIMIT 10"),
    };

    static void Add(List<string> p, string dim, string? skip, string? clause)
    {
        if (clause is not null && skip != dim) p.Add(clause);
    }

    static string? MonthPredicate(OrdersFilter f) =>
        f.MonthLo < 0 ? null : f.MonthLo == f.MonthHi ? $"month = {f.MonthLo}" : $"month BETWEEN {f.MonthLo} AND {f.MonthHi}";

    static string? In(string col, HashSet<string> values)
    {
        if (values.Count == 0) return null;
        var quoted = values.OrderBy(v => v, StringComparer.Ordinal).Select(v => "'" + v.Replace("'", "''") + "'").ToArray();
        return quoted.Length == 1 ? $"{col} = {quoted[0]}" : $"{col} IN ({string.Join(", ", quoted)})";
    }
}

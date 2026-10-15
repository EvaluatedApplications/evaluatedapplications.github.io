// Generates web/data/diagrams/holodb-store.json by running the published HoloDb package.
// Every row, score, total, usage-meter reading and EXPLAIN line in the diagram comes from this run.
//   dotnet run -c Release -- ../../data/diagrams/holodb-store.json
using System.Reflection;
using System.Text.Json;
using HoloDb;

string outPath = args.Length > 0 ? args[0] : "holodb-store.json";
var version = typeof(Database).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

// ---- a small invoice ledger: 24 rows, four regions ----
var rows = new (int Id, string Customer, string Region, string Status, int Cents)[]
{
    (1,"Acme","north","paid",1200),(2,"Beta","south","paid",4500),(3,"Cyan","north","overdue",1150),(4,"Dune","east","paid",30000),
    (5,"Echo","north","pending",1300),(6,"Fern","west","paid",800),(7,"Gale","south","paid",4700),(8,"Holt","east","overdue",29000),
    (9,"Iris","west","paid",950),(10,"Juno","north","paid",2200),(11,"Kite","south","refunded",5200),(12,"Lark","east","paid",3100),
    (13,"Moss","west","pending",700),(14,"Nova","north","paid",1250),(15,"Onyx","south","paid",3900),(16,"Pine","east","paid",15000),
    (17,"Quill","west","paid",1100),(18,"Reed","north","failed",600),(19,"Sage","south","paid",4400),(20,"Tide","east","paid",2700),
    (21,"Umber","west","overdue",1600),(22,"Vale","north","paid",1180),(23,"Wren","south","pending",5000),(24,"Xeno","east","paid",9800),
};
var db = Database.Open();
db.Execute("CREATE TABLE invoices (id INT PRIMARY KEY, customer TEXT, region TEXT, status TEXT, amount_cents INT)");
db.Execute("INSERT INTO invoices VALUES " + string.Join(",", rows.Select(r => $"({r.Id},'{r.Customer}','{r.Region}','{r.Status}',{r.Cents})")));

object Cell(object? v) => v switch { null => "", long l => l, int i => i, double d => Math.Round(d, 4), _ => v.ToString() ?? "" };
List<Dictionary<string, object>> Explain(Database d, string sql) =>
    d.Execute("EXPLAIN " + sql).Rows.Select(r => r.ToDictionary(kv => kv.Key, kv => Cell(kv.Value))).ToList();

object Run(string key, string label, string sql)
{
    var r = db.Execute(sql);
    long scanned = Database.LastRowsScanned, tableRows = Database.LastTableRows, bytes = Database.LastLogicalBytes;
    var result = r.Rows.Select(row => row.ToDictionary(kv => kv.Key, kv => Cell(kv.Value))).ToList();
    return new { key, label, sql, columns = r.Columns, result, rowsScanned = scanned, tableRows, logicalBytes = bytes, explain = Explain(db, sql) };
}

var queries = new[]
{
    Run("key", "By key", "SELECT * FROM invoices WHERE id = 17"),
    Run("content", "By content", "SELECT id, region, amount_cents, score FROM invoices NEAREST (region = 'north', amount_cents = 1200) LIMIT 24"),
    Run("aggregate", "By aggregate", "SELECT region, COUNT(*), SUM(amount_cents) FROM invoices GROUP BY region"),
};

// ---- the optional plane: what it serves and what it declines, from EXPLAIN ----
var plane = Database.Open();
plane.Execute("CREATE TABLE sales (id INT PRIMARY KEY, region TEXT, tier INT, qty INT)");
plane.Execute("INSERT INTO sales (id, region, tier, qty) VALUES (1,'north',1,10),(2,'north',2,-4),(3,'south',1,7),(4,'east',3,5),(5,'south',2,9)");
plane.Execute("CREATE INDEX by_region ON sales (region, tier) INCLUDE (qty) USING SUMMARY");
plane.PleromaMode = PleromaMode.Always;
var planeQueries = new[]
{
    "SELECT SUM(qty) FROM sales WHERE region = 'north' OR tier = 3",
    "SELECT region, SUM(qty) FROM sales WHERE region = 'north' GROUP BY region",
    "SELECT COUNT(DISTINCT region) FROM sales WHERE tier = 1",
    "SELECT COUNT(*) FROM sales WHERE qty > 5",
    "SELECT AVG(qty) FROM sales WHERE region = 'south'",
    "SELECT id FROM sales WHERE id = 4",
};
var planeDoc = planeQueries.Select(sql =>
{
    var ex = Explain(plane, sql);
    bool served = ex.Any(e => (string)e["route"] is string s && s.StartsWith("Pleroma plane"));
    return new { sql, served, explain = ex };
}).ToList();
var indexes = plane.Execute("SHOW INDEXES ON sales").Rows.Select(r => r.ToDictionary(kv => kv.Key, kv => Cell(kv.Value))).ToList();

var doc = new
{
    schema = "ea-diagram/1", kind = "holodb-store", generator = "web/generators/holodb-demo",
    package = new { id = "EvaluatedApplications.HoloDb", version },
    table = new
    {
        name = "invoices",
        columns = new[] { "id", "customer", "region", "status", "amount_cents" },
        rows = rows.Select(r => new object[] { r.Id, r.Customer, r.Region, r.Status, r.Cents }).ToArray(),
    },
    queries,
    plane = new { setup = "CREATE INDEX by_region ON sales (region, tier) INCLUDE (qty) USING SUMMARY", queries = planeDoc, indexes },
};
File.WriteAllText(outPath, JsonSerializer.Serialize(doc), new System.Text.UTF8Encoding(false));
Console.WriteLine($"HoloDb {version}: wrote {outPath}; plane served {planeDoc.Count(p => p.served)} of {planeDoc.Count}");
foreach (var p in planeDoc) Console.WriteLine((p.served ? "served   " : "declined ") + p.sql + "  ->  " + string.Join(" / ", p.explain.Select(e => e["kind"] + ":" + e["route"])));

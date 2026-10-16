using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, region TEXT, total INT)");

// one atomic batch: readers see the table before it or after it, never part of it
var r = db.ApplyChanges("orders", new[]
{
    RowChange.Upsert(new Dictionary<string, object?> { ["id"] = 1L, ["region"] = "north", ["total"] = 40L }),
    RowChange.Upsert(new Dictionary<string, object?> { ["id"] = 2L, ["region"] = "south", ["total"] = 15L }),
    RowChange.Upsert(new Dictionary<string, object?> { ["id"] = 1L, ["region"] = "north", ["total"] = 45L }),
    RowChange.Delete(2L),
    RowChange.Delete(99L),
});
Console.WriteLine($"{r.Inserted} inserted, {r.Updated} updated, {r.Deleted} deleted, {r.MissingDeletes} missing");

// the SQL form changes only the columns in the SET list and keeps the rest
db.Execute("INSERT INTO orders VALUES (1, 'west', 50), (3, 'east', 7) ON CONFLICT (id) DO UPDATE SET total = EXCLUDED.total");
foreach (var row in db.Execute("SELECT id, region, total FROM orders ORDER BY id").Rows)
    Console.WriteLine($"{row["id"]} {row["region"]} {row["total"]}");

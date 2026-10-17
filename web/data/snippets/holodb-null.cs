using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, region TEXT NULL, amount INT NULL, qty INT NOT NULL)");
db.Execute("INSERT INTO orders VALUES (1, 'north', 10, 1), (2, NULL, 20, 1), (3, 'north', NULL, 2)");
var r = db.Execute("SELECT COUNT(*), COUNT(amount), SUM(amount), AVG(amount) FROM orders").Rows[0];
Console.WriteLine($"{r["count(*)"]} rows, {r["count(amount)"]} amounts, sum {r["sum(amount)"]}, avg {r["avg(amount)"]}");
// 3 rows, 2 amounts, sum 30, avg 15
Console.WriteLine(db.Execute("SELECT COUNT(*) FROM orders WHERE region IS NULL").Rows[0]["count(*)"]);
// 1
Console.WriteLine(db.Execute("SELECT COUNT(*) FROM orders WHERE region != 'north'").Rows[0]["count(*)"]);
// 0   (the NULL region is neither equal nor different)
foreach (var g in db.Execute("SELECT region, COUNT(*) FROM orders GROUP BY region ORDER BY region").Rows)
    Console.WriteLine($"{(g["region"] is DBNull ? "(null)" : g["region"])}: {g["count(*)"]}");
// (null): 1
// north: 2

// A change feed carries NULLs as C# nulls; BulkLoad takes a mask per column (true = NULL) and a left-out nullable column is NULL.
db.ApplyChanges("orders", new[] { RowChange.Upsert(new Dictionary<string, object?> { ["id"] = 4L, ["region"] = null, ["amount"] = 5L, ["qty"] = 1L }) });
db.BulkLoad("orders", new[] { "id", "amount", "qty" }, new Array[] { new long[] { 5, 6 }, new long[] { 0, 7 }, new long[] { 1, 1 } }, 2,
    new bool[]?[] { null, new[] { true, false }, null });
Console.WriteLine(db.Execute("SELECT COUNT(*) FROM orders WHERE amount IS NULL").Rows[0]["count(*)"]);
// 2   (id 3 and id 5)

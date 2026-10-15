using HoloDb;

var db = Database.Open();
db.Execute("CREATE TABLE sales (id INT PRIMARY KEY, region TEXT, tier INT, qty INT)");
db.Execute("INSERT INTO sales (id, region, tier, qty) VALUES (1,'north',1,10),(2,'north',2,-4),(3,'south',1,7),(4,'east',3,5),(5,'south',2,9)");

// build the plane from SQL: key columns are dimensions, INCLUDE columns are measures
db.Execute("CREATE INDEX by_region ON sales (region, tier) INCLUDE (qty) USING SUMMARY");
db.PleromaMode = PleromaMode.Always;

Console.WriteLine(db.Execute("SELECT SUM(qty) FROM sales WHERE region = 'north' OR tier = 3").Rows[0]["sum(qty)"]);

// why was it served, or declined?
foreach (var r in db.Execute("EXPLAIN SELECT region, SUM(qty) FROM sales WHERE region = 'north' GROUP BY region").Rows)
    Console.WriteLine($"{r["kind"]}: {r["route"]}");
foreach (var r in db.Execute("EXPLAIN SELECT COUNT(*) FROM sales WHERE qty > 5").Rows)
    Console.WriteLine($"{r["kind"]}: {r["route"]}");

Console.WriteLine(db.PleromaVerify("sales") ?? "plane equals a rebuild from the columns");
Console.WriteLine(db.PleromaSumExact("SELECT SUM(qty) FROM sales"));
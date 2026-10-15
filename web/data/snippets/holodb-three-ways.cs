using HoloDb;

var db = Database.Open();   // null path: in memory
db.Execute("CREATE TABLE invoices (id INT PRIMARY KEY, customer TEXT, region TEXT, amount_cents INT)");
db.Execute("INSERT INTO invoices VALUES (1,'Acme','north',1200),(2,'Beta','south',4500),(3,'Cyan','north',1150),(4,'Dune','east',30000),(5,'Echo','north',1300)");

// 1. By key: ordinary SQL, answered from the primary-key index
Console.WriteLine(db.Execute("SELECT customer FROM invoices WHERE id = 3").Rows[0]["customer"]);

// 2. By content: rank rows by resemblance to a partial description; add score to see the ranking
foreach (var r in db.Execute("SELECT id, region, amount_cents FROM invoices NEAREST (region = 'north', amount_cents = 1200) LIMIT 3").Rows)
    Console.WriteLine($"{r["id"]}: {r["region"]} {r["amount_cents"]}");

// 3. By aggregate: read from maintained totals, no scan
foreach (var r in db.Execute("SELECT region, SUM(amount_cents) FROM invoices GROUP BY region ORDER BY region").Rows)
    Console.WriteLine($"{r["region"]}: {r["sum(amount_cents)"]}");
Console.WriteLine($"rows scanned by the aggregate: {Database.LastRowsScanned}");
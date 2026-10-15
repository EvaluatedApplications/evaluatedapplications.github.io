using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE sales (id INT PRIMARY KEY, region TEXT, amount REAL)");

// Typed column arrays skip the SQL parser: the fast path for large loads.
var ids = new long[] { 1, 2, 3 };
var regions = new string[] { "EU", "US", "EU" };
var amounts = new double[] { 42.50, 17.00, 61.10 };
db.BulkLoad("sales", new[] { "id", "region", "amount" }, new Array[] { ids, regions, amounts }, count: 3);

var r = db.Execute("SELECT region, COUNT(*), SUM(amount) FROM sales GROUP BY region ORDER BY region");
foreach (var row in r.Rows) Console.WriteLine($"{row["region"]}: {row["count(*)"]} rows, {row["sum(amount)"]}");
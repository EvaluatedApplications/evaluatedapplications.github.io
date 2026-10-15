using HoloDb;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHoloDb(options => { options.WalPath = null; });   // singleton HoloDbService

using var provider = services.BuildServiceProvider();
var db = provider.GetRequiredService<HoloDbService>();
db.Execute("CREATE TABLE sales (id INT PRIMARY KEY, region TEXT, amount INT)");
db.Execute("INSERT INTO sales VALUES (1, 'EU', 10), (2, 'US', 5), (3, 'EU', 7)");
var totals = await db.ExecuteAsync("SELECT region, SUM(amount) FROM sales GROUP BY region ORDER BY region");
foreach (var r in totals.Rows) Console.WriteLine($"{r["region"]}: {r["sum(amount)"]}");
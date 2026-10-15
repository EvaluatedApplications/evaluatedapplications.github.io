using HoloDb;

var dir = Path.Combine(Path.GetTempPath(), "holodb-paged-" + Guid.NewGuid().ToString("N"));
try
{
    var db = new HoloDbService(new HoloDbOptions());
    // a durable, larger-than-memory table backed by a directory
    db.CreatePaged("CREATE TABLE events (id INT PRIMARY KEY, kind TEXT, value INT)", dir);
    var rows = Enumerable.Range(1, 1000).Select(i => (object[])new object[] { (long)i, i % 2 == 0 ? "even" : "odd", (long)i });
    db.PagedBulkLoad("events", rows);
    foreach (var r in db.Execute("SELECT kind, COUNT(*), SUM(value) FROM events GROUP BY kind ORDER BY kind").Rows)
        Console.WriteLine($"{r["kind"]}: {r["count(*)"]} rows, sum {r["sum(value)"]}");
    db.Database.ClosePaged("events");

    var again = new HoloDbService(new HoloDbOptions());
    again.OpenPaged(dir);                                               // reopen it later
    Console.WriteLine(again.Execute("SELECT COUNT(*) FROM events").Rows[0]["count(*)"]);
    again.Database.ClosePaged("events");
}
finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
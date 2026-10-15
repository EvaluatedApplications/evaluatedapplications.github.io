using HoloDb;

var wal = Path.Combine(Path.GetTempPath(), "holodb-docs-" + Guid.NewGuid().ToString("N") + ".wal");
try
{
    var db = new HoloDbService(new HoloDbOptions { WalPath = wal });   // writes are durable
    db.Execute("CREATE TABLE notes (id INT PRIMARY KEY, body TEXT)");
    db.Execute("INSERT INTO notes VALUES (1, 'first'), (2, 'second')");
    db.Checkpoint();                                                    // fold the log into a snapshot
    db.Execute("INSERT INTO notes VALUES (3, 'after the checkpoint')");
    db.Database.Close();

    var reopened = new HoloDbService(new HoloDbOptions { WalPath = wal });   // snapshot plus the short tail
    Console.WriteLine(reopened.Execute("SELECT COUNT(*) FROM notes").Rows[0]["count(*)"]);
    reopened.Database.Close();
}
finally
{
    foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(wal) + "*")) File.Delete(f);
}
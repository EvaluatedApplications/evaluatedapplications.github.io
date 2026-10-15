using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE accounts (id INT PRIMARY KEY, balance INT)");
db.Execute("INSERT INTO accounts VALUES (1, 500), (2, 100)");

db.Execute("BEGIN");
try
{
    db.Execute("UPDATE accounts SET balance = 400 WHERE id = 1");
    db.Execute("UPDATE accounts SET balance = 200 WHERE id = 2");
    db.Execute("COMMIT");
}
catch
{
    db.Execute("ROLLBACK");
    throw;
}
Console.WriteLine("after commit: " + string.Join(", ", db.Execute("SELECT balance FROM accounts ORDER BY id").Rows.Select(r => r["balance"])));

db.Execute("BEGIN");
db.Execute("UPDATE accounts SET balance = 0 WHERE id = 1");
db.Execute("ROLLBACK");
Console.WriteLine("after rollback: " + string.Join(", ", db.Execute("SELECT balance FROM accounts ORDER BY id").Rows.Select(r => r["balance"])));
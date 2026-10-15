using HoloDb;

var db = new HoloDbService(new HoloDbOptions());           // in memory
db.Execute("CREATE TABLE users (id INT PRIMARY KEY, age INT, name TEXT)");
db.Execute("INSERT INTO users VALUES (1, 30, 'Ada'), (2, 24, 'Linus'), (3, 15, 'Kid')");

var result = db.Execute("SELECT name FROM users WHERE age >= 18 ORDER BY age DESC");
foreach (var row in result.Rows)
    Console.WriteLine(row["name"]);

var count = await db.ExecuteAsync("SELECT COUNT(*) FROM users");
Console.WriteLine(count.Rows[0]["count(*)"]);
using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE users (id INT PRIMARY KEY, age INT, name TEXT)");
db.Execute("INSERT INTO users VALUES (1, 30, 'Ada')");

var result = await db.ExecuteAsync("SELECT name FROM users WHERE age >= 18");
foreach (var row in result.Rows) Console.WriteLine(row["name"]);
// Ada

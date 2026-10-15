using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE people (id INT PRIMARY KEY, name TEXT, age INT)");
db.Execute("INSERT INTO people VALUES (?, ?, ?)", 1, "O'Neil", 41);   // quoted and escaped for you
db.Execute("INSERT INTO people VALUES (?, ?, ?)", 2, "Ada", 36);

var r = db.Execute("SELECT name FROM people WHERE age > ? ORDER BY name", 30);
foreach (var row in r.Rows) Console.WriteLine(row["name"]);
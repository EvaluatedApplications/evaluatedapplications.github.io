using HoloDb;

var db = new HoloDbService(new HoloDbOptions());
db.Execute("CREATE TABLE products (id INT PRIMARY KEY, category TEXT, price INT)");
db.Execute("INSERT INTO products VALUES (1,'electronics',100),(2,'electronics',120),(3,'furniture',300)");

var near = db.Execute("SELECT id, category, price, score FROM products NEAREST (category = 'electronics', price = 118) LIMIT 2");
foreach (var row in near.Rows) Console.WriteLine($"id={row["id"]} category={row["category"]} price={row["price"]} score={row["score"]:F3}");
// id=2 category=electronics price=120 score=0.993
// id=1 category=electronics price=100 score=0.988

using HoloDb;

// Every SQL example shown in the HoloDb docs, run in order against the published package.
// The docs check that each example's text appears in this file.
var db = new HoloDbService(new HoloDbOptions());

void Run(string sql)
{
    var r = db.Execute(sql);
    var head = sql.Split('\n')[0];
    Console.WriteLine($"ok  {head}{(sql.Contains('\n') ? " ..." : "")}  ({r.RowCount} rows)");
}

Run(@"CREATE TABLE sales (
  id     INT PRIMARY KEY,
  region TEXT,
  amount REAL
)");
Run("INSERT INTO sales VALUES (1, 'EU', 42.50)");
Run("INSERT INTO sales (id, region, amount) VALUES (2, 'US', 17.00), (3, 'EU', 61.10)");
Run("UPDATE sales SET amount = 0 WHERE region = 'US'");
Run("DELETE FROM sales WHERE amount = 0");
Run("INSERT INTO sales VALUES (4, 'US', 120.00), (5, 'US', 15.00), (6, 'EU', 500.00)");
Run(@"SELECT region, COUNT(*), SUM(amount), AVG(amount)
FROM sales
WHERE amount BETWEEN 10 AND 1000
GROUP BY region
HAVING SUM(amount) > 50
ORDER BY sum(amount) DESC
LIMIT 10");
Run("CREATE TABLE customers (id INT PRIMARY KEY, name TEXT)");
Run("INSERT INTO customers VALUES (1, 'Ada'), (2, 'Linus')");
Run("CREATE TABLE orders (id INT PRIMARY KEY, customer_id INT, total INT)");
Run("INSERT INTO orders VALUES (10, 1, 250), (11, 2, 90), (12, 1, 130)");
Run(@"SELECT orders.id, customers.name, orders.total
FROM orders
INNER JOIN customers ON orders.customer_id = customers.id
WHERE orders.total > 100
ORDER BY orders.total DESC");
Run("CREATE INDEX ON sales (region)");
Run("CREATE TABLE articles (id INT PRIMARY KEY, title TEXT)");
Run("INSERT INTO articles VALUES (1, 'holographic database'), (2, 'cooking pasta'), (3, 'a holographic store')");
Run(@"SELECT id, title
FROM articles
NEAREST (title = 'holographic database')
LIMIT 5");
Run("ALTER TABLE sales ADD COLUMN note TEXT DEFAULT ''");
Run("SELECT region, COUNT(*) FROM sales GROUP BY region");
Run("SELECT COUNT(DISTINCT region) FROM sales");

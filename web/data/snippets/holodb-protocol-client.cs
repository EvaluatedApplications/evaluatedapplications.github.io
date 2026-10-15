using HoloDb.Client;

// Reading a columnar result without row unpacking (compile-checked here; it needs a server to run)
await using var client = await HoloDbClient.ConnectAsync("localhost", 5433, new HoloDbClientOptions { AuthToken = "mytoken" });
var result = await client.ExecuteAsync("SELECT id, price FROM prices");

if (result.IsColumnar)
{
    // column 0 is a long, column 1 is a double
    for (int r = 0; r < result.RowCount; r++)
        Console.WriteLine($"ID {result.GetLong(r, 0)}: price {result.GetDouble(r, 1)}");
}
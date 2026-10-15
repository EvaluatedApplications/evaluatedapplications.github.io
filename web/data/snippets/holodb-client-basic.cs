using HoloDb.Client;

// Connect to a server (compile-checked here: running it needs a HoloDb server)
var client = await HoloDbClient.ConnectAsync("db.example.com", 5433);
try
{
    // The same surface as the in-process HoloDbService
    var result = await client.ExecuteAsync("SELECT id, value FROM data LIMIT 10");
    foreach (var row in result.Rows)
        Console.WriteLine($"id={row["id"]}, value={row["value"]}");
}
finally
{
    await client.DisposeAsync();
}
using HoloDb.Client;

// Wraps the client with exponential backoff and a circuit breaker (compile-checked)
var resilient = await ResilientHoloDbClient.ConnectAsync(
    "db.example.com", 5433,
    retryDelay: TimeSpan.FromMilliseconds(200),
    maxAttempts: 3,
    breakerFailureThreshold: 5);

// Retries transport failures; SQL and authentication errors are not retried
var result = await resilient.ExecuteAsync("SELECT COUNT(*) FROM data");
Console.WriteLine(result.RowCount);
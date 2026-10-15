using HoloDb.Client;

// Pin the server certificate by thumbprint, for a self-hosted server (compile-checked)
var options = new HoloDbClientOptions
{
    AuthToken = "your-token",
    UseTls = true,
    PinnedThumbprint = "ABC123DEF456",
};
var client = await HoloDbClient.ConnectAsync("localhost", 5433, options);
await client.DisposeAsync();
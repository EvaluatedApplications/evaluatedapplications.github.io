using Blazier;
using HelloApp;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.UseBlazier(b => b.App<App>());
await builder.Build().RunAsync();

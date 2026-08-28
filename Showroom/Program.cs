using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HoloKernel;
using Showroom;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// One instance for the lifetime of the page load (a WASM singleton IS the page load), so navigating
// between Creature/Forecaster/Prism reuses whichever of their (structurally incompatible) models is
// already loaded instead of rebuilding/re-downloading it on every visit. See HoloKernel/SessionHost.cs.
builder.Services.AddSingleton<SessionHost>();

await builder.Build().RunAsync();

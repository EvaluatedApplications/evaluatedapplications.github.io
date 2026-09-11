using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HoloKernel;
using Showroom;
using Showroom.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// One instance for the lifetime of the page load (a WASM singleton IS the page load), so navigating
// between Creature/Forecaster/Prism reuses whichever of their (structurally incompatible) models is
// already loaded instead of rebuilding/re-downloading it on every visit. See HoloKernel/SessionHost.cs.
builder.Services.AddSingleton<SessionHost>();

// Content-DB spike (see Services/ContentDbHost.cs, Pages/ContentDbSpike.razor). Registering this
// costs nothing at boot -- the constructor does no I/O -- the actual fetch/open pipeline only runs
// once something calls GetOrLoadAsync, which today is only the spike page itself.
builder.Services.AddSingleton<ContentDbHost>();

await builder.Build().RunAsync();

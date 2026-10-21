using Blazier;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HoloKernel;
using Microsoft.JSInterop;
using Showroom;
using Showroom.Services;
using Showroom.Services.PrismTab;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Blazier: the app is NOT mounted at startup. Each published page is a static HTML shell (see [BlazierPage] on the
// pages), and wwwroot/index.html's loader script boots the runtime on intent and mounts the routed app, "app",
// over the shell. Once mounted it is the same single-page app as before: the Router, MainLayout and every page are
// unchanged, and in-app navigation keeps the one SessionHost below. HeadOutlet stays a normal root component.
builder.UseBlazier(b => b.App<App>());
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// One instance for the lifetime of the page load (a WASM singleton IS the page load), so the loaded Prism
// checkpoint (the Prism tool's five tabs, and the Analyst's novelty scan) is reused instead of re-downloaded
// on every visit. See HoloKernel/SessionHost.cs.
// A HARD navigation (typed URL, reload, a link followed before the app has started) loads another static
// shell in a new page load, so this instance and the models in it are gone; only the browser's HTTP cache remains.
builder.Services.AddSingleton<SessionHost>();

// The Prism tool's one checkpoint load (vocabulary, sidecars, boot log) and its one-trainer-at-a-time lock. Fetches nothing until a tab asks.
builder.Services.AddScoped<PrismLoader>();

// Content-DB spike (see Services/ContentDbHost.cs, Pages/ContentDbSpike.razor). Registering this
// costs nothing at boot -- the constructor does no I/O -- the actual fetch/open pipeline only runs
// once something calls GetOrLoadAsync, which today is only the spike page itself.
builder.Services.AddSingleton<ContentDbHost>();

// Council Spending Scanner data (phone-sized files under website-data/council-web); caches small tables for the page load. See Services/CouncilWebData.cs.
builder.Services.AddScoped<CouncilWebData>();
// The visitor's records-request tray (items ticked on the council pages). Lives for the page load; nothing is stored or sent.
builder.Services.AddScoped<FoiTray>();

// The Plenum's clock: a service so a proof can drive time by hand (Services/Plenum/PlenumClock.cs).
builder.Services.AddSingleton<ShowroomPlenum.IPlenumClock, ShowroomPlenum.StopwatchPlenumClock>();
// The Plenum's message relay (MQTT over a secure WebSocket). It connects only when a visitor opens the Duel lobby; the address is
// window.EA_PLENUM_RELAY in wwwroot/index.html.
builder.Services.AddSingleton<ShowroomPlenum.IPlenumRelayFactory, ShowroomPlenum.MqttWsRelayFactory>();

// Big data (council data, Prism checkpoint) lives in the website-data repo. ONE setting says where: window.EA_DATA_BASE in
// wwwroot/index.html (the same value drives the framework redirect there). See Services/DataUrl.cs.
var host = builder.Build();
DataUrl.Configure(await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<string?>("eval", "window.EA_DATA_BASE"));
ShowroomPlenum.PlenumRelayConfig.Configure(await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<string?>("eval", "window.EA_PLENUM_RELAY"));
await host.RunAsync();

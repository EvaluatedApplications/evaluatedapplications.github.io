using Blazier;
using HoloKernel;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Services.PrismTab;

namespace Showroom.Services;

/// <summary>
/// The services the tool pages inject, for Blazier's build-time render (publish writes each tool's static shell with
/// HtmlRenderer). Nothing here does I/O: every constructor only stores its arguments, and the pages return before they
/// fetch when <see cref="IBuildTime.IsPrerender"/> is true. Blazier already supplies IBuildTime, NavigationManager, a
/// throwing IJSRuntime and an HttpClient that refuses to send, so a page that fetches by mistake fails the publish (BLZ102)
/// instead of shipping an error message as HTML.
/// </summary>
public sealed class ShowroomPrerender : IBlazierPrerenderSetup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SessionHost>();
        services.AddScoped<PrismLoader>();
        services.AddScoped<CouncilWebData>();
        services.AddScoped<FoiTray>();
        services.AddSingleton<ShowroomPlenum.IPlenumClock, ShowroomPlenum.StopwatchPlenumClock>();
        services.AddSingleton<ShowroomPlenum.IPlenumRelayFactory, ShowroomPlenum.MqttWsRelayFactory>();    // never asked to connect at build time
    }
}

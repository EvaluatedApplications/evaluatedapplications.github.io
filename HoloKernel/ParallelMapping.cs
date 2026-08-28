using PrismFormer;

namespace HoloKernel;

/// <summary>
/// AlgFormer exposes its own fan-out seam: <c>HoloFormer.Map</c> is a settable
/// <c>IParallelMap</c> with a single method,
/// <c>void Map(int chunks, int minForParallel, Action&lt;int&gt; body)</c>.
///
/// That is the designed-for integration point for a resource-gated pipeline runtime — the model's
/// internal fan-out can be routed through EvalApp rather than EvalApp being bolted on around the
/// outside of it. Landing that implementation is a follow-up, deliberately not guessed at here:
/// nothing in this project should be written against an unverified API surface.
///
/// HONEST CONSTRAINT: in the browser today the .NET runtime is single-threaded (real WASM threads
/// need SharedArrayBuffer, which needs COOP/COEP response headers, which static GitHub Pages hosting
/// cannot send). So a browser-side map is cooperative, not parallel — the wins available are
/// structure, backpressure, cancellation and keeping the UI thread responsive, NOT throughput.
/// Anything claiming otherwise in browser context would be wrong.
/// </summary>
public static class ParallelMapping
{
    /// <summary>
    /// Run every chunk inline on the calling thread. The correct default in a browser tab, and it
    /// makes the single-threaded reality explicit instead of implied.
    /// </summary>
    public static IParallelMap Inline { get; } = new InlineMap();

    /// <summary>The library's own sequential implementation.</summary>
    public static IParallelMap Sequential { get; } = new SequentialMap();

    public static HoloSession UseInline(this HoloSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Model.Map = Inline;
        return session;
    }

    public static HoloSession Use(this HoloSession session, IParallelMap map)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(map);
        session.Model.Map = map;
        return session;
    }

    private sealed class InlineMap : IParallelMap
    {
        public void Map(int chunks, int minForParallel, Action<int> body)
        {
            ArgumentNullException.ThrowIfNull(body);
            for (var i = 0; i < chunks; i++) body(i);
        }
    }
}

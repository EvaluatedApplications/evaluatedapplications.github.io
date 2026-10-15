using PrismFormer;

namespace HoloKernel;

/// <summary>
/// AlgFormer exposes a fan-out seam: <c>HoloFormer.Map</c> is a settable <c>IParallelMap</c> with a
/// single SYNCHRONOUS method, <c>void Map(int chunks, int minForParallel, Action&lt;int&gt; body)</c>.
///
/// It looked like the natural place to plug a resource-gated pipeline runtime in. Measurement says
/// otherwise, on two independent grounds. Recording both here so nobody re-derives them the hard way:
///
/// 1. DEADLOCK HAZARD (do not route EvalApp through this seam as it currently stands).
///    The synchronous contract forces any async implementation to bridge with
///    <c>...GetAwaiter().GetResult()</c>. On desktop a real thread pool hides that. On genuinely
///    single-threaded WASM — which is our actual deployment target, because static GitHub Pages
///    cannot send the COOP/COEP headers that .NET's multithreaded WASM requires — it is a classic
///    sync-over-async deadlock: the one and only thread blocks waiting on a continuation that can
///    only run on that same blocked thread. The symptom is a silently frozen tab, not an exception.
///    Fixing this means making the interface async-native, which is AlgFormer's call, not ours.
///
/// 2. THERE IS NOTHING TO PARALLELISE HERE ANYWAY (measured, AlgFormer 1.5.0).
///    Instrumenting the seam with a spy across every shape this site runs — Forecaster d=128,
///    Creature d=384, Prism d=1536, and Layers 1/2/4, via both <c>LogitsFor</c> and
///    <c>TrainEpoch(parallelism: 4)</c> — the reported <c>chunks</c> was **always 1** while
///    <c>minForParallel</c> was always 2. Across 112 calls on the batch path, not one reached the
///    threshold. The fan-out is width-1 at our shapes, so even a perfect, deadlock-free
///    implementation would gate a single item and buy nothing. (Scoped claim: that is what was
///    measured on these CPU paths at these shapes — other paths or much larger batches may differ.)
///
/// CONCLUSION: EvalApp's real role in a browser host is the OUTER loop — cooperative yielding,
/// progress and cancellation around a long training or generation run so the tab stays responsive
/// and a run can be watched live — pinned at concurrency 1. Not this inner seam. EvalApp's engine
/// itself is structurally fine on WASM; it is this specific synchronous bridge that is not.
/// </summary>
public static class ParallelMapping
{
    /// <summary>
    /// Run every chunk inline on the calling thread. Never blocks, never queues, so it cannot
    /// deadlock a single-threaded host — the correct choice in a browser tab.
    /// </summary>
    public static IParallelMap Inline { get; } = new InlineMap();

    /// <summary>
    /// The library's own sequential implementation, and the DEFAULT on a fresh <c>HoloFormer</c>
    /// (verified: <c>new HoloFormer(...).Map</c> is <c>PrismFormer.SequentialMap</c>). So the
    /// existing tools are safe as they stand — the hazard is strictly opt-in, reached only by
    /// explicitly assigning a blocking map.
    /// </summary>
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

    /// <summary>
    /// Refuse a map that is known to block, so a browser host fails loudly at wire-up instead of
    /// hanging silently later.
    ///
    /// Honest about what this is: a known-bad list, not a proof of safety. It catches the one
    /// concrete trap that exists today — <c>PrismEval.Cpu</c>, whose implementation bridges async
    /// work with a blocking wait — because that is the ready-made map a port would naturally reach
    /// for. It cannot detect an arbitrary future blocking implementation.
    /// </summary>
    public static void EnsureBrowserSafe(HoloSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var map = session.Model.Map;
        if (map is null) return;

        var name = map.GetType().FullName ?? string.Empty;
        if (name.Contains("EvalMap", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'{name}' bridges async work with a blocking wait. On single-threaded WASM that is a " +
                "sync-over-async deadlock (a frozen tab, with no exception to catch). Use " +
                "ParallelMapping.Inline in a browser host.");
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

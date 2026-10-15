using System.Diagnostics;

namespace Showroom.Services;

/// <summary>
/// Cooperative time-slicing for a single-threaded host (Blazor WebAssembly runs everything, rendering
/// included, on the one browser thread). Long work is written as a loop that asks
/// <see cref="YieldIfDueAsync"/> every few rows; once <see cref="SliceMs"/> milliseconds have been spent since
/// the last yield it hands the thread back to the browser (so the page can paint and answer taps), checks the
/// cancellation token, then carries on. Nothing here needs threads.
///
/// Why <c>Task.Delay(1)</c> and not <c>Task.Yield()</c>: in WebAssembly a timer turn is a real browser task, so
/// pending input and rendering run before the continuation; a bare <c>Yield</c> can resume ahead of them. The page
/// already used Delay(1) for its "Loading..." text for the same reason.
///
/// <see cref="MaxSliceMs"/> records the longest stretch of work between two yields, which is the number that
/// matters for "does the page stay responsive": it is what the desktop harness reports.
/// </summary>
public sealed class Cooperative
{
    readonly Stopwatch _sw = Stopwatch.StartNew();
    readonly Func<Task> _yield;

    public CancellationToken Ct { get; }

    /// <summary>Work budget between yields, in milliseconds. 12 keeps the worst input delay under one 60 Hz frame
    /// plus the timer turn on a fast phone; a slower phone simply gets fewer rows per slice.</summary>
    public int SliceMs { get; }

    public int Yields { get; private set; }
    public double MaxSliceMs { get; private set; }

    /// <summary>While false, a yield does not check the cancellation token. Used for the stretch that writes one file's
    /// rows into the database: stopping half way would leave half a file in the table, so a cancel waits for the file.</summary>
    public bool Cancellable { get; set; } = true;

    public Cooperative(CancellationToken ct, int sliceMs = 12, Func<Task>? yielder = null)
    {
        Ct = ct; SliceMs = sliceMs;
        _yield = yielder ?? (() => Task.Delay(1));
    }

    /// <summary>True once the current slice has used up its budget.</summary>
    public bool Due => _sw.ElapsedMilliseconds >= SliceMs;

    /// <summary>Yield to the browser if this slice is spent; otherwise return immediately. Cheap to call often
    /// (one stopwatch read), but call it every few hundred rows, not every row.</summary>
    public ValueTask YieldIfDueAsync()
    {
        if (!Due) { if (Cancellable) Ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        return new ValueTask(YieldAsync());
    }

    public async Task YieldAsync()
    {
        if (Cancellable) Ct.ThrowIfCancellationRequested();
        double slice = _sw.Elapsed.TotalMilliseconds;
        if (slice > MaxSliceMs) MaxSliceMs = slice;
        await _yield();
        Yields++;
        _sw.Restart();
        if (Cancellable) Ct.ThrowIfCancellationRequested();
    }
}

/// <summary>Progress sink the long paths report to; the page wires it to its small status component so a progress
/// tick re-renders that component only, never the whole (large) page.</summary>
public interface IWorkProgress
{
    /// <summary>What is happening now, and how far through it we are (0..1, or a negative number if unknown).</summary>
    void Report(string text, double fraction = -1);
}

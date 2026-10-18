using System.Diagnostics;

namespace ShowroomPlenum;

/// <summary>A monotonic millisecond clock (a service so a proof can drive time by hand).</summary>
public interface IPlenumClock
{
    long NowMs { get; }
}

public sealed class StopwatchPlenumClock : IPlenumClock
{
    public long NowMs => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
}

/// <summary>Says how many Engine ticks are due. It holds only a clock reading (a request for time, like a player's order); the
/// Engine itself changes only through Pleroma.Tick(n), called ONCE with the n this returns. While a tab is hidden the owner simply
/// stops asking, so on return Due() reports every tick missed and the Engine takes them in one call.</summary>
public sealed class EngineTicker
{
    private readonly IPlenumClock _clock;
    private long _accounted;

    public long TickMs { get; }

    public EngineTicker(IPlenumClock clock, long tickMs = 2000)
    {
        _clock = clock;
        TickMs = tickMs;
        _accounted = clock.NowMs;
    }

    /// <summary>Ticks elapsed since the last call that returned them (the remainder of a partial tick is kept).</summary>
    public long Due()
    {
        long now = _clock.NowMs;
        long n = (now - _accounted) / TickMs;
        if (n <= 0) return 0;
        _accounted += n * TickMs;
        return n;
    }
}

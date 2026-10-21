namespace ShowroomDoctrine;

/// <summary>
/// The tick on screen, shared by the replay and the chronicle beside it. The replay reports every tick it shows; the chronicle follows it and, when a
/// line is tapped, asks the replay to jump. No game rule here, only who tells whom which tick it is.
/// </summary>
public sealed class ReplayCursor
{
    public int Tick { get; private set; }
    /// <summary>The replay now shows a different tick.</summary>
    public event Action? Moved;
    /// <summary>Someone asked the replay to jump to this tick.</summary>
    public event Action<int>? SeekRequested;

    public void Report(int tick)
    {
        if (tick == Tick) return;
        Tick = tick;
        Moved?.Invoke();
    }

    public void RequestSeek(int tick) => SeekRequested?.Invoke(tick);
}

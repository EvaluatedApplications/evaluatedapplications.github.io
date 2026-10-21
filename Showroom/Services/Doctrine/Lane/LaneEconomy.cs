using Doctrine;

namespace ShowroomLane;

// UI-free service of the lane battle's playable page (the dev page /lane). No Blazor, no JS, no clock of its own: time is handed in by the caller, so a proof drives it with exact steps.

/// <summary>
/// Each switch breaks ONE thing of this service so that the matching proof check must go red (mutation check, the house pattern: Doctrine's own LaneProbe does the same for the
/// library). Default None; only the proof sets it.
/// </summary>
[Flags]
internal enum LaneMatchProbe
{
    None = 0,
    /// <summary>The human seat may stage packs and orders at any time, not only at a pause.</summary>
    PlaysAnyTime = 1,
    /// <summary>The pause never ends on its own clock.</summary>
    PauseClockIgnored = 2,
    /// <summary>The Ready button does nothing.</summary>
    ReadyIgnored = 4,
    /// <summary>Unspent points are dropped at the next pause instead of carrying.</summary>
    CarryLost = 8,
    /// <summary>The bot draws from an unseeded source.</summary>
    BotUnseeded = 16,
    /// <summary>The bot ignores the match seed.</summary>
    BotIgnoresSeed = 32,
    /// <summary>A plan that costs more than the purse is accepted.</summary>
    OverBudgetAccepted = 64,
    /// <summary>The pause countdown runs faster with the speed setting (speed must touch the run only).</summary>
    SpeedScalesPause = 128,
    /// <summary>The run phase counts one tick too many per second of game time.</summary>
    RunTooFast = 256,
    /// <summary>The picture between two ticks is drawn from the wrong end (alpha reversed).</summary>
    InterpolationReversed = 512,
    /// <summary>The two sides' plays are applied in two separate batches (not together at one tick).</summary>
    PlaysInTwoBatches = 1024,
}

/// <summary>
/// THE PLACEHOLDER ECONOMY (the real one is reworked later). Each side is credited <see cref="PointsPerPause"/> points at every pause, a pack of <see cref="PackSize"/> bodies of one
/// kind costs the kind's price (read from the game's own tables, <see cref="Roster.PriceOf"/>), and points not spent carry to the next pause.
/// </summary>
public sealed class LaneEconomy
{
    public const int PointsPerPause = 10;
    public const int PackSize = 4;

    readonly LaneMatchProbe _probe;
    internal LaneEconomy(LaneMatchProbe probe = LaneMatchProbe.None) => _probe = probe;

    /// <summary>Points in the purse now (what was carried plus what this pause credited, less what was spent).</summary>
    public int Balance { get; private set; }

    /// <summary>What one pack of <paramref name="kind"/> costs.</summary>
    public static int Cost(int kind) => Roster.PriceOf(kind);

    /// <summary>The cheapest pack there is (a purse below this can buy nothing).</summary>
    public static int CheapestPack { get { int c = int.MaxValue; for (int k = 0; k < Roster.Size; k++) c = Math.Min(c, Cost(k)); return c; } }

    /// <summary>A pause begins: the purse gets its points on top of what is carried.</summary>
    public void Credit() => Balance = (_probe.HasFlag(LaneMatchProbe.CarryLost) ? 0 : Balance) + PointsPerPause;

    public bool CanAfford(int kind) => (uint)kind < (uint)Roster.Size && Balance >= Cost(kind);

    /// <summary>Pays for one pack. False (and nothing changes) when the kind is unknown or the purse is short.</summary>
    public bool TrySpend(int kind)
    {
        if ((uint)kind >= (uint)Roster.Size) return false;
        if (!CanAfford(kind) && !_probe.HasFlag(LaneMatchProbe.OverBudgetAccepted)) return false;
        Balance -= Cost(kind);
        return true;
    }
}

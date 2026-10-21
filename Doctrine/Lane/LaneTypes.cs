using Doctrine.Battle;

namespace Doctrine.Lane;

// THE BATTLER LAYER (lane battle). A continuous battle in phases: bodies walk down lanes between two bases on the Tracer navmesh and fight what they meet.
// The sim steps exactly one PHASE (PhaseTicks ticks), then PAUSES on the same tick for both players. Plays are applied only at a pause, never mid-phase.
// Same setup, same seed, same ordered plays => bit-identical state; LaneBattle.StateHash() at each pause is how two clients check it. See Lane\LANE.md.

/// <summary>The constants of the lane battle. Everything on the field is derived from these and from the existing <see cref="UnitRules"/> tables.</summary>
public static class LaneRules
{
    /// <summary>Simulation ticks per second of game time.</summary>
    public const int TicksPerSecond = 10;
    /// <summary>Seconds of game time between two pauses.</summary>
    public const int PhaseSeconds = 30;
    /// <summary>Ticks in one phase (both clients step exactly this many, then pause).</summary>
    public const int PhaseTicks = TicksPerSecond * PhaseSeconds;
    public const int MaxLanes = 3;

    /// <summary>A body strikes once a second. The strike is the existing one (<see cref="UnitRules.Hit"/>); the old battle struck every tick of its own coarser clock.</summary>
    public const int StrikeEvery = TicksPerSecond;
    /// <summary>A body re-decides its goal every this many ticks (staggered by id, so the pathing load is spread over the ticks).</summary>
    public const int ThinkEvery = 5;
    /// <summary>Most path requests handed to Tracer in one tick (a spawn burst waits its turn; the rest of the bodies keep walking their old paths).</summary>
    public const int MaxPathsPerTick = 24;
    /// <summary>Re-path only when the goal moved farther than this (units).</summary>
    public const float RepathDistance = 2.0f;
    /// <summary>How far an Advance body notices an enemy and goes for it (units).</summary>
    public const float SightRange = 12f;
    /// <summary>How far a Hunt body looks for the weakest enemy of its lane (units).</summary>
    public const float HuntRange = 30f;
    /// <summary>Hold line, in percent of the lane from the body's own base edge.</summary>
    public const int HoldPct = 35;
    /// <summary>A body that has stood still and unhit for this many ticks is SET (dug in): <see cref="UnitRules.SetTicks"/> strikes' worth of time.</summary>
    public const int SetTicks = UnitRules.SetTicks * StrikeEvery;

    public const int DefaultBaseHp = 150;
    public const int DefaultMaxUnitsPerSide = 120;
    public const int DefaultCapPhases = 20;

    /// <summary>Units a body walks per tick: the existing pace (units per second) over the tick rate.</summary>
    public static float StepOf(Trait t) => UnitRules.Speed(t) / TicksPerSecond;
}

/// <summary>What the two clients agree on before the first tick. Same setup + same seed + same ordered plays = same match.</summary>
public sealed record LaneSetup
{
    /// <summary>Lanes between the two bases, 1 to 3. Each lane is its own corridor with walls, a neck or pillars and cover.</summary>
    public int Lanes { get; init; } = 3;
    /// <summary>Length of each lane in 2-unit cells, per half of the map (24 or more).</summary>
    public int LaneCols { get; init; } = 40;
    /// <summary>Ticks per phase. Both clients must agree; the default is <see cref="LaneRules.PhaseTicks"/> (30 s at 10 ticks a second).</summary>
    public int PhaseTicks { get; init; } = LaneRules.PhaseTicks;
    /// <summary>The hard tick cap: a match still running at this tick is a draw.</summary>
    public int TickCap { get; init; } = LaneRules.DefaultCapPhases * LaneRules.PhaseTicks;
    /// <summary>Base hit points in soldier-lives (a soldier is <see cref="UnitRules.Fp"/> units of hit points; a strike is that many hundredths).</summary>
    public int BaseHp { get; init; } = LaneRules.DefaultBaseHp;
    /// <summary>Most live bodies per side (a spawn play beyond it is cut short). The cap that keeps a battle inside a browser's budget.</summary>
    public int MaxUnitsPerSide { get; init; } = LaneRules.DefaultMaxUnitsPerSide;
    /// <summary>Test hook: flips ONE behaviour of the sim to see a check go red. None = the game.</summary>
    internal LaneProbe Probe { get; init; }

    internal void Validate()
    {
        if (Lanes is < 1 or > LaneRules.MaxLanes) throw new ArgumentOutOfRangeException(nameof(Lanes), "1 to 3 lanes");
        if (LaneCols < 24) throw new ArgumentOutOfRangeException(nameof(LaneCols), "24 cells or more");
        if (PhaseTicks < 1 || TickCap < 1 || BaseHp < 1 || MaxUnitsPerSide < 1) throw new ArgumentOutOfRangeException(nameof(PhaseTicks));
    }
}

/// <summary>Each switch breaks ONE thing of the sim so that the matching proof check must go red (mutation check). Default None.</summary>
[Flags]
internal enum LaneProbe
{
    None = 0,
    /// <summary>Plays are accepted in the middle of a phase.</summary>
    PlaysMidPhase = 1,
    /// <summary>A step runs one tick past the pause.</summary>
    PhaseOffByOne = 2,
    /// <summary>The spawn jitter comes from an unseeded source.</summary>
    UnseededJitter = 4,
    /// <summary>The state hash leaves the base hit points out.</summary>
    HashIgnoresBase = 8,
    /// <summary>The state hash leaves the hit points of the bodies out.</summary>
    HashIgnoresHp = 16,
    /// <summary>The state hash leaves the position of the LAST body out.</summary>
    HashIgnoresLastBody = 32,
    /// <summary>Survivors vanish at every pause.</summary>
    SurvivorsClearedAtPause = 64,
    /// <summary>A spawn play ignores its lane (always lane 0).</summary>
    SpawnIgnoresLane = 128,
    /// <summary>An order play changes nothing.</summary>
    OrderIgnored = 256,
    /// <summary>A fallen base does not end the match.</summary>
    BaseFallDoesNotEnd = 512,
    /// <summary>The hard tick cap is ignored.</summary>
    CapIgnored = 1024,
    /// <summary>The strike cooldown is re-seeded at every Step call (state hidden outside the hashed state).</summary>
    CooldownResetsPerStep = 2048,
    /// <summary>A dead body is not taken out of the Pleroma roster.</summary>
    RosterKeepsTheDead = 4096,
    /// <summary>The counter ring is not applied.</summary>
    CountersOff = 8192,
    /// <summary>The counter ring is read backwards.</summary>
    CountersReversed = 16384,
    /// <summary>A simultaneous fall of both bases is scored as a win for the player.</summary>
    DrawIsAWin = 32768,
    /// <summary>Ids are handed out in the order plays were applied (one counter for both sides), not interleaved by side: the order of two players' plays then matters.</summary>
    IdsInPlayOrder = 131072,
    /// <summary>The live-body cap is ignored.</summary>
    CapOnBodiesIgnored = 65536,
}

/// <summary>A play: an input applied to the match at a pause. Plays are a plain ordered list; the order is part of the match.</summary>
public abstract record LanePlay;

/// <summary>Spawn <see cref="Count"/> bodies of roster kind <see cref="Kind"/> (0..7) in lane <see cref="Lane"/> at the base of <see cref="Side"/>.</summary>
public sealed record SpawnPlay(Side Side, int Lane, int Kind, int Count) : LanePlay;

/// <summary>
/// Give a field order (the existing <see cref="FieldOrderKind"/>) to the bodies of <see cref="Side"/> in lane <see cref="Lane"/> (-1 = every lane).
/// Mapped: Advance, Hold, FallBack, Hunt, Guard. Flank has no meaning in a one-corridor lane and is refused.
/// </summary>
public sealed record OrderPlay(Side Side, int Lane, FieldOrderKind Order) : LanePlay;

/// <summary>What a play did. A refused play changes nothing; a spawn may be cut short by the body cap.</summary>
public readonly record struct LanePlayResult(int Index, bool Applied, int Accepted, string Reason);

public enum LaneStatus : byte { Running = 0, PlayerWon = 1, EnemyWon = 2, Draw = 3 }

/// <summary>One body as a renderer sees it.</summary>
public readonly record struct LaneUnitView(
    int Id, Side Side, int Lane, int Kind, float X, float Y, float FacingX, float FacingY, int Hp, int MaxHp, int Target, FieldOrderKind Order);

/// <summary>The whole field at one tick: bodies in id order, the two bases, the clock.</summary>
public sealed record LaneSnapshot(
    int Tick, int Phase, LaneStatus Status, long PlayerBaseHp, long EnemyBaseHp, long BaseMaxHp, IReadOnlyList<LaneUnitView> Units);

/// <summary>What one Step did. <c>Hash</c> is the state hash on a pause or at the end of the match, and 0 mid-phase (call <c>StateHash()</c> for it).</summary>
public readonly record struct LaneStepResult(int TicksRun, int Tick, bool AtPause, LaneStatus Status, ulong Hash);

/// <summary>How to draw the ground: the map as text (. open, # wall, , cover; one string per row, Cell units wide) and the places on it.</summary>
public sealed record LaneLayout(
    int Cell, float Width, float Height, IReadOnlyList<string> Rows, IReadOnlyList<float> LaneCentreY, float PlayerBaseEdgeX, float EnemyBaseEdgeX);

/// <summary>Counters of the last match so far (for proofs and the cost table; nothing reads them back into the sim).</summary>
public sealed class LaneStats
{
    public long Ticks, PathRequests, PathsNotFound, EngagementCalls, LineOfSightChecks, Strikes, BaseStrikes, Deaths, Spawned, Plays;
    /// <summary>Time spent in each stage, in <see cref="System.Diagnostics.Stopwatch"/> ticks (the cost table divides by <c>Stopwatch.Frequency</c>).</summary>
    public long ThinkTicks, MoveTicks, EngageTicks, ResolveTicks;
}

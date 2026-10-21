using Doctrine;
using Doctrine.Lane;
using ShowroomPlenum;
using Tracer.Integration;

namespace ShowroomLane;

// What a page binds to for an online lane battle: the phases, the opponent's state, the end, the options and the seat that stands for the other player. No Blazor, no network type.

public enum OnlinePhase
{
    /// <summary>Not in a room (before Create/Join, or after Leave).</summary>
    Idle,
    /// <summary>Host: the room is open and listed, waiting for a second player.</summary>
    Waiting,
    /// <summary>Guest: asked for the second seat, waiting for the host to seat you.</summary>
    Joining,
    /// <summary>Both are seated: each builds the same match from the room's match number.</summary>
    Building,
    /// <summary>Both have built (and their first hashes agree); waiting for the agreed start.</summary>
    Starting,
    /// <summary>The match is on (a pause, a run or the end on screen, see <see cref="LaneOnline.Match"/>).</summary>
    Playing,
    /// <summary>The match is over (see <see cref="LaneOnline.End"/>): played out, or ended by a player leaving, being away too long or the two games not agreeing.</summary>
    Ended,
}

public enum OnlineEndKind { Played, OpponentLeft, OpponentGone, YouWereAway, OutOfSync, PlaysRefused, Failed }

/// <summary>How the match ended, in plain words. <see cref="YouWon"/> is null for a draw and for an end that decided nothing.</summary>
public sealed record OnlineEnd(OnlineEndKind Kind, string Text, bool? YouWon);

/// <summary>What the other player is doing, as this player can tell.</summary>
public enum OpponentLink { Absent, Here, Away, Gone }

/// <summary>The other player's plays at one pause, as a seat. The session hands it the plan it read from their reveal (or an empty one) just before the pause ends.</summary>
public sealed class RemoteSeat : ILaneSeat
{
    readonly Dictionary<int, LanePlan> _plans = new();
    public bool IsHuman => true;
    public void Set(int phase, LanePlan plan) => _plans[phase] = plan;
    public bool Has(int phase) => _plans.ContainsKey(phase);
    public ValueTask<LanePlan> PlanAsync(LaneSeatView view, CancellationToken ct)
        => ValueTask.FromResult(_plans.Remove(view.Phase, out var p) ? p : LanePlan.Empty);
}

/// <summary>Each switch breaks ONE thing of the online service so that the matching proof group must go red (the house mutation pattern). Default None; only the proof sets it.</summary>
[Flags]
internal enum LaneOnlineProbe
{
    None = 0,
    /// <summary>A reveal goes out right after the player's own commitment instead of after both.</summary>
    RevealBeforeCommits = 1,
    /// <summary>The check of a reveal against its commitment is skipped.</summary>
    AcceptTamperedReveal = 2,
    /// <summary>The pause hashes of the two games are never compared.</summary>
    IgnoreHashMismatch = 4,
    /// <summary>No pause is ever declared missed (the clock is not consulted).</summary>
    NeverDeclareMiss = 8,
    /// <summary>A second commitment or reveal for the same pause replaces the first.</summary>
    NoIdempotence = 16,
    /// <summary>The presence message is published outside plenum/v1/doctrine-lane/.</summary>
    WrongTopic = 32,
    /// <summary>A pause is declared missed at the deadline itself, with no grace.</summary>
    NoGrace = 64,
    /// <summary>Consecutive missed pauses are never counted towards the end.</summary>
    MissStreakIgnored = 128,
    /// <summary>A reveal that says "you were too late" does not void the plays of the player it names.</summary>
    FlagIgnored = 256,
    /// <summary>This player's game leaves the other player's plays out (a divergence the hash exchange must catch).</summary>
    DropsRemotePlays = 512,
    /// <summary>The player's own hash message tells a different number than the game has.</summary>
    LyingHash = 1024,
    /// <summary>A room whose host stopped answering long ago is still joined.</summary>
    StaleRoomAccepted = 2048,
    /// <summary>A message of an older game of the same room is read as current.</summary>
    GenIgnored = 4096,
    /// <summary>A player who has been gone past the grace never loses the match for it.</summary>
    NeverGone = 8192,
}

/// <summary>Everything an online match needs from its host page. <see cref="Relays"/> is required.</summary>
public sealed class LaneOnlineOptions
{
    public IPlenumRelayFactory Relays { get; set; } = null!;
    public IPlenumClock Clock { get; set; } = new StopwatchPlenumClock();
    /// <summary>The setup the host proposes (the guest builds the one in the room, so a host and a guest never disagree on it).</summary>
    public LaneSetup Setup { get; set; } = LaneMatchOptions.DefaultSetup;
    public Func<TracerContext?>? Tracer { get; set; }
    public string GameTag { get; set; } = LaneOnlineProtocol.GameTag;

    /// <summary>Length of a pause (the time to choose). The same for both players.</summary>
    public int PauseMs { get; set; } = (int)(LaneMatch.PauseSeconds * 1000);
    /// <summary>Past the pause deadline (or the other player's own announced arrival plus a pause) a player who has not committed gets this much longer before the pause is declared missed.</summary>
    public int GraceMs { get; set; } = 6000;
    /// <summary>When the other player has not even arrived at the pause (a slow device still running the battle), how long past the deadline their arrival is waited for.</summary>
    public int ArrivalSlackMs { get; set; } = 15_000;
    /// <summary>Both committed but one reveal never came: after this long the match is ended for the player who stayed.</summary>
    public int RevealWaitMs { get; set; } = 20_000;
    /// <summary>This many missed pauses in a row end the match for the player who stayed.</summary>
    public int MissLimit { get; set; } = 3;
    /// <summary>A player who is gone (connection lost or closed) this long without coming back loses the match to the player who stayed.</summary>
    public int RejoinGraceMs { get; set; } = 45_000;
    /// <summary>After this player's own connection comes back, misses are not declared for this long, so the messages held for them can arrive first.</summary>
    public int ResyncMs { get; set; } = 3000;
    /// <summary>The start is announced this far ahead, so both players see the countdown.</summary>
    public int StartDelayMs { get; set; } = 3000;
    /// <summary>Presence is repeated this often; three beats of silence count as a connection that died without saying goodbye.</summary>
    public int HeartbeatMs { get; set; } = 15_000;
    /// <summary>Real time to wait for a joined room to show (a retained message) and for the host to seat you.</summary>
    public int JoinWaitMs { get; set; } = 8000;
    public int SeatWaitMs { get; set; } = 10_000;
    public int CreateProbeMs { get; set; } = 600;
    /// <summary>A room whose host presence is older than this is stale (the host left without a trace).</summary>
    public int StaleMs { get; set; } = 60_000;
    /// <summary>The most game time one <see cref="LaneOnline.AdvanceAsync"/> call may feed to the battle (a hidden tab then plays on at the clock's pace and no faster).</summary>
    public double RunDtCap { get; set; } = 0.25;
    /// <summary>The most ticks one call may run (see <see cref="LaneMatchOptions.MaxTicksPerAdvance"/>).</summary>
    public int TicksPerAdvance { get; set; } = 6;
    /// <summary>After a clean leave, a second short connection clears the presence message again after this long (a broker may read the closed socket as a lost link and publish the will). 0 = not done.</summary>
    public int PresenceClearDelayMs { get; set; } = 400;
    public Func<int, int> Below { get; set; } = System.Security.Cryptography.RandomNumberGenerator.GetInt32;
    public Func<string>? CodeSource { get; set; }
    public Func<int>? SeedSource { get; set; }
    /// <summary>TEST SEAM: rewrites this player's reveal (pause, plan text, nonce) before it is sent, to prove a tampered reveal is refused. Never set outside a proof.</summary>
    public Func<int, string, byte[], (string Plan, byte[] Nonce)>? RevealTamper { get; set; }
    internal LaneOnlineProbe Probe { get; set; }
}

using System.Security.Cryptography;
using Doctrine;
using ShowroomPlenum;

namespace ShowroomDoctrine.Duel;

// What the page binds to: the phases, the opponent's state, the outcome of a round, the end, the options and the pluggable auto picker. No Blazor, no network type.

public enum DuelPhase
{
    /// <summary>Not in a room (before Create/Join, or after Leave).</summary>
    Idle,
    /// <summary>In a room, before the first round: the host waits for a guest and for both to be ready.</summary>
    Lobby,
    /// <summary>A round is open and you have not sealed: build your muster, council and formation. The countdown runs.</summary>
    Sealing,
    /// <summary>You sealed (your commitment is out); waiting for the other seat.</summary>
    Sealed,
    /// <summary>Both committed; the reveals are crossing and the host is fighting the battle.</summary>
    Resolving,
    /// <summary>A round's result is in: watch the shared recording, then Continue.</summary>
    Result,
    /// <summary>The duel is over (see <see cref="IDoctrineDuel.End"/>): the final score, a seat that left, or a room that is gone.</summary>
    Ended,
}

/// <summary>What the other seat is doing, as this seat can tell.</summary>
public enum OpponentState
{
    /// <summary>Nobody has taken the second seat yet.</summary>
    Absent,
    /// <summary>Still building their round.</summary>
    Choosing,
    /// <summary>They have sealed (their commitment is in; the picks stay hidden until both have).</summary>
    Sealed,
    /// <summary>Three auto-seals in a row: the timer has been sealing for them.</summary>
    Idle,
    /// <summary>Their connection is down (the relay told us), or they have left.</summary>
    Offline,
}

public enum DuelEndKind { Final, HostLeft, GuestLeft, RoomGone, Refused }

/// <summary>How the duel ended, in plain words. <see cref="YouWon"/> is null for a drawn duel and for an end that was not a final score.</summary>
public sealed record DuelEnd(DuelEndKind Kind, string Text, DuelScore Score, bool? YouWon, DuelDecider? DecidedBy);

/// <summary>
/// One closed round as THIS seat sees it: <see cref="You"/> is the viewer's reading of the shared result (you are always "you"; the recording is the same one both seats get),
/// <see cref="Check"/> is the seat's own verification of its army against the host's digest. The flags say who the timer sealed for and who forfeited; <see cref="Notes"/> are
/// the host's plain lines (why a reveal was refused, who was sealed for).
/// </summary>
public sealed record DuelRoundOutcome(
    int Round, DuelRoundResult Result, DuelSeatView You, SeatCheck Check, IReadOnlyList<string> Notes, bool Final,
    int IdleYou, int IdleThem, bool YouAutoSealed, bool ThemAutoSealed, bool YouForfeited, bool ThemForfeited);

/// <summary>
/// The auto picker: called when the clock runs out on a seat that has not sealed (and on the host's copy of an absent opponent, <paramref name="forOpponent"/> true).
/// The seat is Open or already Drafted (the player had started); the picker must finish it (draft if <c>seat.OrderOffer is null</c>, then <c>SealAsync</c>) and return the picks
/// <c>SealAsync</c> returned. The page passes the game's starter advice, or the general's pick; the opponent's copy always gets the starter advice.
/// </summary>
public delegate ValueTask<DuelPicks> DuelAutoPicker(DuelSeat seat, int round, bool forOpponent, CancellationToken ct);

/// <summary>A plain default picker: buys the offered companies in the muster's order while the purse allows (at most four), then orders the first offered tokens (the game drops what is not allowed).</summary>
public static class DuelAutoPickers
{
    public static DuelAutoPicker Starter { get; } = async (seat, round, forOpponent, ct) =>
    {
        if (seat.OrderOffer is null)
        {
            var packs = new List<DraftPick>(); int gold = seat.Read().Gold;
            foreach (var p in seat.UnitOffer.Packs)
            {
                if (packs.Count >= 4 || !p.Affordable || p.Cost > gold) continue;
                packs.Add(UnitOffer.Pick(p)); gold -= p.Cost;
            }
            await seat.DraftAsync(packs, ct);
        }
        var tokens = seat.OrderOffer!.Tokens.Take(3).ToArray();
        return await seat.SealAsync(tokens.Length == 0 ? DoctrineSet.Empty : DoctrineSet.Of(tokens), ct);
    };
}

/// <summary>Everything a duel needs from its host. <see cref="GameFactory"/> and <see cref="Relays"/> are required.</summary>
public sealed class DuelOptions
{
    /// <summary>Builds the game for a number of rounds (both seats call it with the room's rounds). See <see cref="DuelGames"/>.</summary>
    public Func<int, DoctrineGame> GameFactory { get; set; } = null!;
    public IPlenumRelayFactory Relays { get; set; } = null!;
    public IPlenumClock Clock { get; set; } = new StopwatchPlenumClock();
    public DuelAutoPicker AutoPicker { get; set; } = DuelAutoPickers.Starter;
    public string GameTag { get; set; } = DuelProtocol.GameTag;
    /// <summary>After the deadline the host waits this long for the other seat's own auto-seal before it seals for them.</summary>
    public int GraceMs { get; set; } = 4000;
    /// <summary>Both commitments are in: a reveal that has not come after this long is forfeited (the seat fights with its levy).</summary>
    public int RevealGraceMs { get; set; } = 10000;
    /// <summary>The host opens the next round this long after a result even if the other seat has not pressed Continue.</summary>
    public int WatchCapMs { get; set; } = 60000;
    /// <summary>The guest ends the match plainly when the host's presence has been offline this long.</summary>
    public int AuthorityGraceMs { get; set; } = 30000;
    /// <summary>After a reconnect, the room must show again within this long or the room is gone.</summary>
    public int ResyncWaitMs { get; set; } = 6000;
    /// <summary>Real time to wait for a joined room to show (a retained message), and for a created code to prove free.</summary>
    public int JoinWaitMs { get; set; } = 8000;
    public int CreateProbeMs { get; set; } = 600;
    /// <summary>A result is cut into chunks of at most this many characters of base64 (see the measured sizes in the proof).</summary>
    public int MaxChunkChars { get; set; } = 24_000;
    /// <summary>A random integer in [0, n). Replaceable so a proof can fix codes and seeds.</summary>
    public Func<int, int> Below { get; set; } = RandomNumberGenerator.GetInt32;
    public Func<string>? CodeSource { get; set; }
    public Func<int>? SeedSource { get; set; }
    /// <summary>TEST SEAM: rewrites this seat's reveal (round, canonical picks, nonce) before it is sent, to prove a tampered reveal is refused. Never set outside a proof.</summary>
    public Func<int, byte[], byte[], (byte[] Canonical, byte[] Nonce)>? RevealTamper { get; set; }
}

/// <summary>Test seam: each switch re-introduces one specific bug so the proof can watch a check go red. All false in the shipped app; nothing but the proof sets them.</summary>
public static class DoctrineDuelFaults
{
    /// <summary>Reveal right after committing instead of waiting for both commitments.</summary>
    public static bool RevealBeforeCommits;
    /// <summary>The clock is never consulted: no auto-seal, no substitution, no forfeit for a missing reveal.</summary>
    public static bool IgnoreTimeout;
    /// <summary>A repeated message is applied again (the result a second time, the counters twice).</summary>
    public static bool NoIdempotence;
    /// <summary>The commitment check on a reveal is skipped.</summary>
    public static bool AcceptTamperedReveal;
    /// <summary>Presence is published outside plenum/v1/doctrine/.</summary>
    public static bool WrongTopic;
    public static void Reset() { RevealBeforeCommits = IgnoreTimeout = NoIdempotence = AcceptTamperedReveal = WrongTopic = false; }
}

/// <summary>
/// The surface a page binds to. Read the properties when rendering; <see cref="Changed"/> says something moved (call StateHasChanged; it can arrive on any thread). The countdown is
/// <see cref="RemainingMs"/> (render it on the page's own timer or from <see cref="Tick"/>). Everything the page does is a method here; nothing here draws.
/// </summary>
public interface IDoctrineDuel : IAsyncDisposable
{
    event Action? Changed;

    DuelPhase Phase { get; }
    /// <summary>Which seat you are (null before Create/Join). The host is seat A.</summary>
    Seat? Me { get; }
    bool IsHost { get; }
    string Code { get; }
    string MyName { get; }
    string OpponentName { get; }
    int Seed { get; }
    int Round { get; }
    int Rounds { get; }
    int TimerSec { get; }
    /// <summary>Your own connection to the relay (false while it is down and being retried).</summary>
    bool Online { get; }
    bool MyReady { get; }
    bool OpponentReady { get; }
    OpponentState Opponent { get; }
    /// <summary>Auto-seals in a row for each seat as of the last result (three means idle).</summary>
    int IdleYou { get; }
    int IdleThem { get; }
    /// <summary>True once the other seat has pressed Continue on the current result.</summary>
    bool OpponentContinued { get; }
    /// <summary>Milliseconds left to seal this round, or -1 when no round is open. It counts down to the host's absolute deadline, corrected for the clock difference seen when the round opened.</summary>
    long RemainingMs { get; }
    /// <summary>Your own seat for the open round: draft, form, reroll and seal on THIS (it is replaced, rarely, if the host had to seal for you; read it again after Changed).</summary>
    DuelSeat? LocalSeat { get; }
    /// <summary>The latest closed round, and all of them.</summary>
    DuelRoundOutcome? Outcome { get; }
    IReadOnlyList<DuelRoundOutcome> History { get; }
    DuelEnd? End { get; }
    /// <summary>Plain-language lines about what just happened (a seat joined, a reveal refused, a connection lost). Newest last.</summary>
    IReadOnlyList<string> Notices { get; }
    /// <summary>Internal faults caught by the state machine (should stay 0).</summary>
    int Errors { get; }

    /// <summary>Host: open a room (rounds 3, 5 or 8; a timer preset in seconds). Throws if the relay cannot be reached.</summary>
    Task CreateRoomAsync(string name, int rounds, int timerSec);
    /// <summary>Guest: join a room by its code. Returns null on success, else the plain reason it failed.</summary>
    Task<string?> JoinAsync(string code, string name);
    /// <summary>Lobby: say you are ready (the host's ready is the start, once a guest is seated and ready).</summary>
    Task SetReadyAsync(bool ready = true);
    /// <summary>Seal the picks <c>LocalSeat.SealAsync</c> returned. False if it is not the time, or you were already sealed (by the timer, or by the host).</summary>
    Task<bool> SealAsync(DuelPicks picks);
    /// <summary>Done watching the result: tell the other seat; on the final result this shows the end.</summary>
    Task ContinueAsync();
    /// <summary>After a final score: ask for another duel; when both ask, the host opens a fresh room and both seats move into it.</summary>
    Task RematchAsync();
    /// <summary>Leave: the other seat is told plainly, your messages are cleared from the relay and the connection closes.</summary>
    Task LeaveAsync();
    /// <summary>Let the clock act (auto-seal, substitution, forfeit, the open of the next round). Call it about four times a second, or run <see cref="RunTickerAsync"/>.</summary>
    void Tick();
    Task RunTickerAsync(CancellationToken ct);
}

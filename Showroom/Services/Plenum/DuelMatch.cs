using System.Globalization;
using Phasor;

namespace ShowroomPlenum;

/// <summary>Test seam for tools/PlenumProof: each switch re-introduces one specific bug so the proof can show a test going red.
/// All false in the shipped app; nothing but the proof sets them.</summary>
public static class PlenumFaults
{
    public static bool SwapApplyOrder, NoIdempotence, SkipHashCompare, IgnoreFlipTarget, SkipPrevCheck, TrustAnySeat, SpectatorsMaySubmit;
    public static void Reset() { SwapApplyOrder = NoIdempotence = SkipHashCompare = IgnoreFlipTarget = SkipPrevCheck = TrustAnySeat = SpectatorsMaySubmit = false; }
}

/// <summary>One thing a player does in a round, written as a short token so it travels in a message and can be read by eye:
/// u7 play unit card 7 (all copies), t0 Tally tag 0, f0s Flip tag 0 on my Board, f0o Flip tag 0 on the opponent's Board,
/// x1,2 Fuse cards 1 and 2, p put the hand back (free).</summary>
public readonly record struct Play(char Kind, int A, int B, bool Other)
{
    public static bool TryParse(string? s, out Play p)
    {
        p = default;
        if (string.IsNullOrEmpty(s) || s.Length > 8) return false;
        var rest = s.AsSpan(1);
        switch (s[0])
        {
            case 'u': { if (!Num(rest, out int k, 0, 31)) return false; p = new('u', k, 0, false); return true; }
            case 't': { if (!Num(rest, out int b, 0, 7)) return false; p = new('t', b, 0, false); return true; }
            case 'f':
            {
                if (rest.Length < 2 || (rest[^1] != 's' && rest[^1] != 'o')) return false;
                if (!Num(rest[..^1], out int b, 0, 7)) return false;
                p = new('f', b, 0, rest[^1] == 'o');
                return true;
            }
            case 'x':
            {
                int c = rest.IndexOf(',');
                if (c <= 0 || !Num(rest[..c], out int a, 0, 31) || !Num(rest[(c + 1)..], out int b2, 0, 31)) return false;
                p = new('x', a, b2, false);
                return true;
            }
            case 'p': { if (rest.Length != 0) return false; p = new('p', 0, 0, false); return true; }
        }
        return false;
    }

    private static bool Num(ReadOnlySpan<char> s, out int v, int lo, int hi)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v) && v >= lo && v <= hi;

    public static string Unit(ulong key) => "u" + key;
    public static string Tally(int bit) => "t" + bit;
    public static string Flip(int bit, bool other) => "f" + bit + (other ? "o" : "s");
    public static string Fuse(ulong a, ulong b) => "x" + a + "," + b;
    public const string PutBack = "p";
}

/// <summary>One player's order for one round. Prev is the state hash after the round before, so an order is only valid on the
/// state its author saw.</summary>
public sealed record DuelOrder(int Seat, int Round, string[] Plays, string Prev)
{
    public bool SameAs(DuelOrder o) => o.Seat == Seat && o.Round == Round && o.Prev == Prev && o.Plays.SequenceEqual(Plays);
}

public sealed record PlayLog(int Round, int Seat, string Token, string Title, string Says, bool Refused);

public enum SubmitResult { Resolved, Stored, Duplicate, Conflict, Invalid, Over, Halted }
public enum DuelEnd { Running, Seat0Wins, Seat1Wins, Draw, Halted }

/// <summary>The deterministic lockstep duel: two PlenumGame sides (each its own Pleroma stores), the orders exchanged, and the round
/// rule. EVERY client runs one of these from the same seed and the same orders, so both boards are mirrored on both screens.
///
/// What is NOT Pleroma state, listed honestly: the pending-order table, the log of plays, the round hashes, the result flag and
/// the halt reason (session bookkeeping, like the lobby and the clocks in PlenumSession). The round number itself is read from the
/// Tempo store (Turn). All game state (Deck, Hand, Board, Discard, Tempo of each side) changes only through Pleroma calls made by
/// PlenumGame. The Engine is not part of a duel (its ticks depend on the clock, which differs between devices).
///
/// Round rule: when both orders for round r are in, seat 0's plays are applied, then seat 1's; if either side has cashed the Goal
/// with Tally the match ends (both = draw); otherwise the Flipper acts on each Board (seat 0, seat 1), each side draws a card,
/// and the round advances. An order for a round already resolved is ignored (first one wins), so delivery may repeat or reorder.</summary>
public sealed class DuelMatch
{
    public const int MaxOrderLength = 6;
    public const int MaxRoundsAhead = 8;

    private readonly Dictionary<(int Seat, int Round), DuelOrder> _orders = new();
    private readonly List<string> _hashes = new();      // _hashes[r] = state hash after round r (0 = the deal)
    private readonly List<PlayLog> _log = new();

    public ulong Seed { get; }
    public PlenumGame[] Side { get; }
    public DuelEnd End { get; private set; }
    public string EndReason { get; private set; } = "";
    public string? HaltReason { get; private set; }

    public DuelMatch(ulong seed)
    {
        Seed = seed;
        Side = new[] { new PlenumGame(PlenumRng.SeatSeed(seed, 0)), new PlenumGame(PlenumRng.SeatSeed(seed, 1)) };
        _hashes.Add(Hash());
    }

    /// <summary>The round waiting for orders (1 at the start). Read from the Tempo store of seat 0; both sides advance together.</summary>
    public int Round => Side[0].Turn;
    public bool Over => End != DuelEnd.Running;
    /// <summary>Rounds fully applied so far (the hash list holds the deal plus one entry per resolved round).</summary>
    public int ResolvedRounds => _hashes.Count - 1;
    public IReadOnlyList<PlayLog> Log => _log;
    public string HashAfter(int round) => round >= 0 && round < _hashes.Count ? _hashes[round] : "";
    public string CurrentHash => _hashes[^1];
    public bool HasOrder(int seat, int round) => _orders.ContainsKey((seat, round));
    public DuelOrder? OrderFor(int seat, int round) => _orders.TryGetValue((seat, round), out var o) ? o : null;
    public IEnumerable<DuelOrder> AllOrders => _orders.Values.OrderBy(o => o.Round).ThenBy(o => o.Seat);

    public static bool ValidOrder(DuelOrder o)
    {
        if (o.Seat is < 0 or > 1 || o.Round < 1 || o.Plays is null || o.Plays.Length > MaxOrderLength || string.IsNullOrEmpty(o.Prev) || o.Prev.Length > 32) return false;
        foreach (var t in o.Plays) if (!Play.TryParse(t, out _)) return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ hash

    /// <summary>FNV-1a (64 bit) over the cells of every deterministic store of both sides: Deck, Hand, Board, Discard, Tempo. Integers
    /// only, so every browser gets the same text. (The Engine is excluded: it is not part of a duel.)</summary>
    public string Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (var g in Side)
            foreach (var store in new[] { g.Deck, g.Hand, g.Board, g.Discard, g.Tempo })
            {
                var cells = store.RawCells.ToArray();
                h = (h ^ (ulong)cells.Length) * 0x100000001B3UL;
                foreach (var c in cells)
                    for (int s = 0; s < 64; s += 8) h = (h ^ ((c >> s) & 0xFF)) * 0x100000001B3UL;
            }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    public string BoardHash(int seat)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (var c in Side[seat].Board.RawCells.ToArray())
            for (int s = 0; s < 64; s += 8) h = (h ^ ((c >> s) & 0xFF)) * 0x100000001B3UL;
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------------ orders

    public SubmitResult Submit(DuelOrder o)
    {
        if (HaltReason is not null) return SubmitResult.Halted;
        if (Over) return SubmitResult.Over;
        if (!ValidOrder(o)) return SubmitResult.Invalid;
        if (o.Round >= Round + MaxRoundsAhead) return SubmitResult.Invalid;
        if (_orders.TryGetValue((o.Seat, o.Round), out var have))
        {
            if (PlenumFaults.NoIdempotence) { ApplyPlays(o.Seat, o.Round, o.Plays); return SubmitResult.Duplicate; }   // the bug: applies it again
            return have.SameAs(o) ? SubmitResult.Duplicate : SubmitResult.Conflict;                                     // the first one stands
        }
        if (o.Round < Round) return SubmitResult.Conflict;      // a round already resolved with no order of this seat cannot happen; ignore
        _orders[(o.Seat, o.Round)] = o;
        bool any = false;
        while (!Over && HaltReason is null && _orders.ContainsKey((0, Round)) && _orders.ContainsKey((1, Round))) { Resolve(); any = true; }
        return any ? SubmitResult.Resolved : SubmitResult.Stored;
    }

    private void ApplyPlays(int seat, int round, IEnumerable<string> plays)
    {
        foreach (var t in plays)
        {
            var r = ApplyPlay(seat, t);
            _log.Add(new PlayLog(round, seat, t, r.Title, r.Says, r.Refused));
        }
    }

    /// <summary>Apply one play to a side right now (also used on a preview copy while a player builds an order).</summary>
    public OpResult ApplyPlay(int seat, string token)
    {
        if (!Play.TryParse(token, out var p)) return new OpResult("Play", "", "That play could not be read.", Array.Empty<(Zone, int[])>(), true);
        var me = Side[seat];
        switch (p.Kind)
        {
            case 'u': return me.PlayUnit((ulong)p.A);
            case 't': return me.Tally(p.A);
            case 'f':
                if (p.Other && !PlenumFaults.IgnoreFlipTarget) return me.FlipOther(Side[1 - seat], p.A);
                return me.Flip(p.A);
            case 'x': return me.Fuse((ulong)p.A, (ulong)p.B);
            default: return me.PutBack();
        }
    }

    private void Resolve()
    {
        int r = Round;
        var o0 = _orders[(0, r)];
        var o1 = _orders[(1, r)];
        string prev = Hash();                                   // the live state: an order is valid only on the state its author saw
        if (!PlenumFaults.SkipPrevCheck && (o0.Prev != prev || o1.Prev != prev))
        {
            HaltReason = $"Out of sync in round {r}: an order was built on a different state than this device has.";
            End = DuelEnd.Halted;
            return;
        }
        if (PlenumFaults.SwapApplyOrder) { ApplyPlays(1, r, o1.Plays); ApplyPlays(0, r, o0.Plays); }
        else { ApplyPlays(0, r, o0.Plays); ApplyPlays(1, r, o1.Plays); }

        bool g0 = Side[0].GoalReached, g1 = Side[1].GoalReached;
        if (g0 || g1)
        {
            End = g0 && g1 ? DuelEnd.Draw : g0 ? DuelEnd.Seat0Wins : DuelEnd.Seat1Wins;
            EndReason = g0 && g1 ? "Both sides cashed the Goal in the same round." : "The Goal was cashed with Tally.";
        }
        else
        {
            Side[0].EndTurn();
            Side[1].EndTurn();
        }
        _hashes.Add(Hash());       // hash index r (the list had r entries: 0..r-1)
    }

    /// <summary>The other side left and the match is awarded here (a decision of the session, not of the round rule).</summary>
    public void Forfeit(int winnerSeat, string reason)
    {
        if (Over || HaltReason is not null) return;
        End = winnerSeat == 0 ? DuelEnd.Seat0Wins : DuelEnd.Seat1Wins;
        EndReason = reason;
    }

    public void Halt(string reason)
    {
        if (HaltReason is not null) return;
        HaltReason = reason;
        End = DuelEnd.Halted;
    }

    // ------------------------------------------------------------------------------------------------ preview

    /// <summary>A copy of the match as it stands now (every resolved round replayed from the seed), for building an order: plays applied
    /// to the copy show their effect without touching the real match. The opponent's pending order is not applied.</summary>
    public DuelMatch Fork()
    {
        var f = new DuelMatch(Seed);
        for (int r = 1; r < Round; r++)
        {
            f.Submit(_orders[(0, r)]);
            f.Submit(_orders[(1, r)]);
        }
        return f;
    }
}

using Doctrine.Battle;

namespace Doctrine;

public enum DuelDecider : byte { Fields, Power, Drawn }

/// <summary>
/// The standing of a duel. The winner is the seat with MORE FIELDS WON; a tie goes to the seat that holds more POWER at the end, the quantity the battle itself counts at the tick
/// limit (<see cref="UnitRules.Rating"/> summed over the cohorts, with veterancy and aura in); equal on both is a drawn duel. Derived from the game's own tables: no typed rule.
/// </summary>
public readonly record struct DuelScore(int FieldsA, int FieldsB, long PowerA, long PowerB)
{
    public int Fields(Seat s) => s == Seat.A ? FieldsA : FieldsB;
    public long Power(Seat s) => s == Seat.A ? PowerA : PowerB;
    public Seat? Winner => FieldsA != FieldsB ? (FieldsA > FieldsB ? Seat.A : Seat.B) : PowerA != PowerB ? (PowerA > PowerB ? Seat.A : Seat.B) : null;
    public DuelDecider DecidedBy => FieldsA != FieldsB ? DuelDecider.Fields : PowerA != PowerB ? DuelDecider.Power : DuelDecider.Drawn;
    /// <summary>The power an army holds: <see cref="UnitRules.Rating"/> summed over its cohorts.</summary>
    public static long PowerOf(ArmyReadModel read) => read.Cohorts.Sum(c => UnitRules.Rating(c.Count, c.PowerPct, c.Traits));
}

/// <summary>The opponent as a seat sees it once the picks are revealed: the orders given and the army that fought, by line (<see cref="EnemyBrief.Lines"/>).</summary>
public sealed record DuelOpponentView(DoctrineSet Doctrine, EnemyBrief Army, int Rerolls);

/// <summary>
/// One round from ONE seat's point of view: the viewer is always "you". <see cref="Record"/> is a <see cref="RoundRecord"/> whose player is the viewer (its doctrine, offers, orders report,
/// spoils, its own digest in PlayerDigest, the opponent's in EnemyDigest; the result and the replay read as if the viewer had been the attacker, see <see cref="DuelRoundResult.ViewFor"/>).
/// </summary>
public sealed record DuelSeatView(Seat You, RoundRecord Record, DuelOpponentView Opponent, bool YouBesieged, bool YouWon, DuelScore Score);

/// <summary>
/// Everything the authority says about ONE round of a duel, the same for both seats. The recording is the shared one (seat A on the <see cref="Side.Player"/> label, seat B on
/// <see cref="Side.Enemy"/>); each seat's own reading of it is <see cref="ViewFor"/>. <see cref="Encode"/> is the message the authority sends (plain bytes, the replay inside it in the
/// replay codec's own format), <see cref="TryDecode"/> its inverse.
/// </summary>
public sealed record DuelRoundResult(int Round, BattleReplay Replay, DuelSeatRound A, DuelSeatRound B, bool Final, DuelScore Score)
{
    public string ArenaId => Replay.ArenaId;
    public BattleResult Result => Replay.Result;
    /// <summary>The seat that besieged (null on an open field, or with sieges off): seat A in even rounds, seat B in odd ones.</summary>
    public Seat? Besieger => Replay.Besieger is { } s ? SeatExtensions.Of(s) : null;
    /// <summary>The seat that won the field, or null for a draw.</summary>
    public Seat? Winner => Replay.Result.Outcome switch { BattleStatus.AttackerWon => Seat.A, BattleStatus.DefenderWon => Seat.B, _ => null };
    public DuelSeatRound For(Seat s) => s == Seat.A ? A : B;

    public DuelSeatView ViewFor(Seat viewer) => ViewFor(viewer, DuelProbe.None);

    internal DuelSeatView ViewFor(Seat viewer, DuelProbe probe)
    {
        if (probe.HasFlag(DuelProbe.SeatViewsSwapped)) viewer = viewer.Other();
        var me = For(viewer); var them = For(viewer.Other());
        var replay = viewer == Seat.A ? Replay : DuelMirror.Replay(Replay);
        var record = new RoundRecord(Round, me.Doctrine, DoctrineSet.Empty, 0, false, me.IllegalDropped, replay.ArenaId, replay.Result, replay, me.Spoils,
            me.DigestAtClose, them.DigestAtClose, me.OrdersReport, Final, me.Units, me.Orders, me.Rerolls, me.OfferRefused);
        var opponent = new DuelOpponentView(them.Doctrine,
            new EnemyBrief(them.GoldAfterOrders, them.Cohorts.Sum(c => c.Count), them.Cohorts.Sum(c => UnitRules.Rating(c.Count, c.PowerPct, c.Traits)), them.Cohorts), them.Rerolls);
        return new DuelSeatView(viewer, record, opponent, Besieger == viewer, Winner == viewer, Score);
    }

    /// <summary>One number for the round: both seats can compare it to know they saw the same result (recording, doctrines, digests, spoils, score).</summary>
    public ulong Digest()
    {
        var h = new Fnv();
        h.Add(Round); h.Add(ReplayCodec.Digest(Replay)); h.Add(Final ? 1 : 0);
        h.Add(Score.FieldsA); h.Add(Score.FieldsB); h.Add(Score.PowerA); h.Add(Score.PowerB);
        foreach (var s in new[] { A, B })
        {
            h.Add(s.DigestAfterOrders); h.Add(s.DigestAtClose); h.Add(s.Spoils.Gold); h.Add(s.Spoils.Experience); h.Add(s.Spoils.Muster);
            h.Add(s.Doctrine.S0); h.Add(s.Doctrine.S1); h.Add(s.Doctrine.S2); h.Add(s.Doctrine.S3);
        }
        return h.Value;
    }

    // ---- the message ------------------------------------------------------------------------------------------------------

    public byte[] Encode()
    {
        var w = new ByteWriter();
        w.Byte((byte)'D'); w.Byte((byte)'R'); w.Byte((byte)'1');
        w.Var(Round); w.Bool(Final);
        w.Bytes(ReplayCodec.Encode(Replay));
        w.Var(Score.FieldsA); w.Var(Score.FieldsB); w.VarL(Score.PowerA); w.VarL(Score.PowerB);
        WriteSeat(w, A); WriteSeat(w, B);
        return w.ToArray();
    }

    static void WriteSeat(ByteWriter w, DuelSeatRound s)
    {
        w.Bool(s.Units.Active); w.Var(s.Units.Budget); w.Var(s.Units.Packs.Count);
        foreach (var p in s.Units.Packs) { w.Var(p.Kind); w.Var(p.Count); w.Var(p.Cost); w.Bool(p.Affordable); }
        w.Bool(s.Orders.Active); w.Var(s.Orders.Tokens.Count);
        foreach (var t in s.Orders.Tokens) w.Var(t);
        w.Var(s.Orders.Rerolls); w.Var(s.Orders.RerollsLeft); w.Var(s.Orders.RerollCost);
        w.Var(s.Rerolls); w.Var(s.Doctrine.S0); w.Var(s.Doctrine.S1); w.Var(s.Doctrine.S2); w.Var(s.Doctrine.S3);
        w.Var(s.IllegalDropped); w.Var(s.OfferRefused); w.Var(s.OrdersReport.Applied); w.Var(s.OrdersReport.Skipped); w.Var(s.GoldAfterOrders);
        w.Var(s.Cohorts.Count);
        foreach (var c in s.Cohorts) { w.Var(c.Key); w.Var(c.Count); w.Var((int)c.Traits); w.Var(c.Slot); w.Var(c.Veterancy); w.Var(c.PowerPct); }
        w.U64(s.DigestAfterOrders); w.Var(s.Spoils.Gold); w.Var(s.Spoils.Experience); w.Var(s.Spoils.Muster); w.U64(s.DigestAtClose);
    }

    static DuelSeatRound ReadSeat(ByteReader r, Seat seat)
    {
        bool active = r.Bool(); int budget = r.Var(); int np = r.Count();
        var packs = new List<UnitPack>(np);
        for (int i = 0; i < np; i++) packs.Add(new UnitPack(r.Var(), r.Var(), r.Var(), r.Bool()));
        bool oActive = r.Bool(); int nt = r.Count();
        var tokens = new List<int>(nt);
        for (int i = 0; i < nt; i++) tokens.Add(r.Var());
        var orders = new OrderOffer(oActive, tokens, r.Var(), r.Var(), r.Var());
        int rerolls = r.Var(); var doctrine = new DoctrineSet(r.Var(), r.Var(), r.Var(), r.Var());
        int dropped = r.Var(), refused = r.Var(); var report = new ArmyOrdersReport(r.Var(), r.Var()); int gold = r.Var();
        int nc = r.Count(); var cohorts = new List<CohortRead>(nc);
        for (int i = 0; i < nc; i++)
        {
            int key = r.Var(), count = r.Var(), traits = r.Var(), slot = r.Var(), vet = r.Var(), pct = r.Var();
            if (traits is < 0 or > 255) throw new FormatException("bad traits");
            cohorts.Add(new CohortRead(key, count, (Trait)traits, slot, vet, pct));
        }
        ulong afterOrders = r.U64(); var spoils = new Spoils(r.Var(), r.Var(), r.Var()); ulong close = r.U64();
        return new DuelSeatRound(seat, new UnitOffer(active, budget, packs), orders, rerolls, doctrine, dropped, refused, report, gold, cohorts, afterOrders, spoils, close);
    }

    /// <summary>Read a round result. Never throws: damaged data returns false with a reason.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out DuelRoundResult? result, out string error)
    {
        result = null; error = "";
        try
        {
            var r = new ByteReader(data.ToArray());
            if (r.Byte() != (byte)'D' || r.Byte() != (byte)'R' || r.Byte() != (byte)'1') { error = "not a duel result"; return false; }
            int round = r.Var(); bool final = r.Bool();
            if (!ReplayCodec.TryDecode(r.Bytes(), out var replay, out var why)) { error = "replay: " + why; return false; }
            var score = new DuelScore(r.Var(), r.Var(), r.VarL(), r.VarL());
            var a = ReadSeat(r, Seat.A); var b = ReadSeat(r, Seat.B);
            if (!r.AtEnd) { error = "trailing bytes"; return false; }
            result = new DuelRoundResult(round, replay!, a, b, final, score);
            return true;
        }
        catch (FormatException e) { error = e.Message; result = null; return false; }
    }
}

/// <summary>The mirror image of a recording: the same fight with the two side labels swapped, so a seat that fought as the defender can read it as "you" (the player label).</summary>
public static class DuelMirror
{
    static Side Flip(Side s) => s == Side.Player ? Side.Enemy : Side.Player;
    static BattleStatus Flip(BattleStatus s) => s switch { BattleStatus.AttackerWon => BattleStatus.DefenderWon, BattleStatus.DefenderWon => BattleStatus.AttackerWon, _ => s };

    public static BattleResult Result(BattleResult r) => new(Flip(r.Outcome), r.Ticks, r.DefenderLosses, r.AttackerLosses);

    /// <summary>The recording with the sides swapped (labels, besieger, outcome, losses). Positions are untouched, so the ground is drawn as it was. Mirroring twice gives the original.</summary>
    public static BattleReplay Replay(BattleReplay r)
        => r with
        {
            Frames = r.Frames.Select(f => f with { Blobs = f.Blobs.Select(b => b with { Side = Flip(b.Side) }).ToList() }).ToList(),
            Result = Result(r.Result),
            Besieger = r.Besieger is { } s ? Flip(s) : null,
        };
}

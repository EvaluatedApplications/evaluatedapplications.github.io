namespace Doctrine;

// THE DUEL (Doctrine\todo\duel-design-2026-10-19.md, "Doctrine library seam"). No network code lives here: every type is plain data and pure rules, so the two
// clients and the authority's proof run the SAME definitions. Seat A is the side of the single-player player (the attacker slot, Side.Player), seat B the
// other (Side.Enemy); seat A besieges the even rounds, seat B the odd ones.

public enum Seat : byte { A = 0, B = 1 }

public static class SeatExtensions
{
    public static Seat Other(this Seat s) => s == Seat.A ? Seat.B : Seat.A;
    /// <summary>The side a seat fights as in the battle: seat A is the attacker slot (<see cref="Side.Player"/>), seat B the defender slot (<see cref="Side.Enemy"/>).</summary>
    public static Side Side(this Seat s) => s == Seat.A ? Doctrine.Side.Player : Doctrine.Side.Enemy;
    public static Seat Of(Side side) => side == Doctrine.Side.Player ? Seat.A : Seat.B;
}

/// <summary>Test hooks: each breaks ONE rule of the duel so a contract test can be watched going red. Never set outside tests.</summary>
[Flags] internal enum DuelProbe { None = 0, OffersDifferPerSeat = 1, SiegeParityWrong = 2, SeatViewsSwapped = 4, CommitIgnoresNonce = 8, VerifyAcceptsTampered = 16, CommitIgnoresSeat = 32 }

/// <summary>
/// What a seat seals for one round, and the ONE definition of how it is written down and committed. <see cref="Units"/> are the packs bought (kind and count,
/// kept sorted so the listing order changes nothing), <see cref="Doctrine"/> the tokens in slot order, <see cref="Formation"/> the free turn of the ranks
/// (0..2, see <see cref="IOpponent.FormationAsync"/>) and <see cref="Rerolls"/> how many times the seat paid to redraw the order offer.
/// <para>
/// THE PROTOCOL: a seat sends <c>Commit(Canonical, nonce, round, seat)</c> first; when both have committed it sends <c>Canonical</c> and the nonce. The receiver runs
/// <see cref="Verify"/>, then <see cref="TryParse"/> on the canonical bytes. A reveal that fails either is a forfeit of the round (the host's policy).
/// </para>
/// </summary>
public sealed class DuelPicks : IEquatable<DuelPicks>
{
    public const int MaxUnits = 64;
    /// <summary>The shortest nonce a commitment accepts. A shorter one would let the other seat try every pick against the hash.</summary>
    public const int MinNonce = 16;
    const string CommitTag = "doctrine-duel-commit-1";

    public static DuelPicks Empty { get; } = new([], DoctrineSet.Empty);

    public IReadOnlyList<DraftPick> Units { get; }
    public DoctrineSet Doctrine { get; }
    public int Formation { get; }
    public int Rerolls { get; }

    public DuelPicks(IReadOnlyList<DraftPick> units, DoctrineSet doctrine, int formation = 0, int rerolls = 0)
    {
        if (units.Count > MaxUnits) throw new ArgumentException($"at most {MaxUnits} packs in one round", nameof(units));
        Units = units.OrderBy(p => p.Kind).ThenBy(p => p.Count).ToList();
        Doctrine = doctrine; Formation = formation; Rerolls = rerolls;
    }

    /// <summary>The canonical bytes of these picks (see <see cref="Canonical(IReadOnlyList{DraftPick}, DoctrineSet, int, int)"/>).</summary>
    public byte[] Canonical() => Canonical(Units, Doctrine, Formation, Rerolls);

    /// <summary>
    /// The canonical form, version 1: <c>"DU1"</c>, then zig-zag varints: pack count, each pack (kind, count) in (kind, count) order, the four tokens, the formation, the rerolls.
    /// A pure function of the picks; the listing order of the packs does not matter; every value has exactly one encoding (the reader is strict), so a form decodes back to the
    /// same bytes. Typically 12 to 30 bytes.
    /// </summary>
    public static byte[] Canonical(IReadOnlyList<DraftPick> units, DoctrineSet tokens, int formation, int rerolls)
    {
        if (units.Count > MaxUnits) throw new ArgumentException($"at most {MaxUnits} packs in one round", nameof(units));
        var w = new ByteWriter();
        w.Byte((byte)'D'); w.Byte((byte)'U'); w.Byte((byte)'1');
        w.Var(units.Count);
        foreach (var p in units.OrderBy(p => p.Kind).ThenBy(p => p.Count)) { w.Var(p.Kind); w.Var(p.Count); }
        w.Var(tokens.S0); w.Var(tokens.S1); w.Var(tokens.S2); w.Var(tokens.S3);
        w.Var(formation); w.Var(rerolls);
        return w.ToArray();
    }

    /// <summary>Read canonical bytes back. Never throws: anything that is not exactly what <see cref="Canonical()"/> writes (wrong magic, truncated, trailing bytes, packs out of order, over-long numbers) returns false with a reason.</summary>
    public static bool TryParse(ReadOnlySpan<byte> canonical, out DuelPicks? picks, out string error)
    {
        picks = null; error = "";
        try
        {
            var r = new ByteReader(canonical.ToArray());
            if (r.Byte() != (byte)'D' || r.Byte() != (byte)'U' || r.Byte() != (byte)'1') { error = "not duel picks"; return false; }
            int n = r.Count();
            if (n > MaxUnits) { error = "too many packs"; return false; }
            var units = new List<DraftPick>(n);
            for (int i = 0; i < n; i++)
            {
                var p = new DraftPick(r.Var(), r.Var());
                if (i > 0 && (p.Kind < units[i - 1].Kind || (p.Kind == units[i - 1].Kind && p.Count < units[i - 1].Count))) { error = "packs out of order"; return false; }
                units.Add(p);
            }
            var tokens = new DoctrineSet(r.Var(), r.Var(), r.Var(), r.Var());
            int formation = r.Var(), rerolls = r.Var();
            if (!r.AtEnd) { error = "trailing bytes"; return false; }
            picks = new DuelPicks(units, tokens, formation, rerolls);
            return true;
        }
        catch (FormatException e) { error = e.Message; picks = null; return false; }
    }

    /// <summary>
    /// The commitment: SHA-256 over a tag, the ROUND, the SEAT, the canonical picks and the nonce (each length-prefixed). The nonce hides the picks (a seat's choices are few, so
    /// without it the other seat could try them all); the round and seat stop a commitment from being copied into the other seat's slot or another round. 32 bytes.
    /// </summary>
    public static byte[] Commit(ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> nonce, int round, Seat seat) => Commit(canonical, nonce, round, seat, DuelProbe.None);

    internal static byte[] Commit(ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> nonce, int round, Seat seat, DuelProbe probe)
    {
        if (nonce.Length < MinNonce) throw new ArgumentException($"the nonce must be at least {MinNonce} bytes", nameof(nonce));
        var w = new ByteWriter();
        w.Str(CommitTag);
        w.Var(round);
        w.Byte(probe.HasFlag(DuelProbe.CommitIgnoresSeat) ? (byte)0 : (byte)seat);
        w.Bytes(canonical);
        w.Bytes(probe.HasFlag(DuelProbe.CommitIgnoresNonce) ? ReadOnlySpan<byte>.Empty : nonce);
        return Sha256.Hash(w.ToArray());
    }

    /// <summary>True when <paramref name="commit"/> is the commitment of exactly these bytes, this nonce, this round and this seat. Never throws; a short nonce is simply false.</summary>
    public static bool Verify(ReadOnlySpan<byte> commit, ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> nonce, int round, Seat seat) => Verify(commit, canonical, nonce, round, seat, DuelProbe.None);

    internal static bool Verify(ReadOnlySpan<byte> commit, ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> nonce, int round, Seat seat, DuelProbe probe)
    {
        if (commit.Length != 32 || nonce.Length < MinNonce) return false;
        if (probe.HasFlag(DuelProbe.VerifyAcceptsTampered)) return true;
        var expect = Commit(canonical, nonce, round, seat, probe);
        int diff = 0;
        for (int i = 0; i < 32; i++) diff |= expect[i] ^ commit[i];   // no early exit
        return diff == 0;
    }

    public bool Equals(DuelPicks? other) => other is not null && Canonical().AsSpan().SequenceEqual(other.Canonical());
    public override bool Equals(object? obj) => Equals(obj as DuelPicks);
    public override int GetHashCode() { var h = new HashCode(); foreach (var b in Canonical()) h.Add(b); return h.ToHashCode(); }
    public override string ToString() => $"[{string.Join(",", Units.Select(p => $"{Roster.NameOf(Math.Clamp(p.Kind, 0, Roster.Size - 1))}x{p.Count}"))}] {Doctrine} form {Formation} rerolls {Rerolls}";
}

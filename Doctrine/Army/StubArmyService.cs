using System.Globalization;
using System.Text;

namespace Doctrine.Army;

// STUB for seam (a), owned by phasor-owner. Plain arrays stand in for the Pleroma stores so the whole game runs headless today.
// The real section is written beside this file (folder Doctrine\Army\) and must pass ArmyContractTests. This file STAYS: it is the
// reference the shared mutants and the headless default are built on (SECTIONS.md). Only tests and composition roots may name
// these types; the game itself reaches armies only through IArmyService.

public sealed class StubArmyService : IArmyService
{
    public IArmy Create(Side side, int seed)
    {
        var a = new StubArmy(side);
        var rng = new GameRng(GameRng.Mix(seed, (int)side));
        a.Seed(rng);
        return a;
    }

    public bool TryParse(string code, out IArmy? army, out string error)
    {
        army = null; error = "";
        try
        {
            if (string.IsNullOrEmpty(code) || code.Length > 1000) { error = "empty or too long"; return false; }
            var parts = code.Split('.');
            if (parts.Length != 8 || parts[0] != "DA1") { error = "not an army code"; return false; }
            var a = new StubArmy((Side)int.Parse(parts[1], CultureInfo.InvariantCulture))
            {
                Round = int.Parse(parts[2], CultureInfo.InvariantCulture),
                Gold = int.Parse(parts[3], CultureInfo.InvariantCulture),
                FlipMask = (byte)int.Parse(parts[4], CultureInfo.InvariantCulture),
                AuraMask = (byte)int.Parse(parts[5], CultureInfo.InvariantCulture),
                AuraPct = int.Parse(parts[6], CultureInfo.InvariantCulture),
            };
            if (a.Side is not (Side.Player or Side.Enemy) || a.Round < 0 || a.Gold < 0) { error = "bad header"; return false; }
            foreach (var c in parts[7].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = c.Split(':');
                int k = int.Parse(f[0], CultureInfo.InvariantCulture), n = int.Parse(f[1], CultureInfo.InvariantCulture);
                int s = int.Parse(f[2], CultureInfo.InvariantCulture), v = int.Parse(f[3], CultureInfo.InvariantCulture);
                if ((uint)k >= Roster.Size || n < 0 || (uint)s >= DoctrineVocabulary.Groups || (uint)v >= StubArmy.VeterancyPeriod) { error = "bad cohort"; return false; }
                a.Count[k] = n; a.Slot[k] = s; a.Vet[k] = v;
            }
            army = a; return true;
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException)
        {
            error = e.Message; return false;
        }
    }
}

public sealed class StubArmy(Side side) : IArmy
{
    public Side Side { get; } = side;
    /// <summary>Veterancy is a position on a ring of this many places (CONTRACT.md a): it wraps, it never grows without limit.</summary>
    internal const int VeterancyPeriod = 9;
    internal int Round, Gold, AuraPct;
    internal byte FlipMask, AuraMask;
    internal readonly int[] Count = new int[Roster.Size];
    internal readonly int[] Slot = new int[Roster.Size];
    internal readonly int[] Vet = new int[Roster.Size];

    internal void Seed(GameRng rng)
    {
        for (int k = 0; k < Roster.Size; k++) Slot[k] = k % DoctrineVocabulary.Groups;
        Count[0] = 18 + rng.Next(5); Count[3] = 8 + rng.Next(5); Gold = 12;
    }

    Trait TraitsOf(int k) => (Trait)((byte)Roster.TraitsOf(k) ^ FlipMask);

    int Power(int k)
    {
        int p = 100 + Vet[k] * 5;
        if (((int)TraitsOf(k) & AuraMask) != 0) p += AuraPct;
        return p;
    }

    public ArmyReadModel Read()
    {
        var cohorts = new List<CohortRead>();
        for (int k = 0; k < Roster.Size; k++)
            if (Count[k] > 0) cohorts.Add(new(k, Count[k], TraitsOf(k), Slot[k], Vet[k], Power(k)));
        return new(Side, Round, Gold, cohorts, Legal());
    }

    LegalMask Legal()
    {
        var m = LegalMask.None;
        int soldiers = Count.Sum();
        if (soldiers == 0) return m;
        for (int s = 1; s <= 2; s++) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, s));
        for (int b = 0; b < 8; b++) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b));
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < Roster.Size; k++)
            if (Count[k] > 0 && Count[(k + 1) % Roster.Size] > 0 && (k + 1) % Roster.Size != k)
                m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        for (int b = 0; b < 8; b++)
        {
            int with = 0, without = 0;
            for (int k = 0; k < Roster.Size; k++)
                if (Count[k] > 0) { if (((int)TraitsOf(k) >> b & 1) != 0) with++; else without++; }
            if (with > 0) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
            if (with > 0 && without > 0) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Cull, b));
        }
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
        {
            bool any = false;
            for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0 && Slot[k] == g) any = true;
            if (!any) continue;
            for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
        }
        return m;
    }

    public int Score()
    {
        int s = 0;
        for (int k = 0; k < Roster.Size; k++) s += Count[k] * Roster.PriceOf(k) * Power(k) / 100;
        return s + Gold;
    }

    public ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct)
    {
        var ok = new List<DraftPick>();
        var no = new List<(DraftPick, string)>();
        int spent = 0;
        foreach (var p in request.Picks)
        {
            if ((uint)p.Kind >= Roster.Size || p.Count <= 0) { no.Add((p, "bad pick")); continue; }
            if (Count[p.Kind] == 0 && Count.Count(c => c > 0) >= Roster.Size) { no.Add((p, "command span")); continue; }
            int cost = Roster.PriceOf(p.Kind) * p.Count;
            if (cost > Gold) { no.Add((p, "gold")); continue; }
            Gold -= cost; spent += cost; Count[p.Kind] += p.Count; ok.Add(p);
        }
        return ValueTask.FromResult(new DraftReport(ok, no, spent));
    }

    public ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(Doctrine doctrine, CancellationToken ct)
    {
        int applied = 0, skipped = 0;
        foreach (var o in DoctrineVocabulary.ArmyOrders(doctrine))
        {
            // Legality is judged per token against the army the earlier tokens left (CONTRACT.md a), so it is read fresh each time.
            if (!Legal().Allows(DoctrineVocabulary.Encode(o))) { skipped++; continue; }
            switch ((ArmyOrderKind)o.Kind)
            {
                case ArmyOrderKind.Rotate: for (int k = 0; k < Roster.Size; k++) Slot[k] = (Slot[k] + o.A) % DoctrineVocabulary.Groups; break;
                case ArmyOrderKind.Flip: FlipMask ^= (byte)(1 << o.A); break;
                case ArmyOrderKind.Age: for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) Vet[k] = (Vet[k] + 1) % VeterancyPeriod; break;
                case ArmyOrderKind.Merge:
                    { int k = o.A, j = (k + 1) % Roster.Size; Count[k] += Count[j]; Count[j] = 0; break; }
                case ArmyOrderKind.Aura:
                    { AuraMask |= (byte)(1 << o.A); int n = 0; for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0 && ((int)TraitsOf(k) >> o.A & 1) != 0) n += Count[k]; AuraPct = Math.Max(AuraPct, Math.Min(50, n)); break; }
                case ArmyOrderKind.Cull:
                    for (int k = 0; k < Roster.Size; k++)
                        if (Count[k] > 0 && ((int)TraitsOf(k) >> o.A & 1) != 0) { Gold += Count[k]; Count[k] = 0; }
                    break;
            }
            applied++;
        }
        return ValueTask.FromResult(new ArmyOrdersReport(applied, skipped));
    }

    public ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct)
    {
        int removed = 0;
        foreach (var l in losses)
        {
            if ((uint)l.Key >= Roster.Size || l.Lost <= 0) continue;
            int take = Math.Min(Count[l.Key], l.Lost);
            Count[l.Key] -= take; removed += take;
        }
        return ValueTask.FromResult(removed);
    }

    public ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct)
    {
        Gold += Math.Max(0, spoils.Gold);
        for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) Vet[k] = (int)((Vet[k] + (long)Math.Max(0, spoils.Experience)) % VeterancyPeriod);
        return ValueTask.CompletedTask;
    }

    public ValueTask EndRoundAsync(CancellationToken ct)
    {
        Round++; AuraMask = 0; AuraPct = 0;
        return ValueTask.CompletedTask;
    }

    public string ToCode()
    {
        var sb = new StringBuilder("DA1.");
        sb.Append((int)Side).Append('.').Append(Round).Append('.').Append(Gold).Append('.').Append(FlipMask)
          .Append('.').Append(AuraMask).Append('.').Append(AuraPct).Append('.');
        for (int k = 0; k < Roster.Size; k++)
            sb.Append(k).Append(':').Append(Count[k]).Append(':').Append(Slot[k]).Append(':').Append(Vet[k]).Append(';');
        return sb.ToString();
    }
}

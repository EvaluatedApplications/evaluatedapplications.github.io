using System.Globalization;
using System.Text;

namespace Doctrine.Army;

// STUB for seam (a), owned by phasor-owner. Plain arrays stand in for the Pleroma stores so the whole game runs headless today.
// The real section is written beside this file (folder Doctrine\Army\) and must pass ArmyContractTests. This file STAYS: it is the
// reference the shared mutants and the headless default are built on (SECTIONS.md). Only tests and composition roots may name
// these types; the game itself reaches armies only through IArmyService.

public sealed class StubArmyService(ArmyRules? rules = null) : IArmyService
{
    readonly ArmyRules _rules = rules ?? ArmyRules.Default;

    public IArmy Create(Side side, int seed)
    {
        var a = new StubArmy(side, _rules);
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
            var a = new StubArmy((Side)int.Parse(parts[1], CultureInfo.InvariantCulture), _rules)
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

public sealed class StubArmy(Side side, ArmyRules? rules = null) : IArmy
{
    public Side Side { get; } = side;
    readonly ArmyRules _rules = rules ?? ArmyRules.Default;
    /// <summary>Veterancy is a position on a ring of this many places (CONTRACT.md a): it wraps, it never grows without limit.</summary>
    internal const int VeterancyPeriod = Roster.VetPeriod;
    internal int Round, Gold, AuraPct;
    internal byte FlipMask, AuraMask;
    internal readonly int[] Count = new int[Roster.Size];
    internal readonly int[] Slot = new int[Roster.Size];
    internal readonly int[] Vet = new int[Roster.Size];

    internal void Seed(GameRng rng)
    {
        for (int k = 0; k < Roster.Size; k++) Slot[k] = k % DoctrineVocabulary.Groups;
        Count[0] = 18 + rng.Next(5); Slot[0] = LineForNew(0);
        Count[3] = 8 + rng.Next(5); Slot[3] = LineForNew(3);
        Gold = 12;
    }

    Trait TraitsOf(int k) => (Trait)((byte)Roster.TraitsOf(k) ^ FlipMask);

    /// <summary>The rank a NEW cohort of kind <paramref name="k"/> joins: the rules' placement over the cohorts standing now (the kind itself is not yet standing).</summary>
    int LineForNew(int k)
    {
        var standing = new List<(Trait, int, int)>();
        for (int j = 0; j < Roster.Size; j++) if (j != k && Count[j] > 0) standing.Add((TraitsOf(j), Count[j], Slot[j]));
        return _rules.LineFor(k, TraitsOf(k), standing);
    }

    /// <summary>Gold a Flip of <paramref name="bit"/> costs: soldiers x <see cref="Roster.RelabelCost"/> summed over the cohorts.</summary>
    long FlipCost(int bit)
    {
        long cost = 0;
        for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) cost += (long)Count[k] * Roster.RelabelCost(TraitsOf(k), bit, _rules.PriceReach);
        return cost;
    }

    /// <summary>Gold an Age costs now (0 when the rule is off): the same Roster rule as the Pleroma army.</summary>
    int AgeCost()
    {
        if (!_rules.PriceAge) return 0;
        long sum = 0;
        for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) sum += Count[k] * Roster.AgeWeight(TraitsOf(k), Vet[k], _rules.PriceReach);
        return Roster.GoldFor(sum);
    }

    int AuraPercentFor(int bit)
    {
        int n = 0;
        for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0 && ((int)TraitsOf(k) >> bit & 1) != 0) n += Count[k];
        return Math.Max(AuraPct, Math.Min(50, n));
    }

    /// <summary>Gold an Aura on <paramref name="bit"/> costs this round (0 when the rule is off): rented, spread over the rounds a body would have served.</summary>
    int AuraCost(int bit)
    {
        if (!_rules.PriceAura) return 0;
        int after = AuraPercentFor(bit), maskAfter = AuraMask | (1 << bit);
        long sum = 0;
        for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) sum += Count[k] * Roster.AuraWeight(TraitsOf(k), AuraMask, AuraPct, maskAfter, after, _rules.PriceReach);
        return Roster.GoldFor(sum, Roster.AuraRentalDivisor(Round));
    }

    int Power(int k) => Roster.PowerPct(Vet[k], (uint)TraitsOf(k), AuraMask, AuraPct);

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
        for (int b = 0; b < 8; b++) if (FlipCost(b) <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b));   // legal while the army can pay
        if (AgeCost() <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < Roster.Size; k++)
            if (Count[k] > 0 && Count[(k + 1) % Roster.Size] > 0 && (k + 1) % Roster.Size != k)
                m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        for (int b = 0; b < 8; b++)
        {
            int with = 0, without = 0;
            for (int k = 0; k < Roster.Size; k++)
                if (Count[k] > 0) { if (((int)TraitsOf(k) >> b & 1) != 0) with++; else without++; }
            if (with > 0 && (AuraMask >> b & 1) == 0 && AuraCost(b) <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
            if (with > 0 && without > 0) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Cull, b));
        }
        bool anyLine = false;
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
        {
            bool any = false;
            for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0 && Slot[k] == g) any = true;
            if (!any) continue;
            anyLine = true;
            for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
        }
        if (anyLine) for (int q = 1; q <= DoctrineVocabulary.TriggerLevels; q++) m = m.With(DoctrineVocabulary.WhenLost(q));
        return m;
    }

    public int Score()
    {
        int s = 0;
        for (int k = 0; k < Roster.Size; k++) s += Count[k] * _rules.DraftPrice(k) * Power(k) / 100;
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
            bool present = Count[p.Kind] > 0;
            if (!present && Count.Count(c => c > 0) >= Roster.Size) { no.Add((p, "command span")); continue; }
            int cost = _rules.DraftPrice(p.Kind) * p.Count;
            if (cost > Gold) { no.Add((p, "gold")); continue; }
            Gold -= cost; spent += cost;
            if (!present && _rules.BalancedPlacement) Slot[p.Kind] = LineForNew(p.Kind);      // a new cohort joins the rank the balance rule picks; an existing one keeps its rank
            Count[p.Kind] += p.Count; ok.Add(p);
        }
        return ValueTask.FromResult(new DraftReport(ok, no, spent));
    }

    public ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(DoctrineSet doctrine, CancellationToken ct)
    {
        int applied = 0, skipped = 0;
        foreach (var o in DoctrineVocabulary.ArmyOrders(doctrine))
        {
            // Legality is judged per token against the army the earlier tokens left (CONTRACT.md a), so it is read fresh each time.
            if (!Legal().Allows(DoctrineVocabulary.Encode(o))) { skipped++; continue; }
            switch ((ArmyOrderKind)o.Kind)
            {
                case ArmyOrderKind.Rotate: for (int k = 0; k < Roster.Size; k++) Slot[k] = (Slot[k] + o.A) % DoctrineVocabulary.Groups; break;
                case ArmyOrderKind.Flip: Gold -= (int)FlipCost(o.A); FlipMask ^= (byte)(1 << o.A); break;
                case ArmyOrderKind.Age: Gold -= AgeCost(); for (int k = 0; k < Roster.Size; k++) if (Count[k] > 0) Vet[k] = (Vet[k] + 1) % VeterancyPeriod; break;
                case ArmyOrderKind.Merge:
                    { int k = o.A, j = (k + 1) % Roster.Size; Count[k] += Count[j]; Count[j] = 0; break; }
                case ArmyOrderKind.Aura:
                    { Gold -= AuraCost(o.A); AuraPct = AuraPercentFor(o.A); AuraMask |= (byte)(1 << o.A); break; }
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

    public ValueTask<bool> PayAsync(int gold, CancellationToken ct)
    {
        if (gold <= 0 || gold > Gold) return ValueTask.FromResult(false);
        Gold -= gold;
        return ValueTask.FromResult(true);
    }

    public ValueTask EndRoundAsync(CancellationToken ct)
    {
        Round++; AuraMask = 0; AuraPct = 0;
        if (_rules.BankInterest) Gold += (int)Roster.Interest(Gold);
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

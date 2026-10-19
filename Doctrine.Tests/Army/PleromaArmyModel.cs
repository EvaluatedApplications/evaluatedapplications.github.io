using Phasor;

namespace Doctrine.Tests;

// The BRUTE-FORCE MODEL of one army: plain arrays and the rules written out again, with no Pleroma anywhere. The Pleroma army
// must equal a store REBUILT from this model's cohorts, cell for cell, after every operation (as the Plenum proofs do).
// Only the design decisions are repeated here: Cull is never legal on this shape (Phasor CG1), a new cohort enters at slot kind mod 3
// with veterancy 0, an aura is legal once while it is not active, Score is the sum of count x price x power plus 100 per gold,
// a Flip costs the army the sum over its soldiers of Roster.RelabelCost and is legal only while the army can pay it; an Age costs the sum of
// soldiers x Roster.AgeWeight and an Aura (rented for the round) the sum of soldiers x Roster.AuraWeight over the rounds left, each legal only while
// the army can pay; banked gold pays Roster.Interest when a round closes; a contingency (WhenLost) is legal whenever a line has a cohort.
internal sealed class ArmyModel(Side side, ArmyRules? rules = null)
{
    public Side Side { get; } = side;
    readonly ArmyRules _rules = rules ?? ArmyRules.Default;
    public int Round, Gold, FlipMask;
    public readonly int[] Soldiers = new int[Roster.Size];
    public readonly int[] Slot = new int[Roster.Size];
    public readonly int[] Vet = new int[Roster.Size];
    public readonly int[] AuraPct = new int[DoctrineVocabulary.Traits];   // 0 = not active

    public int Traits(int k) => (int)Roster.TraitsOf(k) ^ FlipMask;
    public int Total => Soldiers.Sum();
    public int AuraMask { get { int m = 0; for (int b = 0; b < DoctrineVocabulary.Traits; b++) if (AuraPct[b] > 0) m |= 1 << b; return m; } }
    public int AuraMax => AuraPct.Max();
    public int Power(int k) => 100 + Roster.VetStepPct * Vet[k] + ((Traits(k) & AuraMask) != 0 ? AuraMax : 0);
    bool Present(int k) => Soldiers[k] > 0;

    public ArmyReadModel Read()
    {
        var cohorts = new List<CohortRead>();
        for (int k = 0; k < Roster.Size; k++)
            if (Present(k)) cohorts.Add(new(k, Soldiers[k], (Trait)Traits(k), Slot[k], Vet[k], Power(k)));
        return new ArmyReadModel(Side, Round, Gold, cohorts, Legal());
    }

    public LegalMask Legal()
    {
        var m = LegalMask.None;
        if (Total == 0) return m;
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1)).With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2));
        for (int b = 0; b < DoctrineVocabulary.Traits; b++) if (FlipCost(b) <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b));
        if (AgeCost() <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < Roster.Size; k++)
            if (Present(k) && Present((k + 1) % Roster.Size)) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        for (int b = 0; b < DoctrineVocabulary.Traits; b++)
        {
            bool anyWith = Enumerable.Range(0, Roster.Size).Any(k => Present(k) && ((Traits(k) >> b) & 1) != 0);
            if (AuraPct[b] == 0 && anyWith && AuraCost(b) <= Gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
        }
        bool anyLine = false;
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
            if (Enumerable.Range(0, Roster.Size).Any(k => Present(k) && Slot[k] == g))
            {
                anyLine = true;
                for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
            }
        if (anyLine) for (int q = 1; q <= DoctrineVocabulary.TriggerLevels; q++) m = m.With(DoctrineVocabulary.WhenLost(q));
        return m;
    }

    public int Score()
    {
        long s = 0;
        for (int k = 0; k < Roster.Size; k++) s += (long)Soldiers[k] * Roster.PriceOf(k) * Power(k);
        return (int)Math.Clamp(s + 100L * Gold, 0, int.MaxValue);
    }

    // ---- the rules, one line each ----
    public void Draft(int kind, int count)
    {
        if (!Present(kind)) { Slot[kind] = kind % DoctrineVocabulary.Groups; Vet[kind] = 0; }
        Soldiers[kind] += count;
        Gold -= Roster.PriceOf(kind) * count;
    }
    public void Rotate(int shift) { for (int k = 0; k < Roster.Size; k++) Slot[k] = (Slot[k] + shift) % DoctrineVocabulary.Groups; }
    /// <summary>A flip is paid for: every soldier whose relabelled traits are worth more costs the difference (Roster.RelabelCost), the rest are free.</summary>
    public long FlipCost(int bit)
    {
        long cost = 0;
        for (int k = 0; k < Roster.Size; k++) if (Present(k)) cost += (long)Soldiers[k] * Roster.RelabelCost((Trait)Traits(k), bit, _rules.PriceReach);
        return cost;
    }
    public void Flip(int bit) { Gold -= (int)FlipCost(bit); FlipMask ^= 1 << bit; }
    /// <summary>An age is paid for: sum over the soldiers of worth x the power one more step gives, in gold, rounded up.</summary>
    public int AgeCost()
    {
        long sum = 0;
        for (int k = 0; k < Roster.Size; k++) if (Present(k)) sum += (long)Soldiers[k] * Roster.AgeWeight((Trait)Traits(k), Vet[k], _rules.PriceReach);
        return _rules.PriceAge ? Roster.GoldFor(sum) : 0;
    }
    public void Age() { Gold -= AgeCost(); for (int k = 0; k < Roster.Size; k++) if (Present(k)) Vet[k] = (Vet[k] + 1) % PleromaArmyLayout.Period; }
    int AuraPctIf(int bit)
    {
        int n = 0;
        for (int k = 0; k < Roster.Size; k++) if (Present(k) && ((Traits(k) >> bit) & 1) != 0) n += Soldiers[k];
        return Math.Max(AuraMax, Math.Min(PleromaArmyLayout.AuraCap, n));
    }
    /// <summary>An aura is rented for the round: sum of worth x the power it adds, over the rounds a body would have served.</summary>
    public int AuraCost(int bit)
    {
        long sum = 0; int after = AuraPctIf(bit), maskAfter = AuraMask | (1 << bit);
        for (int k = 0; k < Roster.Size; k++) if (Present(k)) sum += (long)Soldiers[k] * Roster.AuraWeight((Trait)Traits(k), AuraMask, AuraMax, maskAfter, after, _rules.PriceReach);
        return _rules.PriceAura ? Roster.GoldFor(sum, Roster.AuraRentalDivisor(Round)) : 0;
    }
    public void Merge(int k)
    {
        int j = (k + 1) % Roster.Size;
        Soldiers[k] += Soldiers[j];
        Soldiers[j] = 0;
    }
    public void Aura(int bit)
    {
        Gold -= AuraCost(bit);
        int n = 0;
        for (int k = 0; k < Roster.Size; k++) if (Present(k) && ((Traits(k) >> bit) & 1) != 0) n += Soldiers[k];
        AuraPct[bit] = Math.Min(PleromaArmyLayout.AuraCap, n);
    }
    public void Losses(int kind, int lost)
    {
        if (lost <= 0) return;
        Soldiers[kind] -= Math.Min(Soldiers[kind], lost);
    }
    public void Spoils(int gold, int experience)
    {
        Gold += Math.Max(0, gold);
        if (experience > 0) for (int k = 0; k < Roster.Size; k++) if (Present(k)) Vet[k] = (Vet[k] + experience) % PleromaArmyLayout.Period;
    }
    public void EndRound() { Round++; Array.Clear(AuraPct); if (_rules.BankInterest) Gold += (int)Roster.Interest(Gold); }

    /// <summary>A store built from scratch out of this model's cohorts: the independent reference for the cell comparison.</summary>
    public Pleroma Rebuild()
    {
        var p = new Pleroma(PleromaArmyLayout.ArmyShape);
        for (int k = 0; k < Roster.Size; k++)
            if (Present(k)) p.Add(new HologramItem((ulong)k, (uint)Traits(k), PleromaArmyLayout.SlotArrangement[Slot[k]], Vet[k]), Soldiers[k]);
        return p;
    }
}

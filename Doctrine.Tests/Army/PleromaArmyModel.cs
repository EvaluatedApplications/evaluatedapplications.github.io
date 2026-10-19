using Phasor;

namespace Doctrine.Tests;

// The BRUTE-FORCE MODEL of one army: plain arrays and the rules written out again, with no Pleroma anywhere. The Pleroma army
// must equal a store REBUILT from this model's cohorts, cell for cell, after every operation (as the Plenum proofs do).
// Only the design decisions are repeated here: Cull is never legal on this shape (Phasor CG1), a new cohort enters at slot kind mod 3
// with veterancy 0, an aura is legal once while it is not active, Score is the sum of count x price x power plus 100 per gold,
// a Flip costs the army the sum over its soldiers of Roster.RelabelCost and is legal only while the army can pay it.
internal sealed class ArmyModel(Side side)
{
    public Side Side { get; } = side;
    public int Round, Gold, FlipMask;
    public readonly int[] Soldiers = new int[Roster.Size];
    public readonly int[] Slot = new int[Roster.Size];
    public readonly int[] Vet = new int[Roster.Size];
    public readonly int[] AuraPct = new int[DoctrineVocabulary.Traits];   // 0 = not active

    public int Traits(int k) => (int)Roster.TraitsOf(k) ^ FlipMask;
    public int Total => Soldiers.Sum();
    public int AuraMask { get { int m = 0; for (int b = 0; b < DoctrineVocabulary.Traits; b++) if (AuraPct[b] > 0) m |= 1 << b; return m; } }
    public int AuraMax => AuraPct.Max();
    public int Power(int k) => 100 + Vet[k] * 5 + ((Traits(k) & AuraMask) != 0 ? AuraMax : 0);
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
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < Roster.Size; k++)
            if (Present(k) && Present((k + 1) % Roster.Size)) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        for (int b = 0; b < DoctrineVocabulary.Traits; b++)
        {
            bool anyWith = Enumerable.Range(0, Roster.Size).Any(k => Present(k) && ((Traits(k) >> b) & 1) != 0);
            if (AuraPct[b] == 0 && anyWith) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
        }
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
            if (Enumerable.Range(0, Roster.Size).Any(k => Present(k) && Slot[k] == g))
                for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
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
        for (int k = 0; k < Roster.Size; k++) if (Present(k)) cost += (long)Soldiers[k] * Roster.RelabelCost((Trait)Traits(k), bit);
        return cost;
    }
    public void Flip(int bit) { Gold -= (int)FlipCost(bit); FlipMask ^= 1 << bit; }
    public void Age() { for (int k = 0; k < Roster.Size; k++) if (Present(k)) Vet[k] = (Vet[k] + 1) % PleromaArmyLayout.Period; }
    public void Merge(int k)
    {
        int j = (k + 1) % Roster.Size;
        Soldiers[k] += Soldiers[j];
        Soldiers[j] = 0;
    }
    public void Aura(int bit)
    {
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
    public void EndRound() { Round++; Array.Clear(AuraPct); }

    /// <summary>A store built from scratch out of this model's cohorts: the independent reference for the cell comparison.</summary>
    public Pleroma Rebuild()
    {
        var p = new Pleroma(PleromaArmyLayout.ArmyShape);
        for (int k = 0; k < Roster.Size; k++)
            if (Present(k)) p.Add(new HologramItem((ulong)k, (uint)Traits(k), PleromaArmyLayout.SlotArrangement[Slot[k]], Vet[k]), Soldiers[k]);
        return p;
    }
}

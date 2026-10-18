namespace Doctrine.Tests;

// A simulated player for the general's tests (the probe's Sim, EXP-GEN-2 version): five deterministic styles over the 14-token summary plus legality, 10% noise.

/// <summary>A simulated situation: the 14-token summary the general sees, the legal mask the army would publish.</summary>
public sealed record Situation(int[] S, LegalMask Legal);

public static class GeneralSim
{
    /// <summary>A random army and the summary + mask the real pipeline would hand the general. Legal mask follows PleromaArmy.Legal().</summary>
    public static Situation Make(GameRng r)
    {
        int round = r.Next(10);
        int gold = r.Next(60);
        int nk = 2 + r.Next(5);
        var kinds = new List<int>();
        while (kinds.Count < nk) { int k = r.Next(Roster.Size); if (!kinds.Contains(k)) kinds.Add(k); }
        var cohorts = new List<CohortRead>();
        var slotUsed = new bool[DoctrineVocabulary.Groups];
        var present = new bool[Roster.Size];
        var traitPresent = new bool[DoctrineVocabulary.Traits];
        foreach (var k in kinds)
        {
            int slot = r.Next(DoctrineVocabulary.Groups);
            int count = 8 + r.Next(53);
            cohorts.Add(new CohortRead(k, count, Roster.TraitsOf(k), slot, 0, 100));
            slotUsed[slot] = true; present[k] = true;
            for (int b = 0; b < DoctrineVocabulary.Traits; b++) if (((int)Roster.TraitsOf(k) >> b & 1) != 0) traitPresent[b] = true;
        }
        var m = LegalMask.None;
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1)).With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2));
        for (int b = 0; b < 8; b++) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b));
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < Roster.Size; k++) if (present[k] && present[(k + 1) % Roster.Size]) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        for (int b = 0; b < 8; b++) if (traitPresent[b]) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
            if (slotUsed[g]) for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
        var own = new ArmyReadModel(Side.Player, round, gold, cohorts, m);
        ArmyReadModel? scouted = null;
        if (r.Next(100) < 80)
            scouted = new ArmyReadModel(Side.Enemy, round, 0, [new CohortRead(0, 40 + r.Next(361), Trait.None, 0, 0, 100)], LegalMask.None);
        BattleStatus? last = null; bool won = false;
        if (r.Next(100) >= 20) { last = (BattleStatus)(1 + r.Next(3)); won = last == BattleStatus.AttackerWon; }
        return new Situation(GeneralSummary.Build(own, scouted, last, won).Summary.ToArray(), m);
    }

    // trait indices (Trait bit order): Heavy0 Swift1 Ranged2 Holy3 Cursed4 Beast5 Machine6 Shielded7; summary s[4+b]
    const int Heavy = 0, Swift = 1, Ranged = 2, Holy = 3, Cursed = 4, Beast = 5, Machine = 6, Shielded = 7;
    static int Aura(int b) => DoctrineVocabulary.Army(ArmyOrderKind.Aura, b);
    static int Flip(int b) => DoctrineVocabulary.Army(ArmyOrderKind.Flip, b);
    static int Fld(FieldOrderKind k, int g) => DoctrineVocabulary.Field(k, g);
    static int Age => DoctrineVocabulary.Army(ArmyOrderKind.Age);
    static int Rot(int s) => DoctrineVocabulary.Army(ArmyOrderKind.Rotate, s);
    static int Merge(int k) => DoctrineVocabulary.Army(ArmyOrderKind.Merge, k);

    public static readonly string[] StyleNames = ["aggressive", "turtle", "mirror", "tinkerer", "cautious"];

    /// <summary>The player's NOISE-FREE doctrine for a style. A desired token that is not legal is dropped (the player skips it); max 3 slots.</summary>
    public static Doctrine Clean(int style, Situation st)
    {
        var s = st.S; int tb(int b) => s[4 + b];
        int[] want = style switch
        {
            0 => [tb(Swift) >= 1 ? Aura(Swift) : tb(Beast) >= 1 ? Aura(Beast) : Age,
                  s[12] >= 6 ? Fld(FieldOrderKind.Flank, 0) : Fld(FieldOrderKind.Advance, 0),
                  s[13] == 4 ? Fld(FieldOrderKind.Hunt, 2) : Fld(FieldOrderKind.Advance, 2)],
            1 => [tb(Shielded) >= 1 ? Aura(Shielded) : tb(Heavy) >= 1 ? Aura(Heavy) : Rot(1),
                  s[2] >= 4 ? Fld(FieldOrderKind.Guard, 0) : Fld(FieldOrderKind.Hold, 0),
                  s[1] < 6 ? Fld(FieldOrderKind.FallBack, 2) : Fld(FieldOrderKind.Hold, 1)],
            2 => MirrorWant(s),
            4 => [s[2] >= 3 && s[1] < 8 ? Fld(FieldOrderKind.Hold, 0) : Fld(FieldOrderKind.Advance, 0),
                  tb(Holy) >= 1 && (s[13] == 5 || s[13] == 7) ? Aura(Holy) : Flip(Heavy),
                  s[12] >= 5 && s[3] >= 4 ? Fld(FieldOrderKind.Guard, 1) : Fld(FieldOrderKind.Hunt, 1)],
            _ => [s[0] < 3 ? Age : Rot(1 + s[0] % 2),
                  Flip(s[0] % 8),
                  s[2] >= 3 ? Fld(FieldOrderKind.Hold, 1) : Fld(FieldOrderKind.Advance, 1)],
        };
        return Pack(want, st.Legal);
    }

    static int[] MirrorWant(int[] s)
    {
        int best = -1, bv = 0;
        for (int b = 0; b < 8; b++) if (s[4 + b] > bv) { bv = s[4 + b]; best = b; }
        if (best < 0) return [Age, 0, s[3] >= 5 ? Merge(0) : 0];
        var f = best switch { Heavy or Shielded => FieldOrderKind.Hold, Swift or Beast => FieldOrderKind.Flank, Ranged or Machine => FieldOrderKind.Guard, _ => FieldOrderKind.Hunt };
        return [Aura(best), Fld(f, 0), s[3] >= 5 ? Merge(0) : 0];
    }

    /// <summary>Keep wanted tokens that are legal, not End, not repeated; at most 3; pad with End.</summary>
    public static Doctrine Pack(IEnumerable<int> want, LegalMask legal)
    {
        var l = new List<int>();
        foreach (var t in want) if (t != 0 && legal.Allows(t) && !l.Contains(t) && l.Count < 3) l.Add(t);
        return Doctrine.Of(l.ToArray());
    }

    /// <summary>The player's actual choice: the style's doctrine, or with 10% a random doctrine of 1..3 distinct legal tokens.</summary>
    public static Doctrine Choose(int style, Situation st, GameRng r)
    {
        if (r.Next(100) >= 10) return Clean(style, st);
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(st.Legal.Allows).ToList();
        int n = 1 + r.Next(3);
        var l = new List<int>();
        while (l.Count < n && pool.Count > 0) { int i = r.Next(pool.Count); l.Add(pool[i]); pool.RemoveAt(i); }
        return Doctrine.Of(l.ToArray());
    }

    /// <summary>Stream of states for (seed, kind): train and eval draw from disjoint streams.</summary>
    public static (Situation[] States, Doctrine[][] ByStyle, Doctrine[][] CleanByStyle) Draw(int seed, int kind, int n)
    {
        var r = new GameRng(GameRng.Mix(seed, 7000 + kind));
        var states = new Situation[n];
        for (int i = 0; i < n; i++) states[i] = Make(r);
        var by = new Doctrine[5][]; var cl = new Doctrine[5][];
        for (int s = 0; s < 5; s++)
        {
            var rn = new GameRng(GameRng.Mix(seed, 8000 + kind * 10 + s));
            by[s] = new Doctrine[n]; cl[s] = new Doctrine[n];
            for (int i = 0; i < n; i++) { by[s][i] = Choose(s, states[i], rn); cl[s][i] = Clean(s, states[i]); }
        }
        return (states, by, cl);
    }
}

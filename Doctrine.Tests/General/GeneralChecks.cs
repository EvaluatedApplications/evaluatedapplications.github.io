namespace Doctrine.Tests;

/// <summary>
/// The general's OWN checks, beyond the shared contract. Each returns null when it holds, else why not, so the same check can bind the real
/// general (must be null) and a deliberately broken one in the mutation proofs (must NOT be null).
/// </summary>
public static class GeneralChecks
{
    static GeneralInput In(Situation s) => new(s.S[0], s.S, s.Legal);

    /// <summary>Fewer replay passes than the shipped 96 so the fast suite stays fast; every rule under test is the same.</summary>
    public static GeneralOptions Fast(GeneralMode mode = GeneralMode.Hybrid) => new() { Mode = mode, PassesPerLearn = 24, PassCap = 1200, PassesAfterCap = 8 };

    public static async Task Teach(IGeneral g, int style, int seed, int n)
    {
        var (st, by, _) = GeneralSim.Draw(seed, 0, n);
        for (int i = 0; i < n; i++) await g.LearnAsync(new GeneralExample(In(st[i]), by[style][i], false), default);
    }

    public static async Task<double> Exact(IGeneral g, int style, int seed, int n = 200)
    {
        var (st, by, _) = GeneralSim.Draw(seed, 1, n);
        int hit = 0;
        for (int i = 0; i < n; i++) if ((await g.SuggestAsync(In(st[i]), default)).Doctrine == by[style][i]) hit++;
        return hit / (double)n;
    }

    static async Task<DoctrineSet[]> Answers(IGeneral g, int seed, int n)
    {
        var (st, _, _) = GeneralSim.Draw(seed, 2, n);
        var r = new DoctrineSet[n];
        for (int i = 0; i < n; i++) r[i] = (await g.SuggestAsync(In(st[i]), default)).Doctrine;
        return r;
    }

    /// <summary>Nothing is pre-trained in ANY mode: a new general suggests no orders with no confidence, for every kind of situation.</summary>
    public static async Task<string?> StartsEmpty(Func<IGeneral> make)
    {
        var g = make();
        var (st, _, _) = GeneralSim.Draw(77, 3, 30);
        foreach (var s in st)
        {
            var o = await g.SuggestAsync(In(s), default);
            if (!o.Doctrine.IsEmpty || o.ConfidencePermille != 0) return $"a new general suggested {o.Doctrine} at confidence {o.ConfidencePermille}";
        }
        return g.ExamplesSeen == 0 ? null : "a new general has seen examples";
    }

    /// <summary>The net alone, taught with every order legal, is then asked under masks that forbid what it learned: every answer must obey the mask.</summary>
    public static async Task<string?> NetStaysInsideTheMask(Func<IGeneral> make)
    {
        var g = make();
        await Teach(g, 0, 5, 30);
        var rng = new GameRng(404);
        var (st, _, _) = GeneralSim.Draw(5, 4, 60);
        int differed = 0;
        foreach (var s in st)
        {
            var full = await g.SuggestAsync(In(s), default);
            var mask = new LegalMask(rng.NextU64() & s.Legal.Bits | 1UL);
            var o = await g.SuggestAsync(new GeneralInput(s.S[0], s.S, mask), default);
            if (!mask.AllowsAll(o.Doctrine)) return $"mask {mask.Bits:x} let {o.Doctrine} through";
            if (o.Doctrine != full.Doctrine) differed++;
        }
        return differed > 5 ? null : "the masks never changed an answer, so this check proves nothing";
    }

    /// <summary>Two generals taught the same way answer the same way, confidence included, however many cores ran the training.</summary>
    public static async Task<string?> SameTeachingSameAnswers(Func<IGeneral> make)
    {
        var a = make(); var b = make();
        await Teach(a, 1, 6, 25); await Teach(b, 1, 6, 25);
        var (st, _, _) = GeneralSim.Draw(6, 5, 40);
        foreach (var s in st)
        {
            var x = await a.SuggestAsync(In(s), default); var y = await b.SuggestAsync(In(s), default);
            if (x != y) return $"{x} vs {y}";
        }
        return null;
    }

    /// <summary>Generals raised by different players disagree on fresh situations far more than two generals raised by the same player.</summary>
    public static async Task<string?> PlayersStayApart(Func<IGeneral> make)
    {
        var aggressive1 = make(); var aggressive2 = make(); var turtle1 = make();
        await Teach(aggressive1, 0, 1, 60); await Teach(aggressive2, 0, 2, 60); await Teach(turtle1, 1, 1, 60);
        var a1 = await Answers(aggressive1, 9, 200); var a2 = await Answers(aggressive2, 9, 200); var t1 = await Answers(turtle1, 9, 200);
        double cross = Enumerable.Range(0, 200).Count(i => a1[i] != t1[i]) / 200.0, same = Enumerable.Range(0, 200).Count(i => a1[i] != a2[i]) / 200.0;
        return cross >= same + 0.15 ? null : $"different players disagree {cross:F2}, one player's two generals {same:F2}";
    }

    /// <summary>A habitual player is learned: from 60 of their own choices the ghost reproduces the whole doctrine about 4 times in 10 (noise bounds it near 0.9).</summary>
    public static async Task<string?> HabitIsLearned(Func<IGeneral> make)
    {
        double sum = 0; int n = 0;
        foreach (var style in new[] { 0, 1 }) foreach (var seed in new[] { 1, 2 })
        {
            var g = make(); await Teach(g, style, seed, 60); sum += await Exact(g, style, seed); n++;
        }
        double exact = sum / n;
        return exact >= 0.30 ? null : $"exact doctrine match after 60 choices was {exact:F2}";
    }

    /// <summary>Lost battles take a choice's weight away, won battles give it back (lookup alone, so the arithmetic is exact).</summary>
    public static async Task<string?> LostBattlesErodeAndWinsRestore(Func<IGeneral> make)
    {
        var g = make();
        var s = GeneralSim.Draw(8, 6, 1).States[0];
        var a = DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.Field(FieldOrderKind.Advance, 0));
        var legal = LegalMask.All;
        var input = new GeneralInput(s.S[0], s.S, legal);
        for (int i = 0; i < 3; i++) await g.LearnAsync(new GeneralExample(input, a, true), default);
        if ((await g.SuggestAsync(input, default)).Doctrine != a) return "the taught choice was not suggested";
        for (int i = 0; i < 6; i++) await g.LearnOutcomeAsync(new GeneralOutcome(input, a, BattleStatus.DefenderWon, false), default);
        var after = (await g.SuggestAsync(input, default)).Doctrine;
        if (!after.IsEmpty) return $"after six lost battles the choice was still suggested: {after}";
        for (int i = 0; i < 2; i++) await g.LearnOutcomeAsync(new GeneralOutcome(input, a, BattleStatus.AttackerWon, true), default);
        return (await g.SuggestAsync(input, default)).Doctrine == a ? null : "two won battles did not bring the choice back";
    }

    // ---- the per-slot match record (SlotConfidence) ----------------------------------------------------------------------

    /// <summary>A player with no habit: 1..3 distinct legal tokens, uniformly.</summary>
    public static DoctrineSet RandomChoice(Situation st, GameRng r)
    {
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(st.Legal.Allows).ToList();
        int n = 1 + r.Next(3); var l = new List<int>();
        while (l.Count < n && pool.Count > 0) { int i = r.Next(pool.Count); l.Add(pool[i]); pool.RemoveAt(i); }
        return DoctrineSet.Of(l.ToArray());
    }

    /// <summary>
    /// Teach one example at a time and, before each, ask the general what it would show; keep the record by hand (a slot counts when the shown or
    /// the chosen token is not End; a hit when they are equal; halve everything when 64 examples have passed) and require the reported figure to be
    /// 1000*hits/(counted+2), at most the measured rate, in range, and 0 before anything was seen.
    /// </summary>
    public static async Task<string?> SlotRecordMatchesBruteForce(Func<IGeneral> make, int style, int seed, int n, bool random = false)
    {
        var g = make();
        var (st, by, cl) = GeneralSim.Draw(seed, 0, n);
        var rr = new GameRng(GameRng.Mix(seed, 991));
        int trials = 0; var hit = new int[4]; var cnt = new int[4];
        for (int i = 0; i < n; i++)
        {
            var input = In(st[i]);
            var o = await g.SuggestAsync(input, default);
            if (o.SlotConfidence is not { } sc) return $"example {i}: no SlotConfidence";
            for (int k = 0; k < 4; k++)
            {
                int want = 1000 * hit[k] / (cnt[k] + 2);
                if (sc[k] != want) return $"example {i} slot {k}: reported {sc[k]}, recomputed {want} (hits {hit[k]} of {cnt[k]})";
                if (sc[k] < 0 || sc[k] > 1000) return $"example {i} slot {k}: {sc[k]} out of range";
                if (cnt[k] > 0 && sc[k] * cnt[k] > 1000 * hit[k]) return $"example {i} slot {k}: {sc[k]} is above the measured rate {hit[k]}/{cnt[k]}";
                if (cnt[k] == 0 && sc[k] != 0) return $"example {i} slot {k}: {sc[k]} with nothing counted";
            }
            var chosen = random ? RandomChoice(st[i], rr) : cl[style][i];
            var shown = o.Doctrine;
            var want4 = Pack(chosen, st[i].Legal);
            trials++;
            for (int k = 0; k < 4; k++) if (shown[k] != 0 || want4[k] != 0) { cnt[k]++; if (shown[k] == want4[k]) hit[k]++; }
            if (trials >= 64) { trials /= 2; for (int k = 0; k < 4; k++) { hit[k] /= 2; cnt[k] /= 2; } }
            await g.LearnAsync(new GeneralExample(input, chosen, false), default);
        }
        return null;
    }

    // what the general compares with: the chosen tokens that are legal, not End, not repeated (the mask rule, applied to the player's side)
    static DoctrineSet Pack(DoctrineSet d, LegalMask legal)
    {
        var l = new List<int>();
        foreach (var t in new[] { d.S0, d.S1, d.S2, d.S3 }) if (t != 0 && legal.Allows(t) && !l.Contains(t) && l.Count < 4) l.Add(t);
        return DoctrineSet.Of(l.ToArray());
    }

    /// <summary>The figures after the first <paramref name="n"/> examples of a player (null general output means absent).</summary>
    public static async Task<SlotPermille?> SlotFiguresAfter(IGeneral g, int style, int seed, int n, bool random)
    {
        var (st, _, cl) = GeneralSim.Draw(seed, 0, n); var rr = new GameRng(GameRng.Mix(seed, 991));
        for (int i = 0; i < n; i++) await g.LearnAsync(new GeneralExample(In(st[i]), random ? RandomChoice(st[i], rr) : cl[style][i], false), default);
        var probe = GeneralSim.Draw(seed, 1, 1).States[0];
        return (await g.SuggestAsync(In(probe), default)).SlotConfidence;
    }

    /// <summary>A new general starts at zero in every slot, however much an earlier general learned (no record leaks between generals).</summary>
    public static async Task<string?> NewGeneralStartsAtZero(Func<IGeneral> make)
    {
        var old = make(); await SlotFiguresAfter(old, 0, 3, 30, false);
        var fresh = make();
        var o = await fresh.SuggestAsync(In(GeneralSim.Draw(3, 1, 1).States[0]), default);
        return o.SlotConfidence is { } s && s.S0 == 0 && s.S1 == 0 && s.S2 == 0 && s.S3 == 0 ? null : $"a new general reported {o.SlotConfidence}";
    }

    public static Func<IGeneral> Real(GeneralMode mode) => () => new TinyHoloGeneral(Fast(mode));
    internal static Func<IGeneral> Broken(GeneralMode mode, GeneralFault fault) => () => new TinyHoloGeneral(Fast(mode), fault);
}

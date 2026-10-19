namespace Doctrine.Tests;

// THE OFFERS (roguelike round, 2026-10-19; Doctrine\todo\difficulty-2026-10-19.md, fourth round). The contract of an IOfferSource, stated once and run against
// the game's SeededOffers AND against every mutant below: a case nobody has watched fail is only an assertion.

/// <summary>An offer source and the rules it is asked under (a mutant may break the rules instead of the source).</summary>
public sealed record OfferSut(IOfferSource Source, OfferRules Rules);

public static class OfferContract
{
    static LegalMask LegalOf(int seed, int extraGold = 0)
    {
        var a = new PleromaArmyService().Create(Side.Player, seed);
        if (extraGold > 0) a.ApplySpoilsAsync(new Spoils(extraGold, 0), default).AsTask().GetAwaiter().GetResult();
        return a.Read().Legal;
    }

    /// <summary>The legal lists the cases draw from: a fresh levy, a rich levy (Flip, Age, Aura legal), the whole vocabulary, one line only, no army orders.</summary>
    static IEnumerable<(string Name, LegalMask Legal)> Legals()
    {
        yield return ("fresh", LegalOf(5));
        yield return ("rich", LegalOf(9, 500));
        yield return ("all", LegalMask.All);
        yield return ("scarce line", ScarceLine());
        yield return ("scarce army", ScarceArmy());
    }

    static LegalMask OnlyLine(int line)
    {
        var m = LegalMask.None;
        for (int t = 1; t < DoctrineVocabulary.Size; t++)
        {
            var o = DoctrineVocabulary.Decode(t);
            if (o.Family == OrderFamily.Army || (o.Family == OrderFamily.Field && o.A == line)) m = m.With(t);
        }
        return m;
    }

    /// <summary>Fields of line 0 and ONE field order of line 1 (and the army orders): the two-lines guarantee binds, a random draw would miss the lone token.</summary>
    static LegalMask ScarceLine()
    {
        var m = LegalMask.None;
        for (int t = 1; t < DoctrineVocabulary.Size; t++)
        {
            var o = DoctrineVocabulary.Decode(t);
            if (o.Family == OrderFamily.Army || (o.Family == OrderFamily.Field && o.A == 0)) m = m.With(t);
        }
        return m.With(DoctrineVocabulary.Field(FieldOrderKind.Hold, 1));
    }

    /// <summary>All field orders and ONE army order: the army guarantee binds.</summary>
    static LegalMask ScarceArmy()
    {
        var m = LegalMask.None.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int t = 1; t < DoctrineVocabulary.Size; t++) if (DoctrineVocabulary.Decode(t).Family != OrderFamily.Army) m = m.With(t);
        return m;
    }

    static LegalMask NoArmy()
    {
        var m = LegalMask.None;
        for (int t = 1; t < DoctrineVocabulary.Size; t++) if (DoctrineVocabulary.Decode(t).Family != OrderFamily.Army) m = m.With(t);
        return m;
    }

    static int Count(IEnumerable<int> tokens, OrderFamily f) => tokens.Count(t => DoctrineVocabulary.Decode(t).Family == f);
    static int Lines(IEnumerable<int> tokens) => tokens.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).Select(t => DoctrineVocabulary.Decode(t).A).Distinct().Count();
    static bool Same(OrderOffer a, OrderOffer b) => a.Tokens.SequenceEqual(b.Tokens);
    static bool Same(UnitOffer a, UnitOffer b) => a.Packs.SequenceEqual(b.Packs);
    static int BaseOf(int budget, int kind, OfferRules r) => Math.Max(1, budget / (r.PackShare * Roster.PriceOf(kind)));

    public static IReadOnlyList<ContractCase<OfferSut>> Cases { get; } =
    [
        new("orders-are-a-function-of-seed-round-reroll-and-legal-list", s =>
        {
            var legal = LegalOf(5, 40);
            int differSeed = 0, differRound = 0, differReroll = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var a = s.Source.Orders(seed, 2, 0, legal, s.Rules);
                Expect.True(Same(a, s.Source.Orders(seed, 2, 0, legal, s.Rules)), $"seed {seed}: the same (seed, round, reroll, legal) must give the same offer");
                if (!Same(a, s.Source.Orders(seed + 1000, 2, 0, legal, s.Rules))) differSeed++;
                if (!Same(a, s.Source.Orders(seed, 3, 0, legal, s.Rules))) differRound++;
                if (!Same(a, s.Source.Orders(seed, 2, 1, legal, s.Rules))) differReroll++;
            }
            Expect.True(differSeed >= 28, $"a different seed must offer something else (it did in {differSeed} of 30)");
            Expect.True(differRound >= 28, $"a different round must offer something else (it did in {differRound} of 30)");
            Expect.True(differReroll >= 28, $"a reroll must offer something else (it did in {differReroll} of 30)");
            return Task.CompletedTask;
        }),
        new("orders-are-distinct-legal-sorted-and-at-most-eight", s =>
        {
            foreach (var (name, legal) in Legals())
                for (int seed = 1; seed <= 60; seed++)
                {
                    var o = s.Source.Orders(seed, seed % 5, seed % 3, legal, s.Rules);
                    Expect.True(o.Active, $"{name}/{seed}: an offer under the game's rules is active");
                    int want = Math.Min(8, Enumerable.Range(1, DoctrineVocabulary.Size - 1).Count(legal.Allows));
                    Expect.Equal(want, o.Tokens.Count, $"{name}/{seed}: eight tokens, or all of them when fewer are legal");
                    Expect.Equal(o.Tokens.Count, o.Tokens.Distinct().Count(), $"{name}/{seed}: a token is never offered twice");
                    Expect.True(o.Tokens.All(t => t > 0 && DoctrineVocabulary.IsValid(t)), $"{name}/{seed}: End and ids outside the vocabulary are never offered");
                    Expect.True(o.Tokens.All(legal.Allows), $"{name}/{seed}: an offered token must be legal at the moment of the offer");
                    Expect.True(o.Tokens.SequenceEqual(o.Tokens.OrderBy(t => t)), $"{name}/{seed}: tokens are sorted by id");
                    Expect.True(o.Mask.AllowsAll(DoctrineSet.Of(o.Tokens.Take(4).ToArray())) && !o.Mask.Allows(-1), $"{name}/{seed}: the mask names the offered tokens");
                }
            return Task.CompletedTask;
        }),
        new("orders-guarantee-three-field-orders-on-two-lines-and-an-army-order", s =>
        {
            // The spec numbers are written here, not read from the rules under test: a section that drops a guarantee must fail.
            foreach (var (name, legal) in Legals())
                for (int seed = 1; seed <= 80; seed++)
                {
                    var t = s.Source.Orders(seed, seed % 7, 0, legal, s.Rules).Tokens;
                    int legalField = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Count(x => legal.Allows(x) && DoctrineVocabulary.Decode(x).Family == OrderFamily.Field);
                    int legalLines = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(x => legal.Allows(x) && DoctrineVocabulary.Decode(x).Family == OrderFamily.Field).Select(x => DoctrineVocabulary.Decode(x).A).Distinct().Count();
                    int legalArmy = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Count(x => legal.Allows(x) && DoctrineVocabulary.Decode(x).Family == OrderFamily.Army);
                    Expect.True(Count(t, OrderFamily.Field) >= Math.Min(3, legalField), $"{name}/{seed}: at least 3 field orders ({Count(t, OrderFamily.Field)} of {legalField} legal)");
                    Expect.True(Lines(t) >= Math.Min(2, legalLines), $"{name}/{seed}: the field orders touch at least 2 lines ({Lines(t)} of {legalLines})");
                    Expect.True(Count(t, OrderFamily.Army) >= Math.Min(1, legalArmy), $"{name}/{seed}: at least 1 army order when one is legal");
                }
            // when the list cannot give a guarantee, the offer gives what there is: one line only, or no army order at all
            foreach (var (name, legal) in new[] { ("one line", OnlyLine(1)), ("no army", NoArmy()) })
                for (int seed = 1; seed <= 40; seed++)
                {
                    var t = s.Source.Orders(seed, 1, 0, legal, s.Rules).Tokens;
                    Expect.True(Count(t, OrderFamily.Field) >= 3, $"{name}/{seed}: three field orders are still on offer");
                    if (name == "one line") { Expect.Equal(1, Lines(t), $"{name}/{seed}: only one line is legal"); Expect.True(Count(t, OrderFamily.Army) >= 1, $"{name}/{seed}: army order guaranteed"); }
                    else { Expect.Equal(0, Count(t, OrderFamily.Army), $"{name}/{seed}: none legal, none offered"); Expect.True(Lines(t) >= 2, $"{name}/{seed}: two lines"); }
                }
            return Task.CompletedTask;
        }),
        new("orders-draw-only-from-the-hosts-pool", s =>
        {
            // a pool that widens with the round: round 0 offers only even ids up to 20, round 3 any even id
            var rules = s.Rules with { OrderPool = (round, t) => t % 2 == 0 && (round >= 3 || t <= 20) };
            var legal = LegalMask.All;
            for (int seed = 1; seed <= 40; seed++)
            {
                var early = s.Source.Orders(seed, 0, 0, legal, rules).Tokens;
                var late = s.Source.Orders(seed, 3, 0, legal, rules).Tokens;
                Expect.True(early.All(t => t % 2 == 0 && t <= 20), $"seed {seed}: round 0 offered a token outside the pool: {string.Join(",", early)}");
                Expect.True(late.All(t => t % 2 == 0), $"seed {seed}: round 3 offered a token outside the pool: {string.Join(",", late)}");
            }
            var wide = Enumerable.Range(1, 40).SelectMany(seed => s.Source.Orders(seed, 3, 0, legal, rules).Tokens);
            Expect.True(wide.Any(t => t > 20), "a widened pool must be used");
            return Task.CompletedTask;
        }),
        new("orders-offer-fewer-than-eight-when-fewer-are-legal", s =>
        {
            var two = LegalMask.None.With(5).With(9);
            Expect.True(s.Source.Orders(3, 0, 0, two, s.Rules).Tokens.SequenceEqual([5, 9]), "two legal tokens are both offered");
            Expect.Equal(0, s.Source.Orders(3, 0, 0, LegalMask.None, s.Rules).Tokens.Count, "nothing legal, nothing offered");
            return Task.CompletedTask;
        }),
        new("units-are-a-function-of-seed-round-and-budget", s =>
        {
            int differSeed = 0, differRound = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var a = s.Source.Units(seed, 1, 40, s.Rules);
                Expect.True(Same(a, s.Source.Units(seed, 1, 40, s.Rules)), $"seed {seed}: the same (seed, round, budget) must give the same packs");
                if (!Same(a, s.Source.Units(seed + 1000, 1, 40, s.Rules))) differSeed++;
                if (!Same(a, s.Source.Units(seed, 2, 40, s.Rules))) differRound++;
            }
            Expect.True(differSeed >= 28, $"a different seed must offer other packs (it did in {differSeed} of 30)");
            Expect.True(differRound >= 28, $"a different round must offer other packs (it did in {differRound} of 30)");
            return Task.CompletedTask;
        }),
        new("units-are-affordable-multiples-of-a-base-pack-scaled-to-the-budget", s =>
        {
            int[] multiples = [1, 2, 3, 5];            // the declared set, written here and not read from the rules under test
            foreach (int budget in new[] { 12, 20, 33, 60, 200, 1000 })
                for (int seed = 1; seed <= 40; seed++)
                {
                    var o = s.Source.Units(seed, seed % 6, budget, s.Rules);
                    Expect.True(o.Active && o.Budget == budget, $"{budget}/{seed}: an active offer that knows its budget");
                    Expect.True(o.Packs.Count <= 6, $"{budget}/{seed}: at most 6 packs");
                    Expect.Equal(o.Packs.Count, o.Packs.Select(p => p.Kind).Distinct().Count(), $"{budget}/{seed}: a kind is offered once");
                    int buyable = Enumerable.Range(0, Roster.Size).Count(k => Roster.PriceOf(k) <= budget);
                    Expect.Equal(Math.Min(6, buyable), o.Packs.Count, $"{budget}/{seed}: 6 packs, or one for every kind the purse can buy at all");
                    Expect.True(o.Packs.Count(p => p.Cost <= budget) >= Math.Min(2, buyable), $"{budget}/{seed}: at least 2 packs the purse can pay");
                    foreach (var p in o.Packs)
                    {
                        Expect.True((uint)p.Kind < Roster.Size && p.Count > 0, $"{budget}/{seed}: a real kind and a positive count");
                        Expect.Equal(p.Count * Roster.PriceOf(p.Kind), p.Cost, $"{budget}/{seed}: cost is count x Roster.PriceOf(kind)");
                        Expect.True(p.Cost <= budget && p.Affordable, $"{budget}/{seed}: no pack costs more than the budget ({p})");
                        int basePack = BaseOf(budget, p.Kind, OfferRules.Default);
                        Expect.True(p.Count % basePack == 0 && multiples.Contains(p.Count / basePack), $"{budget}/{seed}: count {p.Count} of kind {p.Kind} is not a declared multiple of its base pack {basePack}");
                    }
                }
            return Task.CompletedTask;
        }),
        new("units-grow-with-the-budget", s =>
        {
            double Mean(int budget) => Enumerable.Range(1, 60).Average(seed => s.Source.Units(seed, 0, budget, s.Rules).Packs.Sum(p => p.Count));
            Expect.True(Mean(60) > 2 * Mean(12), $"packs offered to 60 gold must hold more than twice the soldiers of 12 gold ({Mean(60):F1} against {Mean(12):F1})");
            return Task.CompletedTask;
        }),
        new("units-draw-only-from-the-hosts-pool-and-flag-a-teaser", s =>
        {
            var rules = s.Rules with { KindPool = (round, k) => k is 0 or 3 or 5 || (round >= 2 && k == 7) };
            for (int seed = 1; seed <= 30; seed++)
            {
                Expect.True(s.Source.Units(seed, 0, 60, rules).Packs.All(p => p.Kind is 0 or 3 or 5), $"seed {seed}: round 0 offered a kind outside the pool");
                Expect.True(s.Source.Units(seed, 2, 60, rules).Packs.All(p => p.Kind is 0 or 3 or 5 or 7), $"seed {seed}: round 2 offered a kind outside the pool");
            }
            Expect.True(Enumerable.Range(1, 30).Any(seed => s.Source.Units(seed, 2, 60, rules).Packs.Any(p => p.Kind == 7)), "a widened pool must be used");
            var teaser = s.Rules with { AllowUnaffordable = true };
            int flagged = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var o = s.Source.Units(seed, 1, 12, teaser);
                Expect.True(o.Packs.Count <= 6, $"seed {seed}: a teaser never makes a seventh pack");
                Expect.Equal(o.Packs.Count, o.Packs.Select(p => p.Kind).Distinct().Count(), $"seed {seed}: a teaser never repeats a kind");
                foreach (var p in o.Packs) { Expect.Equal(p.Cost <= 12, p.Affordable, $"seed {seed}: Affordable must say whether the purse can pay {p}"); if (!p.Affordable) flagged++; }
                Expect.True(o.Packs.Count(p => p.Affordable) >= 2, $"seed {seed}: the teaser must not push the affordable packs under 2");
            }
            Expect.True(flagged > 0, "the teaser rule must flag at least one pack over 40 seeds");
            return Task.CompletedTask;
        }),
        new("a-closed-market-offers-nothing", s =>
        {
            var off = s.Rules with { Enabled = false };
            var u = s.Source.Units(3, 0, 40, off); var o = s.Source.Orders(3, 0, 0, LegalMask.All, off);
            Expect.True(!u.Active && u.Packs.Count == 0, "offers off: no packs, nothing refused");
            Expect.True(!o.Active && o.Tokens.Count == 0 && o.Allows(17) && u.PackFor(new DraftPick(0, 7)) is null, "offers off: an open order offer allows any token");
            return Task.CompletedTask;
        }),
    ];

    public static OfferSut Real() => new(SeededOffers.Instance, OfferRules.Default);
}

public enum OfferMut
{
    /// <summary>The offer ignores the game's seed.</summary>
    IgnoresSeed,
    /// <summary>The offer ignores the round.</summary>
    IgnoresRound,
    /// <summary>A reroll gives the first offer again.</summary>
    IgnoresReroll,
    /// <summary>One illegal token among the eight.</summary>
    IllegalToken,
    /// <summary>One token twice.</summary>
    DuplicateToken,
    /// <summary>A ninth token.</summary>
    NinthToken,
    /// <summary>The guarantees are dropped (the rules switch).</summary>
    GuaranteeDropped,
    /// <summary>The host's pool is ignored.</summary>
    PoolIgnored,
    /// <summary>The first pack costs one more than count x price.</summary>
    WrongCost,
    /// <summary>The first pack's count is not a multiple of the base pack.</summary>
    OddCount,
    /// <summary>The first pack costs more than the whole purse.</summary>
    OverBudgetPack,
    /// <summary>The same kind twice.</summary>
    KindTwice,
    /// <summary>The packs do not grow with the budget.</summary>
    BudgetIgnored,
    /// <summary>The offered packs are never scaled: always the budget-12 packs.</summary>
    NeverMorePacksThanTwo,
}

/// <summary>Breaks ONE rule of the offers over any source (the offer twin of the army and battle mutants).</summary>
public sealed class MutantOfferSource(IOfferSource inner, OfferMut m) : IOfferSource
{
    public UnitOffer Units(int seed, int round, int budget, OfferRules rules)
    {
        var o = inner.Units(m == OfferMut.IgnoresSeed ? 0 : seed, m == OfferMut.IgnoresRound ? 0 : round, m == OfferMut.BudgetIgnored ? 12 : budget, rules);
        var p = o.Packs.ToList();
        if (p.Count == 0) return o;
        switch (m)
        {
            case OfferMut.WrongCost: p[0] = p[0] with { Cost = p[0].Cost + 1 }; break;
            case OfferMut.OddCount: p[0] = p[0] with { Count = p[0].Count + 1, Cost = (p[0].Count + 1) * Roster.PriceOf(p[0].Kind) }; break;
            case OfferMut.OverBudgetPack: p[0] = p[0] with { Count = budget + 1, Cost = (budget + 1) * Roster.PriceOf(p[0].Kind) }; break;
            case OfferMut.KindTwice: if (p.Count > 1) p[1] = p[1] with { Kind = p[0].Kind, Cost = p[1].Count * Roster.PriceOf(p[0].Kind) }; break;
            case OfferMut.NeverMorePacksThanTwo: p = p.Take(2).ToList(); break;
        }
        return o with { Budget = budget, Packs = p };
    }

    public OrderOffer Orders(int seed, int round, int reroll, LegalMask legal, OfferRules rules)
    {
        var o = inner.Orders(m == OfferMut.IgnoresSeed ? 0 : seed, m == OfferMut.IgnoresRound ? 0 : round, m == OfferMut.IgnoresReroll ? 0 : reroll, legal,
            m == OfferMut.GuaranteeDropped ? rules with { Guarantees = false } : m == OfferMut.PoolIgnored ? rules with { OrderPool = null } : rules);
        var t = o.Tokens.ToList();
        if (t.Count == 0) return o;
        switch (m)
        {
            case OfferMut.IllegalToken:
                {
                    int bad = Enumerable.Range(1, DoctrineVocabulary.Size - 1).FirstOrDefault(x => !legal.Allows(x));
                    if (bad != 0) { t[^1] = bad; t.Sort(); }
                    break;
                }
            case OfferMut.DuplicateToken: if (t.Count > 1) t[^1] = t[0]; break;
            case OfferMut.NinthToken: { int extra = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(x => !t.Contains(x)); t.Add(extra); t.Sort(); break; }
        }
        return o with { Tokens = t };
    }
}

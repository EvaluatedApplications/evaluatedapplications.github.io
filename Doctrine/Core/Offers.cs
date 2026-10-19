namespace Doctrine;

// THE OFFERS (roguelike round, 2026-10-19; Doctrine\todo\difficulty-2026-10-19.md, fourth round). Every round the player does not choose from everything:
// the game OFFERS. At the start of the round a UnitOffer (6 packs of one kind, a count and a gold cost, scaled to the purse); after the draft an
// OrderOffer (8 distinct doctrine tokens drawn from the legal list as the draft left it; the player picks up to 4). Both are drawn from the game seed, so
// the same game offers the same things, and neither takes a coin from the stream the battle uses.

/// <summary>
/// One pack on offer: <see cref="Count"/> soldiers of <see cref="Kind"/> for <see cref="Cost"/> gold (always Count x Roster.PriceOf(Kind)). A pack is
/// bought whole or not at all: a pick is <c>new DraftPick(pack.Kind, pack.Count)</c>. <see cref="Affordable"/> is false only for a flagged teaser
/// (<see cref="OfferRules.AllowUnaffordable"/>); the army still judges gold either way.
/// </summary>
public readonly record struct UnitPack(int Kind, int Count, int Cost, bool Affordable = true);

/// <summary>
/// The packs on offer this round. <see cref="Active"/> false is the open market (<see cref="OfferRules.Enabled"/> off): every pick is allowed and
/// <see cref="Packs"/> is empty. <see cref="Budget"/> is the purse the packs were scaled to.
/// </summary>
public sealed record UnitOffer(bool Active, int Budget, IReadOnlyList<UnitPack> Packs)
{
    public static UnitOffer Open(int budget) => new(false, budget, Array.Empty<UnitPack>());

    /// <summary>The pack a pick buys, or null when no pack of the offer has exactly this kind and count.</summary>
    public UnitPack? PackFor(DraftPick pick)
    {
        foreach (var p in Packs) if (p.Kind == pick.Kind && p.Count == pick.Count) return p;
        return null;
    }

    /// <summary>The pick that buys <paramref name="pack"/>.</summary>
    public static DraftPick Pick(UnitPack pack) => new(pack.Kind, pack.Count);
}

/// <summary>
/// The doctrine tokens on offer this round, sorted by id, never one twice, End never. The player picks up to four of them (a pick is a token, each
/// at most once). <see cref="Rerolls"/> is how many times it has been rerolled for gold; <see cref="RerollsLeft"/> and <see cref="RerollCost"/> say
/// what another reroll takes. <see cref="Mask"/> has one bit per offered token (End included, as in every <see cref="LegalMask"/>).
/// </summary>
public sealed record OrderOffer(bool Active, IReadOnlyList<int> Tokens, int Rerolls, int RerollsLeft, int RerollCost)
{
    public static OrderOffer Open() => new(false, Array.Empty<int>(), 0, 0, 0);

    public LegalMask Mask
    {
        get
        {
            ulong bits = 1UL;
            foreach (var t in Tokens) bits |= 1UL << t;
            return new LegalMask(bits);
        }
    }

    /// <summary>True when the token may be picked: always for End and, on an open market, for everything.</summary>
    public bool Allows(int token) => token == 0 || !Active || Tokens.Contains(token);
}

/// <summary>
/// The rules of the offers. The defaults ARE the game; every number is a switch so a measurement can move one at a time and a mutant can break exactly
/// one. <see cref="Enabled"/> false (or <see cref="Off"/>) is the old free choice: nothing is offered, nothing refused.
/// </summary>
public sealed record OfferRules
{
    public static OfferRules Default { get; } = new();
    /// <summary>The old game: pick anything legal, buy any pack of anything.</summary>
    public static OfferRules Off { get; } = new() { Enabled = false };

    public bool Enabled { get; init; } = true;
    /// <summary>A pick or a token outside the offer is refused and counted (<see cref="RoundRecord.OfferRefused"/>). Off only as a mutant, to watch the contract go red.</summary>
    public bool Enforce { get; init; } = true;

    // ---- orders ----
    public int OrderOfferSize { get; init; } = 8;
    /// <summary>The guarantees below hold. Off only as a measurement null and as a mutant.</summary>
    public bool Guarantees { get; init; } = true;
    /// <summary>At least this many field orders when that many are legal (fewer legal: all of them).</summary>
    public int MinFieldOrders { get; init; } = 3;
    /// <summary>The field orders touch at least this many different lines (formation groups) when the legal list has that many.</summary>
    public int MinFieldLines { get; init; } = 2;
    /// <summary>At least this many army orders when that many are legal.</summary>
    public int MinArmyOrders { get; init; } = 1;
    /// <summary>Gold a reroll of the order offer costs (paid to the army).</summary>
    public int RerollCost { get; init; } = 1;
    public int MaxRerolls { get; init; } = 2;
    /// <summary>The host's pool: (round, token) true when the token may be offered that round. Null = every legal token. Progression widens it by round.</summary>
    public Func<int, int, bool>? OrderPool { get; init; }

    // ---- units ----
    public int PackCount { get; init; } = 6;
    /// <summary>The multiples of a kind's base pack that may be offered.</summary>
    public IReadOnlyList<int> Multiples { get; init; } = [1, 2, 3, 5];
    /// <summary>The base pack of a kind costs about 1/PackShare of the purse: base = max(1, purse / (PackShare x price)) soldiers.</summary>
    public int PackShare { get; init; } = 6;
    /// <summary>At least this many packs the purse can pay for, when that many kinds can be bought at all.</summary>
    public int MinAffordable { get; init; } = 2;
    /// <summary>One flagged teaser (the biggest multiple, over the purse) may be added. Off in the game: no pack costs more than the purse.</summary>
    public bool AllowUnaffordable { get; init; }
    /// <summary>The host's pool: (round, kind) true when the kind may be offered that round. Null = every kind.</summary>
    public Func<int, int, bool>? KindPool { get; init; }
}

/// <summary>Where offers come from. The game's is <see cref="SeededOffers"/>; a test or a host may stand in another (the contract cases run against any).</summary>
public interface IOfferSource
{
    /// <summary>The packs for a round: a function of (seed, round, budget, rules) alone.</summary>
    UnitOffer Units(int seed, int round, int budget, OfferRules rules);
    /// <summary>The order offer for a round and a reroll index (0 = the first offer): a function of (seed, round, reroll, legal, rules) alone.</summary>
    OrderOffer Orders(int seed, int round, int reroll, LegalMask legal, OfferRules rules);
}

/// <summary>The game's offers: drawn from the game seed on the game's splitmix64, in streams of their own ('units' and 'orders', per round and reroll).</summary>
public sealed class SeededOffers : IOfferSource
{
    public static SeededOffers Instance { get; } = new();

    const int UnitsTag = 0x756E6974, OrdersTag = 0x6F726473;       // 'unit', 'ords'

    static GameRng Stream(int seed, int round, int tag, int salt)
        => new(GameRng.Mix(unchecked((int)GameRng.Mix(unchecked((int)GameRng.Mix(seed, 5000 + round)), tag)), salt));

    static void Shuffle<T>(IList<T> a, GameRng rng)
    {
        for (int i = a.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    public UnitOffer Units(int seed, int round, int budget, OfferRules rules)
    {
        if (!rules.Enabled) return UnitOffer.Open(budget);
        var rng = Stream(seed, round, UnitsTag, 0);
        var kinds = Enumerable.Range(0, Roster.Size).Where(k => rules.KindPool?.Invoke(round, k) ?? true).ToList();
        Shuffle(kinds, rng);
        var packs = new List<UnitPack>();
        foreach (int k in kinds)
        {
            if (packs.Count >= rules.PackCount) break;
            int price = Roster.PriceOf(k);
            int basePack = Math.Max(1, budget / (Math.Max(1, rules.PackShare) * price));
            var fits = rules.Multiples.Where(m => m >= 1 && (long)m * basePack * price <= budget).ToList();
            if (fits.Count == 0) continue;                                        // the purse cannot buy even the base pack of this kind
            int mult = fits[rng.Next(fits.Count)];
            packs.Add(new UnitPack(k, mult * basePack, mult * basePack * price));
        }
        if (rules.AllowUnaffordable && packs.Count > 0 && rules.Multiples.Count > 0)
        {
            // one teaser: the biggest multiple of a kind whose biggest multiple is over the purse, flagged. It replaces the pack of its own kind, else the
            // last pack when the offer is full, so the offer never exceeds PackCount and the kinds stay distinct.
            int m = rules.Multiples.Max();
            var over = kinds.Where(k => (long)m * Math.Max(1, budget / (Math.Max(1, rules.PackShare) * Roster.PriceOf(k))) * Roster.PriceOf(k) > budget).ToList();
            if (over.Count > 0)
            {
                int k = over[rng.Next(over.Count)];
                int price = Roster.PriceOf(k), basePack = Math.Max(1, budget / (Math.Max(1, rules.PackShare) * price));
                long cost = (long)m * basePack * price;
                int same = packs.FindIndex(p => p.Kind == k);
                if (same >= 0) packs.RemoveAt(same); else if (packs.Count >= rules.PackCount) packs.RemoveAt(packs.Count - 1);
                packs.Add(new UnitPack(k, m * basePack, (int)Math.Min(int.MaxValue, cost), false));
            }
        }
        return new UnitOffer(true, budget, packs);
    }

    public OrderOffer Orders(int seed, int round, int reroll, LegalMask legal, OfferRules rules)
    {
        if (!rules.Enabled) return OrderOffer.Open();
        var rng = Stream(seed, round, OrdersTag, reroll);
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(t => legal.Allows(t) && (rules.OrderPool?.Invoke(round, t) ?? true)).ToList();
        Shuffle(pool, rng);
        var chosen = new List<int>();
        void Take(int t) { if (!chosen.Contains(t) && chosen.Count < rules.OrderOfferSize) chosen.Add(t); }
        if (rules.Guarantees)
        {
            // field orders: touch the lines first, then reach the count
            var field = pool.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).ToList();
            int lines = field.Select(t => DoctrineVocabulary.Decode(t).A).Distinct().Count();
            var seen = new HashSet<int>();
            foreach (var t in field)
            {
                if (seen.Count >= Math.Min(rules.MinFieldLines, lines)) break;
                if (seen.Add(DoctrineVocabulary.Decode(t).A)) Take(t);
            }
            foreach (var t in field) { if (chosen.Count(c => DoctrineVocabulary.Decode(c).Family == OrderFamily.Field) >= Math.Min(rules.MinFieldOrders, field.Count)) break; Take(t); }
            var army = pool.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army).ToList();
            foreach (var t in army) { if (chosen.Count(c => DoctrineVocabulary.Decode(c).Family == OrderFamily.Army) >= Math.Min(rules.MinArmyOrders, army.Count)) break; Take(t); }
        }
        foreach (var t in pool) Take(t);
        chosen.Sort();
        return new OrderOffer(true, chosen, reroll, Math.Max(0, rules.MaxRerolls - reroll), rules.RerollCost);
    }
}

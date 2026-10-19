namespace Doctrine;

// The SHARED VOCABULARY (seam d). Nobody owns it alone: a change here is a CONTRACT.md amendment that every section sees.

/// <summary>The eight traits of a cohort (8 bits). A kind's traits are a function of its key (see <see cref="Roster"/>).</summary>
[Flags]
public enum Trait : byte
{
    None = 0, Heavy = 1, Swift = 2, Ranged = 4, Holy = 8, Cursed = 16, Beast = 32, Machine = 64, Shielded = 128,
}

public enum Side : byte { Player = 0, Enemy = 1 }

/// <summary>
/// End pads. Army orders reach the whole army (Pleroma). Field orders steer one formation group. A Trigger token is not an order itself: it splits
/// the field orders of a doctrine into "opening" (before it) and "then" (after it), and says when "then" takes over (see <see cref="LossTrigger"/>).
/// </summary>
public enum OrderFamily : byte { End = 0, Army = 1, Field = 2, Trigger = 3 }

/// <summary>Army-wide orders: one pass over the whole army (Pleroma operations).</summary>
public enum ArmyOrderKind : byte
{
    Rotate = 0,  // A = shift 1..2 (formation slot relabel)
    Flip = 1,    // A = trait bit 0..7 (relabel flags)
    Age = 2,     // no argument (tick the veterancy ring)
    Merge = 3,   // A = kind k; merges kind (k+1) mod 8 into k
    Aura = 4,    // A = trait bit 0..7 (a condition over traits; effect scales with how many match)
    Cull = 5,    // A = trait bit 0..7 (restrict: dismiss cohorts carrying the trait)
}

/// <summary>Field orders: a Tracer behaviour for one formation group (front, middle, back).</summary>
public enum FieldOrderKind : byte { Advance = 0, Hold = 1, Flank = 2, FallBack = 3, Guard = 4, Hunt = 5 }

/// <summary>One decoded doctrine token. <c>End</c> means "no order in this slot".</summary>
public readonly record struct DoctrineOrder(OrderFamily Family, int Kind, int A = 0, int B = 0)
{
    public static readonly DoctrineOrder End = new(OrderFamily.End, 0);
    public bool IsEnd => Family == OrderFamily.End;
}

/// <summary>
/// The doctrine token set. Token 0 is End (also padding). Army orders then field orders, in a fixed table built once:
/// the ids are part of the wire contract (the general learns them, replays and army codes mention them).
/// </summary>
public static class DoctrineVocabulary
{
    public const int Groups = 3;           // formation slots: 0 front, 1 middle, 2 back (S_3 in the Pleroma)
    public const int Traits = 8;
    public const int Kinds = 8;            // the command span (KeyCapacity 8)
    public const int MaxSlots = 4;         // a doctrine is up to 4 slots per round
    public const int TriggerLevels = 3;    // WhenLost25, WhenLost50, WhenLost75: a quarter of the side's soldiers per step
    /// <summary>The <see cref="DoctrineOrder.Kind"/> of a trigger token. It is outside <see cref="ArmyOrderKind"/> and <see cref="FieldOrderKind"/> on purpose, so a host that switches on a kind lands in its default branch.</summary>
    public const int TriggerKind = 100;

    // VERSION 2 of the vocabulary (2026-10-19): tokens 0..53 are version 1 and unchanged (ids are wire data); 54..56 are the loss triggers.
    static readonly DoctrineOrder[] Table = Build();
    static DoctrineOrder[] Build()
    {
        var t = new List<DoctrineOrder> { DoctrineOrder.End };
        for (int s = 1; s <= 2; s++) t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Rotate, s));
        for (int b = 0; b < Traits; b++) t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Flip, b));
        t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Age));
        for (int k = 0; k < Kinds; k++) t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Merge, k));
        for (int b = 0; b < Traits; b++) t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Aura, b));
        for (int b = 0; b < Traits; b++) t.Add(new(OrderFamily.Army, (int)ArmyOrderKind.Cull, b));
        for (int g = 0; g < Groups; g++)
            for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) t.Add(new(OrderFamily.Field, f, g));
        for (int q = 1; q <= TriggerLevels; q++) t.Add(new(OrderFamily.Trigger, TriggerKind, q));
        return t.ToArray();
    }

    /// <summary>Number of tokens (including End). Always at most 64 so a <see cref="LegalMask"/> is one ulong.</summary>
    public static int Size => Table.Length;
    public static bool IsValid(int token) => (uint)token < (uint)Table.Length;
    public static DoctrineOrder Decode(int token) => IsValid(token) ? Table[token] : throw new ArgumentOutOfRangeException(nameof(token));
    public static int Encode(DoctrineOrder o)
    {
        for (int i = 0; i < Table.Length; i++) if (Table[i] == o) return i;
        throw new ArgumentException($"not a doctrine token: {o}");
    }
    public static int Army(ArmyOrderKind k, int a = 0) => Encode(new(OrderFamily.Army, (int)k, a));
    public static int Field(FieldOrderKind k, int group) => Encode(new(OrderFamily.Field, (int)k, group));
    /// <summary>The token "when this side has lost <paramref name="quarters"/> quarters of its soldiers" (1..<see cref="TriggerLevels"/>).</summary>
    public static int WhenLost(int quarters) => Encode(new(OrderFamily.Trigger, TriggerKind, quarters));
    public static string Name(int token)
    {
        var o = Decode(token);
        return o.Family switch
        {
            OrderFamily.End => "End",
            OrderFamily.Trigger => $"WhenLost{25 * o.A}",
            OrderFamily.Army => (ArmyOrderKind)o.Kind switch
            {
                ArmyOrderKind.Age => "Age",
                ArmyOrderKind.Rotate => $"Rotate{o.A}",
                _ => $"{(ArmyOrderKind)o.Kind}{o.A}",
            },
            _ => $"{(FieldOrderKind)o.Kind}@{o.A}",
        };
    }
    /// <summary>The slot of the first trigger token of a doctrine, or <see cref="MaxSlots"/> when it has none.</summary>
    static int TriggerSlot(DoctrineSet d)
    {
        for (int i = 0; i < MaxSlots; i++) if (Decode(d[i]).Family == OrderFamily.Trigger) return i;
        return MaxSlots;
    }

    static FieldOrder[] FieldOrdersIn(DoctrineSet d, int from, int to)
    {
        var byGroup = new FieldOrder?[Groups];
        for (int i = from; i < to; i++)
        {
            var o = Decode(d[i]);
            if (o.Family == OrderFamily.Field) byGroup[o.A] = new FieldOrder(o.A, (FieldOrderKind)o.Kind, 1);
        }
        return byGroup.Where(f => f is not null).Select(f => f!.Value).ToArray();
    }

    /// <summary>
    /// The field orders a battle OPENS with: the field orders carried by a doctrine, in slot order (a group named twice keeps the LAST word).
    /// When the doctrine has a trigger token (<see cref="Trigger"/>) only the slots before the first one count; the rest are the "then" orders.
    /// </summary>
    public static FieldOrder[] FieldOrders(DoctrineSet d) => FieldOrdersIn(d, 0, TriggerSlot(d));

    /// <summary>
    /// The loss trigger of a doctrine, or null when it has no trigger token. The FIRST trigger token splits the doctrine: its level says how many quarters
    /// of the side's soldiers must be lost, and the field orders after it (last word per group) replace the opening orders when that happens (a group not
    /// named after it advances, the default). Later trigger tokens are ignored.
    /// </summary>
    public static LossTrigger? Trigger(DoctrineSet d)
    {
        int at = TriggerSlot(d);
        if (at >= MaxSlots) return null;
        return new LossTrigger(Decode(d[at]).A, FieldOrdersIn(d, at + 1, MaxSlots));
    }
    /// <summary>The army orders carried by a doctrine, in slot order.</summary>
    public static DoctrineOrder[] ArmyOrders(DoctrineSet d)
    {
        var l = new List<DoctrineOrder>();
        for (int i = 0; i < MaxSlots; i++) { var o = Decode(d[i]); if (o.Family == OrderFamily.Army) l.Add(o); }
        return l.ToArray();
    }
}

/// <summary>A doctrine: up to four tokens (slot order matters; End = empty slot). A value type, so equality is free.</summary>
public readonly record struct DoctrineSet(int S0, int S1, int S2, int S3)
{
    public static readonly DoctrineSet Empty = default;
    public int this[int slot] => slot switch { 0 => S0, 1 => S1, 2 => S2, 3 => S3, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public static DoctrineSet Of(params int[] tokens)
    {
        if (tokens.Length > DoctrineVocabulary.MaxSlots) throw new ArgumentException("a doctrine has at most 4 slots");
        Span<int> s = stackalloc int[4];
        for (int i = 0; i < tokens.Length; i++) s[i] = tokens[i];
        return new(s[0], s[1], s[2], s[3]);
    }
    public int[] Tokens => [S0, S1, S2, S3];
    public int Used => Tokens.Count(t => t != 0);
    public bool IsEmpty => Used == 0;
    public override string ToString() => string.Join(" ", Tokens.Where(t => t != 0).Select(DoctrineVocabulary.Name));
}

/// <summary>A field order for one formation group. Weight splits an army in integer parts ("3 parts hold, 1 part flanks").</summary>
public readonly record struct FieldOrder(int Group, FieldOrderKind Kind, int Weight);

/// <summary>Which doctrine tokens the army says are legal right now. One bit per token id (the vocabulary is at most 64).</summary>
public readonly record struct LegalMask(ulong Bits)
{
    public static LegalMask None => new(1UL);   // End is always legal
    public static LegalMask All => new(DoctrineVocabulary.Size >= 64 ? ulong.MaxValue : (1UL << DoctrineVocabulary.Size) - 1);
    public bool Allows(int token) => DoctrineVocabulary.IsValid(token) && ((Bits >> token) & 1UL) != 0 || token == 0;
    public LegalMask With(int token) => new(Bits | (1UL << token));
    public LegalMask Without(int token) => token == 0 ? this : new(Bits & ~(1UL << token));
    public int Count => System.Numerics.BitOperations.PopCount(Bits);
    public bool AllowsAll(DoctrineSet d) => Allows(d.S0) && Allows(d.S1) && Allows(d.S2) && Allows(d.S3);
}

/// <summary>The roster: 8 kinds, each a named combination of traits and a price. One key, one trait set.</summary>
public static class Roster
{
    public const int Size = DoctrineVocabulary.Kinds;
    static readonly (string Name, Trait Traits, int Price)[] Kinds =
    [
        ("Spearmen", Trait.None, 1),
        ("Shieldwall", Trait.Heavy | Trait.Shielded, 2),
        ("Riders", Trait.Swift, 2),
        ("Archers", Trait.Ranged, 2),
        ("Priests", Trait.Holy | Trait.Ranged, 3),
        ("Wights", Trait.Cursed, 2),
        ("Wolves", Trait.Beast | Trait.Swift, 2),
        ("Catapults", Trait.Machine | Trait.Ranged | Trait.Heavy, 4),
    ];
    /// <summary>
    /// THE BALANCE RULE: the rank a NEW cohort joins, so that every rank is a mix of classes (each rank holds a spread of kinds and every kind works in any rank;
    /// shuffling the formation is Rotate, three configurations). The rank holding the FEWEST soldiers of the cohort's class (<see cref="Battle.UnitRules.ClassOf"/>
    /// of the live traits, read from the stat tables, never typed per kind); a tie goes to the rank with the fewest soldiers in all; a tie after that to the lowest
    /// rank index. A pure function of the cohorts already standing, so it is the same in every army section.
    /// </summary>
    public static int BalancedLine(Trait traits, IEnumerable<(Trait Traits, int Count, int Slot)> standing)
    {
        var cls = Battle.UnitRules.ClassOf(traits);
        var sameClass = new long[DoctrineVocabulary.Groups]; var all = new long[DoctrineVocabulary.Groups];
        foreach (var (t, n, slot) in standing)
        {
            if ((uint)slot >= DoctrineVocabulary.Groups || n <= 0) continue;
            all[slot] += n;
            if (Battle.UnitRules.ClassOf(t) == cls) sameClass[slot] += n;
        }
        int best = 0;
        for (int g = 1; g < DoctrineVocabulary.Groups; g++)
            if (sameClass[g] < sameClass[best] || (sameClass[g] == sameClass[best] && all[g] < all[best])) best = g;
        return best;
    }

    public static string NameOf(int kind) => Kinds[kind].Name;
    public static Trait TraitsOf(int kind) => Kinds[kind].Traits;
    /// <summary>The typed draft price of the old game (Spearmen 1, Shieldwall 2, Riders 2, Archers 2, Priests 3, Wights 2, Wolves 2, Catapults 4). Kept for the rules that switch <see cref="Army.ArmyRules.DerivedDraftPrices"/> off.</summary>
    public static int TypedPrice(int kind) => Kinds[kind].Price;
    /// <summary>What one soldier of the kind costs to draft in the GAME: the price the default rules name (derived from worth, a plain soldier costs 2; see <see cref="Army.ArmyRules"/>).</summary>
    public static int PriceOf(int kind) => Army.ArmyRules.Default.DraftPrice(kind);

    /// <summary>One veterancy step adds this many power points (the ring has <see cref="VetPeriod"/> places and wraps).</summary>
    public const int VetStepPct = 5;
    public const int VetPeriod = 9;
    /// <summary>The campaign the economy is priced for (<see cref="DoctrineDomain.MaxRounds"/> default): what a body serves, what an aura is rented against, what interest breaks even over.</summary>
    public const int CampaignRounds = 8;

    /// <summary>Power in percent: 100, plus <see cref="VetStepPct"/> per veterancy step, plus the aura bonus when the cohort carries a trait of an active aura.</summary>
    public static int PowerPct(int veterancy, uint traits, int auraMask, int auraPct)
        => 100 + VetStepPct * veterancy + ((traits & (uint)auraMask) != 0 ? auraPct : 0);

    /// <summary>
    /// What a soldier with these traits is WORTH in gold: ceil(2 x damage x (100 + reach) / (taken x 100)), read from the battle's own stat tables
    /// (<see cref="Battle.UnitRules"/>), reach being <see cref="Battle.UnitRules.ReachPct"/> (the free fire a longer weapon gets while a melee unit closes).
    /// It prices a RELABEL (<see cref="RelabelCost"/>), an Age and an Aura, and since the third round (todo\difficulty-2026-10-19.md) it IS the draft price
    /// too (<see cref="Army.ArmyRules.DerivedDraftPrices"/>: a plain soldier costs 2). With reach: Spearmen 2, Shieldwall 3, Riders 2, Archers 2, Priests 2, Wights 3, Wolves 3, Catapults 5; a Spearman flipped to
    /// Machine is worth 7. Without reach (<paramref name="reach"/> false): Catapults 4, that Machine 5.
    /// </summary>
    public static int TraitPrice(Trait t, bool reach = true)
    {
        long dmg = Battle.UnitRules.DamagePct(t), taken = Math.Max(1, Battle.UnitRules.TakenPct(t));
        long boost = reach ? 100 + Battle.UnitRules.ReachPct(t) : 100;
        return (int)((2 * dmg * boost + taken * 100 - 1) / (taken * 100));
    }

    /// <summary>
    /// What it costs to flip trait bit <paramref name="bit"/> on ONE soldier whose traits are <paramref name="traits"/>: the price difference when
    /// the new trait set is worth more (a better soldier is paid for), nothing when it is worth the same or less. An army-wide Flip costs the sum
    /// over its soldiers, so turning a levy into Machines is as dear as drafting Catapults.
    /// </summary>
    public static int RelabelCost(Trait traits, int bit, bool reach = true)
        => Math.Max(0, TraitPrice((Trait)((byte)traits ^ (1 << bit)), reach) - TraitPrice(traits, reach));

    /// <summary>Gold-percent one soldier adds to an Age: its worth times the power points one more veterancy step gives (a wrap back to 0 loses power and is free).</summary>
    public static long AgeWeight(Trait traits, int veterancy, bool reach)
        => (long)TraitPrice(traits, reach) * Math.Max(0, PowerPct((veterancy + 1) % VetPeriod, 0, 0, 0) - PowerPct(veterancy, 0, 0, 0));

    /// <summary>Gold-percent one soldier adds to an Aura: its worth times the power points the new aura state gives over the old one (never negative).</summary>
    public static long AuraWeight(Trait traits, int maskNow, int pctNow, int maskAfter, int pctAfter, bool reach)
        => (long)TraitPrice(traits, reach) * Math.Max(0, PowerPct(0, (uint)traits, maskAfter, pctAfter) - PowerPct(0, (uint)traits, maskNow, pctNow));

    /// <summary>An aura is rented for one round: its price is spread over the rounds a bought body would have served (the campaign minus the rounds played, at least 1).</summary>
    public static int AuraRentalDivisor(int round) => Math.Max(1, CampaignRounds - Math.Max(0, round));

    /// <summary>The gold a weighted sum of soldiers costs: ceil(sum / (100 x divisor)).</summary>
    public static int GoldFor(long weightedSum, int divisor = 1)
    {
        if (weightedSum <= 0) return 0;
        long d = 100L * Math.Max(1, divisor);
        return (int)Math.Min(int.MaxValue, (weightedSum + d - 1) / d);
    }

    /// <summary>
    /// What the next enemy is worth, in gold, in expectation: the levy it is raised with (<c>StartSoldiers</c> draws of the levy weights, each worth its draft price) plus the gold the
    /// domain gives it for its draft. All read from the tables: no typed number besides the ones the army and the domain already hold.
    /// </summary>
    public static long ExpectedEnemyWealth(int enemyGold)
    {
        long w = 0, pw = 0;
        for (int k = 0; k < Size; k++) { w += Army.PleromaArmyLayout.LevyWeight[k]; pw += Army.PleromaArmyLayout.LevyWeight[k] * PriceOf(k); }
        return (Army.PleromaArmyLayout.StartSoldiers * pw + w / 2) / Math.Max(1, w) + enemyGold;
    }

    /// <summary>
    /// THE COMEBACK MUSTER: half of the army's shortfall against the next enemy, in gold: ceil(max(0, <see cref="ExpectedEnemyWealth"/> - <paramref name="armyWealth"/>) / 2). An army at or above parity
    /// gets nothing, and no army can be lifted above parity, so the muster cannot inflate a game. Half is the meet-in-the-middle split (the other half is the player's to earn).
    /// </summary>
    public static int Muster(long armyWealth, int nextEnemyGold)
    {
        long deficit = ExpectedEnemyWealth(nextEnemyGold) - armyWealth;
        return deficit <= 0 ? 0 : (int)Math.Min(int.MaxValue, (deficit + 1) / 2);
    }

    /// <summary>Interest a purse pays at the close of a round: gold / (campaign - 1), the rate at which banking one round at the start of a campaign breaks even in soldier-rounds.</summary>
    public static long Interest(long gold) => gold <= 0 ? 0 : gold / (CampaignRounds - 1);
}

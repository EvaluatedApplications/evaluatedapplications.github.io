namespace Doctrine;

// The SHARED VOCABULARY (seam d). Nobody owns it alone: a change here is a CONTRACT.md amendment that every section sees.

/// <summary>The eight traits of a cohort (8 bits). A kind's traits are a function of its key (see <see cref="Roster"/>).</summary>
[Flags]
public enum Trait : byte
{
    None = 0, Heavy = 1, Swift = 2, Ranged = 4, Holy = 8, Cursed = 16, Beast = 32, Machine = 64, Shielded = 128,
}

public enum Side : byte { Player = 0, Enemy = 1 }

public enum OrderFamily : byte { End = 0, Army = 1, Field = 2 }

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
    public static string Name(int token)
    {
        var o = Decode(token);
        return o.Family switch
        {
            OrderFamily.End => "End",
            OrderFamily.Army => (ArmyOrderKind)o.Kind switch
            {
                ArmyOrderKind.Age => "Age",
                ArmyOrderKind.Rotate => $"Rotate{o.A}",
                _ => $"{(ArmyOrderKind)o.Kind}{o.A}",
            },
            _ => $"{(FieldOrderKind)o.Kind}@{o.A}",
        };
    }
    /// <summary>The field orders carried by a doctrine, in slot order (a group named twice keeps the LAST word).</summary>
    public static FieldOrder[] FieldOrders(DoctrineSet d)
    {
        var byGroup = new FieldOrder?[Groups];
        for (int i = 0; i < MaxSlots; i++)
        {
            var o = Decode(d[i]);
            if (o.Family == OrderFamily.Field) byGroup[o.A] = new FieldOrder(o.A, (FieldOrderKind)o.Kind, 1);
        }
        return byGroup.Where(f => f is not null).Select(f => f!.Value).ToArray();
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
    public static string NameOf(int kind) => Kinds[kind].Name;
    public static Trait TraitsOf(int kind) => Kinds[kind].Traits;
    public static int PriceOf(int kind) => Kinds[kind].Price;

    /// <summary>
    /// What a soldier with these traits is WORTH in gold: ceil(2 x damage percent / taken percent), read from the battle's own stat tables
    /// (<see cref="Battle.UnitRules"/>). It prices a RELABEL (<see cref="RelabelCost"/>), not a draft: drafting still costs <see cref="PriceOf"/>
    /// (the two scales differ on purpose, the draft prices are the game's feel and are the user's to change; see
    /// todo\difficulty-2026-10-19.md, where deriving the draft price from this made a plain soldier 2 gold). Spearmen 2, Shieldwall 3, Riders 2,
    /// Archers 2, Priests 2, Wights 3, Wolves 3, Catapults 4; a Spearman flipped to Machine is worth 5.
    /// </summary>
    public static int TraitPrice(Trait t)
    {
        int dmg = Battle.UnitRules.DamagePct(t), taken = Math.Max(1, Battle.UnitRules.TakenPct(t));
        return (2 * dmg + taken - 1) / taken;
    }

    /// <summary>
    /// What it costs to flip trait bit <paramref name="bit"/> on ONE soldier whose traits are <paramref name="traits"/>: the price difference when
    /// the new trait set is worth more (a better soldier is paid for), nothing when it is worth the same or less. An army-wide Flip costs the sum
    /// over its soldiers, so turning a levy into Machines is as dear as drafting Catapults.
    /// </summary>
    public static int RelabelCost(Trait traits, int bit)
        => Math.Max(0, TraitPrice((Trait)((byte)traits ^ (1 << bit))) - TraitPrice(traits));
}

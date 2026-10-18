namespace ShowroomPlenum;

/// <summary>The eight tags: bit i of a card's flag word.</summary>
public static class Tags
{
    public const int Fire = 0, Water = 1, Beast = 2, Machine = 3, Rare = 4, Cursed = 5, Light = 6, Cheap = 7;
    public static readonly string[] Names = { "Fire", "Water", "Beast", "Machine", "Rare", "Cursed", "Light", "Cheap" };
    public static uint Mask(int bit) => 1u << bit;
    public static string List(uint word)
    {
        var parts = new List<string>();
        for (int i = 0; i < Names.Length; i++) if ((word >> i & 1) != 0) parts.Add(Names[i]);
        return parts.Count == 0 ? "no tags" : string.Join(", ", parts);
    }
    public static string Bits(uint word) => "0b" + Convert.ToString(word & 0xFF, 2).PadLeft(8, '0');
}

/// <summary>One unit card: its key (identity) and tags (the flag word). Plain catalogue data; the counts live in the stores.</summary>
public sealed record UnitCard(ulong Key, string Name, uint Tags);

/// <summary>The twelve unit cards. Fuse adds keys and XORs tags, so the catalogue is built so that some sums land on a card:
/// Ember+Spark=Wisp (1+2=3), Stoat+Ember=Fox (4+1=5), Tide+Ember=Steam (6+1=7), Gear+Ember=Forge (8+1=9), Hex+Ember=Brimstone (11+1=12).</summary>
public static class PlenumCards
{
    private static uint T(params int[] bits) { uint w = 0; foreach (var b in bits) w |= 1u << b; return w; }

    public static readonly UnitCard[] Units =
    {
        new(1, "Ember", T(Tags.Fire, Tags.Cheap)),
        new(2, "Spark", T(Tags.Light, Tags.Cheap)),
        new(3, "Wisp", T(Tags.Fire, Tags.Light)),
        new(4, "Stoat", T(Tags.Beast, Tags.Cheap)),
        new(5, "Fox", T(Tags.Beast, Tags.Fire)),
        new(6, "Tide", T(Tags.Water, Tags.Cheap)),
        new(7, "Steam", T(Tags.Fire, Tags.Water)),
        new(8, "Gear", T(Tags.Machine, Tags.Cheap)),
        new(9, "Forge", T(Tags.Fire, Tags.Machine)),
        new(10, "Halo", T(Tags.Rare, Tags.Light)),
        new(11, "Hex", T(Tags.Cursed, Tags.Cheap)),
        new(12, "Brimstone", T(Tags.Fire, Tags.Cursed)),
    };

    public static UnitCard? ByKey(ulong key) { foreach (var u in Units) if (u.Key == key) return u; return null; }

    public static string NameOf(ulong key) => ByKey(key)?.Name ?? $"Card {key}";

    /// <summary>Name for a (key, tags) pair: the catalogue name when both match, else "Fused" with the key.</summary>
    public static string NameOf(ulong key, uint tags)
    {
        var u = ByKey(key);
        return u is not null && u.Tags == tags ? u.Name : $"Fused {key}";
    }
}

/// <summary>The three operation cards.</summary>
public enum Verb { Tally, Flip, Fuse }

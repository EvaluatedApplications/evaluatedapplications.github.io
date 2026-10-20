namespace Doctrine;

// THE OVERMAP, the map half (Doctrine\todo\overmap-slice1-2026-10-20.md). Two capitals and three lanes of three provinces; every province has an arena, a
// feature and a yield. These are the plain types a page reads and the seeded generator; the state itself lives in Pleroma stores (CampaignStore.cs).

/// <summary>What a held province opens in the offer pool while it is held. A flag set: a province has at most one; the union of the held ones is the union of the held ones.</summary>
[Flags]
public enum Feature { None = 0, Barracks = 1, Stables = 2, Shrine = 4, Granary = 8 }

/// <summary>Who holds a province.</summary>
public enum Holder : byte { Player = 0, Neutral = 1, Enemy = 2 }

/// <summary>
/// One province as a page reads it. <see cref="Lane"/> 0..2 and <see cref="Index"/> counted from the PLAYER's capital outward (0 = the province next to it); both are -1 for a capital.
/// <see cref="ArenaIndex"/> is an index into the battle engine's arena catalogue (the ground an assault on it is fought on).
/// </summary>
public sealed record Province(int Key, int Lane, int Index, Holder Owner, Feature Feature, int ArenaIndex, int Yield, bool IsCapital);

/// <summary>What the generator decides about one province before it is placed in a store.</summary>
public readonly record struct ProvinceSpec(int Key, Feature Feature, int ArenaIndex, int Yield, Holder Owner);

/// <summary>Keys of the province stores: <c>lane * LaneSlots + index</c>, then the two capitals. All below the key cycle of the store shape.</summary>
public static class MapKeys
{
    public const int Lanes = 3;
    /// <summary>Provinces per lane at the start of a game (three), and the most a lane can hold (endless refills stop here).</summary>
    public const int StartLaneLength = 3;
    public const int LaneSlots = 12;
    public const int PlayerCapital = Lanes * LaneSlots;       // 36
    public const int EnemyCapital = PlayerCapital + 1;        // 37
    public static int Key(int lane, int index) => lane * LaneSlots + index;
    public static bool IsCapital(int key) => key >= PlayerCapital;
    public static int LaneOf(int key) => key / LaneSlots;
    public static int IndexOf(int key) => key % LaneSlots;
}

/// <summary>The rules of the campaign layer. Plain numbers; every default is the game. <see cref="Off"/> is not a switch: the campaign is a NEW mode, the fixed games do not use this type.</summary>
public sealed record CampaignRules
{
    /// <summary>Lanes refill as the player advances (the map scrolls: a fresh enemy province appears beyond a cleared lane), there is no enemy capital, and the enemy's strength grows each epoch without a cap.</summary>
    public bool Endless { get; init; }
    /// <summary>The war is over after this many epochs (0 = the default: 24 in a fixed war, none in an endless run).</summary>
    public int MaxEpochs { get; init; }
    internal int EpochLimit => MaxEpochs > 0 ? MaxEpochs : Endless ? int.MaxValue : 24;

    // ---- yields (gold a held province pays at epoch close; the numbers live in the store as counts) ----
    public int PlainYield { get; init; } = 1;
    public int FeatureYield { get; init; } = 2;
    public int GranaryYield { get; init; } = 3;
    public int CapitalYield { get; init; } = 1;

    // ---- the enemy's lane armies (persistent; see CampaignGame) ----
    /// <summary>The scheduled strength of the army facing a province is the enemy gold <c>Base + Step x depth (+ PerEpoch x epoch when endless)</c>, read on the wealth scale of <see cref="Roster.ExpectedEnemyWealth"/>. Depth = the target's index from the player's capital (the capital itself: 3).</summary>
    public int EnemyBaseGold { get; init; } = 14;
    public int EnemyStepGold { get; init; } = 4;
    public int EnemyEpochGold { get; init; } = 3;
    /// <summary>The most gold of wealth a lane army musters back per epoch (towards its scheduled strength, never above it): a beaten army returns weaker, and heals at this rate.</summary>
    public int EnemyHealGold { get; init; } = 10;

    internal int Yield(Feature f) => f switch { Feature.None => PlainYield, Feature.Granary => GranaryYield, _ => FeatureYield };
    internal int ScheduledGold(int depth, int epoch) => EnemyBaseGold + EnemyStepGold * depth + (Endless ? EnemyEpochGold * epoch : 0);
}

/// <summary>What each feature opens. Unlocks WIDEN the choice and never add power: a kind opened is offered at the same price as any other, an order opened is one more token in the pool.</summary>
public static class UnlockPools
{
    /// <summary>The kinds offered with nothing held: Archers, Wights, Catapults. The levy and soldiers already raised are never taken away.</summary>
    public static readonly IReadOnlyList<int> BaseKinds = [3, 5, 7];
    /// <summary>Barracks: Spearmen, Shieldwall. Stables: Riders, Wolves. Shrine: Priests. Granary opens no kind (it pays more gold).</summary>
    public static IReadOnlyList<int> KindsOf(Feature f) => f switch
    {
        Feature.Barracks => [0, 1], Feature.Stables => [2, 6], Feature.Shrine => [4], _ => [],
    };
    /// <summary>The kinds the offer draws from while <paramref name="held"/> are held: the base kinds plus the kinds of every held feature, sorted.</summary>
    public static IReadOnlyList<int> Kinds(Feature held)
    {
        var set = new SortedSet<int>(BaseKinds);
        foreach (var f in new[] { Feature.Barracks, Feature.Stables, Feature.Shrine })
            if (held.HasFlag(f)) foreach (var k in KindsOf(f)) set.Add(k);
        return set.ToList();
    }
    /// <summary>True when <paramref name="token"/> may be offered: the Aura (banner) order needs a Shrine; every other token is in the pool.</summary>
    public static bool OrderOpen(Feature held, int token)
        => !(DoctrineVocabulary.IsValid(token) && DoctrineVocabulary.Decode(token) is { Family: OrderFamily.Army } o && o.Kind == (int)ArmyOrderKind.Aura) || held.HasFlag(Feature.Shrine);
}

/// <summary>The seeded map: a pure function of (seed, the arena catalogue size, the rules). Symmetric under a seat swap: province i and province 2-i of a lane share arena, feature and yield; the middle one is neutral.</summary>
public static class MapGen
{
    const int FeatureTag = 8000, ArenaTag = 8200, HomeTag = 8100, CapitalTag = 8300, ExtraTag = 9000;

    static int Pick(int seed, int salt, int n) => (int)(GameRng.Mix(seed, salt) % (ulong)Math.Max(1, n));

    /// <summary>The nine provinces and two capitals at the start. Lane l: index 0 is the player's, 1 neutral, 2 the enemy's.</summary>
    public static IReadOnlyList<ProvinceSpec> Initial(int seed, int arenaCount, CampaignRules rules, bool endless) => Initial(seed, arenaCount, rules, endless, CampaignProbe.None);

    internal static int Drift;   // MapNotDeterministic: a counter that makes the same seed draw a different map each time

    internal static IReadOnlyList<ProvinceSpec> Initial(int seed, int arenaCount, CampaignRules rules, bool endless, CampaignProbe probe)
    {
        if (probe.HasFlag(CampaignProbe.MapNotDeterministic)) seed = unchecked(seed + Interlocked.Increment(ref Drift));
        var specs = new List<ProvinceSpec>();
        // the three middle provinces carry Barracks, Stables, Shrine in a seeded order: each lane offers one thing worth fighting for
        var feats = new[] { Feature.Barracks, Feature.Stables, Feature.Shrine };
        var rng = new GameRng(GameRng.Mix(seed, FeatureTag));
        for (int i = feats.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (feats[i], feats[j]) = (feats[j], feats[i]); }
        for (int lane = 0; lane < MapKeys.Lanes; lane++)
        {
            var home = Pick(seed, HomeTag + lane, 2) == 0 ? Feature.Granary : Feature.None;           // mirrored: both homes of a lane share it
            int homeArena = Pick(seed, ArenaTag + lane * 3, arenaCount), midArena = Pick(seed, ArenaTag + lane * 3 + 1, arenaCount);
            int farArena = probe.HasFlag(CampaignProbe.MapNotSymmetric) ? Pick(seed, ArenaTag + lane * 3 + 2, arenaCount) : homeArena;
            var farFeature = probe.HasFlag(CampaignProbe.MapNotSymmetric) ? Feature.None : home;
            specs.Add(new(MapKeys.Key(lane, 0), home, homeArena, rules.Yield(home), Holder.Player));
            specs.Add(new(MapKeys.Key(lane, 1), feats[lane], midArena, rules.Yield(feats[lane]), Holder.Neutral));
            specs.Add(new(MapKeys.Key(lane, 2), farFeature, farArena, rules.Yield(farFeature), Holder.Enemy));
        }
        int cap = Pick(seed, CapitalTag, arenaCount);
        specs.Add(new(MapKeys.PlayerCapital, Feature.None, cap, rules.CapitalYield, Holder.Player));
        if (!endless) specs.Add(new(MapKeys.EnemyCapital, Feature.None, cap, rules.CapitalYield, Holder.Enemy));
        return specs;
    }

    /// <summary>The province a refill adds beyond lane <paramref name="lane"/> at <paramref name="index"/> (endless): the enemy's, a feature or none from the seed.</summary>
    public static ProvinceSpec Extra(int seed, int lane, int index, int arenaCount, CampaignRules rules)
    {
        int salt = ExtraTag + lane * 64 + index;
        var f = Pick(seed, salt, 6) switch { 0 => Feature.Barracks, 1 => Feature.Stables, 2 => Feature.Shrine, 3 => Feature.Granary, _ => Feature.None };
        return new(MapKeys.Key(lane, index), f, Pick(seed, salt + 4096, arenaCount), rules.Yield(f), Holder.Enemy);
    }
}

/// <summary>Test hooks: each breaks ONE rule of the campaign so a contract test can be watched going red. Never set outside tests.</summary>
[Flags]
internal enum CampaignProbe
{
    None = 0, UnlockKeptAfterLoss = 1, RestoreEveryFourth = 2, RestoreIgnoresMax = 4, ConcedeCostsStandard = 8, EnemyPeeks = 16,
    MapNotDeterministic = 32, MapNotSymmetric = 64, BriefingArenaWrong = 128, EnemyRecreated = 256, LossCostsNothing = 512,
}

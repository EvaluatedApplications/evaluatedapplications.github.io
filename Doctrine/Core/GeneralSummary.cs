namespace Doctrine;

/// <summary>
/// The general's world: a short summary of Pleroma reads as small integer tokens. Built here, once, so the general (algformer-owner)
/// and the army (phasor-owner) never meet: the army produces <see cref="ArmyReadModel"/>, this turns it into tokens, the general reads tokens.
/// Layout (<see cref="Length"/> tokens, each in [0, <see cref="Vocab"/>)): round bucket, own soldiers bucket, own gold bucket,
/// kinds in use, the 8 own trait buckets, enemy soldiers bucket (0 = not scouted), last result.
/// </summary>
public static class GeneralSummary
{
    public const int Length = 14;
    public const int Vocab = 16;
    public const int RoundBucketCap = 15;

    public static int Bucket(int value, int step) => Math.Min(Vocab - 1, Math.Max(0, value) / Math.Max(1, step));

    public static GeneralInput Build(ArmyReadModel own, ArmyReadModel? scouted, BattleStatus? last, bool lastPlayerWon)
    {
        var s = new int[Length];
        s[0] = Math.Min(RoundBucketCap, own.Round);
        s[1] = Bucket(own.Soldiers, 20);
        s[2] = Bucket(own.Gold, 10);
        s[3] = Math.Min(Vocab - 1, own.Kinds);
        var tb = own.TraitBuckets();
        for (int t = 0; t < DoctrineVocabulary.Traits; t++) s[4 + t] = Bucket(tb[t], 15);
        s[12] = scouted is null ? 0 : 1 + Bucket(scouted.Soldiers, 25);
        s[13] = last is null ? 0 : 1 + (int)last.Value * 2 + (lastPlayerWon ? 1 : 0);
        return new(own.Round, s, own.Legal);
    }

    /// <summary>A stable key for a summary (table-lookup generals use it; real ones may ignore it).</summary>
    public static ulong Key(IReadOnlyList<int> summary)
    {
        var h = new Fnv();
        foreach (var t in summary) h.Add(t);
        return h.Value;
    }
}

using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// The plain words of the war: roads, provinces, the lane cards, the epoch summary and the end of the war, in the voice of a lord who speaks of his host.
// No game rule here, only how a rule is said. NO NUMBER IS TYPED IN A SENTENCE: every figure a line states is read from the library's briefing, the
// epoch's result or the map when the line is made (the proof scans this file for a digit inside a string). No Blazor.

public static class DoctrineCampaignWords
{
    static readonly string[] Roads = ["Left road", "Middle road", "Right road"];
    public static string Road(int lane) => (uint)lane < (uint)Roads.Length ? Roads[lane] : "Another road";
    static string RoadLow(int lane) => Road(lane).ToLowerInvariant();

    public static string FeatureName(Feature f) => f switch
    {
        Feature.Barracks => "Barracks", Feature.Stables => "Stables", Feature.Shrine => "Shrine", Feature.Granary => "Granary", _ => "Open country",
    };

    public const string EndsWarWords = "Taking it ends the war.";
    public static string HeldBy(Holder h) => h switch { Holder.Player => "held by you", Holder.Enemy => "held by the enemy", _ => "held by no one" };

    public static string HolderWord(Holder h) => h switch { Holder.Player => "Yours", Holder.Enemy => "The enemy's", _ => "Nobody's" };

    /// <summary>The province as a lord names it: its feature (or open country), its place on its road counted from his capital.</summary>
    public static string ProvinceName(Province p)
        => p.IsCapital ? (p.Key == MapKeys.PlayerCapital ? "Your capital" : "The enemy capital")
                       : $"{FeatureName(p.Feature)}, {DoctrineWords.Ordinal(p.Index + 1)} from your capital on the {RoadLow(p.Lane)}";

    /// <summary>The same inside a sentence ("You took your capital"): a capital begins with a small letter.</summary>
    public static string ProvinceMid(Province p) => p.IsCapital ? char.ToLowerInvariant(ProvinceName(p)[0]) + ProvinceName(p)[1..] : ProvinceName(p);

    /// <summary>The ground of a battle, said short (the lane card and the map use the arena's own name, clock and besieger).</summary>
    public static string Ground(ArenaBrief a)
    {
        if (!a.IsSiege) return $"{a.Info.Name}: an open ground.";
        return a.Besieger switch
        {
            Side.Player => $"{a.Info.Name}: a siege, and you besiege it (the clock is {a.SiegeClock} ticks).",
            Side.Enemy => $"{a.Info.Name}: a siege, and the enemy besieges it (the clock is {a.SiegeClock} ticks).",
            _ => $"{a.Info.Name}: a siege that no side besieges.",
        };
    }

    /// <summary>The kinds a feature adds to the muster, and the order it adds to the council (the Shrine's banner), said in one phrase.</summary>
    static string OpensPhrase(IReadOnlyList<int> kinds, bool banner)
    {
        var parts = new List<string>();
        if (kinds.Count > 0) parts.Add(DoctrineWords.JoinAnd(kinds.Select(DoctrineWords.KindName).ToList()) + " to the muster");
        if (banner) parts.Add("the order " + DoctrineWords.ArmyTitle(ArmyOrderKind.Aura) + " to the council");
        return string.Join(" and ", parts);
    }

    /// <summary>The kinds (and the banner order) a muster may offer, said without the place they are offered ("Spearmen and Shieldwall").</summary>
    static string OfferedPhrase(IReadOnlyList<int> kinds, bool banner)
    {
        var parts = new List<string>();
        if (kinds.Count > 0) parts.Add(DoctrineWords.JoinAnd(kinds.Select(DoctrineWords.KindName).ToList()));
        if (banner) parts.Add("the order " + DoctrineWords.ArmyTitle(ArmyOrderKind.Aura));
        return string.Join(" and ", parts);
    }

    /// <summary>What holding the lane's target would open, from the briefing's own lists.</summary>
    public static string Unlocks(LaneBrief lb)
    {
        if (lb.Target is null || lb.Target.IsCapital) return "";
        string phrase = OpensPhrase(lb.NewKinds, lb.OpensAura);
        if (lb.Unlocks == Feature.Granary) return $"Holding it opens no new soldiers; it pays {lb.Target.Yield} gold.";
        if (lb.Unlocks == Feature.None) return $"Holding it opens nothing; it pays {lb.Target.Yield} gold.";
        return phrase.Length == 0 ? $"Holding it opens nothing new; it pays {lb.Target.Yield} gold." : $"Holding it opens {phrase} while you hold it, and pays {lb.Target.Yield} gold.";
    }

    /// <summary>The enemy host that waits there, one line to a rank (front first), from the briefing's cohorts.</summary>
    public static IReadOnlyList<string> EnemyLines(EnemyBrief e)
        => e.Lines.Select(l => $"{DoctrineWords.Slot(l[0].Slot)} line: " + string.Join(", ", l.Select(c => $"{c.Count} {DoctrineWords.KindName(c.Key)}"))).ToList();

    public static string EnemyHead(EnemyBrief e) => $"The enemy host there: {e.Soldiers} {(e.Soldiers == 1 ? "soldier" : "soldiers")}.";

    public const string HoldBackTitle = "Hold back";
    /// <summary>What holding back is, said once: no battle, the enemy marches unopposed, and no standard is lost.</summary>
    public const string HoldBackWords = "You push no lane and fight no battle. The enemy marches unopposed in the lane it picked, so it costs ground, never a standard.";

    public const string SealWords = "Your pick is sealed. The enemy picks its lane without seeing yours, and both are shown together in the result.";

    public static string OfferWords(CampaignBriefing b)
        => b.HeldUnlocks == Feature.None
            ? $"The muster draws from {DoctrineWords.JoinAnd(b.OfferKinds.Select(DoctrineWords.KindName).ToList())}."
            : $"The muster draws from {DoctrineWords.JoinAnd(b.OfferKinds.Select(DoctrineWords.KindName).ToList())}" + (b.AuraOpen ? $", and the council may offer the order {DoctrineWords.ArmyTitle(ArmyOrderKind.Aura)}." : ".");

    public static string HeldWords(Feature held)
    {
        var names = new[] { Feature.Barracks, Feature.Stables, Feature.Shrine, Feature.Granary }.Where(f => held.HasFlag(f)).Select(FeatureName).ToList();
        return names.Count == 0 ? "You hold no feature." : "You hold: " + DoctrineWords.JoinAnd(names) + ".";
    }

    public static string StandardsWords(int standards, int max, int winsToRestore, int restoreEvery)
        => standards >= max
            ? $"All {DoctrineWords.Cardinal(max)} standards fly. A lost battle costs one."
            : $"{standards} of {max} standards fly. Win {winsToRestore} more {(winsToRestore == 1 ? "battle" : "battles")} to raise one again (every {DoctrineWords.Ordinal(restoreEvery)} battle won does).";

    // ---- the epoch summary ----

    static string OutcomeWord(BattleStatus s) => s switch { BattleStatus.AttackerWon => "won", BattleStatus.DefenderWon => "lost", BattleStatus.Draw => "a draw", _ => "unfinished" };

    public static string EpochHeading(EpochResult r)
    {
        int n = r.Epoch + 1;
        return r.Kind switch
        {
            EpochKind.Battle => $"Epoch {n}: a battle on the {RoadLow(r.PlayerLane)}, " + OutcomeWord(r.Battle!.Result.Outcome),
            EpochKind.Race => $"Epoch {n}: a race",
            EpochKind.Conceded => $"Epoch {n}: you held back",
            _ => $"Epoch {n}",
        };
    }

    /// <summary>The lines of the summary of one epoch, each read from its result (and the map after it).</summary>
    public static IReadOnlyList<string> EpochLines(EpochResult r, IReadOnlyList<Province> provinces, Feature heldBefore, Feature heldAfter)
    {
        var l = new List<string>();
        string mine = RoadLow(r.PlayerLane), theirs = RoadLow(r.EnemyLane);
        l.Add(r.Kind switch
        {
            EpochKind.Battle => $"You pushed the {mine} and the enemy picked it too, so the two hosts met there.",
            EpochKind.Race => $"You pushed the {mine}; the enemy picked the {theirs}. Neither met the other, so each took ground unopposed.",
            _ => $"You held back; the enemy picked the {theirs} and marched there unopposed.",
        });
        if (r.Changes.Count == 0) l.Add("No ground changed hands.");
        foreach (var c in r.Changes)
        {
            var p = provinces.FirstOrDefault(x => x.Key == c.Key);
            string nm = p is null ? "a province" : ProvinceMid(p);
            l.Add(c.To == Holder.Player ? $"You took {nm}." : c.From == Holder.Player ? $"The enemy took {nm}, which was yours." : $"The enemy took {nm}.");
        }
        if (r.Kind == EpochKind.Battle && r.Changes.Count == 0 && r.Battle!.Result.Outcome == BattleStatus.Draw) l.Add("A drawn field moves nothing and costs nothing.");
        if (r.StandardsAfter < r.StandardsBefore) l.Add($"You lost a standard: {r.StandardsAfter} left.");
        else if (r.Restored) l.Add($"A standard is raised again: {r.StandardsAfter} fly.");
        else l.Add($"No standard was lost: {r.StandardsAfter} fly.");
        if (r.GoldPaid > 0) l.Add($"Your provinces paid {r.GoldPaid} gold.");
        var before = UnlockPools.Kinds(heldBefore); var after = UnlockPools.Kinds(heldAfter);
        var gained = after.Except(before).ToList(); var lost = before.Except(after).ToList();
        bool bannerGained = heldAfter.HasFlag(Feature.Shrine) && !heldBefore.HasFlag(Feature.Shrine), bannerLost = heldBefore.HasFlag(Feature.Shrine) && !heldAfter.HasFlag(Feature.Shrine);
        if (gained.Count > 0 || bannerGained) l.Add("From the next muster you may be offered " + OfferedPhrase(gained, bannerGained) + ".");
        if (lost.Count > 0 || bannerLost) l.Add("No longer offered, with the ground that gave them: " + OfferedPhrase(lost, bannerLost) + ".");
        l.Add($"Score: {r.Score}.");
        return l;
    }

    public static string ContinueWords(bool over) => over ? "See the end of the war" : "Back to the map";

    // ---- the end of the war ----

    public static string EndHeading(CampaignOutcome o, bool left) => left ? "You left the war." : o switch
    {
        CampaignOutcome.WarWon => "The war is won: the enemy capital is yours.",
        CampaignOutcome.WarLost => "The war is lost: your capital has fallen.",
        CampaignOutcome.WarDrawn => "A draw: both capitals fell in the same epoch.",
        CampaignOutcome.OutOfStandards => "The war is lost: you have no standards left.",
        CampaignOutcome.EpochsUp => "The war is over: the epochs have run out.",
        _ => "The war is over.",
    };

    public static string ScoreWords(CampaignEnd e)
        => $"Score {e.Score}: {e.FieldsWon} {(e.FieldsWon == 1 ? "field" : "fields")} won and {e.ProvincesHeld} {(e.ProvincesHeld == 1 ? "province" : "provinces")} held" + (e.Endless ? " (the score of an endless run)." : ".");

    public static string EpochLine(EpochResult r)
        => r.Kind switch
        {
            EpochKind.Battle => $"Epoch {r.Epoch + 1}: a battle on the {RoadLow(r.PlayerLane)}, " + OutcomeWord(r.Battle!.Result.Outcome),
            EpochKind.Race => $"Epoch {r.Epoch + 1}: a race, you on the {RoadLow(r.PlayerLane)} and the enemy on the {RoadLow(r.EnemyLane)}",
            _ => $"Epoch {r.Epoch + 1}: you held back, the enemy marched on the {RoadLow(r.EnemyLane)}",
        };
}

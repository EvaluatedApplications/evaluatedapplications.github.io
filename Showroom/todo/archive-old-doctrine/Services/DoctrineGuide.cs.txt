using Doctrine;

namespace ShowroomDoctrine;

// The game's own help for a first-time player: a starter piece of advice and a short recap of a battle. Neither is the general: the advice is a
// fixed rule of thumb over the army's legal list (it learns nothing), and the recap is read only from the round's record and its recording.
// No Blazor and no game rule here.

/// <summary>A starter draft: the picks and the one plain line saying where they came from.</summary>
public sealed record DraftAdvice(IReadOnlyList<DraftPick> Picks, string Reason);

/// <summary>A starter doctrine: the orders and the one plain line saying where they came from.</summary>
public sealed record DoctrineAdvice(DoctrineSet Doctrine, string Reason);

/// <summary>Two lines on what happened and (when the record supports one) one thing to try next; <see cref="Siege"/> holds the lines on a siege (none on an open ground).</summary>
public sealed record Recap(IReadOnlyList<string> What, string? TryNext)
{
    public IReadOnlyList<string> Siege { get; init; } = [];
    /// <summary>The line on a battle the tick limit stopped (both sides still stood at the last frame), or null.</summary>
    public string? Limit { get; init; }
}

public static class DoctrineGuide
{
    /// <summary>The label every piece of this advice carries. It is the game's, never the general's.</summary>
    public const string AdvicePrefix = "Game advice:";

    /// <summary>
    /// Buy companies from the muster, one at a time, each time the company of the kind whose cohort is smallest so far (a kind the army does not hold
    /// counts as zero; the dearer the company the later on a tie), while the purse can pay. The picks are the offered companies themselves, in the
    /// order the muster lists them. Null when nothing can be bought.
    /// </summary>
    public static DraftAdvice? Draft(ArmyReadModel army, UnitOffer offer)
    {
        if (!offer.Active) return null;
        var have = new int[Roster.Size];
        foreach (var c in army.Cohorts) have[c.Key] += c.Count;
        var taken = new bool[offer.Packs.Count];
        int gold = army.Gold;
        while (true)
        {
            int best = -1;
            for (int i = 0; i < offer.Packs.Count; i++)
            {
                var p = offer.Packs[i];
                if (taken[i] || !p.Affordable || p.Cost > gold) continue;
                if (best < 0 || have[p.Kind] < have[offer.Packs[best].Kind] || (have[p.Kind] == have[offer.Packs[best].Kind] && p.Cost < offer.Packs[best].Cost)) best = i;
            }
            if (best < 0) break;
            taken[best] = true; gold -= offer.Packs[best].Cost; have[offer.Packs[best].Kind] += offer.Packs[best].Count;
        }
        var picks = Enumerable.Range(0, offer.Packs.Count).Where(i => taken[i]).Select(i => UnitOffer.Pick(offer.Packs[i])).ToList();
        if (picks.Count == 0) return null;
        return new DraftAdvice(picks, $"{AdvicePrefix} {army.Gold - gold} of your {army.Gold} gold, each company going to the kind where your cohort is smallest.");
    }

    /// <summary>
    /// A rule of thumb over the council: Front advance, Back hold, Middle flank, then Age, then any other order for a line, taking each one only if it
    /// is on offer, open at this step AND on the army's legal list, and only as many as there are slots. Null when none qualifies.
    /// </summary>
    public static DoctrineAdvice? Orders(ArmyReadModel army, OrderOffer offer, int step)
    {
        int[] want =
        [
            DoctrineVocabulary.Field(FieldOrderKind.Advance, 0),
            DoctrineVocabulary.Field(FieldOrderKind.Hold, 2),
            DoctrineVocabulary.Field(FieldOrderKind.Flank, 1),
            DoctrineVocabulary.Army(ArmyOrderKind.Age),
        ];
        var rest = offer.Tokens.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).Where(t => !want.Contains(t));
        var chosen = want.Concat(rest).Where(t => t != 0 && offer.Allows(t) && DoctrineUnlocks.Allows(t, step) && army.Legal.Allows(t)).Take(DoctrineUnlocks.Slots(step)).ToArray();
        if (chosen.Length == 0) return null;
        return new DoctrineAdvice(DoctrineSet.Of(chosen), $"{AdvicePrefix} {string.Join(", ", chosen.Select(DoctrineWords.Label))}. A fixed rule of thumb over the orders on offer.");
    }

    static string Plural(int n, string one, string many) => n == 1 ? one : many;

    /// <summary>What happened in a closed round, from its record and its recording only. A line the record cannot back is left out.</summary>
    public static Recap Of(RoundView v, string arenaName)
    {
        var rec = v.Record;
        int lost = rec.Result.AttackerLosses.Sum(l => l.Lost);
        int theirLost = rec.Result.DefenderLosses.Sum(l => l.Lost);
        string ticks = $"{rec.Result.Ticks} {Plural(rec.Result.Ticks, "tick", "ticks")}";
        var what = new List<string>
        {
            rec.Result.Outcome switch
            {
                BattleStatus.AttackerWon => $"You won on {arenaName} in {ticks}.",
                BattleStatus.DefenderWon => $"The enemy held {arenaName} after {ticks}.",
                BattleStatus.Draw => $"Both sides fell together on {arenaName} after {ticks}.",
                _ => $"The battle on {arenaName} did not finish.",
            },
            $"You lost {lost} of {v.YourSoldiers} soldiers; the enemy lost {theirLost} of {v.EnemySoldiers}.",
        };

        string? next = null;
        var worst = rec.Result.AttackerLosses.Where(l => l.Lost > 0).OrderByDescending(l => l.Lost).FirstOrDefault();
        if (rec.Final) next = null;
        else if (rec.PlayerDoctrine.IsEmpty)
            next = $"Try next: you gave no orders, so every line pressed on. Tell one line to {DoctrineWords.FieldOrders[(int)FieldOrderKind.Hold].ToLowerInvariant()} and see what changes.";
        else if (rec.Orders is { Skipped: > 0 } o)
            next = $"Try next: the host skipped {o.Skipped} of your orders for the whole host because {Plural(o.Skipped, "it was", "they were")} not allowed when the turn came. A greyed order in the list cannot be given.";
        else if (rec.Result.Outcome != BattleStatus.AttackerWon && worst.Lost > 0)
            next = $"Try next: {DoctrineWords.KindName(worst.Key)} lost the most ({worst.Lost}). Give the {DoctrineWords.Slot(worst.Slot)} line a different order, or draft more of them.";
        else if (v.ArmyAfter.Gold > 0)
            next = $"Try next: you have {v.ArmyAfter.Gold} gold. The draft turns gold into soldiers.";
        // a siege: the recording names who besieged; the clock is the arena's; whether it fell is the recording's own bit
        var siege = new List<string>();
        if (rec.Replay.Besieger is { } bs)
        {
            siege.Add(DoctrineWords.SiegeRecap(arenaName, bs, v.SiegeClock));
            var fell = rec.Replay.Frames.FirstOrDefault(f => (f.WorldFlags & WorldFlag.ClockFell) != 0);
            if (fell is not null) siege.Add(DoctrineWords.ClockFellRecap(bs, fell.Tick));
            else if (v.SiegeClock > 0 && rec.Result.Ticks > v.SiegeClock && TradedBy(rec.Replay, bs, v.SiegeClock)) siege.Add(DoctrineWords.StanceKeptRecap(bs));
        }
        // the tick limit: both sides still stand in the last frame, so the limit stopped it and POWER left (not the count) gave the field
        var lastFrame = rec.Replay.Frames.Count > 0 ? rec.Replay.Frames[^1] : null;
        bool stopped = lastFrame is not null && lastFrame.Blobs.Any(b => b.Side == Side.Player && b.Strength > 0) && lastFrame.Blobs.Any(b => b.Side == Side.Enemy && b.Strength > 0);
        return new Recap(what, next) { Siege = siege, Limit = stopped ? DoctrineWords.LimitRecap(rec.Result.Outcome) : null };
    }

    /// <summary>True when a frame up to <paramref name="byTick"/> holds a blow dealt or taken by a cohort of <paramref name="side"/>.</summary>
    static bool TradedBy(BattleReplay r, Side side, int byTick)
    {
        foreach (var f in r.Frames)
        {
            if (f.Tick > byTick) break;
            foreach (var e in f.Events)
                foreach (var b in f.Blobs)
                    if ((b.Id == e.Source || b.Id == e.Target) && b.Side == side) return true;
        }
        return false;
    }
}

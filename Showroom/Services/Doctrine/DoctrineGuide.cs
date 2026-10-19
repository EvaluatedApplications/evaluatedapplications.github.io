using Doctrine;

namespace ShowroomDoctrine;

// The game's own help for a first-time player: a starter piece of advice and a short recap of a battle. Neither is the general: the advice is a
// fixed rule of thumb over the army's legal list (it learns nothing), and the recap is read only from the round's record and its recording.
// No Blazor and no game rule here.

/// <summary>A starter draft: the picks and the one plain line saying where they came from.</summary>
public sealed record DraftAdvice(IReadOnlyList<DraftPick> Picks, string Reason);

/// <summary>A starter doctrine: the orders and the one plain line saying where they came from.</summary>
public sealed record DoctrineAdvice(DoctrineSet Doctrine, string Reason);

/// <summary>Two lines on what happened and (when the record supports one) one thing to try next.</summary>
public sealed record Recap(IReadOnlyList<string> What, string? TryNext);

public static class DoctrineGuide
{
    /// <summary>The label every piece of this advice carries. It is the game's, never the general's.</summary>
    public const string AdvicePrefix = "Game advice:";

    /// <summary>
    /// Spend the gold one soldier at a time, each on the smallest cohort among the kinds that are open (a kind the army does not hold counts as
    /// zero), the cheaper kind first on a tie. Null when nothing can be bought.
    /// </summary>
    public static DraftAdvice? Draft(ArmyReadModel army, int step)
    {
        var have = new int[Roster.Size];
        foreach (var c in army.Cohorts) have[c.Key] += c.Count;
        var bought = new int[Roster.Size];
        int gold = army.Gold;
        while (true)
        {
            int best = -1;
            for (int k = 0; k < Roster.Size; k++)
            {
                if (!DoctrineUnlocks.KindOpen(k, step) || Roster.PriceOf(k) > gold) continue;
                if (best < 0 || have[k] + bought[k] < have[best] + bought[best] || (have[k] + bought[k] == have[best] + bought[best] && Roster.PriceOf(k) < Roster.PriceOf(best))) best = k;
            }
            if (best < 0) break;
            bought[best]++; gold -= Roster.PriceOf(best);
        }
        var picks = Enumerable.Range(0, Roster.Size).Where(k => bought[k] > 0).Select(k => new DraftPick(k, bought[k])).ToList();
        if (picks.Count == 0) return null;
        return new DraftAdvice(picks, $"{AdvicePrefix} {army.Gold - gold} of your {army.Gold} gold, each soldier going to your smallest cohort so far.");
    }

    /// <summary>
    /// A rule of thumb: Front advance, Back hold, Middle flank, then Age, taking each one only if it is open at this step AND on the army's legal
    /// list, and only as many as there are slots. Null when none qualifies.
    /// </summary>
    public static DoctrineAdvice? Orders(ArmyReadModel army, int step)
    {
        int[] want =
        [
            DoctrineVocabulary.Field(FieldOrderKind.Advance, 0),
            DoctrineVocabulary.Field(FieldOrderKind.Hold, 2),
            DoctrineVocabulary.Field(FieldOrderKind.Flank, 1),
            DoctrineVocabulary.Army(ArmyOrderKind.Age),
        ];
        var chosen = want.Where(t => DoctrineUnlocks.Allows(t, step) && army.Legal.Allows(t)).Take(DoctrineUnlocks.Slots(step)).ToArray();
        if (chosen.Length == 0) return null;
        return new DoctrineAdvice(DoctrineSet.Of(chosen), $"{AdvicePrefix} {string.Join(", ", chosen.Select(DoctrineWords.Label))}. A fixed rule of thumb over the orders you may give.");
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
        return new Recap(what, next);
    }
}

using Doctrine;

namespace ShowroomDoctrine;

// What a first-time player is shown, and when. THE UNLOCKS ONLY HIDE OR REVEAL: whether an order is legal is still decided by the army's own
// legal list (LegalMask); nothing here makes an illegal order legal. No Blazor and no game rule: the steps are a table over the game's own
// vocabulary, the schedule is a function of the round and the rounds option.
//
// Five steps. Step 0 is round one: two kinds of soldier, two slots, Advance and Hold. Each later step opens more.
//   0  Advance, Hold            2 slots   Spearmen, Archers
//   1  Flank, Fall back         3 slots   + Shieldwall, Riders
//   2  Guard, Hunt              4 slots   + Priests, Wights
//   3  Rotate, Age                        + Wolves
//   4  Flip, Merge, Aura (Cull)           + Catapults

/// <summary>One line the unlock announcement says: a heading line and the plain effect of each thing it names.</summary>
public sealed record UnlockNews(IReadOnlyList<string> Orders, int? NewSlotCount, IReadOnlyList<string> Kinds)
{
    public bool Any => Orders.Count > 0 || NewSlotCount is not null || Kinds.Count > 0;
}

public static class DoctrineUnlocks
{
    public const int Steps = 5;
    public const int LastStep = Steps - 1;

    static readonly int[] SlotsAtStep = [2, 3, 4, 4, 4];
    static readonly int[] FieldStep = [0, 0, 1, 1, 2, 2];                 // by FieldOrderKind: Advance, Hold, Flank, FallBack, Guard, Hunt
    static readonly int[] ArmyStep = [3, 4, 3, 4, 4, 4];                  // by ArmyOrderKind: Rotate, Flip, Age, Merge, Aura, Cull
    static readonly int[] KindStep = [0, 1, 1, 0, 2, 2, 3, 4];            // by roster key: Spearmen, Shieldwall, Riders, Archers, Priests, Wights, Wolves, Catapults

    /// <summary>The step a round is on. Rounds 1 to 4 open steps 0 to 3; step 4 (the last orders) opens at round 5, or at the first round of the last third of a shorter game.</summary>
    public static int StepForRound(int round, int totalRounds)
    {
        int lastThird = Math.Max(0, totalRounds - (totalRounds + 2) / 3);
        int last = Math.Min(LastStep, lastThird);
        return round >= last ? LastStep : Math.Min(round, LastStep - 1);
    }

    public static int Slots(int step) => SlotsAtStep[Math.Clamp(step, 0, LastStep)];

    /// <summary>The step that opens a token. End is always open.</summary>
    public static int StepOf(int token)
    {
        if (token == 0) return 0;
        var o = DoctrineVocabulary.Decode(token);
        return o.Family == OrderFamily.Field ? FieldStep[o.Kind] : ArmyStep[o.Kind];
    }

    public static bool Allows(int token, int step) => StepOf(token) <= step;
    public static bool AllowsAll(DoctrineSet d, int step) => d.Tokens.All(t => Allows(t, step)) && d.Used <= Slots(step);
    public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length && KindStep[kind] <= step;

    static string[] OrderNamesAt(int step)
    {
        var l = new List<string>();
        for (int f = 0; f < FieldStep.Length; f++) if (FieldStep[f] == step) l.Add(DoctrineWords.FieldOrders[f]);
        foreach (var (k, name) in new[] { (ArmyOrderKind.Rotate, "Rotate"), (ArmyOrderKind.Age, "Age"), (ArmyOrderKind.Flip, "Flip"), (ArmyOrderKind.Merge, "Merge"), (ArmyOrderKind.Aura, "Aura") })
            if (ArmyStep[(int)k] == step) l.Add(name);
        return l.ToArray();
    }

    /// <summary>What opens when the player moves from step <paramref name="from"/> (exclusive) to <paramref name="to"/> (inclusive).</summary>
    public static UnlockNews News(int from, int to)
    {
        var orders = new List<string>();
        var kinds = new List<string>();
        for (int s = from + 1; s <= to; s++)
        {
            orders.AddRange(OrderNamesAt(s));
            for (int k = 0; k < KindStep.Length; k++) if (KindStep[k] == s) kinds.Add(Roster.NameOf(k));
        }
        int? slots = to > from && Slots(to) > Slots(Math.Max(from, 0)) ? Slots(to) : null;
        return new UnlockNews(orders, slots, kinds);
    }

    /// <summary>One plain line of what a named order does (the same facts as <see cref="DoctrineWords.Sentence"/>, without a line or trait).</summary>
    public static string Effect(string order) => order switch
    {
        "Advance" => DoctrineWords.FieldSentences[0],
        "Hold" => DoctrineWords.FieldSentences[1],
        "Flank" => DoctrineWords.FieldSentences[2],
        "Fall back" => DoctrineWords.FieldSentences[3],
        "Guard" => DoctrineWords.FieldSentences[4],
        "Hunt" => DoctrineWords.FieldSentences[5],
        "Rotate" => "Move every cohort one or two places along the line.",
        "Age" => "Every cohort moves one step along the veterancy ring of 9; each step adds 5 power.",
        "Flip" => "Relabel one trait on every cohort. It can cost gold.",
        "Merge" => "Move the soldiers of one kind into the kind before it.",
        "Aura" => "Cohorts carrying a trait get a bonus this round, up to 50 percent.",
        _ => "",
    };
}

/// <summary>
/// How far this visitor has opened the game. Lives for the page session (a scoped service in the app), so a restarted game keeps what was unlocked.
/// </summary>
public sealed class DoctrineProgress
{
    /// <summary>The highest step reached.</summary>
    public int Step { get; private set; }
    public bool ShowAll { get; private set; }

    /// <summary>Open everything at once.</summary>
    public void ShowEverything() { ShowAll = true; Step = DoctrineUnlocks.LastStep; }

    /// <summary>A round begins: move up to its step (never down). Returns what opened, or null when nothing did.</summary>
    public UnlockNews? Reach(int round, int totalRounds)
    {
        int to = DoctrineUnlocks.StepForRound(round, totalRounds);
        if (to <= Step) return null;
        var news = DoctrineUnlocks.News(Step, to);
        Step = to;
        return news.Any ? news : null;
    }
}

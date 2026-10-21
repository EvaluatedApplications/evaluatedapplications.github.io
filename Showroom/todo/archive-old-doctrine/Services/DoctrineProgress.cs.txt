using Doctrine;

namespace ShowroomDoctrine;

// What a first-time player is shown, and when. THE UNLOCKS ONLY HIDE OR REVEAL: whether an order is legal is still decided by the army's own
// legal list (LegalMask); nothing here makes an illegal order legal. No Blazor and no game rule: the steps are a table over the game's own
// vocabulary, the schedule is a function of the round and the rounds option.
//
// Five steps. Step 0 is round one: two kinds of soldier, two slots, Advance and Hold. Each later step opens more.
//   0  Press on, Hold the line   2 slots   Spearmen, Archers
//   1  Ride the flank, Fall back 3 slots   + Shieldwall, Riders
//   2  Guard the host, Run them down, Should the host fall (the three loss triggers)   4 slots   + Priests, Wights
//   3  Reform the ranks, Drill the host    + Wolves
//   4  Reforge, Swear into a kind, Raise a banner (Cull)   + Catapults

/// <summary>One line the unlock announcement says: a heading line and the plain effect of each thing it names (<see cref="Effects"/> runs beside <see cref="Orders"/>).</summary>
public sealed record UnlockNews(IReadOnlyList<string> Orders, int? NewSlotCount, IReadOnlyList<string> Kinds)
{
    public IReadOnlyList<string> Effects { get; init; } = [];
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
    /// <summary>The step the loss triggers open: with the four slots (an opening order, the trigger, an order that takes over, and one more).</summary>
    public const int TriggerStep = 2;

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
        return o.Family switch
        {
            OrderFamily.Field => FieldStep[o.Kind],
            OrderFamily.Trigger => TriggerStep,
            _ => ArmyStep[o.Kind],
        };
    }

    public static bool Allows(int token, int step) => StepOf(token) <= step;
    public static bool AllowsAll(DoctrineSet d, int step) => d.Tokens.All(t => Allows(t, step)) && d.Used <= Slots(step);
    public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length && KindStep[kind] <= step;

    /// <summary>The orders that open at a step: each one's name and its one plain effect (Cull is never announced: no army offers it).</summary>
    static List<(string Name, string Effect)> OrdersAt(int step)
    {
        var l = new List<(string, string)>();
        for (int f = 0; f < FieldStep.Length; f++) if (FieldStep[f] == step) l.Add((DoctrineWords.FieldOrders[f], DoctrineWords.FieldSentences[f]));
        if (TriggerStep == step) l.Add((DoctrineWords.TriggerTitle, DoctrineWords.TriggerRule()));
        foreach (var k in new[] { ArmyOrderKind.Rotate, ArmyOrderKind.Age, ArmyOrderKind.Flip, ArmyOrderKind.Merge, ArmyOrderKind.Aura })
            if (ArmyStep[(int)k] == step) l.Add((DoctrineWords.ArmyTitle(k), DoctrineWords.Rule(k)));
        return l;
    }

    /// <summary>What opens when the player moves from step <paramref name="from"/> (exclusive) to <paramref name="to"/> (inclusive).</summary>
    public static UnlockNews News(int from, int to)
    {
        var orders = new List<string>();
        var effects = new List<string>();
        var kinds = new List<string>();
        for (int s = from + 1; s <= to; s++)
        {
            foreach (var (name, effect) in OrdersAt(s)) { orders.Add(name); effects.Add(effect); }
            for (int k = 0; k < KindStep.Length; k++) if (KindStep[k] == s) kinds.Add(Roster.NameOf(k));
        }
        int? slots = to > from && Slots(to) > Slots(Math.Max(from, 0)) ? Slots(to) : null;
        return new UnlockNews(orders, slots, kinds) { Effects = effects };
    }
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

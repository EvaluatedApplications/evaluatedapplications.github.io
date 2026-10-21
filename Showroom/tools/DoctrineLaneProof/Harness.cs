using Doctrine;
using Doctrine.Lane;
using Tracer;
using Tracer.Integration;

static class H
{
    public static int Failures, Checks, BatchFails;
    public static string FirstRed = "";
    public static bool Quiet;

    public static void Check(bool ok, string what)
    {
        Checks++;
        if (!ok)
        {
            BatchFails++;
            if (BatchFails == 1) FirstRed = what;
            if (!Quiet) { Failures++; Console.WriteLine("  FAIL: " + what); }
        }
    }

    public static void Section(string s) { if (!Quiet) Console.WriteLine("== " + s); }
    public static void Note(string s) { if (!Quiet) Console.WriteLine("  " + s); }
}

enum Mode { Phases, Chunks }

/// <summary>Small helpers to drive matches the way two clients would: create, apply the plays of each phase at the pause, step.</summary>
static class Rig
{
    static readonly string Scratch = Path.Combine(Path.GetTempPath(), "lane-proof-" + Guid.NewGuid().ToString("N"));
    public static TracerContext Ctx() => new(new PathfindingWorld(tunerStatePath: Scratch));
    public static Task<LaneBattle> New(LaneSetup s, int seed) => LaneBattle.CreateAsync(s, seed, Ctx());

    public static LanePlay[] Spawns(Side side, int lanes, params (int Kind, int Count)[] groups)
        => [.. Enumerable.Range(0, lanes).SelectMany(l => groups.Select(g => (LanePlay)new SpawnPlay(side, l, g.Kind, g.Count)))];

    /// <summary>
    /// The standard plan: phase 0 both sides spawn a mixed company in every lane and the player holds lane 1; phase 1 the player releases lane 1 and sends catapults down lane 0
    /// while the enemy falls back in lane 2; phase 2 the player hunts in lane 0 and the enemy advances everywhere; then nothing.
    /// </summary>
    public static List<LanePlay[]> StandardPlan(int lanes)
    {
        var p0 = Spawns(Side.Player, lanes, (0, 8), (3, 6), (2, 4)).Concat(Spawns(Side.Enemy, lanes, (1, 6), (0, 8), (6, 4)));
        if (lanes > 1) p0 = p0.Append(new OrderPlay(Side.Player, 1, FieldOrderKind.Hold));
        var p1 = new List<LanePlay>();
        if (lanes > 1) p1.Add(new OrderPlay(Side.Player, 1, FieldOrderKind.Advance));
        p1.Add(new SpawnPlay(Side.Player, 0, 7, 3));
        if (lanes > 2) p1.Add(new OrderPlay(Side.Enemy, 2, FieldOrderKind.FallBack));
        var p2 = new LanePlay[] { new OrderPlay(Side.Player, 0, FieldOrderKind.Hunt), new OrderPlay(Side.Enemy, -1, FieldOrderKind.Advance) };
        return [p0.ToArray(), p1.ToArray(), p2];
    }

    /// <summary>Plays the plan: at every pause apply that phase's plays, then step to the next pause (whole phases, or in odd chunks that must give the same match).</summary>
    public static async Task<LaneBattle> Drive(LaneBattle b, IReadOnlyList<LanePlay[]> plan, int phases, Mode mode = Mode.Phases)
    {
        int[] chunks = [7, 13, 1, 50, 3, 29];
        int ci = 0;
        for (int p = 0; p < phases && b.Status == LaneStatus.Running; p++)
        {
            if (p < plan.Count && plan[p].Length > 0) b.ApplyPlays(plan[p]);
            if (mode == Mode.Phases) await b.StepAsync();
            else do { await b.StepAsync(chunks[ci++ % chunks.Length]); } while (!b.AtPause && b.Status == LaneStatus.Running);
        }
        return b;
    }

    public static async Task<LaneBattle> Run(LaneSetup s, int seed, IReadOnlyList<LanePlay[]> plan, int phases, Mode mode = Mode.Phases)
        => await Drive(await New(s, seed), plan, phases, mode);

    public static bool SameUnits(LaneSnapshot a, LaneSnapshot b) => a.Tick == b.Tick && a.Units.SequenceEqual(b.Units) && a.PlayerBaseHp == b.PlayerBaseHp && a.EnemyBaseHp == b.EnemyBaseHp;
    public static bool SamePauses(LaneBattle a, LaneBattle b) => a.Replay.Pauses.SequenceEqual(b.Replay.Pauses);
}

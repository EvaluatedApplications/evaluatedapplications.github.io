using Doctrine.Battle;

namespace Doctrine;

// THE PRE-BATTLE BRIEFING (Doctrine\todo\design-queue-2026-10-19.md item 6). One read-only function the page calls BEFORE the player chooses, so the UI never
// copies the arena rule. Every figure comes from the SAME rules the round pipeline uses (ArenaRules, the enemy raising in DraftStage), so a briefing cannot
// disagree with the battle that follows. Enemy opening ORDERS are not here (a later 'scouting' reveal).

/// <summary>The ground of one round: the catalogue entry, who besieges (null = open field or sieges off), and how to draw it (null when the engine cannot say).</summary>
public sealed record ArenaBrief(ArenaInfo Info, Side? Besieger, ArenaLayout? Layout)
{
    public bool IsSiege => Info.IsSiege;
    public int SiegeClock => Info.SiegeClock;
    /// <summary>The tick the gate falls (0 = no gate, or no layout).</summary>
    public int GateFallsAtTick => Layout?.GateFallsAtTick ?? 0;
}

/// <summary>The enemy army as the general reads it at the choose point: cohorts (Slot = line), gold the enemy was raised with, soldiers and power.</summary>
public sealed record EnemyBrief(int Gold, int Soldiers, long Power, IReadOnlyList<CohortRead> Cohorts)
{
    /// <summary>The cohorts grouped by line (slot), front line first.</summary>
    public IReadOnlyList<IReadOnlyList<CohortRead>> Lines => Cohorts.GroupBy(c => c.Slot).OrderBy(g => g.Key).Select(g => (IReadOnlyList<CohortRead>)g.ToList()).ToList();
}

/// <summary>One later round: its ground and the strength of the enemy raised for it.</summary>
public sealed record RoadStep(int Round, ArenaBrief Arena, EnemyBrief Enemy);

public sealed record RoundBriefing(int Seed, int Round, ArenaBrief Arena, EnemyBrief Enemy, IReadOnlyList<RoadStep> Road);

/// <summary>Test hooks: each breaks ONE rule of the briefing so a contract test can be watched going red. Never set outside tests.</summary>
[Flags] internal enum BriefingProbe { None = 0, ArenaRuleWrong = 1, EnemyWrongRound = 2, ParityWrong = 4, RoadStartsAtNow = 8, GoldCurveWrong = 16 }

/// <summary>The rules shared by the round pipeline and the briefing: nothing is copied.</summary>
internal static class RoundRules
{
    public static ArenaInfo ArenaFor(IReadOnlyList<ArenaInfo> arenas, int seed, int round)
        => arenas[(int)(GameRng.Mix(seed, 3000 + round) % (ulong)arenas.Count)];

    public static Side? BesiegerFor(DoctrineDomain d, ArenaInfo arena, int round)
        => d.Siege && arena.IsSiege ? SiegeRules.BesiegerOf(round, d.SwapSiegeRoles) : null;

    public static int EnemyGold(DoctrineDomain d, int round) => d.EnemyBaseGold + d.EnemyGoldPerRound * round;

    /// <summary>The enemy of a round, raised exactly as the draft stage raises it: the opponent's packs, then its free turn of the ranks (0 for the scripted enemy: nothing applied).</summary>
    public static async ValueTask<IArmy> RaiseEnemyAsync(IArmyService armies, IOpponent opponent, DoctrineDomain d, int seed, int round, CancellationToken ct)
    {
        var turn = new OpponentRound(seed, round, EnemyGold(d, round));
        var picks = await opponent.DraftAsync(turn, ct);
        var enemy = armies.Create(Side.Enemy, (int)GameRng.Mix(seed, round));
        await enemy.DraftAsync(new DraftRequest(round, seed, picks), ct);
        await RoundLaws.RotateAsync(enemy, await opponent.FormationAsync(turn, ct), ct);
        return enemy;
    }

    /// <summary>The enemy of a round as the briefing shows it, before anyone has chosen: from <see cref="IOpponent.Preview"/>. An opponent that cannot be previewed (a remote seat) is refused.</summary>
    public static async ValueTask<IArmy> PreviewEnemyAsync(IArmyService armies, IOpponent opponent, DoctrineDomain d, int seed, int round, CancellationToken ct)
    {
        var picks = opponent.Preview(new OpponentRound(seed, round, EnemyGold(d, round)))
            ?? throw new InvalidOperationException("this opponent's picks are secret until it reveals them, so there is no briefing of its army");
        var enemy = armies.Create(Side.Enemy, (int)GameRng.Mix(seed, round));
        await enemy.DraftAsync(new DraftRequest(round, seed, picks), ct);
        return enemy;
    }
}

public sealed partial class DoctrineGame
{
    /// <summary>The arena round <paramref name="round"/> of <paramref name="seed"/> is fought on (the one the setup stage picks).</summary>
    public ArenaInfo ArenaFor(int seed, int round) => RoundRules.ArenaFor(Context.Battle.Arenas, seed, round);

    /// <summary>
    /// Everything the page may show before the player chooses round <paramref name="round"/>: the arena (with who besieges and how to draw it), the enemy army
    /// by line, and the next <paramref name="roadAhead"/> rounds (clipped at the last round of the game). Read-only: it raises throwaway armies and touches no game state.
    /// </summary>
    public ValueTask<RoundBriefing> BriefingAsync(int seed, int round, int roadAhead = 3, CancellationToken ct = default) => BriefingAsync(seed, round, roadAhead, BriefingProbe.None, ct);

    internal async ValueTask<RoundBriefing> BriefingAsync(int seed, int round, int roadAhead, BriefingProbe probe, CancellationToken ct)
    {
        if (round < 0 || round >= Rules.MaxRounds) throw new ArgumentOutOfRangeException(nameof(round), $"round must be 0..{Rules.MaxRounds - 1}");
        var arenas = Context.Battle.Arenas;
        async ValueTask<(ArenaBrief, EnemyBrief)> ForRound(int r)
        {
            var info = probe.HasFlag(BriefingProbe.ArenaRuleWrong) ? arenas[(int)(GameRng.Mix(seed, 3001 + r) % (ulong)arenas.Count)] : RoundRules.ArenaFor(arenas, seed, r);
            Side? b = RoundRules.BesiegerFor(Rules, info, probe.HasFlag(BriefingProbe.ParityWrong) ? r + 1 : r);
            var arena = new ArenaBrief(info, b, Context.LayoutOf(info.Id));
            int er = probe.HasFlag(BriefingProbe.EnemyWrongRound) ? r + 1 : r;
            var army = await RoundRules.PreviewEnemyAsync(Context.Armies, Context.Opponent, Rules, seed, er, ct);
            var read = army.Read();
            long power = read.Cohorts.Sum(c => UnitRules.Rating(c.Count, c.PowerPct, c.Traits));
            int gold = RoundRules.EnemyGold(Rules, probe.HasFlag(BriefingProbe.GoldCurveWrong) ? r + 1 : r);
            return (arena, new EnemyBrief(gold, read.Soldiers, power, read.Cohorts));
        }
        var (a0, e0) = await ForRound(round);
        var road = new List<RoadStep>();
        int first = probe.HasFlag(BriefingProbe.RoadStartsAtNow) ? round : round + 1;
        for (int r = first; r <= round + roadAhead && r < Rules.MaxRounds; r++) { var (a, e) = await ForRound(r); road.Add(new RoadStep(r, a, e)); }
        return new RoundBriefing(seed, round, a0, e0, road);
    }
}

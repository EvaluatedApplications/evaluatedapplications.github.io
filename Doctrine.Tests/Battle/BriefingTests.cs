using Doctrine.Player;

namespace Doctrine.Tests;

/// <summary>
/// The briefing (design-queue item 6) must equal what the pipeline later does. Pre-registered: for many seeds and rounds, (1) the briefing arena and besieger equal the
/// setup the engine is handed, (2) the enemy read at the choose point equals the briefing's, (3) the road ahead equals what later rounds draw. Each check is a function
/// of a probe so a mutant (arena rule copied wrong, enemy from the wrong round, siege parity wrong, road off by one, gold curve wrong) goes red.
/// </summary>
public class BriefingTests
{
    sealed class Spy(IArmyService inner) : IArmyService
    {
        public List<IArmy> Enemies { get; } = [];
        public IArmy Create(Side side, int seed) { var a = inner.Create(side, seed); if (side == Side.Enemy) Enemies.Add(a); return a; }
        public bool TryParse(string code, out IArmy? army, out string error) => inner.TryParse(code, out army, out error);
    }

    sealed class Watching(Spy spy, List<ArmyReadModel> seen) : IPlayerInput
    {
        readonly IPlayerInput _in = new ScriptedPlayer(1);
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => _in.DraftAsync(army, round, ct);
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct) => _in.DraftAsync(army, offer, round, ct);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => _in.ChooseAsync(army, ghost, round, ct);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
        { seen.Add(spy.Enemies[^1].Read()); return _in.ChooseAsync(army, ghost, offer, round, ct); }
        public ValueTask<bool> RerollAsync(ArmyReadModel army, OrderOffer offer, int round, CancellationToken ct) => _in.RerollAsync(army, offer, round, ct);
    }

    sealed class Recording(IBattleEngine inner) : IBattleEngine
    {
        public List<BattleSetup> Setups { get; } = [];
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
        public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) { Setups.Add(setup); return inner.StartAsync(setup, ct); }
    }

    static bool Same(IReadOnlyList<CohortRead> a, IReadOnlyList<CohortRead> b) => a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);

    /// <summary>Plays one game and compares every round with a briefing made BEFORE the game (null = all equal).</summary>
    internal static async Task<string?> BriefingEqualsTheGame(int seed, BriefingProbe probe, int rounds = 5)
    {
        var spy = new Spy(new Doctrine.Army.StubArmyService()); var seen = new List<ArmyReadModel>();
        var engine = new Recording(TracerWorlds.Engine());
        var game = DoctrineBuilder.New().WithArmy(spy).WithBattle(engine).WithPlayer(new Watching(spy, seen)).WithRules(new DoctrineDomain { MaxRounds = rounds }).Build();
        var briefs = new List<RoundBriefing>();
        for (int r = 0; r < rounds; r++) briefs.Add(await game.BriefingAsync(seed, r, 3, probe, default));
        await game.PlayAsync(seed);
        if (engine.Setups.Count != rounds || seen.Count != rounds) return $"seed {seed}: played {engine.Setups.Count} battles, saw {seen.Count} choose points, expected {rounds}";
        for (int r = 0; r < rounds; r++)
        {
            var b = briefs[r]; var s = engine.Setups[r];
            if (b.Arena.Info.Id != s.ArenaId) return $"seed {seed} round {r}: briefing arena {b.Arena.Info.Id}, battle on {s.ArenaId}";
            if (b.Arena.Besieger != s.Besieger) return $"seed {seed} round {r}: briefing besieger {b.Arena.Besieger}, battle {s.Besieger}";
            if (!Same(b.Enemy.Cohorts, seen[r].Cohorts)) return $"seed {seed} round {r}: the enemy in the briefing is not the enemy read at the choose point";
            if (b.Enemy.Soldiers != seen[r].Soldiers) return $"seed {seed} round {r}: enemy soldiers differ";
            if (b.Enemy.Gold != 14 + 4 * r) return $"seed {seed} round {r}: enemy gold {b.Enemy.Gold}, expected {14 + 4 * r}";
            // the road ahead against what those later rounds actually drew
            for (int i = 0; i < b.Road.Count; i++)
            {
                var step = b.Road[i]; int lr = r + 1 + i;
                if (step.Round != lr) return $"seed {seed} round {r}: road step {i} is round {step.Round}, expected {lr}";
                if (step.Arena.Info.Id != engine.Setups[lr].ArenaId || step.Arena.Besieger != engine.Setups[lr].Besieger) return $"seed {seed} round {r}: road round {lr} arena or besieger differs from the battle";
                if (!Same(step.Enemy.Cohorts, seen[lr].Cohorts) || step.Enemy.Gold != 14 + 4 * lr) return $"seed {seed} round {r}: road round {lr} enemy differs from the one raised then";
            }
            if (b.Road.Count != Math.Min(3, rounds - 1 - r)) return $"seed {seed} round {r}: road has {b.Road.Count} steps";
        }
        return null;
    }

    [Fact]
    public async Task The_briefing_equals_the_game_for_many_seeds()
    {
        for (int seed = 1; seed <= 12; seed++) Assert.Null(await BriefingEqualsTheGame(seed, BriefingProbe.None));
    }

    [Fact]
    public async Task The_briefing_names_the_siege_and_the_layout()
    {
        var game = DoctrineBuilder.New().WithBattle(TracerWorlds.Engine()).Build();
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 20; seed++)
            for (int r = 0; r < 3; r++)
            {
                var b = (await game.BriefingAsync(seed, r)).Arena;
                seen.Add(b.Info.Id);
                Assert.Equal(b.Info.IsSiege, b.Besieger is not null);
                Assert.Equal(b.Info.IsSiege ? ((r & 1) == 0 ? Side.Player : Side.Enemy) : (Side?)null, b.Besieger);
                Assert.NotNull(b.Layout); Assert.Equal(b.Info.Id, b.Layout!.Id);
                Assert.Equal(b.Info.Id == "falling-gate" ? 14 : 0, b.GateFallsAtTick);
                Assert.Equal(game.ArenaFor(seed, r), b.Info);
            }
        Assert.True(seen.Count >= 3);
    }

    [Fact] public async Task An_arena_rule_copied_wrong_is_caught() => Assert.NotNull(await Any(BriefingProbe.ArenaRuleWrong));
    [Fact] public async Task An_enemy_read_from_the_wrong_round_is_caught() => Assert.NotNull(await Any(BriefingProbe.EnemyWrongRound));
    [Fact] public async Task A_siege_parity_that_is_wrong_is_caught() => Assert.NotNull(await Any(BriefingProbe.ParityWrong));
    [Fact] public async Task A_road_that_starts_at_now_is_caught() => Assert.NotNull(await Any(BriefingProbe.RoadStartsAtNow));
    [Fact] public async Task A_gold_curve_that_is_wrong_is_caught() => Assert.NotNull(await Any(BriefingProbe.GoldCurveWrong));

    internal static async Task<string?> Any(BriefingProbe p)
    {
        for (int seed = 1; seed <= 12; seed++) { var r = await BriefingEqualsTheGame(seed, p); if (r is not null) return r; }
        return null;
    }
}

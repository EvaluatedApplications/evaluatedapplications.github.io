namespace Doctrine.Tests;

/// <summary>
/// The opponent seam (Doctrine\todo\duel-seam-2026-10-19.md, item 1). The scripted opponent must BE the old enemy: the digest pins in DigestPinTests are untouched, and here a frozen test-side
/// copy of the old enemy is held against it (functions, then whole games). A mutant that alters the scripted enemy must turn both red.
/// </summary>
public class OpponentSeamTests
{
    /// <summary>The enemy of the single-player game exactly as it was before the seam existed (copied, never edited).</summary>
    static class LegacyEnemy
    {
        public static IReadOnlyList<DraftPick> Picks(int round, int seed, int gold)
        {
            var rng = new GameRng(GameRng.Mix(seed, 1000 + round));
            var picks = new List<DraftPick>();
            for (int i = 0; i < 3 && gold > 0; i++)
            {
                int kind = rng.Next(Roster.Size);
                int n = Math.Max(1, (i < 2 ? gold / 2 : gold) / ArmyRules.Default.DraftPrice(kind));
                if (n * ArmyRules.Default.DraftPrice(kind) > gold) continue;
                picks.Add(new(kind, n)); gold -= n * ArmyRules.Default.DraftPrice(kind);
            }
            return picks;
        }

        public static DoctrineSet DoctrineFor(int round, int seed, LegalMask legal)
        {
            var rng = new GameRng(GameRng.Mix(seed, 2000 + round));
            var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows).ToList();
            var tokens = new List<int>();
            for (int i = 0; i < 3 && pool.Count > 0; i++) tokens.Add(pool[rng.Next(pool.Count)]);
            return DoctrineSet.Of(tokens.ToArray());
        }
    }

    sealed class LegacyOpponent : IOpponent
    {
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(LegacyEnemy.Picks(r.Round, r.Seed, r.Gold));
        public ValueTask<int> FormationAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask<DoctrineSet> DoctrineAsync(OpponentRound r, LegalMask legal, CancellationToken ct) => ValueTask.FromResult(LegacyEnemy.DoctrineFor(r.Round, r.Seed, legal));
        public IReadOnlyList<DraftPick>? Preview(OpponentRound r) => LegacyEnemy.Picks(r.Round, r.Seed, r.Gold);
    }

    /// <summary>MUTANT: the scripted enemy forgets its last pack.</summary>
    sealed class AlteredScripted : IOpponent
    {
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound r, CancellationToken ct)
        { var p = ScriptedOpponent.Picks(r.Round, r.Seed, r.Gold); return ValueTask.FromResult<IReadOnlyList<DraftPick>>(p.Take(Math.Max(0, p.Count - 1)).ToList()); }
        public ValueTask<int> FormationAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask<DoctrineSet> DoctrineAsync(OpponentRound r, LegalMask legal, CancellationToken ct) => ValueTask.FromResult(ScriptedOpponent.DoctrineFor(r.Round, r.Seed, legal));
        public IReadOnlyList<DraftPick>? Preview(OpponentRound r) => ScriptedOpponent.Picks(r.Round, r.Seed, r.Gold);
    }

    static readonly int[] Purses = [0, 1, 5, 14, 18, 26, 60, 200];

    /// <summary>The scripted opponent against the frozen enemy on the functions themselves. Null when they agree everywhere.</summary>
    static async Task<string?> FunctionsAgree(IOpponent candidate)
    {
        var rng = new Random(77);
        for (int seed = 0; seed < 300; seed++)
            for (int round = 0; round < 13; round++)
                foreach (int gold in Purses)
                {
                    var turn = new OpponentRound(seed, round, gold);
                    var want = LegacyEnemy.Picks(round, seed, gold); var got = await candidate.DraftAsync(turn, default);
                    if (!want.SequenceEqual(got)) return $"seed {seed} round {round} gold {gold}: picks differ";
                    if (await candidate.FormationAsync(turn, default) != 0) return "the scripted enemy turns its ranks";
                    var masks = new[] { LegalMask.All, LegalMask.None, new LegalMask(((ulong)rng.NextInt64() & LegalMask.All.Bits) | 1UL), new LegalMask(((ulong)rng.NextInt64() & LegalMask.All.Bits) | 1UL) };
                    foreach (var m in masks)
                        if (LegacyEnemy.DoctrineFor(round, seed, m) != await candidate.DoctrineAsync(turn, m, default)) return $"seed {seed} round {round} mask {m.Bits}: doctrine differs";
                }
        return null;
    }

    [Fact] public async Task The_scripted_opponent_agrees_with_the_frozen_old_enemy_on_every_pick_and_doctrine() => Assert.Null(await FunctionsAgree(ScriptedOpponent.Instance));
    [Fact] public async Task An_altered_scripted_enemy_is_caught_by_the_function_check() => Assert.NotNull(await FunctionsAgree(new AlteredScripted()));

    static async Task<ulong> Game(string army, int seed, int rounds, IOpponent? opponent)
    {
        var b = DoctrineBuilder.New().WithPlayer(new ScriptedPlayer(1)).WithRules(new DoctrineDomain { MaxRounds = rounds });
        if (army == "pleroma")
            b.WithDefaults(r => r.WithArmy(new PleromaArmyService()).WithTracer(TracerWorlds.Scratch())
                .WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!)).WithGeneral(new TinyHoloGeneral()));
        if (opponent is not null) b.WithOpponent(opponent);
        return (await b.Build().PlayAsync(seed)).Digest();
    }

    [Fact]
    public async Task A_stub_game_with_the_frozen_old_enemy_is_the_default_game_to_the_digest_for_many_seeds()
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            var dflt = await Game("stub", seed, 8, null);
            Assert.Equal(dflt, await Game("stub", seed, 8, new LegacyOpponent()));
            Assert.Equal(dflt, await Game("stub", seed, 8, ScriptedOpponent.Instance));
        }
    }

    [Fact]
    public async Task A_real_section_game_with_the_frozen_old_enemy_is_the_default_game_to_the_digest()
    {
        foreach (int seed in new[] { 1234, 7, 99 })
            Assert.Equal(await Game("pleroma", seed, 3, null), await Game("pleroma", seed, 3, new LegacyOpponent()));
    }

    [Fact]
    public async Task An_altered_scripted_enemy_changes_the_game_digest()
    {
        int changed = 0;
        for (int seed = 1; seed <= 10; seed++) if (await Game("stub", seed, 8, null) != await Game("stub", seed, 8, new AlteredScripted())) changed++;
        Assert.True(changed >= 8, $"only {changed} of 10 games noticed the altered enemy");
    }

    // ---- the duel implementation ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_duel_opponent_waits_for_the_hand_in_and_answers_from_it()
    {
        var box = new DuelOpponent();
        var picks = new DuelPicks([new(2, 3)], DoctrineSet.Of(5, 6), 1);
        var draft = box.DraftAsync(new OpponentRound(1, 4, 20), default).AsTask();
        var form = box.FormationAsync(new OpponentRound(1, 4, 20), default).AsTask();
        var doc = box.DoctrineAsync(new OpponentRound(1, 4, 20), LegalMask.All, default).AsTask();
        await Task.Delay(30);
        Assert.False(draft.IsCompleted); Assert.False(form.IsCompleted); Assert.False(doc.IsCompleted); Assert.False(box.Has(4));
        Assert.True(box.HandIn(4, picks));
        Assert.Equal(new[] { new DraftPick(2, 3) }, await draft);
        Assert.Equal(1, await form);
        Assert.Equal(DoctrineSet.Of(5, 6), await doc);
        Assert.True(box.Has(4));
    }

    [Fact]
    public async Task A_duplicate_hand_in_does_nothing_and_the_first_stands()
    {
        var box = new DuelOpponent();
        Assert.True(box.HandIn(0, new DuelPicks([], DoctrineSet.Of(1))));
        Assert.False(box.HandIn(0, new DuelPicks([], DoctrineSet.Of(2))));
        Assert.Equal(DoctrineSet.Of(1), (await box.PicksAsync(0)).Doctrine);
        Assert.True(box.HandIn(1, DuelPicks.Empty));   // another round is another slot
    }

    [Fact]
    public async Task A_waiting_question_can_be_cancelled()
    {
        var box = new DuelOpponent(); using var cts = new CancellationTokenSource();
        var t = box.PicksAsync(0, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);
        Assert.True(box.HandIn(0, DuelPicks.Empty));       // a cancelled question leaves the slot usable
    }

    sealed class Spy(IArmyService inner) : IArmyService
    {
        public List<IArmy> Enemies { get; } = [];
        public IArmy Create(Side side, int seed) { var a = inner.Create(side, seed); if (side == Side.Enemy) Enemies.Add(a); return a; }
        public bool TryParse(string code, out IArmy? army, out string error) => inner.TryParse(code, out army, out error);
    }

    sealed class Looking(Spy spy, List<ArmyReadModel> seen) : IPlayerInput
    {
        readonly IPlayerInput _in = new ScriptedPlayer(1);
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => _in.DraftAsync(army, round, ct);
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct) => _in.DraftAsync(army, offer, round, ct);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => _in.ChooseAsync(army, ghost, round, ct);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
        { seen.Add(spy.Enemies[^1].Read()); return _in.ChooseAsync(army, ghost, offer, round, ct); }
    }

    sealed class Formed(IOpponent inner, int formation) : IOpponent
    {
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound r, CancellationToken ct) => inner.DraftAsync(r, ct);
        public ValueTask<int> FormationAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(formation);
        public ValueTask<DoctrineSet> DoctrineAsync(OpponentRound r, LegalMask legal, CancellationToken ct) => inner.DoctrineAsync(r, legal, ct);
    }

    static async Task<ArmyReadModel> EnemyAtChoice(IOpponent? opponent)
    {
        var spy = new Spy(new StubArmyService()); var seen = new List<ArmyReadModel>();
        var b = DoctrineBuilder.New().WithArmy(spy).WithPlayer(new Looking(spy, seen)).WithRules(new DoctrineDomain { MaxRounds = 1 });
        if (opponent is not null) b.WithOpponent(opponent);
        await b.Build().PlayAsync(5);
        return seen.Single();
    }

    [Fact]
    public async Task A_formation_turns_the_enemys_ranks_by_the_armys_own_rotate()
    {
        var plain = await EnemyAtChoice(null);
        for (int f = 1; f <= 2; f++)
        {
            var turned = await EnemyAtChoice(new Formed(ScriptedOpponent.Instance, f));
            Assert.Equal(plain.Cohorts.Select(c => (c.Key, c.Count)), turned.Cohorts.Select(c => (c.Key, c.Count)));
            Assert.Equal(plain.Cohorts.Select(c => (c.Slot + f) % DoctrineVocabulary.Groups), turned.Cohorts.Select(c => c.Slot));
        }
        // a formation that is not 0..2 changes nothing
        var refused = await EnemyAtChoice(new Formed(ScriptedOpponent.Instance, 7));
        Assert.Equal(plain.Cohorts, refused.Cohorts);
    }

    [Fact]
    public async Task The_briefing_refuses_an_opponent_whose_picks_are_secret()
    {
        var game = DoctrineBuilder.New().WithOpponent(new DuelOpponent()).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await game.BriefingAsync(1, 0));
        // and an opponent that can be previewed is briefed as before
        var scripted = DoctrineBuilder.New().WithOpponent(new LegacyOpponent()).Build();
        var plain = DoctrineBuilder.New().Build();
        Assert.Equal((await plain.BriefingAsync(1, 0)).Enemy.Cohorts, (await scripted.BriefingAsync(1, 0)).Enemy.Cohorts);
    }

    [Fact]
    public async Task A_duel_opponent_fed_the_scripted_picks_plays_the_scripted_games_enemy()
    {
        // The pipeline can take a DuelOpponent as its enemy: hand it the scripted picks (and no orders) and the game is the scripted game with an enemy that gives no orders.
        var box = new DuelOpponent();
        var noOrders = new NoOrders(ScriptedOpponent.Instance);
        for (int r = 0; r < 3; r++) box.HandIn(r, new DuelPicks(ScriptedOpponent.Picks(r, 21, 14 + 4 * r), DoctrineSet.Empty));
        Assert.Equal(await Game("stub", 21, 3, noOrders), await Game("stub", 21, 3, box));
    }

    sealed class NoOrders(IOpponent inner) : IOpponent
    {
        // A DuelPicks keeps its packs in (kind, count) order; two packs of one kind bought with too little gold for both are judged first-come, so the scripted list is put in that order too.
        public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound r, CancellationToken ct) => (await inner.DraftAsync(r, ct)).OrderBy(p => p.Kind).ThenBy(p => p.Count).ToList();
        public ValueTask<int> FormationAsync(OpponentRound r, CancellationToken ct) => inner.FormationAsync(r, ct);
        public ValueTask<DoctrineSet> DoctrineAsync(OpponentRound r, LegalMask legal, CancellationToken ct) => ValueTask.FromResult(DoctrineSet.Empty);
    }
}

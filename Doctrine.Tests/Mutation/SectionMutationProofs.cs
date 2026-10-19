namespace Doctrine.Tests;

/// <summary>
/// Mutation proofs for the cases added in the amendment pass, run through the SHARED mutants over BOTH the reference stub and the real
/// section of each seam. A case that no mutant ever made fail is only an assertion; every row here is a case watched red.
/// </summary>
public class SectionMutationProofs
{
    static void Caught(IReadOnlyList<ContractFailure> failures, string expectedCase)
        => Assert.True(failures.Any(f => f.Case == expectedCase), $"expected '{expectedCase}' to fail; failures were: [{string.Join(" | ", failures)}]");

    // ---- ARMY: the new cases, over the stub and over the Pleroma army ----
    const string Timing = "orders-see-the-state-earlier-orders-left", SlotCase = "loss-names-its-cohort-by-kind-not-slot",
                 Reason = "refused-draft-changes-nothing", Picks = "draft-is-decided-by-the-picks-alone",
                 Ring = "veterancy-lives-on-a-ring", Score = "score-follows-soldiers-and-gold", FlipPaid = "flip-is-paid-for-and-legal-only-when-affordable",
                 AgePaid = "age-is-paid-for-and-legal-only-when-affordable", AuraRent = "aura-is-rented-and-legal-only-when-affordable",
                 Interest = "banked-gold-pays-interest-when-a-round-closes";

    [Theory]
    [InlineData(ArmyMut.LegalityOnce, Timing)]
    [InlineData(ArmyMut.LossSlotMatters, SlotCase)]
    [InlineData(ArmyMut.DraftRefusalNoReason, Reason)]
    [InlineData(ArmyMut.DraftUsesSeed, Picks)]
    [InlineData(ArmyMut.VeterancyGrows, Ring)]
    [InlineData(ArmyMut.ScoreFallsWithGold, Score)]
    [InlineData(ArmyMut.FlipAlwaysLegal, FlipPaid)]
    [InlineData(ArmyMut.FlipIsFree, FlipPaid)]
    [InlineData(ArmyMut.InterestDoubled, Interest)]
    public async Task Army_mutants_over_the_stub_are_caught(ArmyMut m, string expected)
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(new StubArmyService(), m)), expected);

    [Theory]
    [InlineData(ArmyMut.LegalityOnce, Timing)]
    [InlineData(ArmyMut.LossSlotMatters, SlotCase)]
    [InlineData(ArmyMut.DraftRefusalNoReason, Reason)]
    [InlineData(ArmyMut.DraftUsesSeed, Picks)]
    [InlineData(ArmyMut.VeterancyGrows, Ring)]
    [InlineData(ArmyMut.ScoreFallsWithGold, Score)]
    [InlineData(ArmyMut.FlipAlwaysLegal, FlipPaid)]
    [InlineData(ArmyMut.FlipIsFree, FlipPaid)]
    [InlineData(ArmyMut.InterestDoubled, Interest)]
    [InlineData(ArmyMut.ClaimsEverythingLegal, "legal-list-matches-behaviour")]
    [InlineData(ArmyMut.StaleCode, "code-round-trips-everything")]
    [InlineData(ArmyMut.LossesAppliedTwice, "losses-apply-once")]
    [InlineData(ArmyMut.NondeterministicCreate, "create-is-deterministic")]
    public async Task Army_mutants_over_the_pleroma_army_are_caught(ArmyMut m, string expected)
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(new PleromaArmyService(), m)), expected);


    // The fee of a reroll (fourth round).
    [Theory]
    [InlineData(ArmyMut.PayIsFree)]
    [InlineData(ArmyMut.PayAlwaysYes)]
    public async Task Fee_mutants_are_caught_on_both_armies(ArmyMut m)
    {
        const string c = "a-fee-is-paid-from-the-purse-or-refused-and-changes-nothing";
        Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(new StubArmyService(), m)), c);
        Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(new PleromaArmyService(), m)), c);
    }

    // The balance rule of placement (fourth round): the old kind-mod-3 seeding is a rule switch, and it is the mutant.
    [Fact]
    public async Task The_old_seeding_switched_back_on_is_caught_by_the_balance_case_on_both_armies()
    {
        var old = new ArmyRules { BalancedPlacement = false };
        Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new StubArmyService(old)), "a-new-cohort-joins-the-rank-with-the-least-of-its-class");
        Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new PleromaArmyService(old)), "a-new-cohort-joins-the-rank-with-the-least-of-its-class");
    }
    // Age and Aura are free in the game (third round), so their two cases run on services built with the rules ON; each mutant breaks one rule and the case must go red.
    [Theory]
    [InlineData(ArmyMut.AgeIsFree, AgePaid)]
    [InlineData(ArmyMut.AgeAlwaysLegal, AgePaid)]
    [InlineData(ArmyMut.AuraIsFree, AuraRent)]
    [InlineData(ArmyMut.AuraAlwaysLegal, AuraRent)]
    public async Task Priced_army_mutants_are_caught_on_both_armies(ArmyMut m, string expected)
    {
        Caught(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new MutantArmyService(new StubArmyService(ArmyContract.PricedRules), m)), expected);
        Caught(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new MutantArmyService(new PleromaArmyService(ArmyContract.PricedRules), m)), expected);
    }

    [Fact]
    public async Task The_priced_cases_hold_on_clean_priced_armies_and_would_fail_on_the_free_game()
    {
        Assert.Empty(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new StubArmyService(ArmyContract.PricedRules)));
        Assert.Empty(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new PleromaArmyService(ArmyContract.PricedRules)));
        // the game's rules: an Age and an Aura cost nothing, which is exactly what the cases forbid
        Caught(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new StubArmyService()), ArmyContract.AgeCase);
        Caught(await ContractRunner.RunAllAsync(ArmyContract.PricedCases, () => new PleromaArmyService()), ArmyContract.AuraCase);
    }
    // The section's own rule switches are mutants too: turn ONE rule off and the case that states it must go red (stub and Pleroma army alike).
    [Theory]
    [InlineData("age", AgePaid)]
    [InlineData("aura", AuraRent)]
    [InlineData("interest", Interest)]
    [InlineData("reach", FlipPaid)]
    public async Task A_rule_switched_off_is_caught_on_both_armies(string rule, string expected)
    {
        var rules = rule switch
        {
            "age" => ArmyContract.PricedRules with { PriceAge = false },
            "aura" => ArmyContract.PricedRules with { PriceAura = false },
            "interest" => new ArmyRules { BankInterest = false },
            _ => new ArmyRules { PriceReach = false },
        };
        var cases = rule is "age" or "aura" ? ArmyContract.PricedCases : ArmyContract.Cases;
        Caught(await ContractRunner.RunAllAsync(cases, () => new StubArmyService(rules)), expected);
        Caught(await ContractRunner.RunAllAsync(cases, () => new PleromaArmyService(rules)), expected);
    }

    [Fact]
    public async Task The_free_form_army_rewriter_catches_a_read_that_breaks_the_well_formed_army()
    {
        // power of zero is not allowed on a fresh army: the rewriter lets a section owner break any one read and watch a case fail
        var bad = new ReadMutantArmyService(new PleromaArmyService(), r => r with { Cohorts = r.Cohorts.Select(c => c with { PowerPct = 0 }).ToList() });
        Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => bad), "fresh-army-is-well-formed");
    }

    // ---- BATTLE: the limit case, over the stub and over Tracer ----
    const string Limit = "a-battle-nobody-can-win-in-time-runs-to-the-limit-and-is-decided";

    [Fact]
    public async Task Stub_that_ignores_the_tick_limit_is_caught_by_the_forced_limit_case()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.TickLimitIgnored)), Limit);

    [Fact]
    public async Task Tracer_that_ignores_the_tick_limit_is_caught_by_the_forced_limit_case_even_though_its_sample_fight_ends_early()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantOver(TracerWorlds.Engine(), BattleMut.TickLimitIgnored)), Limit);

    [Fact]
    public async Task A_battle_that_runs_one_tick_past_the_limit_is_caught_by_the_forced_limit_case()
    {
        // a session that records one tick too many but is stopped by the pipeline would still report ticks one past the limit in its result
        var engine = new ResultMutantEngine(new StubBattleEngine(), r => r with { Ticks = r.Ticks + 1 });
        Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => engine), Limit);
    }


    // The tick limit counts POWER left (third round). A section that goes back to counting soldiers must turn the contract red, on the stub and on Tracer.
    const string PowerLimit = "tick-limit-decides-by-power-left";

    /// <summary>Decides a battle the limit stopped by soldiers left (the old rule), whatever the engine under it says.</summary>
    sealed class BodiesMutantEngine(IBattleEngine inner) : IBattleEngine
    {
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
        public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => new Session(await inner.StartAsync(setup, ct), setup);
        sealed class Session(IBattleSession s, BattleSetup setup) : IBattleSession
        {
            public int Tick => s.Tick;
            public BattleStatus Status => s.Status;
            public ValueTask FieldOrdersAsync(CancellationToken ct) => s.FieldOrdersAsync(ct);
            public ValueTask MoveAsync(CancellationToken ct) => s.MoveAsync(ct);
            public ValueTask EngageAsync(CancellationToken ct) => s.EngageAsync(ct);
            public ValueTask ApplyLossesAsync(CancellationToken ct) => s.ApplyLossesAsync(ct);
            public ReplayFrame Snapshot() => s.Snapshot();
            public ReplayFrame Record() => s.Record();
            public BattleResult Finish(bool tickLimitReached)
            {
                var r = s.Finish(tickLimitReached);
                if (!tickLimitReached || r.Outcome is BattleStatus.Running) return r;
                int a = setup.Attacker.Cohorts.Sum(c => c.Count) - r.AttackerLosses.Sum(l => l.Lost), d = setup.Defender.Cohorts.Sum(c => c.Count) - r.DefenderLosses.Sum(l => l.Lost);
                return r with { Outcome = a > d ? BattleStatus.AttackerWon : d > a ? BattleStatus.DefenderWon : BattleStatus.Draw };
            }
        }
    }

    [Fact]
    public async Task A_limit_that_counts_soldiers_instead_of_power_is_caught_on_the_stub()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new BodiesMutantEngine(new StubBattleEngine())), Limit);

    [Fact]
    public async Task A_limit_that_counts_soldiers_instead_of_power_is_caught_on_tracer()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new BodiesMutantEngine(TracerWorlds.Engine())), Limit);
    /// <summary>Rewrites the finished result, over any engine (the result twin of <see cref="FrameMutantEngine"/>).</summary>
    sealed class ResultMutantEngine(IBattleEngine inner, Func<BattleResult, BattleResult> rewrite) : IBattleEngine
    {
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
        public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => new Session(await inner.StartAsync(setup, ct), rewrite);
        sealed class Session(IBattleSession s, Func<BattleResult, BattleResult> rewrite) : IBattleSession
        {
            public int Tick => s.Tick;
            public BattleStatus Status => s.Status;
            public ValueTask FieldOrdersAsync(CancellationToken ct) => s.FieldOrdersAsync(ct);
            public ValueTask MoveAsync(CancellationToken ct) => s.MoveAsync(ct);
            public ValueTask EngageAsync(CancellationToken ct) => s.EngageAsync(ct);
            public ValueTask ApplyLossesAsync(CancellationToken ct) => s.ApplyLossesAsync(ct);
            public ReplayFrame Snapshot() => s.Snapshot();
            public ReplayFrame Record() => s.Record();
            public BattleResult Finish(bool tickLimitReached) => rewrite(s.Finish(tickLimitReached));
        }
    }

    // ---- REPLAY: world flags, over the stub and over Tracer ----
    const string Flags = "world-flags-use-only-defined-bits-and-never-clear";

    static async Task<IReadOnlyList<ContractFailure>> ReplayOf(IBattleEngine engine, Func<ReplayFrame, ReplayFrame> rewrite)
    {
        var replay = (await Sample.RunAsync(new FrameMutantEngine(engine, rewrite), Sample.Setup(engine.Arenas[0].Id, 3, 30, 20, 20))).Replay!;
        return await ContractRunner.RunAllAsync(ReplayContract.Cases, () => replay);
    }

    [Fact] public async Task A_gate_flag_that_clears_again_is_caught_on_the_stub() => Caught(await ReplayOf(new StubBattleEngine(), f => f with { WorldFlags = f.Tick % 2 }), Flags);
    [Fact] public async Task A_gate_flag_that_clears_again_is_caught_on_tracer() => Caught(await ReplayOf(TracerWorlds.Engine(), f => f with { WorldFlags = f.Tick % 2 }), Flags);
    [Fact] public async Task An_undefined_world_bit_is_caught() => Caught(await ReplayOf(new StubBattleEngine(), f => f with { WorldFlags = f.WorldFlags | 4 }), Flags);
    [Fact] public async Task A_start_frame_that_already_shows_a_changed_arena_is_caught() => Caught(await ReplayOf(new StubBattleEngine(), f => f.Tick == 0 ? f with { WorldFlags = 1 } : f), Flags);
    [Fact] public async Task A_negative_world_flag_value_is_caught() => Caught(await ReplayOf(new StubBattleEngine(), f => f with { WorldFlags = -1 }), Flags);

    // ---- GENERAL: the shared mutants over a general that is not self-contained (the table stands in for any real model) ----
    static Task<IReadOnlyList<ContractFailure>> OverGeneral(GeneralMut m)
        => ContractRunner.RunAllAsync(GeneralContract.Cases, () => (Func<IGeneral>)(() => new MutantGeneralOver(new TableLookupGeneral(), m)));

    [Fact] public async Task General_mutant_over_a_general_ignores_the_mask() => Caught(await OverGeneral(GeneralMut.IgnoresMask), "suggestions-are-always-legal");
    [Fact] public async Task General_mutant_over_a_general_ships_pretrained() => Caught(await OverGeneral(GeneralMut.PreTrained), "starts-empty-nothing-is-pretrained");
    [Fact] public async Task General_mutant_over_a_general_is_overconfident() => Caught(await OverGeneral(GeneralMut.OverConfident), "confidence-is-permille");
    [Fact] public async Task General_mutant_over_a_general_never_learns() => Caught(await OverGeneral(GeneralMut.Forgetful), "imitation-repeats-a-consistent-choice");

    [Fact]
    public async Task General_mutant_over_a_general_shares_state()
    {
        var one = new TableLookupGeneral();   // every wrapper reaches the same inner general
        Caught(await ContractRunner.RunAllAsync(GeneralContract.Cases, () => (Func<IGeneral>)(() => new MutantGeneralOver(one, GeneralMut.SharedState))), "generals-do-not-share-state");
    }

    [Fact]
    public async Task The_free_form_general_rewriter_catches_a_suggestion_that_breaks_a_case()
    {
        Caught(await ContractRunner.RunAllAsync(GeneralContract.Cases,
            () => (Func<IGeneral>)(() => new OutputMutantGeneral(new TableLookupGeneral(), (_, o) => o with { ConfidencePermille = o.ConfidencePermille + 5000 }))),
            "confidence-is-permille");
    }
}

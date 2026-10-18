namespace Doctrine.Tests;

/// <summary>
/// MUTATION PROOFS. Each broken section must make the NAMED contract case fail, and the clean stub must pass every case.
/// If one of these goes green on a mutant, the contract has a hole; fix the case, not the mutant.
/// </summary>
public class MutationProofs
{
    static void Caught(IReadOnlyList<ContractFailure> failures, string expectedCase)
        => Assert.True(failures.Any(f => f.Case == expectedCase), $"expected '{expectedCase}' to fail; failures were: [{string.Join(" | ", failures)}]");

    // ---- baselines: the clean stubs hold every contract ----
    [Fact] public async Task Clean_army_stub_passes() => Assert.Empty(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new StubArmyService()));
    [Fact] public async Task Clean_battle_stub_passes() => Assert.Empty(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new StubBattleEngine()));
    [Fact] public async Task Clean_general_stub_passes() => Assert.Empty(await ContractRunner.RunAllAsync(GeneralContract.Cases, () => (Func<IGeneral>)(() => new TableLookupGeneral())));

    // ---- army ----
    [Fact] public async Task Army_that_claims_every_order_is_legal_is_caught()
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(ArmyMut.ClaimsEverythingLegal)), "legal-list-matches-behaviour");

    [Fact] public async Task Army_code_that_goes_stale_is_caught()
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(ArmyMut.StaleCode)), "code-round-trips-everything");

    [Fact] public async Task Army_that_applies_losses_twice_is_caught()
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(ArmyMut.LossesAppliedTwice)), "losses-apply-once");

    [Fact] public async Task Army_create_that_is_not_deterministic_is_caught()
        => Caught(await ContractRunner.RunAllAsync(ArmyContract.Cases, () => new MutantArmyService(ArmyMut.NondeterministicCreate)), "create-is-deterministic");

    // ---- battle ----
    [Fact] public async Task Battle_frames_off_by_one_tick_are_caught()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.FrameTickOffByOne)), "frames-are-consecutive-and-complete");

    [Fact] public async Task Battle_losses_reported_twice_are_caught()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.LossesReportedTwice)), "losses-equal-what-the-replay-shows");

    [Fact] public async Task Battle_that_ignores_the_tick_limit_is_caught()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.TickLimitIgnored)), "battle-ends-with-a-decided-result-inside-the-limit");

    [Fact] public async Task Battle_that_is_not_deterministic_is_caught()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.Nondeterministic)), "same-setup-same-recording");

    [Fact] public async Task Battle_where_soldiers_reappear_is_caught()
        => Caught(await ContractRunner.RunAllAsync(BattleContract.Cases, () => new MutantBattleEngine(BattleMut.SoldiersReappear)), "blobs-are-stable-and-only-lose-soldiers");

    // ---- general ----
    static Task<IReadOnlyList<ContractFailure>> General(GeneralMut m) => ContractRunner.RunAllAsync(GeneralContract.Cases, () => (Func<IGeneral>)(() => new MutantGeneral(m)));

    [Fact] public async Task General_that_ignores_the_legal_mask_is_caught() => Caught(await General(GeneralMut.IgnoresMask), "suggestions-are-always-legal");
    [Fact] public async Task General_that_ships_pretrained_is_caught() => Caught(await General(GeneralMut.PreTrained), "starts-empty-nothing-is-pretrained");
    [Fact] public async Task General_with_confidence_out_of_range_is_caught() => Caught(await General(GeneralMut.OverConfident), "confidence-is-permille");
    [Fact] public async Task General_that_never_learns_is_caught() => Caught(await General(GeneralMut.Forgetful), "imitation-repeats-a-consistent-choice");
    [Fact] public async Task General_that_shares_state_between_players_is_caught() => Caught(await General(GeneralMut.SharedState), "generals-do-not-share-state");

    // ---- replay ----
    static async Task<BattleReplay> Good() => (await Sample.RunAsync(new StubBattleEngine(), Sample.Setup("open-field", 31, 60))).Replay!;
    static async Task<IReadOnlyList<ContractFailure>> Replay(Func<BattleReplay, BattleReplay> break_)
        => await ContractRunner.RunAllAsync(ReplayContract.Cases, () => break_(Good().GetAwaiter().GetResult()));

    [Fact]
    public async Task Clean_replay_passes() => Assert.Empty(await ContractRunner.RunAllAsync(ReplayContract.Cases, () => Good().GetAwaiter().GetResult()));

    [Fact]
    public async Task Replay_missing_its_last_frame_is_caught()
        => Caught(await Replay(r => r with { Frames = r.Frames.Take(r.Frames.Count - 1).ToList() }), "frames-are-in-tick-order-from-zero");

    [Fact]
    public async Task Replay_whose_result_disagrees_with_the_recording_is_caught()
        => Caught(await Replay(r => r with { Result = r.Result with { DefenderLosses = r.Result.DefenderLosses.Select(l => l with { Lost = l.Lost + 1 }).ToList() } }), "final-strengths-agree-with-the-result");
}

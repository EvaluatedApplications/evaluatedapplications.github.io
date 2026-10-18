namespace Doctrine.Tests;

// Deliberately broken sections. Each wraps a working stub and breaks ONE invariant of CONTRACT.md. MutationProofs runs the contract
// cases against each and requires the right case to go red: a contract nobody has watched fail is only an assertion.

public enum ArmyMut { ClaimsEverythingLegal, StaleCode, LossesAppliedTwice, NondeterministicCreate }

public sealed class MutantArmyService(ArmyMut m) : IArmyService
{
    readonly StubArmyService _inner = new();
    int _n;
    public IArmy Create(Side side, int seed) => new MutantArmy(_inner.Create(side, m == ArmyMut.NondeterministicCreate ? seed + _n++ : seed), m);
    public bool TryParse(string code, out IArmy? army, out string error)
    {
        var ok = _inner.TryParse(code, out var a, out error);
        army = ok ? new MutantArmy(a!, m) : null; return ok;
    }
}

sealed class MutantArmy(IArmy inner, ArmyMut m) : IArmy
{
    string? _cached = m == ArmyMut.StaleCode ? inner.ToCode() : null;   // StaleCode: the code is built once, when the army is made
    public Side Side => inner.Side;
    public ArmyReadModel Read()
    {
        var r = inner.Read();
        return m == ArmyMut.ClaimsEverythingLegal ? r with { Legal = LegalMask.All } : r;
    }
    public int Score() => inner.Score();
    public ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct)
    {
        return inner.DraftAsync(request, ct);
    }
    public ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(Doctrine doctrine, CancellationToken ct) => inner.ApplyArmyOrdersAsync(doctrine, ct);
    public async ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct)
    {
        int r = await inner.ApplyLossesAsync(losses, ct);
        if (m == ArmyMut.LossesAppliedTwice) await inner.ApplyLossesAsync(losses, ct);
        return r;
    }
    public ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct) => inner.ApplySpoilsAsync(spoils, ct);
    public ValueTask EndRoundAsync(CancellationToken ct) => inner.EndRoundAsync(ct);
    public string ToCode() => _cached ?? inner.ToCode();
}

public enum BattleMut { FrameTickOffByOne, LossesReportedTwice, TickLimitIgnored, Nondeterministic, SoldiersReappear }

public sealed class MutantBattleEngine(BattleMut m) : IBattleEngine
{
    readonly StubBattleEngine _inner = new();
    static int _runs;   // shared on purpose: the nondeterministic mutant differs between two runs
    public IReadOnlyList<ArenaInfo> Arenas => _inner.Arenas;
    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => new MutantSession(await _inner.StartAsync(setup, ct), m, Interlocked.Increment(ref _runs));
}

sealed class MutantSession(IBattleSession inner, BattleMut m, int runNumber) : IBattleSession
{
    public int Tick => inner.Tick;
    public BattleStatus Status => inner.Status;
    public ValueTask FieldOrdersAsync(CancellationToken ct) => inner.FieldOrdersAsync(ct);
    public ValueTask MoveAsync(CancellationToken ct) => inner.MoveAsync(ct);
    public ValueTask EngageAsync(CancellationToken ct) => inner.EngageAsync(ct);
    public ValueTask ApplyLossesAsync(CancellationToken ct) => inner.ApplyLossesAsync(ct);
    public ReplayFrame Snapshot() => inner.Snapshot();
    public ReplayFrame Record()
    {
        var f = inner.Record();
        return m switch
        {
            BattleMut.FrameTickOffByOne => f with { Tick = f.Tick + 1 },
            BattleMut.Nondeterministic => f with { Blobs = f.Blobs.Select(b => b with { X = b.X + runNumber }).ToList() },
            BattleMut.SoldiersReappear => f.Tick == 3 ? f with { Blobs = f.Blobs.Select(b => b with { Strength = b.Strength + 1 }).ToList() } : f,
            _ => f,
        };
    }
    public BattleResult Finish(bool tickLimitReached)
    {
        var r = inner.Finish(tickLimitReached);
        return m switch
        {
            BattleMut.LossesReportedTwice => r with { AttackerLosses = r.AttackerLosses.Concat(r.AttackerLosses).ToList() },
            BattleMut.TickLimitIgnored when tickLimitReached => r with { Outcome = BattleStatus.Running },
            _ => r,
        };
    }
}

public enum GeneralMut { IgnoresMask, PreTrained, OverConfident, Forgetful, SharedState }

public sealed class MutantGeneral(GeneralMut m) : IGeneral
{
    static Doctrine _shared;   // SharedState: every instance remembers into the same slot
    Doctrine _last; int _n;
    public int ExamplesSeen => m == GeneralMut.PreTrained ? _n + 5 : _n;
    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var d = m == GeneralMut.SharedState ? _shared : _last;
        if (m == GeneralMut.PreTrained && d.IsEmpty) d = Doctrine.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        if (m != GeneralMut.IgnoresMask && m != GeneralMut.PreTrained) d = new(input.Legal.Allows(d.S0) ? d.S0 : 0, input.Legal.Allows(d.S1) ? d.S1 : 0, input.Legal.Allows(d.S2) ? d.S2 : 0, input.Legal.Allows(d.S3) ? d.S3 : 0);
        int conf = d.IsEmpty ? 0 : _n * 1000 / (_n + 2);
        if (m == GeneralMut.OverConfident && !d.IsEmpty) conf = 2000;
        if (m == GeneralMut.PreTrained) conf = 500;
        return ValueTask.FromResult(new GeneralOutput(d, conf));
    }
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct)
    {
        _n++;
        if (m != GeneralMut.Forgetful) { _last = e.Chosen; _shared = e.Chosen; }
        return ValueTask.CompletedTask;
    }
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) { _n++; return ValueTask.CompletedTask; }
}

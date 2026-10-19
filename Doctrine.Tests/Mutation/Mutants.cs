namespace Doctrine.Tests;

// SHARED MUTANTS. Each wraps a working section (the reference stub, or ANY real section) and breaks ONE invariant of CONTRACT.md.
// MutationProofs / SectionMutationProofs run the contract cases against each and require the right case to go red: a contract nobody has
// watched fail is only an assertion. A real section's owner runs the same mutants over their own section:
//   new MutantArmyService(new PleromaArmyService(), ArmyMut.LegalityOnce)     // army
//   new MutantOver(TracerWorlds.Engine(), BattleMut.TickLimitIgnored)         // battle
//   new MutantGeneralOver(new MyGeneral(), GeneralMut.OverConfident)          // general
// and, for an invariant of your own, the free-form rewriters (ReadMutantArmyService, FrameMutantEngine, OutputMutantGeneral).
// The one-argument constructors (MutantArmyService(m), MutantBattleEngine(m), MutantGeneral(m)) wrap the stubs.

// ---- ARMY ----------------------------------------------------------------------------------------------------------

public enum ArmyMut
{
    ClaimsEverythingLegal, StaleCode, LossesAppliedTwice, NondeterministicCreate,
    /// <summary>Judges legality once, before the doctrine, instead of per token against the state earlier tokens left.</summary>
    LegalityOnce,
    /// <summary>Applies a loss only when its Slot agrees with the cohort's current slot (the army must identify a cohort by Key).</summary>
    LossSlotMatters,
    /// <summary>A refused pick comes back with an empty reason.</summary>
    DraftRefusalNoReason,
    /// <summary>The draft depends on the request's Seed (it must depend on the picks alone).</summary>
    DraftUsesSeed,
    /// <summary>The score drops when gold is won.</summary>
    ScoreFallsWithGold,
    /// <summary>Veterancy grows without limit instead of living on a ring.</summary>
    VeterancyGrows,
    /// <summary>The legal list offers every Flip whatever the army can pay.</summary>
    FlipAlwaysLegal,
    /// <summary>A Flip costs nothing: the gold it took is handed back.</summary>
    FlipIsFree,
}

public sealed class MutantArmyService(IArmyService inner, ArmyMut m) : IArmyService
{
    public MutantArmyService(ArmyMut m) : this(new StubArmyService(), m) { }
    int _n;
    public IArmy Create(Side side, int seed) => new MutantArmy(inner.Create(side, m == ArmyMut.NondeterministicCreate ? seed + _n++ : seed), m);
    public bool TryParse(string code, out IArmy? army, out string error)
    {
        var ok = inner.TryParse(code, out var a, out error);
        army = ok ? new MutantArmy(a!, m) : null; return ok;
    }
}

sealed class MutantArmy(IArmy inner, ArmyMut m) : IArmy
{
    string? _cached = m == ArmyMut.StaleCode ? inner.ToCode() : null;   // StaleCode: the code is built once, when the army is made
    int _goldWon, _ordersApplied;
    public Side Side => inner.Side;
    public ArmyReadModel Read()
    {
        var r = inner.Read();
        return m switch
        {
            ArmyMut.ClaimsEverythingLegal => r with { Legal = LegalMask.All },
            ArmyMut.FlipAlwaysLegal => r with { Legal = Enumerable.Range(0, DoctrineVocabulary.Traits).Aggregate(r.Legal, (l, b) => l.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b))) },
            ArmyMut.VeterancyGrows => r with { Cohorts = r.Cohorts.Select(c => c with { Veterancy = c.Veterancy + 10 * _ordersApplied }).ToList() },
            _ => r,
        };
    }
    public int Score() => m == ArmyMut.ScoreFallsWithGold ? Math.Max(0, inner.Score() - 1000 * _goldWon) : inner.Score();
    public async ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct)
    {
        if (m == ArmyMut.DraftUsesSeed && request.Seed >= 2) request = request with { Picks = request.Picks.Take(1).ToList() };
        var rep = await inner.DraftAsync(request, ct);
        return m == ArmyMut.DraftRefusalNoReason ? rep with { Refused = rep.Refused.Select(x => (x.Pick, "")).ToList() } : rep;
    }
    public async ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(DoctrineSet doctrine, CancellationToken ct)
    {
        ArmyOrdersReport rep;
        if (m == ArmyMut.LegalityOnce)
        {
            var legalAtStart = inner.Read().Legal;
            int applied = 0, skipped = 0;
            foreach (var o in DoctrineVocabulary.ArmyOrders(doctrine))
            {
                var t = DoctrineVocabulary.Encode(o);
                if (!legalAtStart.Allows(t)) { skipped++; continue; }
                var one = await inner.ApplyArmyOrdersAsync(DoctrineSet.Of(t), ct);
                applied += one.Applied; skipped += one.Skipped;
            }
            rep = new ArmyOrdersReport(applied, skipped);
        }
        else if (m == ArmyMut.FlipIsFree)
        {
            int before = inner.Read().Gold;
            rep = await inner.ApplyArmyOrdersAsync(doctrine, ct);
            int taken = before - inner.Read().Gold;
            if (taken > 0) await inner.ApplySpoilsAsync(new Spoils(taken, 0), ct);   // orders spend gold only on a Flip: hand it back
        }
        else rep = await inner.ApplyArmyOrdersAsync(doctrine, ct);
        _ordersApplied += rep.Applied;
        return rep;
    }
    public async ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct)
    {
        if (m == ArmyMut.LossSlotMatters)
        {
            var now = inner.Read().Cohorts;
            losses = losses.Where(l => now.Any(c => c.Key == l.Key && c.Slot == l.Slot)).ToList();
        }
        int r = await inner.ApplyLossesAsync(losses, ct);
        if (m == ArmyMut.LossesAppliedTwice) await inner.ApplyLossesAsync(losses, ct);
        return r;
    }
    public ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct)
    {
        _goldWon += Math.Max(0, spoils.Gold);
        return inner.ApplySpoilsAsync(spoils, ct);
    }
    public ValueTask EndRoundAsync(CancellationToken ct) => inner.EndRoundAsync(ct);
    public string ToCode() => _cached ?? inner.ToCode();
}

/// <summary>Rewrites what <c>Read()</c> says, over any army service (the army twin of <see cref="FrameMutantEngine"/>).</summary>
public sealed class ReadMutantArmyService(IArmyService inner, Func<ArmyReadModel, ArmyReadModel> rewrite) : IArmyService
{
    public IArmy Create(Side side, int seed) => new ReadMutantArmy(inner.Create(side, seed), rewrite);
    public bool TryParse(string code, out IArmy? army, out string error)
    {
        var ok = inner.TryParse(code, out var a, out error);
        army = ok ? new ReadMutantArmy(a!, rewrite) : null; return ok;
    }

    sealed class ReadMutantArmy(IArmy i, Func<ArmyReadModel, ArmyReadModel> rewrite) : IArmy
    {
        public Side Side => i.Side;
        public ArmyReadModel Read() => rewrite(i.Read());
        public int Score() => i.Score();
        public ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct) => i.DraftAsync(request, ct);
        public ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(DoctrineSet doctrine, CancellationToken ct) => i.ApplyArmyOrdersAsync(doctrine, ct);
        public ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct) => i.ApplyLossesAsync(losses, ct);
        public ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct) => i.ApplySpoilsAsync(spoils, ct);
        public ValueTask EndRoundAsync(CancellationToken ct) => i.EndRoundAsync(ct);
        public string ToCode() => i.ToCode();
    }
}

// ---- BATTLE --------------------------------------------------------------------------------------------------------

public enum BattleMut { FrameTickOffByOne, LossesReportedTwice, TickLimitIgnored, Nondeterministic, SoldiersReappear }

/// <summary>The shared battle mutants, applied over any engine.</summary>
public sealed class MutantOver(IBattleEngine inner, BattleMut m) : IBattleEngine
{
    static int _runs;   // shared on purpose: the nondeterministic mutant differs between two runs
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct)
        => new MutantSession(await inner.StartAsync(setup, ct), m, Interlocked.Increment(ref _runs));
}

/// <summary>The shared battle mutants over the reference stub.</summary>
public sealed class MutantBattleEngine(BattleMut m) : IBattleEngine
{
    readonly MutantOver _over = new(new StubBattleEngine(), m);
    public IReadOnlyList<ArenaInfo> Arenas => _over.Arenas;
    public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => _over.StartAsync(setup, ct);
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

/// <summary>Rewrites every recorded frame (the start frame too), over any engine.</summary>
public sealed class FrameMutantEngine(IBattleEngine inner, Func<ReplayFrame, ReplayFrame> rewrite) : IBattleEngine
{
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => new Session(await inner.StartAsync(setup, ct), rewrite);

    sealed class Session(IBattleSession s, Func<ReplayFrame, ReplayFrame> rewrite) : IBattleSession
    {
        public int Tick => s.Tick;
        public BattleStatus Status => s.Status;
        public ValueTask FieldOrdersAsync(CancellationToken ct) => s.FieldOrdersAsync(ct);
        public ValueTask MoveAsync(CancellationToken ct) => s.MoveAsync(ct);
        public ValueTask EngageAsync(CancellationToken ct) => s.EngageAsync(ct);
        public ValueTask ApplyLossesAsync(CancellationToken ct) => s.ApplyLossesAsync(ct);
        public ReplayFrame Snapshot() => rewrite(s.Snapshot());
        public ReplayFrame Record() => rewrite(s.Record());
        public BattleResult Finish(bool tickLimitReached) => s.Finish(tickLimitReached);
    }
}

// ---- GENERAL -------------------------------------------------------------------------------------------------------

public enum GeneralMut { IgnoresMask, PreTrained, OverConfident, Forgetful, SharedState }

/// <summary>
/// The shared general mutants over ANY general. <c>SharedState</c> is "every instance remembers into the same place": give every wrapper the
/// SAME inner general (<c>var one = new MyGeneral(); () =&gt; new MutantGeneralOver(one, GeneralMut.SharedState)</c>).
/// </summary>
public sealed class MutantGeneralOver(IGeneral inner, GeneralMut m) : IGeneral
{
    int _dropped;   // Forgetful: imitation examples that were counted but not learned
    public int ExamplesSeen => inner.ExamplesSeen + _dropped + (m == GeneralMut.PreTrained ? 5 : 0);
    public async ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var o = await inner.SuggestAsync(input, ct);
        switch (m)
        {
            case GeneralMut.IgnoresMask:
                // Put the first token the mask forbids into the last slot (nothing forbidden, nothing to break).
                for (int t = 1; t < DoctrineVocabulary.Size; t++)
                    if (!input.Legal.Allows(t)) return o with { Doctrine = o.Doctrine with { S3 = t } };
                return o;
            case GeneralMut.PreTrained:
                return o.Doctrine.IsEmpty ? new GeneralOutput(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age)), 500) : o;
            case GeneralMut.OverConfident:
                return o.Doctrine.IsEmpty ? o : o with { ConfidencePermille = 2000 };
            default:
                return o;
        }
    }
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct)
    {
        if (m != GeneralMut.Forgetful) return inner.LearnAsync(e, ct);
        _dropped++; return ValueTask.CompletedTask;
    }
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => inner.LearnOutcomeAsync(o, ct);
}

/// <summary>Rewrites every suggestion, over any general (the general twin of <see cref="FrameMutantEngine"/>).</summary>
public sealed class OutputMutantGeneral(IGeneral inner, Func<GeneralInput, GeneralOutput, GeneralOutput> rewrite) : IGeneral
{
    public int ExamplesSeen => inner.ExamplesSeen;
    public async ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct) => rewrite(input, await inner.SuggestAsync(input, ct));
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct) => inner.LearnAsync(e, ct);
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => inner.LearnOutcomeAsync(o, ct);
}

/// <summary>The original self-contained general mutants (not wrapping a general). Kept for the stub proofs.</summary>
public sealed class MutantGeneral(GeneralMut m) : IGeneral
{
    static DoctrineSet _shared;   // SharedState: every instance remembers into the same slot
    DoctrineSet _last; int _n;
    public int ExamplesSeen => m == GeneralMut.PreTrained ? _n + 5 : _n;
    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var d = m == GeneralMut.SharedState ? _shared : _last;
        if (m == GeneralMut.PreTrained && d.IsEmpty) d = DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age));
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

using EvalApp;

namespace Doctrine.General;

/// <summary>
/// The player's general. It starts EMPTY (nothing pre-trained ships) and learns only from its own player: every doctrine the player sets is
/// an imitation example, every battle result a success-weighted one. Two learners sit inside, a per-slot lookup (<see cref="SlotTable"/>) and a
/// tiny HoloFormer (<see cref="GeneralNet"/>); for each slot the one that has guessed the player's next choices better so far speaks. MEASURED
/// (AlgFormer\todo\general-probe-2026-10-18.md): the net alone never beat the best non-parametric baseline, the lookup does nearly all the work, and
/// the hybrid tracks the lookup to within about a point (a point ahead at 120 choices). The net is there because it is cheap, it never hurts
/// much, and it may earn a slot from a player whose habit the lookup cannot see. The mask is applied to the OUTPUT: whatever a learner says,
/// only legal tokens leave. Single thread, no disk, no reflection; training runs as sliced EvalApp steps so a one-thread host can yield.
/// </summary>
public sealed class TinyHoloGeneral : IGeneral
{
    static readonly SlotTable SharedTable = new();   // used only by the SharedState mutant
    static int _unseeded;                              // used only by the NetUnseeded mutant

    readonly GeneralOptions _o;
    readonly SlotTable _table;
    readonly GeneralNet? _net;
    GeneralFault _fault;
    // prequential record: before the general learns from a choice it first guesses it; these count how often each learner was right.
    // Counters are halved when the window passes 64 choices, so the general follows a player who changes habits.
    int _trials, _exact; readonly int[] _tableHits = new int[SlotTable.Slots], _netHits = new int[SlotTable.Slots];

    public TinyHoloGeneral(GeneralOptions? options = null)
    {
        _o = options ?? new GeneralOptions();
        if (_o.NetDim < 2 || _o.NetDim % 2 != 0) throw new ArgumentOutOfRangeException(nameof(options), "NetDim must be even and at least 2");
        _table = new SlotTable();
        _net = _o.Mode == GeneralMode.TableOnly ? null : new GeneralNet(_o, _o.Seed);
    }

    internal TinyHoloGeneral(GeneralOptions options, GeneralFault fault) : this(options with { Seed = (fault & GeneralFault.NetUnseeded) != 0 ? Interlocked.Increment(ref _unseeded) : options.Seed })
    {
        _fault = fault;
        if ((fault & GeneralFault.FirstFeatureOnly) != 0) _table = new SlotTable(firstFeatureOnly: true);
    }

    /// <summary>Learn calls so far (imitation plus outcome). A new general has 0.</summary>
    public int ExamplesSeen { get; private set; }

    public GeneralOptions Options => _o;
    /// <summary>Trainable weights in the net (0 in <see cref="GeneralMode.TableOnly"/>).</summary>
    public long NetParameters => _net?.ParamCount ?? 0;
    public int NetPasses => _net?.Passes ?? 0;

    SlotTable Table => (_fault & GeneralFault.SharedState) != 0 ? SharedTable : _table;

    // ---- reading ---------------------------------------------------------------------------------------------------------

    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var s = Clean(input.Summary);
        var (d, _, _) = Think(s, input.Legal);
        int conf = d.IsEmpty || _trials == 0 ? 0 : Math.Clamp((int)(1000L * _exact / (_trials + 2)), 0, 1000);
        return ValueTask.FromResult(new GeneralOutput(d, conf));
    }

    /// <summary>Both learners' raw per-slot tokens, the slot-by-slot choice between them, and the doctrine that leaves (always masked).</summary>
    (DoctrineSet Doctrine, int[] Table, int[] Net) Think(int[] s, LegalMask legal)
    {
        var tt = new int[SlotTable.Slots]; var nt = new int[SlotTable.Slots];
        var ask = (_fault & GeneralFault.NetOutputUnmasked) != 0 ? LegalMask.All : legal;   // the mutant lets the learners speak unmasked
        if (_o.Mode != GeneralMode.NetOnly) Table.Predict(s, ask, tt);
        bool net = _net is not null && (_net.Trained || (_fault & GeneralFault.NetAlwaysTrusted) != 0);
        if (net) _net!.Decode(s, ask, nt, (_fault & GeneralFault.NetAlwaysTrusted) != 0);
        var pick = new int[SlotTable.Slots];
        for (int k = 0; k < pick.Length; k++)
        {
            bool useNet = _o.Mode switch
            {
                GeneralMode.NetOnly => net,
                GeneralMode.TableOnly => false,
                _ => net && ((_fault & GeneralFault.NetAlwaysTrusted) != 0 || _netHits[k] - _tableHits[k] >= _o.Margin),
            };
            pick[k] = useNet ? nt[k] : tt[k];
        }
        return (Enforce(pick, legal), tt, nt);
    }

    /// <summary>THE MASK, applied to the output: only legal tokens, End dropped, no token twice, in order, padded with End.</summary>
    DoctrineSet Enforce(ReadOnlySpan<int> tokens, LegalMask legal)
    {
        Span<int> o = stackalloc int[SlotTable.Slots]; int n = 0;
        foreach (var t in tokens)
        {
            if (t == 0 || n == o.Length) continue;
            if ((_fault & GeneralFault.NetOutputUnmasked) == 0 && !legal.Allows(t)) continue;
            bool dup = false; for (int i = 0; i < n; i++) if (o[i] == t) dup = true;
            if (!dup) o[n++] = t;
        }
        return new DoctrineSet(o[0], o[1], o[2], o[3]);
    }

    /// <summary>A usable 14-token summary whatever came in: wrong length is padded with 0 or cut, values are clamped to 0..15.</summary>
    static int[] Clean(IReadOnlyList<int>? summary)
    {
        var s = new int[GeneralSummary.Length];
        if (summary is null) return s;
        for (int i = 0; i < s.Length && i < summary.Count; i++) s[i] = Math.Clamp(summary[i], 0, GeneralSummary.Vocab - 1);
        return s;
    }

    // ---- learning --------------------------------------------------------------------------------------------------------

    public ValueTask LearnAsync(GeneralExample example, CancellationToken ct)
        => RunAsync(new LearnJob(this, Imitation: true, Clean(example.Input.Summary), example.Input.Legal, example.Chosen, Won: false), ct);

    public ValueTask LearnOutcomeAsync(GeneralOutcome outcome, CancellationToken ct)
        => RunAsync(new LearnJob(this, Imitation: false, Clean(outcome.Input.Summary), outcome.Input.Legal, outcome.Chosen, outcome.PlayerWon), ct);

    async ValueTask RunAsync(LearnJob job, CancellationToken ct)
    {
        var run = await Learning.Pipeline.RunAsync(job, ct);
        if (run is PipelineResult<LearnJob>.Failure f) throw f.Exception;
    }

    /// <summary>Step 1 of a Learn call: score our own guess against what the player did, then teach both learners.</summary>
    internal void Remember(LearnJob j)
    {
        ExamplesSeen++;
        if (j.Imitation)
        {
            Score(j);
            Table.Add(j.Summary, j.Chosen, 2);
            _net?.Remember(j.Summary, j.Chosen);
        }
        else if ((_fault & GeneralFault.OutcomeIgnored) == 0)
        {
            Table.Add(j.Summary, j.Chosen, j.Won ? 2 : -1);
            if (j.Won) _net?.Remember(j.Summary, j.Chosen); else _net?.Forget(j.Summary, j.Chosen);
        }
    }

    void Score(LearnJob j)
    {
        var (d, tt, nt) = Think(j.Summary, j.Legal);
        bool net = _net is not null && _net.Trained;
        for (int k = 0; k < SlotTable.Slots; k++)
        {
            int want = Table.Token(j.Chosen[k]);
            if (_o.Mode != GeneralMode.NetOnly && tt[k] == want) _tableHits[k]++;
            if (net && nt[k] == want) _netHits[k]++;
        }
        _trials++;
        if (d == Enforce(j.Chosen.Tokens, j.Legal)) _exact++;
        if (_trials >= 64)
        {
            _trials /= 2; _exact /= 2;
            for (int k = 0; k < SlotTable.Slots; k++) { _tableHits[k] /= 2; _netHits[k] /= 2; }
        }
    }

    /// <summary>Steps 2..: one slice of replay training for the net.</summary>
    internal void Replay(LearnJob j)
    {
        if (_net is null) return;
        int budget = _net.Passes < _o.PassCap ? _o.PassesPerLearn : _o.PassesAfterCap;
        _net.Train(budget / Learning.Slices);
    }
}

/// <summary>Task data of the learning pipeline: the general (a live handle, like the game's RoundState) and the one example.</summary>
internal sealed record LearnJob(TinyHoloGeneral General, bool Imitation, int[] Summary, LegalMask Legal, DoctrineSet Chosen, bool Won);

/// <summary>Nothing is shared through the context: the general rides in the task data, so one compiled pipeline serves every general.</summary>
internal sealed class GeneralContext : GlobalContext { }
internal sealed class GeneralDomain : DomainContext { }

abstract class LearnStep : ContextSideEffectStep<GeneralContext, GeneralDomain, LearnJob> { }

sealed class RememberStep : LearnStep
{
    protected override ValueTask<LearnJob> ExecuteAsync(LearnJob j, GeneralContext g, GeneralDomain d, CancellationToken ct)
    { j.General.Remember(j); return ValueTask.FromResult(j); }
}

sealed class ReplaySliceStep : LearnStep
{
    protected override ValueTask<LearnJob> ExecuteAsync(LearnJob j, GeneralContext g, GeneralDomain d, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); j.General.Replay(j); return ValueTask.FromResult(j); }
}

/// <summary>A Learn call as an EvalApp pipeline: remember, then four slices of replay. EvalApp yields to a one-thread host between steps.</summary>
static class Learning
{
    public const int Slices = 4;
    public static readonly ICompiledPipeline<LearnJob> Pipeline = Build();

    static ICompiledPipeline<LearnJob> Build()
    {
        Eval.App("Doctrine.General").WithContext(new GeneralContext())
            .DefineDomain("General", new GeneralDomain())
                .DefineTask<LearnJob>("Learn")
                    .AddStep<RememberStep>("General.Remember")
                    .AddStep<ReplaySliceStep>("General.Replay.1")
                    .AddStep<ReplaySliceStep>("General.Replay.2")
                    .AddStep<ReplaySliceStep>("General.Replay.3")
                    .AddStep<ReplaySliceStep>("General.Replay.4")
                .Run(out var pipeline)
            .Build();
        return pipeline;
    }
}

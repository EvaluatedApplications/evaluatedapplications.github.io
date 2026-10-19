using EvalApp;

namespace Doctrine.General;

/// <summary>
/// The player's general. It starts EMPTY (nothing pre-trained ships) and learns only from its own player: every doctrine the player sets is an
/// imitation example, every battle result a success-weighted one. Under the roguelike offers the player does not pick from everything: each round it
/// is offered a handful of order cards and takes up to four, in order. The offer IS the general's input: <see cref="GeneralInput.Legal"/> reaches it
/// already cut down to the offer and the legal list, and the general only ever ranks the cards on it. Three learners sit inside: a per-slot lookup of
/// exact tokens in a situation (<see cref="SlotTable"/>, the habit of a player on an open market), a taste table over the cards' FEATURES
/// (<see cref="CardTable"/>: family, order type, the trait or line it touches, whether it repeats last round), and a tiny HoloFormer that scores each
/// offered card (<see cref="GeneralNet"/>). For each slot the learner that has guessed the player's next picks best so far speaks (the net only after it
/// is clearly ahead). MEASURED (Doctrine.Tests\General\OfferProbe.cs, EXP-OFFER-1): the net never beat the taste table, so the shipped default is the two
/// tables (<see cref="GeneralMode.TableOnly"/>, no net weights at all); Hybrid and NetOnly stay available and tested. The mask is applied to the OUTPUT too: whatever a learner says, only offered, legal tokens leave. Single thread, no disk, no
/// reflection; training runs as sliced EvalApp steps so a one-thread host can yield.
/// </summary>
public sealed class TinyHoloGeneral : IGeneral
{
    static readonly SlotTable SharedTable = new();   // used only by the SharedState mutant
    static readonly CardTable SharedCards = new(false);
    static readonly int[] SharedSlotHits = new int[SlotTable.Slots];   // used only by the SlotsNotReset mutant
    static readonly int[] SharedSlotTrials = new int[SlotTable.Slots];
    readonly int[] _slotHits = new int[SlotTable.Slots], _slotTrials = new int[SlotTable.Slots];   // per slot, within the window: shown == player's / either was not End
    static int _unseeded;                            // used only by the NetUnseeded mutant

    readonly GeneralOptions _o;
    readonly SlotTable _habit;
    readonly CardTable _taste;
    readonly GeneralNet? _net;
    GeneralFault _fault;
    int[] _prev = new int[SlotTable.Slots], _prevOfLast = new int[SlotTable.Slots];   // the player's picks last round; the ones the latest example was weighed against
    // prequential record: before the general learns from a choice it first guesses it; these count how often each learner was right.
    // Counters are halved when the window passes 64 choices, so the general follows a player who changes habits.
    int _trials, _exact;
    readonly int[] _habitHits = new int[SlotTable.Slots], _tasteHits = new int[SlotTable.Slots], _netHits = new int[SlotTable.Slots];

    public TinyHoloGeneral(GeneralOptions? options = null)
    {
        _o = options ?? new GeneralOptions();
        if (_o.NetDim < 2 || _o.NetDim % 2 != 0) throw new ArgumentOutOfRangeException(nameof(options), "NetDim must be even and at least 2");
        _habit = new SlotTable();
        _taste = new CardTable(_o.History);
        _net = _o.Mode == GeneralMode.TableOnly ? null : new GeneralNet(_o, _o.Seed);
    }

    internal TinyHoloGeneral(GeneralOptions options, GeneralFault fault) : this(options with { Seed = (fault & GeneralFault.NetUnseeded) != 0 ? Interlocked.Increment(ref _unseeded) : options.Seed })
    {
        _fault = fault;
        if ((fault & GeneralFault.FirstFeatureOnly) != 0) _habit = new SlotTable(firstFeatureOnly: true);
    }

    /// <summary>Learn calls so far (imitation plus outcome). A new general has 0.</summary>
    public int ExamplesSeen { get; private set; }

    public GeneralOptions Options => _o;
    /// <summary>Trainable weights in the net (0 in <see cref="GeneralMode.TableOnly"/>).</summary>
    public long NetParameters => _net?.ParamCount ?? 0;
    public int NetPasses => _net?.Passes ?? 0;

    SlotTable Habit => (_fault & GeneralFault.SharedState) != 0 ? SharedTable : _habit;
    CardTable Taste => (_fault & GeneralFault.SharedState) != 0 ? SharedCards : _taste;

    // ---- reading ---------------------------------------------------------------------------------------------------------

    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var s = Clean(input.Summary);
        var (d, _, _, _) = Think(s, input.Legal);
        int conf = d.IsEmpty || _trials == 0 ? 0 : Math.Clamp((int)(1000L * _exact / (_trials + 2)), 0, 1000);
        return ValueTask.FromResult(new GeneralOutput(d, conf, SlotFigures()));
    }

    /// <summary>
    /// THE MATCH RECORD, per slot (CONTRACT section c). Before each imitation example is learned, the doctrine this general would have shown (the
    /// cards it ranked, first to fourth, from that round's offer) is compared slot by slot with the doctrine the player then set. A slot COUNTS in an
    /// example when either side said something there (shown card or player's card is not End); two Ends are not a trial, so a slot the player leaves
    /// empty and the general leaves empty reads 0, not a flattering 1000. Slot k reads  1000 * hits_k / (counted_k + 2) (integer division), hits_k =
    /// counted examples where the card shown at slot k was the card the player took at slot k. counted_k = 0 reads 0. It is a Laplace-style lower
    /// bound on the slot's recent hit rate, never above it, so it climbs fast on a habit (5 straight matches read 714) and stays low on a random
    /// player. Window: counts are halved whenever the whole-suggestion window passes 64 examples. A record, not a probability.
    /// </summary>
    SlotPermille SlotFigures()
    {
        bool shared = (_fault & GeneralFault.SlotsNotReset) != 0;
        int[] h = shared ? SharedSlotHits : _slotHits, n = shared ? SharedSlotTrials : _slotTrials;
        int Fig(int k)
        {
            int a = (_fault & GeneralFault.SlotsSwapped) != 0 ? (k ^ 1) : k;
            long v = (_fault & GeneralFault.SlotsOptimistic) != 0 ? 1000L * (h[a] + 1) / (n[a] + 1) : 1000L * h[a] / (n[a] + 2);
            return (int)Math.Clamp(v, 0, 1000);
        }
        return new SlotPermille(Fig(0), Fig(1), Fig(2), Fig(3));
    }

    /// <summary>The cards of an offer: the tokens the mask allows, End dropped, ascending (the order an <see cref="OrderOffer"/> lists them).</summary>
    static int[] Cards(LegalMask legal)
    {
        var l = new List<int>(DoctrineVocabulary.Size);
        for (int t = 1; t < DoctrineVocabulary.Size; t++) if (legal.Allows(t)) l.Add(t);
        return l.ToArray();
    }

    /// <summary>Every learner's raw per-slot tokens, the slot-by-slot choice between them, and the doctrine that leaves (always masked to the offer).</summary>
    (DoctrineSet Doctrine, int[] Habit, int[] Taste, int[] Net) Think(int[] s, LegalMask legal)
    {
        var th = new int[SlotTable.Slots]; var tt = new int[SlotTable.Slots]; var tn = new int[SlotTable.Slots];
        // the learners read the offer as given; the mutants let them read everything instead
        var ask = (_fault & (GeneralFault.NetOutputUnmasked | GeneralFault.OfferIgnored)) != 0 ? LegalMask.All : legal;
        var cards = Cards(ask);
        if (_o.Mode != GeneralMode.NetOnly)
        {
            Habit.Predict(s, ask, th);
            Taste.Predict(cards, _prev, tt);
        }
        bool speakUntrained = (_fault & GeneralFault.NetAlwaysTrusted) != 0;
        bool net = _net is not null && (_net.Trained || speakUntrained);
        if (net) _net!.Decode(s, cards, _prev, _o.History, speakUntrained ? SlotTable.Slots : Taste.Fill(), tn, speakUntrained);
        var pick = new int[SlotTable.Slots];
        int fill = _o.Mode == GeneralMode.NetOnly ? 0 : Taste.Fill();       // how many slots this player usually fills
        for (int k = 0; k < pick.Length; k++)
        {
            int best = Math.Max(_habitHits[k], _tasteHits[k]);
            bool useNet = _o.Mode switch
            {
                GeneralMode.NetOnly => net,
                GeneralMode.TableOnly => false,
                _ => net && (speakUntrained || _netHits[k] - best >= _o.Margin),
            };
            int[] table = _habitHits[k] > _tasteHits[k] ? th : tt, spare = ReferenceEquals(table, th) ? tt : th;
            int[][] order = _o.Mode == GeneralMode.NetOnly ? [tn] : useNet ? [tn, table, spare] : [table, spare];
            // the learner that speaks for the slot names the card; when it has nothing (End) in a slot this player usually fills, or names a card already
            // placed, the next learner may; otherwise End ends the doctrine
            int t = 0;
            for (int i = 0; i < order.Length; i++)
            {
                int c = order[i][k];
                if (c != 0 && !Used(pick, k, c)) { t = c; break; }
                if (c == 0 && k >= fill) break;
            }
            pick[k] = t;
            if (t == 0) break;
        }        return (Enforce(pick, legal), th, tt, tn);
    }

    static bool Used(int[] pick, int k, int t) { for (int i = 0; i < k; i++) if (pick[i] == t) return true; return false; }

    /// <summary>THE MASK, applied to the output: only offered, legal tokens, End dropped, no token twice, in order, padded with End.</summary>
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

    /// <summary>Step 1 of a Learn call: score our own guess against what the player did, then teach every learner.</summary>
    internal void Remember(LearnJob j)
    {
        ExamplesSeen++;
        var cards = Cards(j.Legal);
        var picks = Enforce(j.Chosen.Tokens, j.Legal).Tokens.Where(t => t != 0).ToArray();   // the picks that were on offer, in order, none twice
        if (j.Imitation)
        {
            Score(j);
            Habit.Add(j.Summary, j.Chosen, 2);
            if ((_fault & GeneralFault.TasteDeaf) == 0) Taste.Add(cards, picks, _prev, 2);
            _net?.Remember(j.Summary, cards, picks, _prev, _o.History);
            _prevOfLast = _prev; _prev = Pad(picks);
        }
        else if ((_fault & GeneralFault.OutcomeIgnored) == 0)
        {
            Habit.Add(j.Summary, j.Chosen, j.Won ? 2 : -1);
            if ((_fault & GeneralFault.TasteDeaf) == 0) Taste.Add(cards, picks, _prevOfLast, j.Won ? 2 : -1);
            if (j.Won) _net?.Remember(j.Summary, cards, picks, _prevOfLast, _o.History); else _net?.Forget(j.Summary, picks);
        }
    }

    static int[] Pad(int[] picks) { var p = new int[SlotTable.Slots]; for (int i = 0; i < p.Length && i < picks.Length; i++) p[i] = picks[i]; return p; }

    void Score(LearnJob j)
    {
        var (d, th, tt, tn) = Think(j.Summary, j.Legal);
        bool net = _net is not null && _net.Trained;
        for (int k = 0; k < SlotTable.Slots; k++)
        {
            int want = Habit.Token(j.Chosen[k]);
            if (_o.Mode != GeneralMode.NetOnly && th[k] == want) _habitHits[k]++;
            if (_o.Mode != GeneralMode.NetOnly && tt[k] == want) _tasteHits[k]++;
            if (net && tn[k] == want) _netHits[k]++;
        }
        _trials++;
        var shouldHave = Enforce(j.Chosen.Tokens, j.Legal);
        if (d == shouldHave) _exact++;
        bool shared = (_fault & GeneralFault.SlotsNotReset) != 0;
        int[] sh = shared ? SharedSlotHits : _slotHits, sn = shared ? SharedSlotTrials : _slotTrials;
        for (int k = 0; k < SlotTable.Slots; k++)
            if (d[k] != 0 || shouldHave[k] != 0) { sn[k]++; if (d[k] == shouldHave[k]) sh[k]++; }
        if (_trials >= 64)
        {
            _trials /= 2; _exact /= 2;
            for (int k = 0; k < SlotTable.Slots; k++) { _habitHits[k] /= 2; _tasteHits[k] /= 2; _netHits[k] /= 2; sh[k] /= 2; sn[k] /= 2; }
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

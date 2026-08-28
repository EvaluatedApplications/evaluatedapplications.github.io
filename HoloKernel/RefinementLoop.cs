namespace HoloKernel;

/// <summary>
/// Outcome of a multi-position update. <paramref name="TotalLoss"/> is the library's raw summed
/// loss; <paramref name="MeanLoss"/> is per-scored-position and is the one to plot.
/// </summary>
public readonly record struct SequenceResult(double TotalLoss, double MeanLoss, int Scored);

/// <summary>
/// The live-training loop, in one place.
///
/// The Creature and The Forecaster each hand-write the identical
/// <c>NewGrads() -> IterAccumulate(...) -> Step(...)</c> triple in their own page files — the
/// Forecaster's own comment says it is "the same loop The Creature uses". Prism has no training loop
/// at all, which is the gap that stops it being refinable in the browser. One implementation closes
/// both.
///
/// Note this deliberately does NOT use <c>HoloFormer.TrainStep</c>: that convenience method ignores
/// the <c>Iters</c>/K-pass depth, so it silently trains a different model than the one being served.
///
/// This is the ONLY sanctioned way a browser session changes a model: weights move, shape does not.
/// Structural growth (<c>GrowLayers</c>/<c>GrowShifts</c>) is a server-side/PrismStudio operation and
/// is out of scope for the platform — see the remarks on <see cref="HoloSession.Model"/>.
/// </summary>
public sealed class RefinementLoop
{
    public HoloSession Session { get; }
    public AlphaRamp Ramp { get; }

    /// <summary>Adam step size. Tools differ (Creature ~0.0025-0.004, Forecaster 0.005).</summary>
    public double LearningRate { get; set; }

    /// <summary>Gradient clipping; 0 disables (the library default).</summary>
    public double Clip { get; set; }

    public long Steps { get; private set; }
    public double LastLoss { get; private set; }

    public RefinementLoop(HoloSession session, AlphaRamp ramp, double learningRate)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
        LearningRate = learningRate;
    }

    /// <summary>
    /// Train toward a single decisive answer at the end of <paramref name="context"/>.
    ///
    /// The extra pass is trained at the CURRENT ramped alpha, matching what serving uses — training
    /// at a blend you don't serve at is a real (and quiet) way to make a model worse.
    /// </summary>
    public double Observe(int[] context, int target)
    {
        ArgumentNullException.ThrowIfNull(context);

        var model = Session.Model;
        var alpha = Ramp.Alpha;

        var grads = model.NewGrads();
        var loss = model.IterAccumulate(context, target, grads, Session.KPass, alpha);
        model.Step(grads, LearningRate, 1.0, 0.9, 0.999, 1e-8, Clip);

        Advance(loss);
        return loss;
    }

    /// <summary>
    /// Train on MANY positions across the sequence in a single update, instead of only the last one.
    ///
    /// Worth knowing: a single-position loss throws away almost all the signal in a sequence — every
    /// token except the scored one contributes nothing to the update. This is the better default for
    /// any tool learning from a stream (a price tape, a trajectory, a line of text). Single-position
    /// remains correct for The Creature's advantage-weighted move choice, which genuinely IS one
    /// decision per step.
    /// </summary>
    /// <param name="sequence">The token sequence to learn from.</param>
    /// <param name="maxPositions">
    /// How many positions to score. Defaults to all of them.
    ///
    /// VERIFIED SEMANTICS (measured against the real 1.5.0 DLL, because the parameter name
    /// <c>scoreP</c> reads like an offset and is NOT one): this is a COUNT, and it saturates at
    /// <c>sequence.Length - 1</c> — the final token has no successor to predict. Passing 0 scores
    /// nothing and returns a zero loss, which would be a silent no-op update.
    /// </param>
    public SequenceResult ObserveSequence(int[] sequence, int? maxPositions = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Length < 2)
            throw new ArgumentException("Need at least 2 tokens: the last one has no successor to predict.", nameof(sequence));

        var requested = maxPositions ?? sequence.Length - 1;
        if (requested < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPositions), requested, "Scoring zero positions is a silent no-op.");

        var model = Session.Model;
        var alpha = Ramp.Alpha;

        var grads = model.NewGrads();
        var (totalLoss, scored) = model.StackIterAccumulateAllPos(sequence, grads, requested, Session.KPass, alpha);
        model.Step(grads, LearningRate, 1.0, 0.9, 0.999, 1e-8, Clip);

        // The library returns a SUMMED loss (verified: 31 scored positions -> ~87.5, i.e. ~2.82
        // each). Reporting the sum as-is would make a training curve leap the moment a tool switched
        // from single-position to multi-position, so normalise to a per-position figure that is
        // directly comparable with Observe().
        var mean = scored > 0 ? totalLoss / scored : 0.0;
        Advance(mean);
        return new SequenceResult(totalLoss, mean, scored);
    }

    private void Advance(double loss)
    {
        LastLoss = loss;
        Steps++;
        Ramp.Advance();
        Session.ApplyServe(Ramp);
    }
}

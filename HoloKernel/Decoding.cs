namespace HoloKernel;

/// <summary>
/// The confidence gate's thresholds. Defaults are the values Prism runs today, ported verbatim from
/// PrismStudio's <c>HoloEngine</c>; they are supplied as data rather than baked in so a tool with a
/// different vocabulary or task can set its own without forking the algorithm.
/// </summary>
public sealed record DecodePolicy
{
    /// <summary>Top-1 probability at or above which the pick is greedy (no sampling).</summary>
    public double ConfidentThreshold { get; init; } = 0.60;

    /// <summary>Softmax temperature used when sampling below the confidence threshold.</summary>
    public double Temperature { get; init; } = 0.80;

    /// <summary>Candidates must sit at least this many sigma above the mean to be sampleable.</summary>
    public double FloorK { get; init; } = 3.0;

    /// <summary>Consecutive identical emissions treated as a collapsed run.</summary>
    public int DegenRepeat { get; init; } = 4;

    public static DecodePolicy Default { get; } = new();
}

/// <summary>Why a token was emitted — enough for an Inspector to label it without re-drawing the sample.</summary>
public readonly record struct GateDecision(int Top, double TopProb, bool Greedy, int OverFloor);

public static class Gate
{
    /// <summary>
    /// Classify a decode WITHOUT drawing from the RNG, so the Inspector can explain a choice that
    /// <see cref="Pick"/> already made.
    /// </summary>
    public static GateDecision Evaluate(double[] logits, int vocab, DecodePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(logits);
        ArgumentNullException.ThrowIfNull(policy);
        if (vocab <= 0 || vocab > logits.Length)
            throw new ArgumentOutOfRangeException(nameof(vocab), vocab, "vocab must be within the logits array.");

        var (top, max) = ArgMax(logits, vocab);

        double sum = 0;
        for (var i = 0; i < vocab; i++) sum += Math.Exp(logits[i] - max);
        var topProb = 1.0 / sum;

        if (topProb >= policy.ConfidentThreshold)
            return new GateDecision(top, topProb, Greedy: true, OverFloor: 1);

        var floor = ResonanceFloor(logits, vocab, max, policy.FloorK);
        var over = 0;
        for (var i = 0; i < vocab; i++) if (logits[i] >= floor) over++;

        return new GateDecision(top, topProb, Greedy: false, OverFloor: over);
    }

    /// <summary>
    /// Draw a token: greedy when confident, otherwise temperature-sampled over everything standing
    /// above the resonance floor.
    ///
    /// The RNG is a parameter (rather than <c>Random.Shared</c>) so a run can be made reproducible —
    /// which matters a great deal for an Inspector whose whole job is explaining what happened.
    /// </summary>
    public static int Pick(double[] logits, int vocab, DecodePolicy policy, Random rng)
    {
        ArgumentNullException.ThrowIfNull(logits);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rng);

        var (top, max) = ArgMax(logits, vocab);

        double sum = 0;
        for (var i = 0; i < vocab; i++) sum += Math.Exp(logits[i] - max);
        if (1.0 / sum >= policy.ConfidentThreshold) return top;

        var floor = ResonanceFloor(logits, vocab, max, policy.FloorK);

        var weights = new double[vocab];
        double total = 0;
        for (var i = 0; i < vocab; i++)
        {
            weights[i] = logits[i] >= floor ? Math.Exp((logits[i] - max) / policy.Temperature) : 0.0;
            total += weights[i];
        }

        var r = rng.NextDouble() * total;
        double acc = 0;
        for (var i = 0; i < vocab; i++)
        {
            acc += weights[i];
            if (r <= acc) return i;
        }
        return top;
    }

    /// <summary>Softmax the scores and return the top <paramref name="count"/> as (index, percent).</summary>
    public static IReadOnlyList<(int Index, double Percent)> TopK(double[] scores, int vocab, int count)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var (_, max) = ArgMax(scores, vocab);

        var p = new double[vocab];
        double s = 0;
        for (var i = 0; i < vocab; i++) { p[i] = Math.Exp(scores[i] - max); s += p[i]; }

        return Enumerable.Range(0, vocab)
            .OrderByDescending(i => p[i])
            .Take(count)
            .Select(i => (i, s > 0 ? 100.0 * p[i] / s : 0.0))
            .ToList();
    }

    private static (int Index, double Max) ArgMax(double[] v, int vocab)
    {
        var top = 0;
        var max = v[0];
        for (var i = 1; i < vocab; i++) if (v[i] > max) { max = v[i]; top = i; }
        return (top, max);
    }

    private static double ResonanceFloor(double[] logits, int vocab, double max, double floorK)
    {
        double mean = 0;
        for (var i = 0; i < vocab; i++) mean += logits[i];
        mean /= vocab;

        double var2 = 0;
        for (var i = 0; i < vocab; i++) { var d = logits[i] - mean; var2 += d * d; }
        var2 /= vocab;

        return Math.Min(mean + floorK * Math.Sqrt(var2), max);
    }
}

/// <summary>
/// Stops a repetition-collapsed run instead of letting it pad out silently.
///
/// Not a theoretical safeguard: dry-running the decode algorithm against a real checkpoint snapshot
/// produced a 100%-confidence greedy space-repeat on every short prompt tried. A collapsed run
/// should be visibly labelled as collapsed.
/// </summary>
public sealed class DegenGuard
{
    private int _last = -1;
    private int _run;

    public int Threshold { get; }

    public DegenGuard(int threshold) => Threshold = threshold;

    public DegenGuard(DecodePolicy policy) : this((policy ?? throw new ArgumentNullException(nameof(policy))).DegenRepeat) { }

    /// <summary>Feed each emitted token. Returns true once the run has collapsed.</summary>
    public bool Observe(int token)
    {
        if (token == _last) _run++;
        else { _last = token; _run = 1; }
        return Threshold > 0 && _run >= Threshold;
    }

    public void Reset() { _last = -1; _run = 0; }
}

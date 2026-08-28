using PrismFormer;

namespace HoloKernel;

/// <summary>One weight-tied pass: the raw face, what it decodes to, and its current best guess.</summary>
public sealed record PassSnapshot(int Pass, double[] Face, double[] Logits, int Top, double TopProb);

/// <summary>
/// Everything observable about a single emission: how the answer firmed up across the K passes,
/// what the model was resonating with, and why the gate chose what it chose.
/// </summary>
public sealed record PositionTrace(
    IReadOnlyList<PassSnapshot> Passes,
    IReadOnlyList<double[]> Attention,
    GateDecision Gate)
{
    public PassSnapshot Final => Passes[^1];

    /// <summary>
    /// How top-1 confidence evolved across the passes. This is the sparkline a mobile Inspector can
    /// show in a few pixels instead of rendering K full rows of a table.
    /// </summary>
    public IReadOnlyList<double> ConfidenceCurve => Passes.Select(p => p.TopProb).ToList();
}

/// <summary>
/// The per-pass Inspector, extracted so it is not exclusive to one tool.
///
/// Prism has this and no training loop; The Creature and The Forecaster have a training loop and no
/// Inspector. That asymmetry is an accident of which tool was built first, not a design decision —
/// watching a model firm up its answer across passes is just as interesting while it LEARNS.
/// </summary>
public static class Inspector
{
    /// <summary>
    /// Capture a full trace for the next emission after <paramref name="context"/>.
    ///
    /// Deliberately uses the full-recompute inspection path rather than <c>LogitsFor</c>: it is the
    /// only way to get per-pass data, and it guarantees the trace describes exactly the distribution
    /// the caller then decodes from. The KV-cache path is faster but yields no per-pass detail and
    /// is not verified to honour <c>Iters</c>.
    /// </summary>
    public static PositionTrace Capture(HoloSession session, int[] context, DecodePolicy policy, int? vocabLimit = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);

        var model = session.Model;
        var k = session.KPass;
        var alpha = session.ServeAlpha;

        var faces = model.InspectStackIter(context, k, alpha);
        var attention = model.InspectAttention(context, k, alpha);

        var passes = new List<PassSnapshot>(faces.Length);
        for (var i = 0; i < faces.Length; i++)
        {
            var logits = model.DecodeFace(faces[i]);
            var v = Clamp(vocabLimit, logits.Length);
            var (top, topProb) = TopOf(logits, v);
            passes.Add(new PassSnapshot(i, faces[i], logits, top, topProb));
        }

        var final = passes[^1];
        var gate = Gate.Evaluate(final.Logits, Clamp(vocabLimit, final.Logits.Length), policy);

        return new PositionTrace(passes, attention, gate);
    }

    /// <summary>
    /// Turn a raw attention row into "what it looked at", as (context index, token, percent).
    ///
    /// Returning source POSITIONS rather than a matrix is deliberate: it lets a UI shade the prompt
    /// text itself instead of drawing a heatmap, which is the only form of this that survives a
    /// phone-width screen.
    /// </summary>
    public static IReadOnlyList<(int Position, int Token, double Percent)> Focus(
        int[] context, double[] resonance, int count)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resonance);

        return Gate.TopK(resonance, resonance.Length, count)
            .Where(x => x.Index < context.Length)
            .Select(x => (x.Index, context[x.Index], x.Percent))
            .ToList();
    }

    private static int Clamp(int? limit, int available) =>
        limit is null ? available : Math.Min(limit.Value, available);

    private static (int Top, double TopProb) TopOf(double[] logits, int vocab)
    {
        var top = 0;
        var max = logits[0];
        for (var i = 1; i < vocab; i++) if (logits[i] > max) { max = logits[i]; top = i; }

        double sum = 0;
        for (var i = 0; i < vocab; i++) sum += Math.Exp(logits[i] - max);
        return (top, 1.0 / sum);
    }
}

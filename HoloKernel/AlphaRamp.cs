namespace HoloKernel;

/// <summary>
/// The identity-init ease-in for weight-tied extra passes: alpha ramps 0 -> 1 so the K-pass fades
/// in rather than shocking a cold model.
///
/// Both live-training tools implement this separately today (The Creature over 20 episodes, The
/// Forecaster over 40 clicks) and Prism reconstructs the SAME curve from checkpoint metadata to
/// recover what alpha a frozen checkpoint was trained at. One curve, three call sites, so it lives
/// here once.
/// </summary>
public sealed class AlphaRamp
{
    /// <summary>Steps taken to go from alpha=0 to alpha=1. Zero or less means "always fully composed".</summary>
    public int WarmSteps { get; }

    public long Steps { get; private set; }

    public AlphaRamp(int warmSteps) => WarmSteps = warmSteps;

    /// <summary>A ramp that is already complete — the right choice when serving a trained checkpoint.</summary>
    public static AlphaRamp Complete { get; } = new(0);

    public double Alpha => WarmSteps <= 0 ? 1.0 : Math.Clamp((double)Steps / WarmSteps, 0.0, 1.0);

    public void Advance() => Steps++;

    public void Reset() => Steps = 0;

    /// <summary>
    /// Reconstruct the alpha a persisted checkpoint was actually trained at.
    ///
    /// This matters because <c>HoloFormer.Iters</c> / <c>IterAlphaServe</c> are NOT persisted by
    /// <c>Serialize()</c> (verified by round-tripping a real checkpoint — they always read back 1),
    /// so a loaded model cannot tell you its own K or alpha. They have to come from sidecar
    /// metadata, and alpha has to be recomputed from the training-round counter.
    ///
    /// Valid for SINGLE-LAYER checkpoints (addRound is per-layer); callers with Layers > 1 or
    /// missing metadata should fall back to 1.0 rather than trusting this.
    /// </summary>
    public static double Reconstruct(long trainedRounds, long addRound, int iterWarm)
    {
        if (iterWarm <= 0) return 1.0;
        return Math.Clamp((double)(trainedRounds - addRound) / iterWarm, 0.0, 1.0);
    }
}

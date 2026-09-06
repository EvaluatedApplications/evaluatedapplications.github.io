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

    // Reconstruct(trainedRounds, addRound, iterWarm) used to live here as a hand-copy of the pure
    // ramp formula (`clamp((trainedRounds-addRound)/iterWarm, 0, 1)`) — REMOVED 2026-09-05, migrated
    // to Prism: `Prism.Lineage.LineageJournal.AlphaFor(round, addRound, iterWarm)` is the byte-
    // identical static formula (confirmed against source, not assumed), so callers reconstructing a
    // checkpoint's mid-ramp alpha from sidecar metadata (Prism.razor, Analyst.razor's novelty scan)
    // now call that directly instead of a second copy of the same math living here. This class's
    // remaining members (WarmSteps/Steps/Alpha/Advance/Reset/Complete) are a DIFFERENT concept — a
    // live, stateful, per-step ramp driver `RefinementLoop` advances during training — and have no
    // Prism equivalent (Prism.Lineage is a stamped-history journal, not a live ramp object), so they
    // stay here unchanged.
}

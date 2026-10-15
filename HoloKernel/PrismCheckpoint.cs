namespace HoloKernel;

/// <summary>
/// The two small pieces of "how do we correctly serve Prism's checkpoint" math that both Prism.razor
/// and Nano Stories' Stories.razor need identically, since they serve the SAME checkpoint under the
/// SAME <see cref="SessionHost"/> key. Factored out (2026-09-21) so the two pages can't silently
/// disagree on K or alpha — see <c>HoloSession</c>'s own doc for why getting either wrong serves a
/// crippled model without an error.
///
/// Deliberately NOT a full loader: each page still does its own HTTP fetch + boot-log narration for
/// the checkpoint bytes and metadata sidecars (that part is page-local UI, low risk to keep
/// per-page) — only the two formulas below, where a mismatch would be a real, silent correctness bug
/// rather than a cosmetic difference, are centralised.
/// </summary>
public static class PrismCheckpoint
{
    /// <summary>The <see cref="SessionHost"/> key every tool serving this checkpoint must share.</summary>
    public const string SessionKey = "prism";

    /// <summary>The trained K-pass depth to serve at — the <c>oracle-stackk.txt</c> sidecar's value,
    /// or 1 if that sidecar wasn't shipped with this checkpoint.</summary>
    public static int ResolveK(int? trainedStackK) => trainedStackK ?? 1;

    /// <summary>
    /// Reconstructs the served alpha blend for a checkpoint at <paramref name="layers"/> layers.
    /// Only ever fractional for a single-layer checkpoint still mid-ramp; every other case (multi-
    /// layer, or missing metadata) is fully composed. Identical formula the checkpoint was actually
    /// TRAINED against — <c>Prism.Lineage.LineageJournal.AlphaFor</c> — never re-derived by hand here.
    /// </summary>
    public static double ReconstructAlpha(int layers, long? trainedRounds, int? trainedIterWarm) =>
        (layers == 1 && trainedRounds is not null && trainedIterWarm is > 0)
            ? global::Prism.Lineage.LineageJournal.AlphaFor((int)trainedRounds.Value, addRound: 0, trainedIterWarm.Value)
            : 1.0;
}

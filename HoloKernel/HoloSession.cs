using PrismFormer;

namespace HoloKernel;

/// <summary>Read-only facts about a live model, for "model stats" UI.</summary>
public sealed record ModelStats(
    int Dim,
    int Layers,
    int Shifts,
    int Context,
    int Vocab,
    long ParamCount,
    int KPass,
    double ServeAlpha,
    long EquivCompute,
    double ComputePerStoredParam,
    int BindRank,
    int CleanCapacity,
    bool CleanCapacityCoversContext,
    bool Golden);

/// <summary>
/// A live HoloFormer plus the two facts the model itself cannot tell you.
///
/// THE REASON THIS TYPE EXISTS — a verified gotcha: <c>HoloFormer.Iters</c> and
/// <c>IterAlphaServe</c> (the K-pass depth and its blend) are NOT persisted by <c>Serialize()</c>.
/// A deserialized checkpoint always reports 1/1 regardless of what it was trained at. Defaulting to
/// those read-back values silently serves a crippled model, which is a bug you cannot see — it just
/// produces worse output.
///
/// So <see cref="FromCheckpoint"/> REQUIRES K and alpha as explicit arguments. There is no overload
/// that lets you forget them. That is the point: make the failure structurally impossible rather
/// than relying on everyone remembering the footnote.
/// </summary>
public sealed class HoloSession
{
    public HoloFormer Model { get; }

    /// <summary>Weight-tied pass count. A structural fact about the model, never a user control.</summary>
    public int KPass { get; }

    /// <summary>The blend the K-pass is served at.</summary>
    public double ServeAlpha { get; private set; }

    /// <summary>Set when built from a <see cref="ModelSpec"/>; null when loaded from a checkpoint.</summary>
    public ModelSpec? Spec { get; }

    private HoloSession(HoloFormer model, int kPass, double serveAlpha, ModelSpec? spec)
    {
        if (kPass < 1) throw new ArgumentOutOfRangeException(nameof(kPass), kPass, "K must be at least 1.");

        Model = model;
        KPass = kPass;
        Spec = spec;
        ApplyServe(serveAlpha);
    }

    /// <summary>Build a fresh, untrained model from a spec.</summary>
    public static HoloSession Create(ModelSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new HoloSession(spec.Build(), spec.KPass, serveAlpha: 0.0, spec);
    }

    /// <summary>
    /// Load a trained checkpoint. <paramref name="kPass"/> and <paramref name="serveAlpha"/> are
    /// mandatory because the checkpoint does not carry them (see the type remarks) — source them
    /// from sidecar metadata shipped alongside the weights, never from a hardcoded guess.
    /// </summary>
    public static HoloSession FromCheckpoint(byte[] checkpoint, int kPass, double serveAlpha)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new HoloSession(HoloFormer.Deserialize(checkpoint), kPass, serveAlpha, spec: null);
    }

    /// <summary>Keep the served blend in step with a training ramp.</summary>
    public void ApplyServe(double alpha)
    {
        ServeAlpha = Math.Clamp(alpha, 0.0, 1.0);
        Model.Iters = KPass;
        Model.IterAlphaServe = ServeAlpha;
    }

    public void ApplyServe(AlphaRamp ramp)
    {
        ArgumentNullException.ThrowIfNull(ramp);
        ApplyServe(ramp.Alpha);
    }

    /// <summary>Serving logits. Honours <c>Iters</c>, unlike the KV-cache <c>Prime</c>/<c>Step</c> path.</summary>
    public double[] Logits(int[] context) => Model.LogitsFor(context);

    public byte[] Export() => Model.Serialize();

    public ModelStats Stats()
    {
        var d = Model.Dim;
        var layers = Model.Layers;
        var shifts = Model.Shifts;
        var equiv = HoloShape.EquivCompute(d, layers, KPass);

        return new ModelStats(
            Dim: d,
            Layers: layers,
            Shifts: shifts,
            Context: Model.Context,
            Vocab: Model.Vocab,
            ParamCount: Model.ParamCount,
            KPass: KPass,
            ServeAlpha: ServeAlpha,
            EquivCompute: equiv,
            ComputePerStoredParam: HoloShape.InvisibleMultiplier(Model.ParamCount, d, layers, KPass),
            BindRank: HoloShape.BindRank(shifts, d),
            CleanCapacity: HoloShape.CleanCapacity(shifts, d),
            CleanCapacityCoversContext: HoloShape.CleanCapacity(shifts, d) >= Model.Context,
            Golden: Model.Golden);
    }
}

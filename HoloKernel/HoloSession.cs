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
    /// <summary>
    /// The underlying model.
    ///
    /// BROWSER CONTRACT — train-only, fixed shape: a visitor refines this model's WEIGHTS (via
    /// <see cref="RefinementLoop"/>) and never changes its STRUCTURE. <c>GrowLayers</c> and
    /// <c>GrowShifts</c> are real capabilities on the package but are a PrismStudio / server-side
    /// operation; the browser platform does not trigger or expose them, which is why this kernel
    /// wraps neither. They are reachable through this property as a pragmatic escape hatch, not as
    /// an invitation — a "grow the model" control does not belong in a tool UI.
    ///
    /// To put a bigger or better model in front of visitors, publish a different CHECKPOINT. Mutating
    /// shape at runtime would also invalidate an already-downloaded checkpoint and force a multi-MB
    /// re-fetch, which is exactly what the layered load strategy exists to prevent.
    /// </summary>
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

    /// <summary>
    /// Cached serving state for the O(1)/token KV-cache path (<see cref="NewServeCache"/>/
    /// <see cref="Prime(ServeCache,int[])"/>/<see cref="StepToken"/>) — additive alongside
    /// <see cref="Logits"/>, which stays untouched for full-recompute callers (e.g. the Analyst's
    /// novelty scan).
    ///
    /// Wraps AlgFormer's StackK-aware incremental pair, <c>HoloFormer.NewCache(stackK)</c> /
    /// <c>PrimeIter</c> / <c>StepIter</c> — NOT the plain (K=1-only) <c>Prime</c>/<c>Step</c> pair
    /// this type's own doc-comment warns about above, which would silently under-serve a K&gt;1
    /// model. Per AlgFormer's own CLAUDE.md this pair is bit-identical to <c>StackIterLogitsCpu</c>;
    /// measured directly against a real checkpoint (Prism's L=6/K=2/ctx=192 shape) to return the
    /// EXACT SAME generated token sequence as calling <see cref="Logits"/> once per step, including
    /// through a rolling-window overflow (see the caller-side eviction note below).
    ///
    /// ONE REAL GOTCHA: this mirrors <c>StackIterForward</c>, which <see cref="Logits"/> itself only
    /// dispatches to when the model has MORE THAN ONE LAYER — <c>HoloFormer.IterAwareForward</c>
    /// routes a single-layer model through a DIFFERENT function, <c>IterForward</c> (the only one
    /// that also supports <c>IterClean</c> cleanup). So this cache is verified equivalent to
    /// <see cref="Logits"/> for a multi-layer model (Prism) but was NOT verified for a single-layer
    /// one (Creature/Forecaster are Layers=1 today) — don't route those onto this path without
    /// re-verifying equivalence first.
    ///
    /// EVICTION: the cache is append-only and cannot forget its oldest token — a caller doing
    /// rolling generation past the model's own context window must re-<see cref="Prime(ServeCache,int[])"/>
    /// (not <see cref="StepToken"/>) with the freshly-sliced window the moment
    /// <see cref="ServeCache.Filled"/> would reach <c>Stats().Context</c>, exactly reproducing what a
    /// per-step <see cref="Logits"/> call already does over that same slice — see Prism.razor's
    /// <c>GenerateReplyAsync</c> for the reference caller.
    /// </summary>
    public sealed class ServeCache
    {
        internal readonly HoloFormer.KvCache Cache;
        internal readonly int StackK;
        internal readonly double[] Alpha;

        /// <summary>Tokens currently resident in the cache — compare against <c>Stats().Context</c> to
        /// decide whether the next step needs a re-<see cref="Prime(ServeCache,int[])"/> instead of a
        /// <see cref="StepToken"/>.</summary>
        public int Filled { get; internal set; }

        internal ServeCache(HoloFormer.KvCache cache, int stackK, double[] alpha)
        {
            Cache = cache; StackK = stackK; Alpha = alpha;
        }
    }

    /// <summary>
    /// Build a fresh cache sized to this session's own <see cref="KPass"/>, with the current
    /// <see cref="ServeAlpha"/> captured once at creation time — call this again (not just
    /// <see cref="Prime(ServeCache,int[])"/>) if <see cref="ApplyServe(double)"/> changes the served
    /// alpha afterwards, since an existing cache won't pick that up. See <see cref="ServeCache"/>'s
    /// own doc for what this path does and does not reproduce bit-for-bit.
    /// </summary>
    public ServeCache NewServeCache()
    {
        var alpha = new double[Model.Layers];
        Array.Fill(alpha, ServeAlpha);
        return new ServeCache(Model.NewCache(KPass), KPass, alpha);
    }

    /// <summary>
    /// Reset <paramref name="cache"/> and run <paramref name="context"/> through it, returning
    /// logits for the position right after the last token — the O(context) "cold start"/re-prime
    /// step. Callers doing rolling generation (where the full conversation may exceed the model's
    /// own context window) should slice to at most <c>Stats().Context</c> tokens first, exactly as
    /// they would before calling <see cref="Logits"/>.
    /// </summary>
    public double[] Prime(ServeCache cache, int[] context)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var logits = Model.PrimeIter(cache.Cache, context, cache.StackK, cache.Alpha);
        cache.Filled = context.Length;
        return logits;
    }

    /// <summary>
    /// Append one token, O(1) in context length, returning next-token logits. Only valid while
    /// <see cref="ServeCache.Filled"/> is still under the model's own context window
    /// (<c>Stats().Context</c>) — the cache cannot forget its oldest token on its own; once the
    /// rolling window would need to evict one, re-<see cref="Prime(ServeCache,int[])"/> instead (see
    /// the eviction note on <see cref="ServeCache"/>).
    /// </summary>
    public double[] StepToken(ServeCache cache, int token)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var logits = Model.StepIter(cache.Cache, token, cache.StackK, cache.Alpha);
        cache.Filled++;
        return logits;
    }

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

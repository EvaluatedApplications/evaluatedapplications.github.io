using PrismFormer;

namespace HoloKernel;

/// <summary>
/// The shape of a live-training HoloFormer, in one place.
///
/// This exists because The Creature and The Forecaster each derived the same shape decisions
/// independently, in their own page files, with the reasoning in prose comments. Centralising it
/// means the two HARD invariants below are enforced by construction rather than by remembering.
/// </summary>
public sealed record ModelSpec
{
    /// <summary>Token count the model must cover.</summary>
    public required int Vocab { get; init; }

    /// <summary>Maximum context window, in TOKENS (not items — the Forecaster spends 2 tokens/candle).</summary>
    public required int MaxContext { get; init; }

    /// <summary>Model width (<c>dModel</c>).</summary>
    public required int Dim { get; init; }

    public int Layers { get; init; } = 1;

    /// <summary>
    /// Floor for the shift count. MUST be >= 2 — see <see cref="Shifts"/>. Derive it per task from
    /// <c>bindRank = shifts*d/2</c>; do NOT copy another tool's value because it "looks right".
    /// </summary>
    public int MinShifts { get; init; } = 8;

    /// <summary>Weight-tied extra passes (maps to <c>HoloFormer.Iters</c>).</summary>
    public int KPass { get; init; } = 2;

    public int FrozenPrefix { get; init; } = -1;
    public bool Golden { get; init; } = true;
    public int Seed { get; init; } = 42;

    /// <summary>
    /// Resolved shift count: the natural <see cref="HoloShape.ShiftsFor(int,int,double)"/> value,
    /// floored at <see cref="MinShifts"/>.
    ///
    /// HARD INVARIANT: shifts must always be > 1. At S=1 every relation bank is a pure diagonal —
    /// zero cross-channel routing — which silently breaks attention rather than failing loudly.
    /// <c>ShiftsFor</c> genuinely returns 1 for small windows (e.g. ShiftsFor(32, 384)), which is
    /// exactly how The Creature ended up needing a floor, so this is a real case, not a theoretical one.
    /// </summary>
    public int Shifts => ResolveShifts(MaxContext, Dim, MinShifts);

    public static int ResolveShifts(int maxContext, int dim, int minShifts)
    {
        if (minShifts < 2)
            throw new ArgumentOutOfRangeException(nameof(minShifts), minShifts,
                "Shifts must be > 1. At S=1 every relation bank is a pure diagonal (no cross-channel routing).");

        var natural = HoloShape.ShiftsFor(maxContext, dim);
        var resolved = Math.Max(natural, minShifts);

        // Belt and braces: if ShiftsFor ever changes behaviour, still refuse to hand back 1.
        return resolved < 2 ? 2 : resolved;
    }

    /// <summary>True when the floor actually bit (i.e. the natural value was below it).</summary>
    public bool ShiftFloorApplied => HoloShape.ShiftsFor(MaxContext, Dim) < MinShifts;

    /// <summary>
    /// Clean read-back capacity for this shape. When this is BELOW <see cref="MaxContext"/> the
    /// older half of the window reads back less cleanly — a real, documented tuning caveat on the
    /// Forecaster (CleanCapacity(16,128)=122 against a 256-token window), not a blocker.
    /// </summary>
    public int CleanCapacity => HoloShape.CleanCapacity(Shifts, Dim);

    public bool CleanCapacityCoversContext => CleanCapacity >= MaxContext;

    public int BindRank => HoloShape.BindRank(Shifts, Dim);

    internal HoloFormer Build() => new(
        vocab: Vocab,
        shifts: Shifts,
        layers: Layers,
        maxContext: MaxContext,
        dModel: Dim,
        frozenPrefix: FrozenPrefix,
        embedSeed: null,
        seed: Seed,
        bindFfn: false,
        golden: Golden,
        normalize: true,
        unitary: false);
}

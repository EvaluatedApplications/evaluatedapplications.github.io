using PrismFormer;

namespace Doctrine.General;

/// <summary>
/// The tiny HoloFormer half. It reads the 14 summary tokens and then writes the four doctrine slots, one token at a time, trained next-token
/// on the player's own choices by replay from memory. It starts with random weights and says nothing until it has been trained; whatever it
/// says is cut down by the mask by the caller. Floats live here only: the general decides in its owner's session and its choices leave as
/// plain doctrine tokens.
/// </summary>
internal sealed class GeneralNet
{
    public const int Context = 17;                 // 14 summary + 3 teacher-forced output tokens; the 4th output is the library's overflow target
    const int Batch = 8, Slots = DoctrineVocabulary.MaxSlots;
    readonly GeneralOptions _o;
    readonly HoloFormer _m;
    readonly List<(int[] Seq, int[] Key)> _memory = new();
    readonly GameRng _rng;
    readonly int _doctrineIds = DoctrineVocabulary.Size;
    long _steps;

    public int Passes { get; private set; }
    public bool Trained => Passes > 0;
    public int MemoryCount => _memory.Count;

    public GeneralNet(GeneralOptions o, int seed)
    {
        _o = o;
        int vocab = _doctrineIds + (o.FeatureTokens ? GeneralSummary.Length * GeneralSummary.Vocab : GeneralSummary.Vocab);
        int shifts = Math.Min(o.NetDim, Math.Max(4, HoloShape.ShiftsFor(Context, o.NetDim)));   // always > 1
        // (vocab, shifts, layers, context, width, frozenPrefix 0 = every embedding trainable, no embed seed, seed, bindFfn off, golden on, normalize on, unitary off)
        _m = new HoloFormer(vocab, shifts, o.NetLayers, Context, o.NetDim, 0, null, seed, false, true, true, false);
        _m.Iters = o.NetK; _m.IterAlphaServe = 1.0;
        _rng = new GameRng((ulong)seed * 31UL + 5);
    }

    public long ParamCount => _m.ParamCount;
    public byte[] Serialize() => _m.Serialize();

    int Summary(int pos, int v) => _doctrineIds + (_o.FeatureTokens ? pos * GeneralSummary.Vocab + v : v);

    int[] Encode(int[] s, DoctrineSet d)
    {
        var seq = new int[GeneralSummary.Length + Slots];
        for (int i = 0; i < GeneralSummary.Length; i++) seq[i] = Summary(i, s[i]);
        for (int k = 0; k < Slots; k++) seq[GeneralSummary.Length + k] = DoctrineVocabulary.IsValid(d[k]) ? d[k] : 0;
        return seq;
    }

    /// <summary>The player chose <paramref name="d"/> here (imitation, or a battle it won): remember it for replay.</summary>
    public void Remember(int[] s, DoctrineSet d)
    {
        if (_memory.Count >= _o.MemoryCap) _memory.RemoveAt(0);
        _memory.Add((Encode(s, d), Key(s, d)));
    }

    /// <summary>The player chose <paramref name="d"/> here and lost: drop the newest remembered copy of exactly that choice, if any.</summary>
    public void Forget(int[] s, DoctrineSet d)
    {
        var key = Key(s, d);
        for (int i = _memory.Count - 1; i >= 0; i--)
            if (_memory[i].Key.AsSpan().SequenceEqual(key)) { _memory.RemoveAt(i); return; }
    }

    static int[] Key(int[] s, DoctrineSet d) => [.. s, d.S0, d.S1, d.S2, d.S3];

    /// <summary>Adam steps over <paramref name="passes"/> example passes drawn from memory (batches of 8, clip 1, alpha eased in over the first steps).</summary>
    public void Train(int passes)
    {
        if (_memory.Count == 0 || passes <= 0) return;
        int steps = Math.Max(1, passes / Batch);
        var alpha = new double[_m.Layers];
        double ramp = Math.Max(1.0, _o.PassCap / (double)Batch * 0.25);
        for (int st = 0; st < steps; st++)
        {
            Array.Fill(alpha, _o.NetK > 1 ? Math.Min(1.0, (_steps + 1) / ramp) : 1.0);
            var g = _m.NewGrads();
            int scored = 0;
            for (int b = 0; b < Batch; b++)
            {
                var seq = _memory[_rng.Next(_memory.Count)].Seq;
                scored += _m.StackIterAccumulateAllPos(seq, g, Slots, _o.NetK, alpha).count;
            }
            // Clip the global gradient norm to 1 HERE, not in Step: Step sums the norm across threads in whatever order they finish, so
            // its clip factor differs in the last bits from run to run and two identically taught generals drift apart (measured).
            // Reading the gradient out in its fixed order and summing serially keeps the general exactly reproducible.
            double c = ClipFactor(_m.SerializeGradient(g), scored);
            _m.Step(g, _o.LearningRate, scored / c, 0.9, 0.999, 1e-8, 0);
            _steps++; Passes += Batch;
        }
        _m.Iters = _o.NetK; _m.IterAlphaServe = 1.0;
    }

    /// <summary>1 when the (per-example mean) gradient norm is at most 1, else 1/norm. A non-finite gradient gives 1 and Step drops the batch itself.</summary>
    static double ClipFactor(byte[] grad, int count)
    {
        double sq = 0;
        for (int i = 0; i + 8 <= grad.Length; i += 8) { double x = BitConverter.ToDouble(grad, i) / count; sq += x * x; }
        double norm = Math.Sqrt(sq);
        return double.IsFinite(norm) && norm > 1.0 ? 1.0 / norm : 1.0;
    }

    /// <summary>Greedy decode, slot by slot, from legal tokens only (End ends the doctrine, no token twice). Returns End everywhere before the first training pass.</summary>
    public void Decode(int[] s, LegalMask legal, Span<int> tokens, bool speakUntrained = false)
    {
        tokens.Clear();
        if (!Trained && !speakUntrained) return;
        var seq = new int[GeneralSummary.Length + Slots - 1];
        for (int i = 0; i < GeneralSummary.Length; i++) seq[i] = Summary(i, s[i]);
        int len = GeneralSummary.Length;
        Span<bool> used = stackalloc bool[64];
        for (int k = 0; k < Slots; k++)
        {
            var lg = _m.LogitsFor(seq.AsSpan(0, len).ToArray());
            int best = 0; double bv = double.NegativeInfinity;
            for (int t = 0; t < _doctrineIds; t++)
            {
                if (!legal.Allows(t) || (t != 0 && used[t])) continue;
                if (lg[t] > bv) { bv = lg[t]; best = t; }
            }
            tokens[k] = best;
            if (best == 0) return;                 // End ends the doctrine
            used[best] = true;
            if (k < Slots - 1) seq[len++] = best;
        }
    }
}

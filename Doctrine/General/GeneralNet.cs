using PrismFormer;

namespace Doctrine.General;

/// <summary>
/// The tiny HoloFormer half, now a per-offer scorer. For ONE offered card it reads the 14 summary tokens, the card's order type, its argument and
/// whether it repeats last round's picks, and says what became of such a card: not taken, or taken at slot 1, 2, 3 or 4. To answer a whole offer it
/// reads each offered card in turn (the offer IS the input: only cards on it are ever read, so nothing outside it can be named) and fills the slots
/// greedily. Trained next-token on the player's own choices by replay from memory. It starts with random weights and says nothing until trained.
/// Floats live here only: the general decides in its owner's session and its choices leave as plain doctrine tokens.
/// </summary>
internal sealed class GeneralNet
{
    public const int Context = 17;                 // 14 summary + type + argument + repeat; the label is the one scored token
    const int Batch = 8, Slots = DoctrineVocabulary.MaxSlots, Labels = Slots + 1, MaxNegatives = 8;
    const int SumBase = 0, SumSize = GeneralSummary.Length * GeneralSummary.Vocab;
    const int TypBase = SumBase + SumSize, ArgBase = TypBase + CardFeatures.Types, RepBase = ArgBase + CardFeatures.Args, LabBase = RepBase + CardFeatures.Reps;
    readonly GeneralOptions _o;
    readonly HoloFormer _m;
    readonly List<(int[] Key, int[][] Seqs)> _memory = new();
    readonly GameRng _rng;
    long _steps;

    public int Passes { get; private set; }
    public bool Trained => Passes > 0;
    public int MemoryCount => _memory.Count;

    public GeneralNet(GeneralOptions o, int seed)
    {
        _o = o;
        int vocab = LabBase + Labels;
        int shifts = o.NetDim;   // S = d (user rule 2026-10-19): the relation bank is then a full dense map; S < d is a low-rank, gimped net. Always > 1 (NetDim >= 2)
        // (vocab, shifts, layers, context, width, frozenPrefix 0 = every embedding trainable, no embed seed, seed, bindFfn off, golden on, normalize on, unitary off)
        _m = new HoloFormer(vocab, shifts, o.NetLayers, Context, o.NetDim, 0, null, seed, false, true, true, false);
        _m.Iters = o.NetK; _m.IterAlphaServe = 1.0;
        _rng = new GameRng((ulong)seed * 31UL + 5);
    }

    public long ParamCount => _m.ParamCount;
    public byte[] Serialize() => _m.Serialize();

    int[] Prefix(int[] s, int card, int rep)
    {
        var p = new int[Context];
        for (int i = 0; i < GeneralSummary.Length; i++) p[i] = SumBase + i * GeneralSummary.Vocab + s[i];
        p[14] = TypBase + CardFeatures.Type(card); p[15] = ArgBase + CardFeatures.Arg_(card); p[16] = RepBase + rep;
        return p;
    }

    /// <summary>The player chose <paramref name="picks"/> from <paramref name="offer"/> (imitation, or a battle it won): remember every card's fate for replay.</summary>
    public void Remember(int[] s, int[] offer, int[] picks, int[] prev, bool history)
    {
        if (_memory.Count >= _o.MemoryCap) _memory.RemoveAt(0);
        var cards = new List<int>();
        foreach (var c in offer) if (c != 0) cards.Add(c);
        foreach (var p in picks) if (p != 0 && !cards.Contains(p)) cards.Add(p);
        // an open market lists dozens of cards: keep the picks and at most MaxNegatives of the rest, spread across the list
        var keep = new List<int>();
        foreach (var c in cards) if (Array.IndexOf(picks, c) >= 0) keep.Add(c);
        var rest = cards.Where(c => Array.IndexOf(picks, c) < 0).ToList();
        int stride = Math.Max(1, (rest.Count + MaxNegatives - 1) / MaxNegatives);
        for (int i = (_memory.Count % stride); i < rest.Count && keep.Count < MaxNegatives + Slots; i += stride) keep.Add(rest[i]);
        var seqs = new int[keep.Count][];
        for (int i = 0; i < seqs.Length; i++)
        {
            int c = keep[i], at = Array.IndexOf(picks, c);
            var seq = new int[Context + 1];
            Array.Copy(Prefix(s, c, CardTable.Rep(c, prev, history)), seq, Context);
            seq[Context] = LabBase + (at >= 0 ? 1 + at : 0);
            seqs[i] = seq;
        }
        _memory.Add((Key(s, picks), seqs));
    }

    /// <summary>The player chose <paramref name="picks"/> here and lost: drop the newest remembered copy of exactly that choice, if any.</summary>
    public void Forget(int[] s, int[] picks)
    {
        var key = Key(s, picks);
        for (int i = _memory.Count - 1; i >= 0; i--)
            if (_memory[i].Key.AsSpan().SequenceEqual(key)) { _memory.RemoveAt(i); return; }
    }

    static int[] Key(int[] s, int[] picks)
    {
        var k = new int[s.Length + Slots]; s.CopyTo(k, 0);
        for (int i = 0; i < Slots && i < picks.Length; i++) k[s.Length + i] = picks[i];
        return k;
    }

    /// <summary>Adam steps over <paramref name="passes"/> card passes drawn from memory (batches of 8, clip 1, alpha eased in over the first steps).</summary>
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
                var seqs = _memory[_rng.Next(_memory.Count)].Seqs;
                if (seqs.Length == 0) continue;
                scored += _m.StackIterAccumulateAllPos(seqs[_rng.Next(seqs.Length)], g, 1, _o.NetK, alpha).count;
            }
            if (scored == 0) continue;
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

    /// <summary>
    /// Fill the first <paramref name="fill"/> slots with offered cards: each card is read once, then slot k takes the not yet used card most likely to
    /// have been taken at slot k (ties: the lowest id). Returns End everywhere before the first training pass (unless <paramref name="speakUntrained"/>).
    /// </summary>
    public void Decode(int[] s, int[] offer, int[] prev, bool history, int fill, Span<int> tokens, bool speakUntrained = false)
    {
        tokens.Clear();
        if ((!Trained && !speakUntrained) || fill <= 0) return;
        var cards = new List<int>(); foreach (var c in offer) if (c != 0) cards.Add(c);
        var p = new double[cards.Count][];
        for (int i = 0; i < cards.Count; i++)
        {
            var lg = _m.LogitsFor(Prefix(s, cards[i], CardTable.Rep(cards[i], prev, history)));
            var q = new double[Labels]; double mx = double.NegativeInfinity;
            for (int l = 0; l < Labels; l++) mx = Math.Max(mx, lg[LabBase + l]);
            double z = 0; for (int l = 0; l < Labels; l++) { q[l] = Math.Exp(lg[LabBase + l] - mx); z += q[l]; }
            for (int l = 0; l < Labels; l++) q[l] /= z;
            p[i] = q;
        }
        var used = new bool[cards.Count];
        for (int k = 0; k < Slots && k < fill; k++)
        {
            int best = -1; double bv = double.NegativeInfinity;
            for (int i = 0; i < cards.Count; i++) if (!used[i] && p[i][1 + k] > bv) { bv = p[i][1 + k]; best = i; }
            if (best < 0) return;
            used[best] = true; tokens[k] = cards[best];
        }
    }
}

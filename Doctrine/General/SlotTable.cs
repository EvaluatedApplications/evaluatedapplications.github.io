namespace Doctrine.General;

/// <summary>
/// The lookup half of the general. For each of the four doctrine slots it keeps weighted counts of "feature f had value v and the
/// player put token t here", picks the ONE summary feature whose majority rule fits the player best, and answers with the heaviest
/// LEGAL token for the value now seen. Integer counts only, so it is exact, incremental and identical on every device.
/// </summary>
internal sealed class SlotTable
{
    public const int Slots = DoctrineVocabulary.MaxSlots;
    const int Features = GeneralSummary.Length, Values = GeneralSummary.Vocab;
    readonly int _tokens = DoctrineVocabulary.Size;
    readonly int[] _w;          // [slot][feature][value][token]
    readonly int[] _g;          // [slot][token]: no feature at all
    readonly int[] _best = new int[Slots];
    readonly bool _firstFeatureOnly;

    public SlotTable(bool firstFeatureOnly = false)
    {
        _w = new int[Slots * Features * Values * _tokens];
        _g = new int[Slots * _tokens];
        _firstFeatureOnly = firstFeatureOnly;
    }

    int Idx(int k, int f, int v) => ((k * Features + f) * Values + v) * _tokens;

    public int Token(int t) => DoctrineVocabulary.IsValid(t) ? t : 0;

    /// <summary>Add <paramref name="delta"/> to every cell this example touches (a cell never goes below zero).</summary>
    public void Add(int[] s, Doctrine d, int delta)
    {
        for (int k = 0; k < Slots; k++)
        {
            int t = Token(d[k]);
            ref int g = ref _g[k * _tokens + t]; g = Math.Max(0, g + delta);
            for (int f = 0; f < Features; f++)
            {
                ref int c = ref _w[Idx(k, f, s[f]) + t]; c = Math.Max(0, c + delta);
            }
            _best[k] = BestFeature(k);
        }
    }

    /// <summary>The feature whose per-value majority would have been right most often on what the player did (ties: lowest feature).</summary>
    int BestFeature(int k)
    {
        if (_firstFeatureOnly) return 0;
        int bestF = 0, bestHits = -1;
        for (int f = 0; f < Features; f++)
        {
            int hits = 0;
            for (int v = 0; v < Values; v++)
            {
                int o = Idx(k, f, v), m = 0;
                for (int t = 0; t < _tokens; t++) if (_w[o + t] > m) m = _w[o + t];
                hits += m;
            }
            if (hits > bestHits) { bestHits = hits; bestF = f; }
        }
        return bestF;
    }

    /// <summary>One token per slot: the heaviest legal token for this value of the chosen feature, else the heaviest legal token for the slot, else End.</summary>
    public void Predict(int[] s, LegalMask legal, Span<int> tokens)
    {
        for (int k = 0; k < Slots; k++)
        {
            int o = Idx(k, _best[k], s[_best[k]]);
            int t = Heaviest(_w, o, legal);
            if (t < 0) t = Heaviest(_g, k * _tokens, legal);
            tokens[k] = t < 0 ? 0 : t;
        }
    }

    int Heaviest(int[] a, int offset, LegalMask legal)
    {
        int best = -1, bw = 0;
        for (int t = 0; t < _tokens; t++)
            if (a[offset + t] > bw && legal.Allows(t)) { bw = a[offset + t]; best = t; }
        return best;
    }
}

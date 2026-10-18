using HoloKernel;
using PrismFormer;

namespace Showroom.Services.PrismTab;

/// <summary>
/// One question put to the model as TEXT. The prompt is a string; the answer is ONE token chosen among a few
/// label tokens, scored by the model's next-token logits over those labels (no free generation).
/// <see cref="Allowed"/> = the labels that may be chosen at all (Creature: the legal moves); <see cref="Good"/> =
/// the labels that count as right (Creature: every move that gets one step closer to an apple).
/// </summary>
public sealed record TaskItem(string Prompt, bool[] Allowed, bool[] Good)
{
    /// <summary>The fraction of the allowed labels that are right: what picking an allowed label at random scores.</summary>
    public double RandomHitRate
    {
        get
        {
            int allowed = 0, hit = 0;
            for (int i = 0; i < Allowed.Length; i++) if (Allowed[i]) { allowed++; if (Good[i]) hit++; }
            return allowed == 0 ? 0 : (double)hit / allowed;
        }
    }

    /// <summary>A right label to train toward. When several are right (two ways to get closer), one is drawn.</summary>
    public int PickGood(Random r)
    {
        var good = new List<int>(4);
        for (int i = 0; i < Good.Length; i++) if (Good[i] && Allowed[i]) good.Add(i);
        if (good.Count == 0) throw new InvalidOperationException("This item has no right answer to train toward.");
        return good[good.Count == 1 ? 0 : r.Next(good.Count)];
    }
}

/// <summary>A task the Prism tab can score and retrain on. Pure: no UI, no model, no I/O.</summary>
public interface ITextTask
{
    /// <summary>Single-token label texts, in the order <see cref="TaskItem.Allowed"/> and <see cref="TaskItem.Good"/> use.</summary>
    IReadOnlyList<string> Labels { get; }

    /// <summary>What the baseline is, in words: "a random legal move".</summary>
    string BaselineName { get; }

    /// <summary>The fixed held-out items every score in this tab is measured on. Same call, same items.</summary>
    TaskItem[] MakeEvalSet(int n);

    /// <summary>A fresh training item that is never one of the eval items.</summary>
    TaskItem NextTrainItem(Random r);

    /// <summary>The hit rate the baseline policy gets on exactly these items.</summary>
    double Baseline(IReadOnlyList<TaskItem> items);
}

/// <summary>What one scoring run found. All of it is read off the run; nothing here is typed in.</summary>
public sealed record EvalResult(int Hits, int N, double Baseline, double Lo, double Hi)
{
    public double Accuracy => N == 0 ? 0 : (double)Hits / N;

    /// <summary>Plain verdict against the baseline: the interval either clears it, contains it, or sits under it.</summary>
    public string Verdict => Lo > Baseline ? "better than the baseline"
                           : Hi < Baseline ? "worse than the baseline"
                           : "no better than the baseline";

    public bool ClearsBaseline => Lo > Baseline;
}

public static class TaskStats
{
    /// <summary>Wilson 95 percent interval for k hits in n tries.</summary>
    public static (double Lo, double Hi) Wilson(int k, int n)
    {
        if (n <= 0) return (0, 1);
        double p = (double)k / n, z = 1.96, d = 1 + z * z / n;
        double c = (p + z * z / (2 * n)) / d;
        double h = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / d;
        return (Math.Max(0, c - h), Math.Min(1, c + h));
    }

    /// <summary>
    /// A 64-bit FNV-1a fingerprint of a model's serialized weights, as hex. Used to show that the shared model is the
    /// same bytes before and after a visitor's private retraining. Not a security hash; it only has to differ when
    /// any weight differs. Plain managed code, so it runs in the browser without a crypto provider.
    /// </summary>
    public static string Fingerprint(byte[] bytes)
    {
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < bytes.Length; i++) { h ^= bytes[i]; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    public static string Fingerprint(HoloSession s) => Fingerprint(s.Export());
}

/// <summary>
/// Scores a <see cref="TaskItem"/> with a session: encode the prompt, take the logits after its last token, compare the
/// label tokens. The label texts must each be ONE token of the checkpoint's vocabulary and distinct (checked here once).
/// </summary>
public sealed class TextTaskScorer
{
    readonly SubwordVocab _vocab;
    readonly int _context;
    public int[] LabelIds { get; }

    public TextTaskScorer(SubwordVocab vocab, int context, IReadOnlyList<string> labels)
    {
        _vocab = vocab; _context = context;
        LabelIds = new int[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            var e = vocab.Encode(labels[i]);
            if (e.Length != 1) throw new InvalidOperationException($"Label '{labels[i]}' is {e.Length} tokens in this vocabulary, it must be exactly one.");
            LabelIds[i] = e[0];
        }
        if (LabelIds.Distinct().Count() != LabelIds.Length) throw new InvalidOperationException("Two labels map to the same token.");
    }

    /// <summary>The prompt as tokens, at most one window long (the newest tokens are kept if it is longer).</summary>
    public int[] Tokens(string prompt)
    {
        var t = _vocab.Encode(prompt);
        return t.Length > _context ? t[^_context..] : t;
    }

    /// <summary>Raw logits of every label after the prompt.</summary>
    public double[] LabelLogits(HoloSession session, string prompt)
    {
        var lg = session.Logits(Tokens(prompt));
        var r = new double[LabelIds.Length];
        for (int i = 0; i < r.Length; i++) r[i] = lg[LabelIds[i]];
        return r;
    }

    /// <summary>The allowed label with the highest logit (first one wins a tie).</summary>
    public static int Pick(double[] labelLogits, bool[] allowed)
    {
        int best = -1;
        for (int i = 0; i < labelLogits.Length; i++)
            if (allowed[i] && (best < 0 || labelLogits[i] > labelLogits[best])) best = i;
        return best;
    }

    public int Predict(HoloSession session, TaskItem item) => Pick(LabelLogits(session, item.Prompt), item.Allowed);

    /// <summary>Softmax over the allowed labels only; a disallowed label gets 0.</summary>
    public static double[] Probabilities(double[] labelLogits, bool[] allowed)
    {
        var p = new double[labelLogits.Length];
        double max = double.NegativeInfinity;
        for (int i = 0; i < p.Length; i++) if (allowed[i] && labelLogits[i] > max) max = labelLogits[i];
        double sum = 0;
        for (int i = 0; i < p.Length; i++) if (allowed[i]) { p[i] = Math.Exp(labelLogits[i] - max); sum += p[i]; }
        if (sum > 0) for (int i = 0; i < p.Length; i++) p[i] /= sum;
        return p;
    }

    /// <summary>The training sequence for an item: its prompt tokens, then the right label's token.</summary>
    public int[] TrainingSequence(TaskItem item, int label)
    {
        var p = Tokens(item.Prompt);
        // A sequence may be one token longer than the window (the library scores that last transition); keep it inside.
        var seq = new int[p.Length + 1];
        Array.Copy(p, seq, p.Length);
        seq[^1] = LabelIds[label];
        return seq;
    }

    /// <summary>Score a session on a set, synchronously. The page uses the yielding version (<see cref="PrismEvaluator"/>).</summary>
    public EvalResult Evaluate(HoloSession session, IReadOnlyList<TaskItem> items, ITextTask task)
    {
        int hits = 0;
        foreach (var it in items) if (it.Good[Predict(session, it)]) hits++;
        var (lo, hi) = TaskStats.Wilson(hits, items.Count);
        return new EvalResult(hits, items.Count, task.Baseline(items), lo, hi);
    }
}

/// <summary>Scores a set in time slices so a single-threaded browser tab keeps painting and answering taps.</summary>
public static class PrismEvaluator
{
    public static async Task<EvalResult?> EvaluateAsync(TextTaskScorer scorer, HoloSession session, IReadOnlyList<TaskItem> items,
        ITextTask task, CancellationToken ct, Action<int>? progress = null)
    {
        var coop = new Cooperative(ct, sliceMs: 12);
        int hits = 0;
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Good[scorer.Predict(session, it)]) hits++;
                progress?.Invoke(i + 1);
                await coop.YieldIfDueAsync();
            }
        }
        catch (OperationCanceledException) { return null; }
        var (lo, hi) = TaskStats.Wilson(hits, items.Count);
        return new EvalResult(hits, items.Count, task.Baseline(items), lo, hi);
    }
}

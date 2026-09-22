namespace HoloKernel;

/// <summary>
/// Finds a repeating run at the END of a generated token sequence, so a caller can cut it before it
/// ever reaches the visible output (or, for a turn-based tool, before it re-enters the model's own
/// context). Lifted out of <c>Prism.razor</c> (2026-09-21) so Nano Stories can trim the same way
/// rather than re-derive it — a generated tail like "ERE ERE ERE" is a property of this checkpoint's
/// current training state, not of either tool, so both should treat it identically.
///
/// Deliberately does NOT key off <c>Prism.Inference.DegenGuard</c>: DegenGuard fires only on
/// consecutive IDENTICAL tokens (its repeat threshold; the non-greedy-run trigger is usually disabled
/// by both consumers here to preserve prior stop behaviour), and its own doc records the blind spot —
/// "A B A B A B" trips neither trigger. That period&gt;1 case is exactly what this checkpoint
/// produces, so scanning the emitted tokens after the fact closes that gap at the consumer without
/// changing the guard, which still does its own (earlier) job of stopping compute.
/// </summary>
public static class DegenerateTail
{
    // Smallest period wins, so "ERE ERE ERE" cuts at p=3 rather than reporting p=6. The default
    // MinReps=3/MinSpan=6 keep real text safe: an ordinary doubled word is 2 reps and survives, and a
    // 1-token period needs 6 in a row, which is past DegenGuard's own RepeatThreshold=4 anyway.
    public const int DefaultMaxPeriod = 12, DefaultMinReps = 3, DefaultMinSpan = 6;

    /// <summary>
    /// Index up to which a live, token-by-token reveal can safely paint. Same scan as <see cref="Start"/>
    /// but one repetition short of the real trigger, so a repeat that is still FORMING is held back
    /// instead of being painted and then yanked away the moment it completes. If the repeat breaks, the
    /// held-back tokens simply paint on the next step (nothing is lost — this only delays the reveal by a
    /// few tokens); if it completes, <see cref="Start"/> fires and the caller cuts without the visitor
    /// ever having seen the run. Added 2026-09-22 (user: the cut used to land at the END of the animation,
    /// after the doomed tokens had already been typed out).
    /// </summary>
    public static int SafePrefix(IReadOnlyList<int> ids) => Start(ids, DefaultMaxPeriod, 2, 2);

    /// <summary>Index where a repeating tail begins, or <c>ids.Count</c> when there isn't one.</summary>
    public static int Start(IReadOnlyList<int> ids, int maxPeriod = DefaultMaxPeriod, int minReps = DefaultMinReps, int minSpan = DefaultMinSpan)
    {
        for (var p = 1; p <= maxPeriod; p++)
        {
            var reps = 1;
            while (TailRepeats(ids, p, reps + 1)) reps++;
            if (reps >= minReps && reps * p >= minSpan) return ids.Count - reps * p;
        }
        return ids.Count;
    }

    // Is the final block of `p` tokens repeated `reps` times back-to-back at the tail?
    static bool TailRepeats(IReadOnlyList<int> ids, int p, int reps)
    {
        var len = ids.Count;
        if (p * reps > len) return false;
        for (var r = 1; r < reps; r++)
            for (var i = 0; i < p; i++)
                if (ids[len - p + i] != ids[len - (r + 1) * p + i]) return false;
        return true;
    }
}

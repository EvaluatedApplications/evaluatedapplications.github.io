namespace HoloKernel;

/// <summary>
/// Context-window arithmetic for the tools that GENERATE from Prism's checkpoint (Prism chat, Nano
/// Stories), derived from the loaded model's own <c>Context</c> instead of a window size typed into a page.
///
/// WHY IT EXISTS (2026-10-08): both pages once pinned a step cap and then computed the prompt budget as
/// <c>Context - cap</c>. That was right while every checkpoint had ctx=512, and it went negative the moment
/// a ctx=64 checkpoint shipped. The pinned caps (160 reply steps, 448 story steps) are still the right
/// numbers for a 512 window and are kept as the CEILING; what changes is that a smaller window can only
/// shrink them, never be asked for a negative prompt budget.
///
/// THE WINDOW RULE these helpers protect: prompt + generated steps must stay within <c>Context</c>, or the
/// serve cache fills and every further step re-Primes the whole window (an O(context) pass per step).
/// So the prompt budget is always <c>Context - cap</c>, and the cap is what gives way on a small window.
///
/// At Context=512 both functions return exactly the values the pages used to hard-code (160 / 448 steps,
/// 352 / 64 prompt tokens), so nothing about the 512-window behaviour moves.
/// </summary>
public static class ContextBudget
{
    /// <summary>Never plan a prompt smaller than this many tokens (when the window can hold it at all).</summary>
    public const int MinPromptTokens = 8;

    /// <summary>
    /// Chat reply step cap: the pinned ceiling, or half the window if the window is small. Half keeps at least
    /// as much room for the conversation as for the answer (512 -> min(160, 256) = 160; 64 -> 32).
    /// </summary>
    public static int ReplyCap(int context, int pinnedCap) =>
        Math.Max(1, Math.Min(pinnedCap, context / 2));

    /// <summary>
    /// Story step cap: the pinned ceiling, or the window minus an opening-line reserve. The reserve is an
    /// eighth of the window (512 -> 64, exactly what the old 512-448 split left) but never under 16 tokens
    /// on a small window, and never more than half of it (64 -> 16, so a story of 48 steps).
    /// </summary>
    public static int StoryCap(int context, int pinnedCap)
    {
        var reserve = Math.Max(context / 8, Math.Min(context / 2, 16));
        return Math.Max(1, Math.Min(pinnedCap, context - reserve));
    }

    /// <summary>
    /// Story length when the page SLIDES its window (2026-10-08): the larger of the no-roll cap
    /// (<see cref="StoryCap"/>, which a small window cannot make longer than one sentence) and a rolling
    /// target, bounded by the pinned ceiling. On a window big enough to hold the pinned ceiling the first
    /// term wins and nothing rolls (512 -> 448, as before); on ctx=64 the window cannot hold a story, so the
    /// target (e.g. 400) applies and the page re-primes on <see cref="RollTail"/> each time the cache fills.
    /// </summary>
    public static int RollingStoryCap(int context, int pinnedCap, int rollingTarget) =>
        Math.Max(StoryCap(context, pinnedCap), Math.Min(pinnedCap, rollingTarget));

    /// <summary>
    /// Tokens of the most recent text to re-prime on when the serve cache is full: half the window. One
    /// re-prime of this many tokens buys <c>context - RollTail</c> O(1) steps, so the cost is one Prime per
    /// half-window of text instead of one per token.
    /// </summary>
    public static int RollTail(int context) => Math.Max(1, context / 2);

    /// <summary>Tokens the prompt may occupy so that prompt + <paramref name="cap"/> steps fit the window.</summary>
    public static int PromptBudget(int context, int cap) =>
        Math.Clamp(context - cap, Math.Min(MinPromptTokens, context), Math.Max(1, context));

    /// <summary>Drop leading tokens so at most <paramref name="budget"/> remain (the tail is what the model continues from).</summary>
    public static void KeepTail<T>(List<T> tokens, int budget)
    {
        if (budget >= 0 && tokens.Count > budget) tokens.RemoveRange(0, tokens.Count - budget);
    }
}

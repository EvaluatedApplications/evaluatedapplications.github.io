namespace Doctrine.General;

/// <summary>Which learners speak. Hybrid = a per-slot lookup and the tiny net, arbitrated slot by slot on how each did on the player's own next choices.</summary>
public enum GeneralMode : byte { Hybrid = 0, TableOnly = 1, NetOnly = 2 }

/// <summary>
/// Knobs of <see cref="TinyHoloGeneral"/>. The defaults are the shape the probe (AlgFormer\todo\general-probe-2026-10-18.md) left standing.
/// Nothing here is pre-trained: every value is a size or a rate, and a new general starts empty whatever it holds.
/// </summary>
public sealed record GeneralOptions
{
    public GeneralMode Mode { get; init; } = GeneralMode.Hybrid;

    /// <summary>HoloFormer width (even). The probe's smallest useful width is 16.</summary>
    public int NetDim { get; init; } = 16;
    public int NetLayers { get; init; } = 1;
    /// <summary>Weight-tied extra passes. 1 = off (the probe found no gain from K above 1).</summary>
    public int NetK { get; init; } = 1;
    /// <summary>One token per (position, value) instead of one per value: vocabulary 278 instead of 70.</summary>
    public bool FeatureTokens { get; init; }

    public double LearningRate { get; init; } = 0.03;
    /// <summary>Replay passes (one example forward and backward each) after every Learn call, cut into four EvalApp steps so a one-thread host can yield, until <see cref="PassCap"/> in total.</summary>
    public int PassesPerLearn { get; init; } = 96;
    public int PassCap { get; init; } = 4800;
    public int PassesAfterCap { get; init; } = 24;
    /// <summary>Examples kept for replay (oldest dropped first).</summary>
    public int MemoryCap { get; init; } = 256;
    public int Seed { get; init; } = 1;
    /// <summary>The net speaks for a slot only after it has guessed the player's own next choices right this many more times than the lookup.</summary>
    public int Margin { get; init; } = 2;
}

/// <summary>Deliberate breakages, for the mutation proofs in Doctrine.Tests only (internal; the game never sets one).</summary>
[Flags]
internal enum GeneralFault
{
    None = 0,
    NetOutputUnmasked = 1,     // the final mask pass is skipped, so a net token the army forbids reaches the caller
    NetAlwaysTrusted = 2,      // the net speaks from the first example, trained or not
    OutcomeIgnored = 4,        // a battle result teaches nothing
    SharedState = 8,           // every instance learns into one lookup
    NetUnseeded = 16,          // the net's initial weights depend on the clock
    FirstFeatureOnly = 32,     // the lookup always reads feature 0 instead of the feature that fits the player
}

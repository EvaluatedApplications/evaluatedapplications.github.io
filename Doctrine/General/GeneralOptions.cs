namespace Doctrine.General;

/// <summary>
/// Which learners speak. TableOnly (the shipped default) = the exact-token habit lookup and the card-feature taste table, arbitrated slot by slot. Hybrid adds the tiny
/// net, which speaks for a slot only once it has been right on the player's own next picks <see cref="GeneralOptions.Margin"/> more often than the tables; NetOnly is the net alone.
/// MEASURED (Doctrine.Tests\General\OfferProbe.cs, EXP-OFFER-1): under the offers the net never beat the taste table, so it is not the default.
/// </summary>
public enum GeneralMode : byte { Hybrid = 0, TableOnly = 1, NetOnly = 2 }

/// <summary>
/// Knobs of <see cref="TinyHoloGeneral"/>. The defaults are the shape EXP-OFFER-1 (Doctrine.Tests\General\OfferProbe.cs) left standing: the tables alone, history off.
/// Nothing here is pre-trained: every value is a size or a rate, and a new general starts empty whatever it holds.
/// </summary>
public sealed record GeneralOptions
{
    public GeneralMode Mode { get; init; } = GeneralMode.Hybrid;

    /// <summary>HoloFormer width (even; also its shifts, S = d). Only read when the net is on (Hybrid, NetOnly).</summary>
    public int NetDim { get; init; } = 16;
    public int NetLayers { get; init; } = 1;
    /// <summary>Weight-tied extra passes. 1 = off (the probe found no gain from K above 1).</summary>
    public int NetK { get; init; } = 1;

    /// <summary>Read the player's previous round's picks as input: each card is marked "the very card", "same order type" or neither, to the taste table and the net. OFF by default: EXP-OFFER-1 measured +0.02 exact (N=40) on a player who chases last round's order type, below the 0.03 bar set beforehand, and nothing elsewhere.</summary>
    public bool History { get; init; }

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
    SlotsSwapped = 64,         // slot figures are read from the neighbouring slot (0<->1, 2<->3)
    SlotsNotReset = 128,       // slot hit counts live in one static array, so a new general inherits an old one's record
    SlotsOptimistic = 256,     // the figure is (hits+1)/(trials+1): above the measured hit rate
    OfferIgnored = 512,        // the learners rank every card of the vocabulary instead of the cards on offer (the final mask still applies)
    TasteDeaf = 1024,          // the taste table never learns from a choice
}

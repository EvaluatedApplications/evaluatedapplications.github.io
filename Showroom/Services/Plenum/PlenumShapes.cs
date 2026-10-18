using Phasor;

namespace ShowroomPlenum;

/// <summary>Every Pleroma shape The Plenum uses, written in ONE place. What fixed these numbers (tests in tools/PlenumProof):
/// KeyCycle 31 divides q-1 of the default field (2^61-1) and is the smallest cycle that holds the 12 card keys plus every Fuse
/// sum; CompleteKeys is needed for Counts() and SampleKey (the draw); ItemLines=false drops the per-key item lines that nothing
/// here reads (TryItems), leaving 286 cells per zone. Restrict is NOT usable on this shape (it needs the full FlagSpectrum, about
/// 8,000 cells), which is why the hand is emptied with Subtract and not filtered.</summary>
public static class PlenumShapes
{
    /// <summary>Distinct kinds a hand can be READ with (TryKeys refuses past it).</summary>
    public const int HandKinds = 8;

    /// <summary>Key cycle: keys 0..30. Card keys are 1..12; a Fuse adds keys modulo this.</summary>
    public const ulong KeyCycle = 31;

    /// <summary>Tag bits (flag word of 8 bits).</summary>
    public const int FlagBits = 8;

    /// <summary>Ticks in the Engine's time cycle (6 divides q-1).</summary>
    public const int EngineTicks = 6;

    /// <summary>Deck, Hand, Board and Discard all use this shape, so a whole store can be added to or subtracted from another.</summary>
    public static HologramShape Zone() => new()
    {
        KeyCapacity = HandKinds,
        FlagBits = FlagBits,
        KeyCycle = KeyCycle,
        CompleteKeys = true,
        ItemLines = false,
    };

    /// <summary>The Engine: a zone with a time cycle.</summary>
    public static HologramShape Engine() => Zone() with { TimePeriod = EngineTicks };

    /// <summary>Tempo: a tiny keys-only store (plays left, turn, draws made, goal reached).</summary>
    public static HologramShape Tempo() => new() { KeyCapacity = 4 };
}

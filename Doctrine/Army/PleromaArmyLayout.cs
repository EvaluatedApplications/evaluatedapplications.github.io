using Phasor;

namespace Doctrine.Army;

// The Pleroma shapes of the army and the index maps the army orders use. Nothing here holds game state.
// A cohort is one item: Key = unit kind (the command span is KeyCapacity 8), Flags = the eight traits XOR the army's flip
// mask, Arrangement = formation slot (slots 0, 1, 2 are the lexicographic indices 0, 3, 4 of the three rotations of
// (0 1 2), so one left factor rotates the line), Time = veterancy on a ring of Period ticks, count = soldiers.

internal static class PleromaArmyLayout
{
    public const int Period = 9;          // T, the veterancy ring. 9 divides 2^61 - 2.
    public const int KeyCycle = 31;       // the complete key band. CompleteKeys needs N >= 2K + 1 (K = 8) and N | 2^61 - 2.
    public const int AuraCap = 50;        // an aura is worth at most 50 percent (the stub's rule, kept)
    public const int StartGold = 12;
    public const int StartSoldiers = 24;
    public const int CommandSpan = DoctrineVocabulary.Kinds;
    public const int MaxCode = 600;

    // Levy weights for the starting army: a Pleroma SampleKey over these counts, drawn on the game's SplitMix64.
    public static readonly long[] LevyWeight = [6, 2, 3, 3, 1, 1, 2, 1];

    // Lexicographic arrangement index of slot s (0 front, 1 middle, 2 back).
    public static readonly int[] SlotArrangement = [0, 3, 4];

    /// <summary>The left factor of a rotation by <paramref name="shift"/> slots (1 or 2): a 3-cycle, so slot s moves to s + shift.</summary>
    public static int RotationFactor(int shift) => shift == 1 ? 3 : 4;

    /// <summary>Slot of a lexicographic arrangement index (-1 when the index is not one of the three slots).</summary>
    public static int SlotOf(int arrangement) => arrangement switch { 0 => 0, 3 => 1, 4 => 2, _ => -1 };

    /// <summary>Power in percent: 100, plus 5 per veteran tick, plus the aura bonus when the cohort carries an active aura's trait.</summary>
    public static int Power(int veterancy, uint traits, int auraMask, int auraPct)
        => 100 + 5 * veterancy + ((traits & (uint)auraMask) != 0 ? auraPct : 0);

    // The shapes. Each store is built once per army; the Layout behind them is shared by Pleroma.
    public static HologramShape ArmyShape => new()
    {
        KeyCapacity = CommandSpan, FlagBits = DoctrineVocabulary.Traits, OrderSize = 3, TimePeriod = Period,
        CompleteKeys = true, KeyCycle = KeyCycle,
    };

    /// <summary>Keys and counts only: the projection that Score reads through InnerProduct.</summary>
    public static HologramShape KeysShape => new() { KeyCapacity = CommandSpan, CompleteKeys = true, KeyCycle = KeyCycle };

    /// <summary>Gold: the count of the single key 0.</summary>
    public static HologramShape PurseShape => new() { KeyCapacity = 1 };

    /// <summary>The round number: the count of the single key 0.</summary>
    public static HologramShape ClockShape => new() { KeyCapacity = 1 };

    /// <summary>The flip record: one flag word per flip applied; the parity of each bit gives the army's flip mask.</summary>
    public static HologramShape FlipShape => new() { KeyCapacity = 1, FlagBits = DoctrineVocabulary.Traits };

    /// <summary>Active auras: key = the trait bit, count = the aura's percent.</summary>
    public static HologramShape AuraShape => new() { KeyCapacity = CommandSpan };

    /// <summary>Trait predicates, compiled once. A FlagPredicate is reusable across every store of the same FlagBits.</summary>
    public static readonly FlagPredicate[] HasTrait = Enumerable.Range(0, DoctrineVocabulary.Traits)
        .Select(b => FlagPredicate.Compile(DoctrineVocabulary.Traits, x => ((x >> b) & 1u) != 0u)).ToArray();
}

/// <summary>
/// The game's own SplitMix64 as a <see cref="Random"/>, so that the Pleroma's SampleKey draws from <see cref="GameRng"/> and from nothing else.
/// </summary>
internal sealed class GameRandom(GameRng rng) : Random
{
    public override long NextInt64(long maxValue) => (long)(rng.NextU64() % (ulong)maxValue);
}

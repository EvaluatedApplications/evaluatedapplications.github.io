namespace Doctrine;

// The small shared DATA RECORDS. Sections talk only in these; no section reaches into another's types.
// Everything here is integers and strings on purpose: deterministic, trim safe, and comparable across devices.

// ---- Army ----------------------------------------------------------------------------------------------------------

/// <summary>One cohort as the army reports it: Key = unit kind (0..7), Count = soldiers, Slot = formation slot (0..2).</summary>
public sealed record CohortRead(int Key, int Count, Trait Traits, int Slot, int Veterancy, int PowerPct);

/// <summary>Everything anyone else may know about an army. The Pleroma is the truth; this is a read of it.</summary>
public sealed record ArmyReadModel(
    Side Side, int Round, int Gold, IReadOnlyList<CohortRead> Cohorts, LegalMask Legal)
{
    public int Soldiers => Cohorts.Sum(c => c.Count);
    public int Kinds => Cohorts.Count(c => c.Count > 0);
    /// <summary>Soldiers carrying each trait (index = trait bit). A soldier with two traits counts in two buckets.</summary>
    public int[] TraitBuckets()
    {
        var b = new int[DoctrineVocabulary.Traits];
        foreach (var c in Cohorts)
            for (int t = 0; t < b.Length; t++) if (((int)c.Traits >> t & 1) != 0) b[t] += c.Count;
        return b;
    }
    /// <summary>A stable hash of the whole read (FNV-1a over the integers), used by determinism checks.</summary>
    public ulong Digest()
    {
        var h = new Fnv();
        h.Add((int)Side); h.Add(Round); h.Add(Gold); h.Add(Legal.Bits);
        foreach (var c in Cohorts.OrderBy(c => c.Key).ThenBy(c => c.Slot))
        { h.Add(c.Key); h.Add(c.Count); h.Add((int)c.Traits); h.Add(c.Slot); h.Add(c.Veterancy); h.Add(c.PowerPct); }
        return h.Value;
    }
}

/// <summary>Buy <see cref="Count"/> soldiers of kind <see cref="Kind"/>.</summary>
public readonly record struct DraftPick(int Kind, int Count);

public sealed record DraftRequest(int Round, int Seed, IReadOnlyList<DraftPick> Picks);

/// <summary>What the draft did. Refusals are data (a ninth kind, too little gold), never exceptions.</summary>
public sealed record DraftReport(IReadOnlyList<DraftPick> Accepted, IReadOnlyList<(DraftPick Pick, string Reason)> Refused, int GoldSpent);

public sealed record ArmyOrdersReport(int Applied, int Skipped);

/// <summary>Soldiers lost by one cohort in a battle (Key and Slot name the cohort).</summary>
public readonly record struct CohortLoss(int Key, int Slot, int Lost);

public sealed record Spoils(int Gold, int Experience);

// ---- Battle --------------------------------------------------------------------------------------------------------

public sealed record ArenaInfo(string Id, string Name, int Width, int Height);

public sealed record BattleSide(Side Side, IReadOnlyList<CohortRead> Cohorts, IReadOnlyList<FieldOrder> Orders);

/// <summary>Everything a battle needs; built by the pipeline from two army reads and two doctrines. Same setup, same result.</summary>
public sealed record BattleSetup(string ArenaId, int Seed, int TickLimit, BattleSide Attacker, BattleSide Defender)
{
    public const int DefaultTickLimit = 120;
}

/// <summary>Running while both sides stand. A finished battle's Outcome is never Running: when the tick limit stops a battle, the side with more soldiers left wins (equal = Draw).</summary>
public enum BattleStatus : byte { Running = 0, AttackerWon = 1, DefenderWon = 2, Draw = 3 }

public sealed record BattleResult(
    BattleStatus Outcome, int Ticks,
    IReadOnlyList<CohortLoss> AttackerLosses, IReadOnlyList<CohortLoss> DefenderLosses)
{
    public bool AttackerWon => Outcome == BattleStatus.AttackerWon;
}

// ---- Replay (seam e): the recording a battle leaves. Playback never recomputes. ---------------------------------------

/// <summary>A cohort blob in one frame. X and Y are quantised to 1/16 of an arena unit (integers on the wire).</summary>
public readonly record struct ReplayBlob(int Id, Side Side, int Key, int Slot, int X, int Y, int Strength);

/// <summary>One engagement result in a tick: Source struck Target for Damage soldiers.</summary>
public readonly record struct ReplayEvent(int Source, int Target, int Damage);

/// <summary>State after <see cref="Tick"/> completed ticks (frame 0 = the start). Strength is soldiers still standing.</summary>
public sealed record ReplayFrame(int Tick, IReadOnlyList<ReplayBlob> Blobs, IReadOnlyList<ReplayEvent> Events);

public sealed record BattleReplay(
    int Version, string ArenaId, int Seed, int TickLimit, IReadOnlyList<ReplayFrame> Frames, BattleResult Result)
{
    public const int CurrentVersion = 1;
    public const int Quantum = 16;   // positions are stored as (arena unit * 16)
}

// ---- General (seam c) ----------------------------------------------------------------------------------------------

/// <summary>A short token summary of the situation: no game types, just ids in <c>[0, GeneralSummary.Vocab)</c>.</summary>
public sealed record GeneralInput(int Round, IReadOnlyList<int> Summary, LegalMask Legal);

/// <summary>
/// The general's suggestion: a doctrine already masked by <see cref="GeneralInput.Legal"/>, and a confidence in permille
/// (0 = knows nothing, 1000 = certain). Integers, so a result is exactly reproducible.
/// </summary>
public sealed record GeneralOutput(Doctrine Doctrine, int ConfidencePermille);

/// <summary>An imitation example: in this situation the player chose this doctrine.</summary>
public sealed record GeneralExample(GeneralInput Input, Doctrine Chosen, bool Overrode);

/// <summary>An outcome example: this choice was followed by this result (success-weighted replay).</summary>
public sealed record GeneralOutcome(GeneralInput Input, Doctrine Chosen, BattleStatus Result, bool PlayerWon);

// ---- Rounds --------------------------------------------------------------------------------------------------------

/// <summary>What one round left behind: the part of the game the UI shows and the ladder would share.</summary>
public sealed record RoundRecord(
    int Round, Doctrine PlayerDoctrine, Doctrine Ghost, int GhostConfidence, bool Overrode, int IllegalDropped,
    string ArenaId, BattleResult Result, BattleReplay Replay, Spoils Spoils, ulong PlayerDigest, ulong EnemyDigest);

// ---- helpers -------------------------------------------------------------------------------------------------------

/// <summary>FNV-1a over 64-bit words. Stable across platforms (no string hashing, no randomised seeds).</summary>
public struct Fnv
{
    ulong _h;
    public ulong Value => _h == 0 ? 14695981039346656037UL : _h;
    public void Add(long v)
    {
        var h = Value;
        for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; }
        _h = h;
    }
    public void Add(ulong v) => Add(unchecked((long)v));
}

/// <summary>splitmix64: the one random source in the game. Same seed, same stream, on every device.</summary>
public sealed class GameRng(ulong seed)
{
    ulong _s = seed;
    public ulong NextU64()
    {
        unchecked
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    /// <summary>Uniform integer in [0, n). n must be positive.</summary>
    public int Next(int n) => (int)(NextU64() % (ulong)n);
    public static ulong Mix(int a, int b) { var r = new GameRng(((ulong)(uint)a << 32) | (uint)b); return r.NextU64(); }
}

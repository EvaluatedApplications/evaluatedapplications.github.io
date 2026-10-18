using EvalApp;

namespace Doctrine;

/// <summary>Per-game rules; lives in the pipeline's DomainContext. Plain numbers, set once.</summary>
public sealed class DoctrineDomain : DomainContext
{
    public const int MaxRoundsLimit = 200;
    public int MaxRounds { get; init; } = 8;
    public int TickLimit { get; init; } = BattleSetup.DefaultTickLimit;
    public int EnemyBaseGold { get; init; } = 14;
    public int EnemyGoldPerRound { get; init; } = 4;
}

/// <summary>
/// TASK DATA of the round pipeline: the whole game moves forward as this record. A round's pipeline ends by running the next
/// round's on the record <see cref="RoundState.Advance"/> builds, so history rides along and the last round returns it all.
/// </summary>
public sealed record RoundState(
    int Round, int Seed, IArmy Player,
    IArmy? Enemy = null,
    GeneralInput? Input = null, GeneralOutput? Ghost = null, PlayerChoice? Choice = null, int IllegalDropped = 0,
    DoctrineSet EnemyDoctrine = default, BattleSetup? Setup = null, BattleResult? Result = null, BattleReplay? Replay = null,
    Spoils? Spoils = null, BattleStatus? LastOutcome = null, bool LastPlayerWon = false,
    IReadOnlyList<RoundRecord>? History = null, bool Done = false, ArmyOrdersReport? Orders = null)
{
    public IReadOnlyList<RoundRecord> Records => History ?? Array.Empty<RoundRecord>();

    /// <summary>The next round's starting record: same armies and history, everything transient cleared.</summary>
    public RoundState Advance() => new(Round + 1, Seed, Player, History: History,
        LastOutcome: Result?.Outcome ?? LastOutcome, LastPlayerWon: Result?.AttackerWon ?? LastPlayerWon);
}

/// <summary>TASK DATA of the battle pipelines: the setup, the live session, and the frames recorded so far.</summary>
public sealed record BattleState(
    BattleSetup Setup, IBattleSession? Session = null, List<ReplayFrame>? Frames = null,
    bool Done = false, bool TickLimitReached = false, BattleResult? Result = null, BattleReplay? Replay = null);

/// <summary>What a finished game hands back: every round's record, and the player's army as a code.</summary>
public sealed record GameResult(int Seed, IReadOnlyList<RoundRecord> Rounds, string PlayerArmyCode)
{
    /// <summary>One number for the whole game: equal games, equal digest (rounds, doctrines, replays, final army).</summary>
    public ulong Digest()
    {
        var h = new Fnv();
        h.Add(Seed);
        foreach (var r in Rounds)
        {
            h.Add(r.Round); h.Add(r.PlayerDoctrine.S0); h.Add(r.PlayerDoctrine.S1); h.Add(r.PlayerDoctrine.S2); h.Add(r.PlayerDoctrine.S3);
            h.Add(r.GhostConfidence); h.Add(r.Overrode ? 1 : 0); h.Add(r.IllegalDropped);
            h.Add(ReplayCodec.Digest(r.Replay)); h.Add(r.PlayerDigest); h.Add(r.EnemyDigest);
            h.Add(r.Spoils.Gold); h.Add(r.Spoils.Experience);
        }
        foreach (var c in PlayerArmyCode) h.Add(c);
        return h.Value;
    }
}

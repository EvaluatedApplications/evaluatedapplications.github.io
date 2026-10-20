using EvalApp;

namespace Doctrine;

/// <summary>Per-game rules; lives in the pipeline's DomainContext. Plain numbers, set once.</summary>
public sealed class DoctrineDomain : DomainContext
{
    public const int MaxRoundsLimit = 200;
    public int MaxRounds { get; init; } = Roster.CampaignRounds;
    public int TickLimit { get; init; } = BattleSetup.DefaultTickLimit;
    public int EnemyBaseGold { get; init; } = 14;
    public int EnemyGoldPerRound { get; init; } = 4;
    /// <summary>The roguelike offers (default ON: the game). <see cref="OfferRules.Off"/> is the old free choice.</summary>
    public OfferRules Offers { get; init; } = OfferRules.Default;
    /// <summary>
    /// Sieges (fifth round): in a siege arena (<see cref="ArenaInfo.IsSiege"/>) the setup names a besieger, which carries the siege clock. Off = every battle is on an open field (the
    /// fourth round's game, to the digest).
    /// </summary>
    public bool Siege { get; init; } = true;
    /// <summary>The roles in a siege swap every round (<see cref="SiegeRules.BesiegerOf"/>). Off only as a measurement null and as a mutant: the player always besieges.</summary>
    public bool SwapSiegeRoles { get; init; } = true;
    /// <summary>
    /// The comeback muster (<see cref="Roster.Muster"/>): when spoils are paid, half of the army's shortfall against the strength of the next enemy is added as gold. Measured in the fifth
    /// round (Doctrine\todo\difficulty-2026-10-19.md); the default is what that measurement decided.
    /// </summary>
    public bool Comeback { get; init; }
    /// <summary>MEASUREMENT NULL only: the spoils of a round are the same for a win and a loss (kills / 2 + 4 gold, no veterancy), so a snowball cannot come from the spoils.</summary>
    public bool FlatSpoils { get; init; }
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
    IReadOnlyList<RoundRecord>? History = null, bool Done = false, ArmyOrdersReport? Orders = null,
    UnitOffer? Units = null, OrderOffer? OrderOffer = null, int Rerolls = 0, int OfferRefused = 0,
    FieldScenario? Field = null)
{
    public IReadOnlyList<RoundRecord> Records => History ?? Array.Empty<RoundRecord>();

    /// <summary>The next round's starting record: same armies and history, everything transient cleared.</summary>
    public RoundState Advance() => new(Round + 1, Seed, Player, History: History,
        LastOutcome: Result?.Outcome ?? LastOutcome, LastPlayerWon: Result?.AttackerWon ?? LastPlayerWon);
}

/// <summary>
/// ONE battle of a campaign epoch (Campaign\CampaignGame.cs), carried in <see cref="RoundState.Field"/>. With a field set the round pipeline fights exactly one battle on the named
/// arena against the GIVEN enemy army (a persistent one, so its losses carry), with the given besieger and offer rules (the campaign's unlock pools), and ends there
/// (<c>Done</c>; the campaign owns what comes next). No comeback muster. Null (the default) is the round pipeline of the fixed games, byte for byte.
/// </summary>
public sealed record FieldScenario(string ArenaId, Side? Besieger, int EnemyRound, IArmy Enemy, OfferRules Offers);

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

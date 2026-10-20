using Doctrine.Battle;

namespace Doctrine;

// THE EPOCH LOOP (overmap slice 1; rules and decisions in Doctrine\todo\overmap-slice1-2026-10-20.md). A campaign is a NEW mode beside the fixed 3/5/8-round games: those
// do not touch this file, and the round pipeline they use is byte for byte what it was (a battle here is the same pipeline with a FieldScenario).
//   epoch = pick a lane (sealed against the enemy's seeded pick, revealed together)
//           same lane            -> ONE battle there through the round pipeline; the winner moves the front one province
//           different lanes      -> both advance unopposed (a race)
//           Concede              -> no push, no battle; if the enemy came to the lane it takes the ground; NO standard is lost
//         then: provinces held pay their yield, the enemy's lane armies muster back towards their schedule.

public enum CampaignOutcome { Running, WarWon, WarLost, WarDrawn, OutOfStandards, EpochsUp }

/// <summary>The player's sealed pick: push <see cref="Lane"/> (0..2), or concede it (no battle is forced, the epoch is a pass).</summary>
public readonly record struct LanePick(int Lane, bool Concede = false);

/// <summary>What the enemy's lane choice may depend on: the seed and the epoch. NOT the player's pick (it is sealed until both are in).</summary>
public readonly record struct LaneTurn(int Seed, int Epoch);

/// <summary>The other side's lane choice. The scripted enemy is <see cref="ScriptedLanes"/>; a later duel hands in a seat's revealed pick the same way.</summary>
public interface ILaneOpponent
{
    ValueTask<int> PickAsync(LaneTurn turn, CancellationToken ct);
}

/// <summary>The solo enemy's lane: a seeded function of (seed, epoch), blind to the player.</summary>
public sealed class ScriptedLanes : ILaneOpponent
{
    public static ScriptedLanes Instance { get; } = new();
    public static int Lane(int seed, int epoch) => (int)(GameRng.Mix(seed, 7000 + epoch) % MapKeys.Lanes);
    public ValueTask<int> PickAsync(LaneTurn turn, CancellationToken ct) => ValueTask.FromResult(Lane(turn.Seed, turn.Epoch));
}

public enum EpochKind { Battle, Race, Conceded, Refused }

public sealed record ProvinceChange(int Key, int Lane, int Index, Holder From, Holder To);

/// <summary>Everything one epoch did. <see cref="Battle"/> is the round record of the fight (null when there was none).</summary>
public sealed record EpochResult(
    int Epoch, EpochKind Kind, int PlayerLane, bool Conceded, int EnemyLane, RoundRecord? Battle, IReadOnlyList<ProvinceChange> Changes,
    int StandardsBefore, int StandardsAfter, bool Restored, int GoldPaid, CampaignOutcome Outcome, int Score, string? Refused = null);

/// <summary>One lane in the briefing: the province a push there would assault, its ground, the army that would defend it, what holding it opens.</summary>
public sealed record LaneBrief(
    int Lane, Province? Target, bool EndsWar, ArenaBrief? Arena, EnemyBrief? Enemy, Feature Unlocks, IReadOnlyList<int> NewKinds, bool OpensAura);

/// <summary>The page's view before the pick. Nothing of the enemy's pick is here: it is chosen blind and revealed with the player's.</summary>
public sealed record CampaignBriefing(
    int Seed, int Epoch, int Standards, int WinsToRestore, int FieldsWon, int Score, Feature HeldUnlocks, IReadOnlyList<int> OfferKinds, bool AuraOpen,
    IReadOnlyList<LaneBrief> Lanes);

public sealed class CampaignGame
{
    readonly DoctrineGame _game;
    readonly CampaignStore _store;
    readonly ILaneOpponent _lanes;
    readonly CampaignProbe _probe;
    readonly IArmy[] _enemies = new IArmy[MapKeys.Lanes];
    Feature _everHeld;                       // only the UnlockKeptAfterLoss probe reads it
    BattleStatus? _lastOutcome; bool _lastWon;   // the general's input (what the last battle was), not a rule

    CampaignGame(DoctrineGame game, int seed, CampaignRules rules, ILaneOpponent lanes, CampaignProbe probe)
    {
        _game = game; Seed = seed; Rules = rules; _lanes = lanes; _probe = probe;
        int arenas = game.Context.Battle.Arenas.Count;
        _store = new CampaignStore(MapGen.Initial(seed, arenas, rules, rules.Endless, probe), probe);
        Army = game.Context.Armies.Create(Side.Player, seed);
    }

    internal static async ValueTask<CampaignGame> CreateAsync(DoctrineGame game, int seed, CampaignRules rules, ILaneOpponent? lanes, CampaignProbe probe, CancellationToken ct)
    {
        var c = new CampaignGame(game, seed, rules, lanes ?? ScriptedLanes.Instance, probe);
        for (int l = 0; l < MapKeys.Lanes; l++) { c._enemies[l] = game.Context.Armies.Create(Side.Enemy, (int)GameRng.Mix(seed, 600 + l)); }
        var provs = c._store.Provinces();
        for (int l = 0; l < MapKeys.Lanes; l++) await c.MusterAsync(l, provs, 0, ct);
        return c;
    }

    public int Seed { get; }
    public CampaignRules Rules { get; }
    /// <summary>The player's army for the whole campaign (the same Pleroma army the fixed games use).</summary>
    public IArmy Army { get; }

    // ---- reads (pure) ----
    public int Epoch => _store.Epoch;
    public int Standards => _store.Standards;
    public int FieldsWon => _store.FieldsWon;
    public IReadOnlyList<Province> Provinces() => _store.Provinces();
    /// <summary>The features the player holds right now.</summary>
    public Feature Unlocks => _store.Unlocks();
    /// <summary>The enemy army that waits in lane <paramref name="lane"/> (the one a battle there is fought against).</summary>
    public ArmyReadModel EnemyArmy(int lane) => _enemies[lane].Read();
    public CampaignOutcome Outcome => OutcomeOf(_store.Provinces());
    public bool Over => Outcome != CampaignOutcome.Running;
    /// <summary>Fields won plus provinces held (capitals not counted).</summary>
    public int Score => ScoreOf(_store.Provinces());

    int ScoreOf(IReadOnlyList<Province> provs) => _store.FieldsWon + provs.Count(p => !p.IsCapital && p.Owner == Holder.Player);

    CampaignOutcome OutcomeOf(IReadOnlyList<Province> provs)
    {
        var pc = provs.First(p => p.Key == MapKeys.PlayerCapital);
        var ec = provs.FirstOrDefault(p => p.Key == MapKeys.EnemyCapital);
        bool lost = pc.Owner == Holder.Enemy, won = ec is { Owner: Holder.Player };
        if (won && lost) return CampaignOutcome.WarDrawn;
        if (won) return CampaignOutcome.WarWon;
        if (lost) return CampaignOutcome.WarLost;
        if (_store.Standards <= 0) return CampaignOutcome.OutOfStandards;
        if (_store.Epoch >= Rules.EpochLimit) return CampaignOutcome.EpochsUp;
        return CampaignOutcome.Running;
    }

    // ---- who may go where ----

    static IEnumerable<Province> Line(IReadOnlyList<Province> provs, int lane) => provs.Where(p => !p.IsCapital && p.Lane == lane).OrderBy(p => p.Index);

    /// <summary>The province a push on <paramref name="lane"/> assaults: the first one from the player's capital outward that the player does not hold; the enemy capital when the lane is all his.</summary>
    internal static Province? PlayerTarget(IReadOnlyList<Province> provs, int lane)
    {
        foreach (var p in Line(provs, lane)) if (p.Owner != Holder.Player) return p;
        var cap = provs.FirstOrDefault(p => p.Key == MapKeys.EnemyCapital);
        return cap is { Owner: not Holder.Player } ? cap : null;
    }

    /// <summary>The province the enemy takes on <paramref name="lane"/>: the first one from its side that it does not hold; the player's capital when the lane is all its own.</summary>
    internal static Province? EnemyTarget(IReadOnlyList<Province> provs, int lane)
    {
        foreach (var p in Line(provs, lane).Reverse()) if (p.Owner != Holder.Enemy) return p;
        var cap = provs.FirstOrDefault(p => p.Key == MapKeys.PlayerCapital);
        return cap is { Owner: not Holder.Enemy } ? cap : null;
    }

    static int DepthOf(Province? target) => target is null || target.IsCapital ? MapKeys.StartLaneLength : target.Index;

    // ---- the one place a battle's terms are decided (the briefing and the battle both call this) ----

    Feature Held()
    {
        var now = _store.Unlocks();
        if (_probe.HasFlag(CampaignProbe.UnlockKeptAfterLoss)) { _everHeld |= now; return _everHeld; }
        return now;
    }

    OfferRules OffersFor(Feature held)
    {
        var b = _game.Rules.Offers;
        if (!b.Enabled) return b;                                    // the offers OFF: nothing is offered, so there is nothing to widen
        var kinds = Unlocks_Kinds(held);
        return b with
        {
            KindPool = (r, k) => kinds.Contains(k) && (b.KindPool?.Invoke(r, k) ?? true),
            OrderPool = (r, t) => UnlockPools.OrderOpen(held, t) && (b.OrderPool?.Invoke(r, t) ?? true),
        };
    }

    static HashSet<int> Unlocks_Kinds(Feature held) => [.. UnlockPools.Kinds(held)];

    FieldScenario ScenarioFor(Province target, int lane, int epoch, Feature held)
    {
        var arenas = _game.Context.Battle.Arenas;
        var arena = arenas[target.ArenaIndex % arenas.Count];
        return new FieldScenario(arena.Id, RoundRules.BesiegerFor(_game.Rules, arena, epoch), epoch * MapKeys.Lanes + lane, _enemies[lane], OffersFor(held));
    }

    // ---- the briefing ----

    /// <summary>
    /// What the page may show before the pick: for each lane the province a push would assault, its arena (and who besieges), the enemy army that waits there (the very army
    /// the battle will use, read from the same object), what holding it opens, and what the offer is made of. Read-only. The enemy's pick is not here and cannot be.
    /// </summary>
    public ValueTask<CampaignBriefing> BriefingAsync(CancellationToken ct = default)
    {
        var provs = _store.Provinces(); int epoch = _store.Epoch; var held = Held();
        var arenas = _game.Context.Battle.Arenas;
        var lanes = new List<LaneBrief>();
        for (int lane = 0; lane < MapKeys.Lanes; lane++)
        {
            var t = PlayerTarget(provs, lane);
            if (t is null) { lanes.Add(new LaneBrief(lane, null, false, null, null, Feature.None, [], false)); continue; }
            var sc = ScenarioFor(t, lane, epoch, held);
            var info = _probe.HasFlag(CampaignProbe.BriefingArenaWrong) ? arenas[(t.ArenaIndex + 1) % arenas.Count] : arenas.First(a => a.Id == sc.ArenaId);
            var read = sc.Enemy.Read();
            long power = read.Cohorts.Sum(c => UnitRules.Rating(c.Count, c.PowerPct, c.Traits));
            var now = UnlockPools.Kinds(held);
            var fresh = UnlockPools.KindsOf(t.Feature).Where(k => !now.Contains(k)).ToList();
            lanes.Add(new LaneBrief(lane, t, t.IsCapital, new ArenaBrief(info, sc.Besieger, _game.Context.LayoutOf(info.Id)),
                new EnemyBrief(Rules.ScheduledGold(DepthOf(t), epoch), read.Soldiers, power, read.Cohorts), t.Feature, fresh,
                t.Feature == Feature.Shrine && !held.HasFlag(Feature.Shrine)));
        }
        int pos = _store.RingPosition;
        return ValueTask.FromResult(new CampaignBriefing(Seed, epoch, _store.Standards, CampaignStore.RestoreEvery - pos, _store.FieldsWon, ScoreOf(provs), held,
            UnlockPools.Kinds(held), held.HasFlag(Feature.Shrine), lanes));
    }

    // ---- the enemy's lane armies ----

    /// <summary>
    /// A lane army musters back: its wealth (purse plus the price of its soldiers) is held up to the scheduled strength of the province it defends, by at most
    /// <see cref="CampaignRules.EnemyHealGold"/> a epoch, then the opponent drafts with the purse. Losses persist; nothing is re-levied.
    /// </summary>
    async ValueTask MusterAsync(int lane, IReadOnlyList<Province> provs, int epoch, CancellationToken ct)
    {
        var a = _enemies[lane];
        var read = a.Read();
        long wealth = read.Gold + read.Cohorts.Sum(c => (long)c.Count * Roster.PriceOf(c.Key));
        long deficit = Roster.ExpectedEnemyWealth(Rules.ScheduledGold(DepthOf(PlayerTarget(provs, lane)), epoch)) - wealth;
        int muster = (int)Math.Clamp(deficit, 0, Rules.EnemyHealGold);
        if (muster > 0) await a.ApplySpoilsAsync(new Spoils(muster, 0), ct);
        int label = epoch * MapKeys.Lanes + lane;
        var picks = await _game.Context.Opponent.DraftAsync(new OpponentRound(Seed, label, a.Read().Gold), ct);
        await a.DraftAsync(new DraftRequest(epoch, Seed, picks), ct);
    }

    // ---- the epoch ----

    EpochResult Refuse(string why) => new(_store.Epoch, EpochKind.Refused, -1, false, -1, null, [], _store.Standards, _store.Standards, false, 0, Outcome, Score, why);

    ProvinceChange Move(Province p, Holder to)
    {
        _store.Transfer(p, to);
        return new ProvinceChange(p.Key, p.Lane, p.Index, p.Owner, to);
    }

    /// <summary>
    /// Play one epoch. The player's pick goes in; the enemy's lane is chosen here, by the opponent, blind to it (it depends on the seed and the epoch only); both are then
    /// revealed in the result. A refusal (the war is over, a lane that does not exist, a push on a lane with nothing left to assault) is data and changes nothing.
    /// The map changes only after the battle has been fought, so a cancelled battle leaves the map untouched (the army has changed, as in CONTRACT.md g).
    /// </summary>
    public async ValueTask<EpochResult> PlayEpochAsync(LanePick pick, CancellationToken ct = default)
    {
        if (Over) return Refuse("the war is over");
        if (pick.Lane is < 0 or >= MapKeys.Lanes) return Refuse("no such lane");
        var provs = _store.Provinces(); int epoch = _store.Epoch;
        var pTarget = PlayerTarget(provs, pick.Lane);
        if (!pick.Concede && pTarget is null) return Refuse("nothing left to assault in that lane");

        int enemyLane = await _lanes.PickAsync(new LaneTurn(Seed, epoch), ct);
        if (_probe.HasFlag(CampaignProbe.EnemyPeeks)) enemyLane = pick.Lane;   // MUTANT: the enemy reads the sealed pick
        enemyLane = ((enemyLane % MapKeys.Lanes) + MapKeys.Lanes) % MapKeys.Lanes;
        var eTarget = EnemyTarget(provs, enemyLane);

        int standardsBefore = _store.Standards;
        var changes = new List<ProvinceChange>();
        RoundRecord? record = null; bool restored = false;
        EpochKind kind;

        if (!pick.Concede && enemyLane == pick.Lane)
        {
            kind = EpochKind.Battle;
            var held = Held();                                          // the unlocks AS THE EPOCH OPENS: a province lost or won in this epoch changes the NEXT offer
            if (_probe.HasFlag(CampaignProbe.EnemyRecreated))
            {
                _enemies[pick.Lane] = _game.Context.Armies.Create(Side.Enemy, (int)GameRng.Mix(Seed, 600 + pick.Lane));
                await MusterAsync(pick.Lane, provs, epoch, ct);
            }
            var scenario = ScenarioFor(pTarget!, pick.Lane, epoch, held);
            var start = new RoundState(epoch, Seed, Army, LastOutcome: _lastOutcome, LastPlayerWon: _lastWon, Field: scenario);
            var run = await _game.Context.Round.RunAsync(start, ct);
            var done = PipelineFailure.DataOrThrow(run, $"the campaign battle of epoch {epoch} (seed {Seed}, lane {pick.Lane})");
            record = done.Records[^1];
            await _enemies[pick.Lane].EndRoundAsync(ct);                // auras wear off, interest on what it holds
            var outcome = record.Result.Outcome;
            _lastOutcome = outcome; _lastWon = outcome == BattleStatus.AttackerWon;
            if (outcome == BattleStatus.AttackerWon)
            {
                changes.Add(Move(pTarget!, Holder.Player));
                restored = _store.WinField();
            }
            else if (outcome == BattleStatus.DefenderWon)
            {
                if (eTarget is not null) changes.Add(Move(eTarget, Holder.Enemy));
                _store.LoseStandard();
            }
            // a Draw moves nothing and costs nothing
        }
        else
        {
            kind = pick.Concede ? EpochKind.Conceded : EpochKind.Race;
            if (!pick.Concede) changes.Add(Move(pTarget!, Holder.Player));
            if (eTarget is not null) changes.Add(Move(eTarget, Holder.Enemy));
            if (pick.Concede && _probe.HasFlag(CampaignProbe.ConcedeCostsStandard)) _store.LoseStandard();
            await Army.EndRoundAsync(ct);                               // a battle's close does this inside the pipeline
        }

        // endless: a lane the player has cleared refills with a fresh enemy province (the map scrolls)
        if (Rules.Endless)
        {
            var now = _store.Provinces();
            for (int l = 0; l < MapKeys.Lanes; l++)
            {
                var line = Line(now, l).ToList();
                if (line.Count > 0 && line.Count < MapKeys.LaneSlots && line.All(p => p.Owner == Holder.Player))
                    _store.Append(MapGen.Extra(Seed, l, line.Count, _game.Context.Battle.Arenas.Count, Rules));
            }
        }

        _store.EndEpoch();
        var after = _store.Provinces();
        var outcomeNow = OutcomeOf(after);
        int paid = 0;
        if (outcomeNow == CampaignOutcome.Running)
        {
            paid = _store.Gold();                                        // the yield of what is held at epoch close, the Total of the player's store
            if (paid > 0) await Army.ApplySpoilsAsync(new Spoils(paid, 0), ct);
            for (int l = 0; l < MapKeys.Lanes; l++) await MusterAsync(l, after, _store.Epoch, ct);
        }
        return new EpochResult(epoch, kind, pick.Lane, pick.Concede, enemyLane, record, changes, standardsBefore, _store.Standards, restored, paid, outcomeNow, ScoreOf(after));
    }
}

public sealed partial class DoctrineGame
{
    /// <summary>
    /// A new campaign from <paramref name="seed"/>: a seeded map of two capitals and three lanes of three provinces, a persistent enemy army in every lane, a
    /// player's army and three standards. The game's rules supply the offers, the ticks and the sieges; the campaign supplies the ground, the enemy and the pools.
    /// The fixed round games are untouched.
    /// </summary>
    public ValueTask<CampaignGame> NewCampaignAsync(int seed, CampaignRules? rules = null, ILaneOpponent? lanes = null, CancellationToken ct = default)
        => CampaignGame.CreateAsync(this, seed, rules ?? new CampaignRules(), lanes, CampaignProbe.None, ct);

    internal ValueTask<CampaignGame> NewCampaignAsync(int seed, CampaignRules rules, ILaneOpponent? lanes, CampaignProbe probe, CancellationToken ct = default)
        => CampaignGame.CreateAsync(this, seed, rules, lanes, probe, ct);
}

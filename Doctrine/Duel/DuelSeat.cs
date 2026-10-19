namespace Doctrine;

/// <summary>What a seat did in one round and what came of it. The first block is filled when the seat seals, the last two when the field is settled.</summary>
/// <param name="Units">The pack offer this seat was given (scaled to ITS purse at the start of the round).</param>
/// <param name="Orders">The order offer as it stood when the seat chose (after <paramref name="Rerolls"/> paid rerolls, from the legal list its army held then).</param>
/// <param name="Doctrine">The tokens that stood after the game's binding (what the battle was given); the raw choice is the revealed <see cref="DuelPicks"/>.</param>
/// <param name="IllegalDropped">Tokens (and a formation) the game removed: not in the vocabulary, or a field or trigger order the army could not give.</param>
/// <param name="OfferRefused">Packs and tokens that were not on offer (or picked twice), and rerolls the purse could not pay or the rules did not allow.</param>
/// <param name="Cohorts">The army that fought: what the orders left (kind, soldiers, traits, rank, veterancy, power).</param>
/// <param name="DigestAfterOrders">The digest of the army read after the packs, the formation and the orders: the figure a seat checks against ITS OWN local build.</param>
/// <param name="DigestAtClose">The digest after the battle's losses and the spoils, before the round ends (the figure <see cref="RoundRecord.PlayerDigest"/> is).</param>
public sealed record DuelSeatRound(
    Seat Seat, UnitOffer Units, OrderOffer Orders, int Rerolls, DoctrineSet Doctrine, int IllegalDropped, int OfferRefused,
    ArmyOrdersReport OrdersReport, int GoldAfterOrders, IReadOnlyList<CohortRead> Cohorts, ulong DigestAfterOrders,
    Spoils Spoils, ulong DigestAtClose);

/// <summary>A seat's check of the authority's word about ITS OWN army against the army it built locally (see <see cref="DuelSeat.ApplyResultAsync"/>).</summary>
/// <param name="AfterOrdersMatches">The authority's digest of this seat's army after the orders equals the local one: the authority fought the army this seat made.</param>
/// <param name="SpoilsMatchRules">The spoils the authority paid are what the rules pay for that outcome and those kills.</param>
/// <param name="AtCloseMatches">After the authority's losses and spoils, the local army digests to the authority's closing figure.</param>
public sealed record SeatCheck(bool AfterOrdersMatches, bool SpoilsMatchRules, bool AtCloseMatches, ulong LocalAfterOrders, ulong LocalAtClose)
{
    public bool Ok => AfterOrdersMatches && SpoilsMatchRules && AtCloseMatches;
}

/// <summary>
/// ONE seat's army and the rules of its round. The authority holds two (the whole duel), a client holds one (its own, built locally from the same seed). Both run THIS code, so a
/// client can say whether the authority fought the army it made. A round runs in this order, and a step out of order throws:
/// <c>UnitOffer</c> (read) -> <see cref="DraftAsync"/> -> <see cref="FormAsync"/> (optional) -> <see cref="RerollAsync"/> (0..n) -> <see cref="SealAsync"/> -> the battle (elsewhere)
/// -> <see cref="ApplyResultAsync"/>. <see cref="ApplyPicksAsync"/> does the first five from a revealed <see cref="DuelPicks"/> in one call.
/// </summary>
public sealed class DuelSeat
{
    enum Phase { Open, Drafted, Sealed }

    readonly IArmyService _armies;
    readonly IOfferSource _offers;
    readonly DoctrineDomain _rules;
    readonly DuelProbe _probe;
    Phase _phase = Phase.Open;
    UnitOffer? _units;
    OrderOffer? _orders;
    List<DraftPick> _asked = [];
    int _formation, _rerolls, _refused, _dropped;
    DuelSeatRound? _sealed;

    internal DuelSeat(IArmyService armies, IOfferSource offers, DoctrineDomain rules, int seed, Seat seat, DuelProbe probe = DuelProbe.None, int round = 0)
    {
        _armies = armies; _offers = offers; _rules = rules; _probe = probe;
        Seed = seed; Seat = seat; Round = round;
        // A MIRROR: both seats are raised from the same (side, seed), so they start with the same levy and gold. The side a seat fights as is named by the battle setup, not by the army.
        Army = armies.Create(Side.Player, seed);
    }

    public Seat Seat { get; }
    public int Seed { get; }
    /// <summary>The round being played (0-based). It moves on when <see cref="ApplyResultAsync"/> has settled the field.</summary>
    public int Round { get; private set; }
    public IArmy Army { get; }
    public ArmyReadModel Read() => Army.Read();

    /// <summary>The seed the offers of this seat are drawn from: the game seed for both seats (the stream is shared; a seat's own purse and legal list are what differ).</summary>
    int OfferSeed => _probe.HasFlag(DuelProbe.OffersDifferPerSeat) && Seat == Seat.B ? Seed + 1 : Seed;

    /// <summary>This round's pack offer, scaled to this seat's purse as the round opens (the same function, stream and rules as the single-player game).</summary>
    public UnitOffer UnitOffer => _units ??= _rules.Offers.Enabled ? _offers.Units(OfferSeed, Round, Army.Read().Gold, _rules.Offers) : UnitOffer.Open(Army.Read().Gold);

    /// <summary>The order offer as it stands: null until the packs are bought. Redrawn after a formation or a reroll (it is drawn from the legal list the army holds now).</summary>
    public OrderOffer? OrderOffer => _orders;

    /// <summary>Rerolls still allowed this round (0 when the offers are off).</summary>
    public int RerollsLeft => _rules.Offers.Enabled ? Math.Max(0, _rules.Offers.MaxRerolls - _rerolls) : 0;

    OrderOffer DrawOrders() => _rules.Offers.Enabled ? _offers.Orders(OfferSeed, Round, _rerolls, Army.Read().Legal, _rules.Offers) : Doctrine.OrderOffer.Open();

    void Need(Phase p, string what) { if (_phase != p) throw new InvalidOperationException($"{what} is not allowed now: the seat is {_phase.ToString().ToLowerInvariant()} in round {Round}"); }

    /// <summary>Step 1: buy packs. A pick that is not exactly an offered pack, or a pack bought twice, is refused with a reason and counted; the army still judges the gold of the rest.</summary>
    public async ValueTask<DraftReport> DraftAsync(IReadOnlyList<DraftPick> packs, CancellationToken ct = default)
    {
        Need(Phase.Open, "the draft");
        var offer = UnitOffer;
        // The packs are applied in the canonical order of DuelPicks (kind, then count), the order the authority receives them in: two packs of one kind with too little gold left to buy both
        // are judged first-come, so a listing order of the client's own would build a different army than the one it reveals.
        _asked = packs.OrderBy(p => p.Kind).ThenBy(p => p.Count).ToList();
        var (bound, notOffered) = RoundLaws.BindDraft(offer, _asked, _rules.Offers);
        var report = await Army.DraftAsync(new DraftRequest(Round, Seed, bound), ct);
        if (notOffered.Count > 0) report = report with { Refused = report.Refused.Concat(notOffered).ToList() };
        _refused += notOffered.Count;
        _phase = Phase.Drafted;
        _orders = DrawOrders();
        return report;
    }

    /// <summary>Step 2 (optional, once): the free turn of the ranks. 0 leaves them; 1 or 2 is the army's own Rotate; anything else is refused and counted. The order offer is redrawn.</summary>
    public async ValueTask FormAsync(int formation, CancellationToken ct = default)
    {
        Need(Phase.Drafted, "the formation");
        if (_formation != 0 || _rerolls != 0) throw new InvalidOperationException("the formation is turned once, before any reroll");
        if (!await RoundLaws.RotateAsync(Army, formation, ct)) { _dropped++; _formation = 0; }
        else _formation = formation;
        _orders = DrawOrders();
    }

    /// <summary>Step 3 (0..<see cref="OfferRules.MaxRerolls"/> times): pay the reroll fee and redraw the order offer from the reroll's own stream. False (and nothing changed but a count) when the rules or the purse refuse.</summary>
    public async ValueTask<bool> RerollAsync(CancellationToken ct = default)
    {
        Need(Phase.Drafted, "a reroll");
        if (!_rules.Offers.Enabled || _rerolls >= _rules.Offers.MaxRerolls) { _refused++; return false; }
        if (_rules.Offers.RerollCost > 0 && !await Army.PayAsync(_rules.Offers.RerollCost, ct)) { _refused++; return false; }
        _rerolls++;
        _orders = DrawOrders();
        return true;
    }

    /// <summary>
    /// Step 4: choose and give the orders. <paramref name="choice"/> is the raw doctrine (up to four tokens): the game removes what is not on offer, picked twice, or not allowed (counted),
    /// then the army applies the army orders in slot order. Returns the picks as they must be committed: the packs asked for, the formation, the rerolls paid, this choice.
    /// </summary>
    public async ValueTask<DuelPicks> SealAsync(DoctrineSet choice, CancellationToken ct = default)
    {
        Need(Phase.Drafted, "the seal");
        var legal = Army.Read().Legal;                                   // the start mask: after the packs, the formation and the paid rerolls
        int dropped = _dropped, refused = _refused;
        var clean = RoundLaws.BindChoice(choice, legal, _orders!, _rules.Offers, ref dropped, ref refused);
        var report = await Army.ApplyArmyOrdersAsync(clean, ct);
        var read = Army.Read();
        _sealed = new DuelSeatRound(Seat, UnitOffer, _orders!, _rerolls, clean, dropped, refused, report, read.Gold, read.Cohorts, read.Digest(), new Spoils(0, 0), 0);
        _phase = Phase.Sealed;
        return new DuelPicks(_asked, choice, _formation, _rerolls);
    }

    /// <summary>The whole pre-battle half from a revealed <see cref="DuelPicks"/>: draft, formation, rerolls, seal. What the authority runs for each seat; a client's own build runs the same steps.</summary>
    public async ValueTask<DuelSeatRound> ApplyPicksAsync(DuelPicks picks, CancellationToken ct = default)
    {
        await DraftAsync(picks.Units, ct);
        if (picks.Formation != 0) await FormAsync(picks.Formation, ct);
        for (int i = 0; i < picks.Rerolls; i++) if (!await RerollAsync(ct)) break;      // the first refused reroll ends the asking, as in the single-player game
        await SealAsync(picks.Doctrine, ct);
        return _sealed!;
    }

    /// <summary>What the seat sealed (null before <see cref="SealAsync"/> or <see cref="ApplyPicksAsync"/>).</summary>
    public DuelSeatRound? Sealed => _sealed;

    /// <summary>Settle the field with the battle's losses for this seat and the spoils, return the closing digest. Used by the authority (its own figures) and by <see cref="ApplyResultAsync"/>.</summary>
    internal async ValueTask<ulong> SettleAsync(IReadOnlyList<CohortLoss> losses, Spoils spoils, CancellationToken ct)
    {
        Need(Phase.Sealed, "the settlement");
        await Army.ApplyLossesAsync(losses, ct);
        await Army.ApplySpoilsAsync(spoils, ct);
        return Army.Read().Digest();
    }

    /// <summary>Close the round: the army's clock moves on, the next round opens.</summary>
    internal async ValueTask EndRoundAsync(CancellationToken ct)
    {
        await Army.EndRoundAsync(ct);
        Round++; _phase = Phase.Open; _units = null; _orders = null; _asked = []; _formation = _rerolls = _refused = _dropped = 0; _sealed = null;
    }

    /// <summary>
    /// A CLIENT's step after the authority's result: apply the losses and spoils the result names for THIS seat, compare the digest of this seat's army after the orders and at the close
    /// with the authority's, check the spoils against the rules, and open the next round. The outcome itself is the authority's word; this is how a seat knows the authority fought the army it made.
    /// </summary>
    public async ValueTask<SeatCheck> ApplyResultAsync(DuelRoundResult result, CancellationToken ct = default)
    {
        Need(Phase.Sealed, "the result");
        if (result.Round != Round) throw new ArgumentException($"the result is for round {result.Round}, the seat is in round {Round}", nameof(result));
        var mine = result.For(Seat);
        var res = result.Replay.Result;
        var losses = Seat == Seat.A ? res.AttackerLosses : res.DefenderLosses;
        var theirLosses = Seat == Seat.A ? res.DefenderLosses : res.AttackerLosses;
        bool won = Seat == Seat.A ? res.Outcome == BattleStatus.AttackerWon : res.Outcome == BattleStatus.DefenderWon;
        bool afterOrders = _sealed!.DigestAfterOrders == mine.DigestAfterOrders;
        bool spoilsOk = mine.Spoils == RoundLaws.BaseSpoils(_rules, won, theirLosses.Sum(l => l.Lost));
        ulong close = await SettleAsync(losses, mine.Spoils, ct);
        var check = new SeatCheck(afterOrders, spoilsOk, close == mine.DigestAtClose, _sealed.DigestAfterOrders, close);
        await EndRoundAsync(ct);
        return check;
    }
}

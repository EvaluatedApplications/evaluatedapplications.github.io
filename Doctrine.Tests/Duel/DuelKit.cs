namespace Doctrine.Tests;

/// <summary>Shared builders and the honest-client driver for the duel tests: two clients, one authority, commit and reveal as bytes, the result as bytes.</summary>
static class DuelKit
{
    public static readonly string[] Armies = ["stub", "pleroma"];

    public static DoctrineGame Game(string army, string battle, int rounds, Func<IBattleEngine, IBattleEngine>? wrap = null, OfferRules? offers = null)
    {
        wrap ??= e => e;
        IArmyService armies = army == "pleroma" ? new PleromaArmyService() : new StubArmyService();
        IBattleEngine engine = wrap(battle == "tracer" ? TracerWorlds.Engine() : new StubBattleEngine());
        return DoctrineBuilder.New().WithArmy(armies).WithBattle(engine)
            .WithRules(new DoctrineDomain { MaxRounds = rounds, Offers = offers ?? OfferRules.Default }).Build();
    }

    public static DuelSeat SeatAt(DoctrineGame game, int seed, Seat seat, int round) => new(game.Context.Armies, game.Context.Offers, game.Rules, seed, seat, DuelProbe.None, round);

    public static bool SamePacks(UnitOffer a, UnitOffer b) => a.Active == b.Active && a.Budget == b.Budget && a.Packs.SequenceEqual(b.Packs);
    public static bool SameOrders(OrderOffer a, OrderOffer b) => a.Active == b.Active && a.Tokens.SequenceEqual(b.Tokens) && a.Rerolls == b.Rerolls && a.RerollsLeft == b.RerollsLeft && a.RerollCost == b.RerollCost;

    /// <summary>A seat-neutral plan: up to two affordable offered packs, maybe a reroll, up to three offered tokens; all random choices from <paramref name="rng"/>. Leaves the seat sealed and returns what it would commit.</summary>
    public static async Task<DuelPicks> Plan(DuelSeat s, GameRng rng, bool allowReroll = true)
    {
        var offer = s.UnitOffer; var packs = new List<DraftPick>(); int gold = s.Read().Gold;
        for (int i = 0; i < 2; i++)
        {
            var can = offer.Packs.Where(p => p.Cost <= gold && !packs.Contains(UnitOffer.Pick(p))).ToList();
            if (can.Count == 0) break;
            var pack = can[rng.Next(can.Count)]; packs.Add(UnitOffer.Pick(pack)); gold -= pack.Cost;
        }
        await s.DraftAsync(packs);
        if (allowReroll && s.RerollsLeft > 0 && rng.Next(3) == 0) await s.RerollAsync();
        var pool = s.OrderOffer!.Tokens.ToList(); var tokens = new List<int>();
        for (int i = 0; i < 3 && pool.Count > 0; i++) { int t = pool[rng.Next(pool.Count)]; tokens.Add(t); pool.Remove(t); }
        return await s.SealAsync(DoctrineSet.Of(tokens.ToArray()));
    }

    public sealed record Played(DuelRoundResult Result, DuelRoundResult Wire, SeatCheck CheckA, SeatCheck CheckB, UnitOffer ClientUnitsA, UnitOffer ClientUnitsB, OrderOffer ClientOrdersA, OrderOffer ClientOrdersB, DuelPicks PicksA, DuelPicks PicksB);

    /// <summary>
    /// An honest duel in progress: the clients plan and seal locally, commit, reveal (verified, parsed), the authority fights, the result crosses as bytes, each client checks its own army.
    /// <paramref name="mirrored"/>: both clients draw their choices from the same stream (identical picks while their armies are equal).
    /// </summary>
    public sealed class Session
    {
        public DuelGame Duel { get; }
        public DuelSeat A { get; }
        public DuelSeat B { get; }
        readonly GameRng _rngA, _rngB;
        readonly Random _nonces;

        public Session(DoctrineGame game, int seed, bool mirrored, DuelProbe probe, int startRound, bool swapped = false)
        {
            Duel = game.NewDuel(seed, probe, startRound);
            A = SeatAt(game, seed, Seat.A, startRound); B = SeatAt(game, seed, Seat.B, startRound);
            _rngA = new GameRng(GameRng.Mix(seed, swapped ? 32 : 31)); _rngB = new GameRng(GameRng.Mix(seed, mirrored || swapped ? 31 : 32));
            _nonces = new Random(seed);
        }

        public async Task<Played> NextAsync()
        {
            int r = Duel.Round;
            var ua = A.UnitOffer; var ub = B.UnitOffer;
            var pa = await Plan(A, _rngA); var pb = await Plan(B, _rngB);
            var oa = A.OrderOffer!; var ob = B.OrderOffer!;
            foreach (var (seat, picks) in new[] { (Seat.A, pa), (Seat.B, pb) })
            {
                var nonce = new byte[16]; _nonces.NextBytes(nonce);
                var canon = picks.Canonical();
                var commit = DuelPicks.Commit(canon, nonce, r, seat);
                DuelPicks? parsed = null; string why = "the commitment did not verify";
                if (!DuelPicks.Verify(commit, canon, nonce, r, seat) || !DuelPicks.TryParse(canon, out parsed, out why)) throw new InvalidOperationException("an honest reveal failed: " + why);
                if (!Duel.HandIn(seat, r, parsed!)) throw new InvalidOperationException("hand-in refused");
            }
            var result = await Duel.PlayRoundAsync();
            if (!DuelRoundResult.TryDecode(result.Encode(), out var wire, out var err)) throw new InvalidOperationException("the result did not decode: " + err);
            var checkA = await A.ApplyResultAsync(wire!); var checkB = await B.ApplyResultAsync(wire!);
            return new Played(result, wire!, checkA, checkB, ua, ub, oa, ob, pa, pb);
        }
    }

    public static async Task<List<Played>> Honest(DoctrineGame game, int seed, bool mirrored = false, DuelProbe probe = DuelProbe.None, int startRound = 0, int? only = null)
    {
        var s = new Session(game, seed, mirrored, probe, startRound);
        var played = new List<Played>();
        while (!s.Duel.Finished && (only is null || played.Count < only)) played.Add(await s.NextAsync());
        return played;
    }
}
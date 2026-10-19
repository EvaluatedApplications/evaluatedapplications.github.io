namespace Doctrine.Tests;

// The offers end to end through the real pipeline (CONTRACT.md "The offers"), and every mutant of the offers watched red.

public class OfferSourceContractTests
{
    static void Caught(IReadOnlyList<ContractFailure> failures, string expectedCase)
        => Assert.True(failures.Any(f => f.Case == expectedCase), $"expected '{expectedCase}' to fail; failures were: [{string.Join(" | ", failures)}]");

    [Fact]
    public async Task The_seeded_offers_hold_the_whole_contract()
        => Assert.Empty(await ContractRunner.RunAllAsync(OfferContract.Cases, OfferContract.Real));

    const string Det = "orders-are-a-function-of-seed-round-reroll-and-legal-list", Sorted = "orders-are-distinct-legal-sorted-and-at-most-eight",
                 Guar = "orders-guarantee-three-field-orders-on-two-lines-and-an-army-order", Pool = "orders-draw-only-from-the-hosts-pool",
                 UDet = "units-are-a-function-of-seed-round-and-budget", Packs = "units-are-affordable-multiples-of-a-base-pack-scaled-to-the-budget",
                 Grow = "units-grow-with-the-budget";

    [Theory]
    [InlineData(OfferMut.IgnoresSeed, Det)]
    [InlineData(OfferMut.IgnoresSeed, UDet)]
    [InlineData(OfferMut.IgnoresRound, Det)]
    [InlineData(OfferMut.IgnoresRound, UDet)]
    [InlineData(OfferMut.IgnoresReroll, Det)]
    [InlineData(OfferMut.IllegalToken, Sorted)]
    [InlineData(OfferMut.DuplicateToken, Sorted)]
    [InlineData(OfferMut.NinthToken, Sorted)]
    [InlineData(OfferMut.GuaranteeDropped, Guar)]
    [InlineData(OfferMut.PoolIgnored, Pool)]
    [InlineData(OfferMut.WrongCost, Packs)]
    [InlineData(OfferMut.OddCount, Packs)]
    [InlineData(OfferMut.OverBudgetPack, Packs)]
    [InlineData(OfferMut.KindTwice, Packs)]
    [InlineData(OfferMut.BudgetIgnored, Packs)]
    [InlineData(OfferMut.BudgetIgnored, Grow)]
    [InlineData(OfferMut.NeverMorePacksThanTwo, Packs)]
    public async Task An_offer_mutant_is_caught(OfferMut m, string expected)
        => Caught(await ContractRunner.RunAllAsync(OfferContract.Cases, () => new OfferSut(new MutantOfferSource(SeededOffers.Instance, m), OfferRules.Default)), expected);

    [Fact]
    public async Task A_guarantee_dropped_by_the_rules_alone_is_caught_too()
        => Caught(await ContractRunner.RunAllAsync(OfferContract.Cases, () => new OfferSut(SeededOffers.Instance, OfferRules.Default with { Guarantees = false })), Guar);

    [Fact]
    public async Task Each_guarantee_is_needed_on_its_own()
    {
        // drop one guarantee at a time: the same case goes red for the right reason
        foreach (var rules in new[] { OfferRules.Default with { MinFieldOrders = 0, MinFieldLines = 0 }, OfferRules.Default with { MinArmyOrders = 0 }, OfferRules.Default with { MinFieldLines = 1 } })
            Caught(await ContractRunner.RunAllAsync(OfferContract.Cases, () => new OfferSut(SeededOffers.Instance, rules)), Guar);
    }
}

public class OfferPipelineTests
{
    public static TheoryData<string> Armies => new("stub", "pleroma");
    static IArmyService Army(string which, ArmyMut? m = null)
    {
        IArmyService a = which == "stub" ? new StubArmyService() : new PleromaArmyService();
        return m is null ? a : new MutantArmyService(a, m.Value);
    }

    /// <summary>A player with every hand a delegate; the free-choice methods are not used by the offered game.</summary>
    sealed class Hands : IPlayerInput
    {
        public Func<ArmyReadModel, UnitOffer, IReadOnlyList<DraftPick>>? Draft;
        public Func<ArmyReadModel, OrderOffer, DoctrineSet>? Choose;
        public Func<ArmyReadModel, OrderOffer, bool>? Reroll;
        public List<DraftReport> Reports { get; } = [];
        public List<(ArmyReadModel Army, OrderOffer Offer)> Rerolls { get; } = [];
        public List<(ArmyReadModel Army, GeneralOutput Ghost, OrderOffer Offer)> Chosen { get; } = [];
        public List<(ArmyReadModel Army, UnitOffer Offer)> Drafted { get; } = [];
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<DraftPick>>([]);
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct)
        { Drafted.Add((army, offer)); return ValueTask.FromResult(Draft?.Invoke(army, offer) ?? []); }
        public ValueTask DraftedAsync(DraftReport report, CancellationToken ct) { Reports.Add(report); return ValueTask.CompletedTask; }
        public ValueTask<bool> RerollAsync(ArmyReadModel army, OrderOffer offer, int round, CancellationToken ct)
        { Rerolls.Add((army, offer)); return ValueTask.FromResult(Reroll?.Invoke(army, offer) ?? false); }
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => ValueTask.FromResult(new PlayerChoice(DoctrineSet.Empty, true));
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
        { Chosen.Add((army, ghost, offer)); return ValueTask.FromResult(new PlayerChoice(Choose?.Invoke(army, offer) ?? DoctrineSet.Empty, true)); }
    }

    static Task<GameResult> One(IArmyService army, Hands h, OfferRules rules, int seed = 7, IGeneral? general = null, int rounds = 1)
    {
        var b = DoctrineBuilder.New().WithArmy(army).WithPlayer(h).WithRules(new DoctrineDomain { MaxRounds = rounds, Offers = rules });
        if (general is not null) b.WithGeneral(general);
        return b.Build().PlayAsync(seed).AsTask();
    }

    // ---- the draft is bound by the offer ----

    static async Task<string?> DraftBinding(string which, OfferRules rules)
    {
        var h = new Hands();
        h.Draft = (army, offer) =>
        {
            var p = offer.Packs[0];
            // the offered pack, the same pack again, a pack of the right kind with one more soldier, and a free-for-all pick
            return [UnitOffer.Pick(p), UnitOffer.Pick(p), new DraftPick(p.Kind, p.Count + 1), new DraftPick(offer.Packs[1].Kind, offer.Packs[1].Count + 100)];
        };
        var r = await One(Army(which), h, rules);
        var rep = h.Reports.Single(); var offered = h.Drafted.Single().Offer;
        var first = offered.Packs[0];
        if (rep.Accepted.Count != 1 || rep.Accepted[0] != UnitOffer.Pick(first)) return $"accepted {string.Join(",", rep.Accepted)}: only the offered pack should be bought, once";
        if (rep.GoldSpent != first.Cost) return $"spent {rep.GoldSpent}, the pack costs {first.Cost}";
        if (rep.Refused.Count(x => x.Reason == DraftStageReason) != 3) return $"refused {string.Join(" | ", rep.Refused.Select(x => $"{x.Pick}:{x.Reason}"))}: three picks are not on offer";
        if (r.Rounds[0].OfferRefused != 3) return $"the record counted {r.Rounds[0].OfferRefused} refusals, not 3";
        return null;
    }
    const string DraftStageReason = "not on offer";

    [Theory, MemberData(nameof(Armies))]
    public async Task A_draft_pick_outside_the_offer_is_refused_with_a_reason_and_counted(string which)
        => Assert.Null(await DraftBinding(which, OfferRules.Default));

    [Theory, MemberData(nameof(Armies))]
    public async Task A_pipeline_that_lets_any_pick_through_is_caught(string which)
        => Assert.NotNull(await DraftBinding(which, OfferRules.Default with { Enforce = false }));

    [Theory, MemberData(nameof(Armies))]
    public async Task Any_subset_of_the_offered_packs_may_be_bought_and_the_army_still_judges_the_gold(string which)
    {
        var h = new Hands { Draft = (_, offer) => offer.Packs.Select(UnitOffer.Pick).ToList() };       // ALL six packs: more than the purse can pay
        var r = await One(Army(which), h, OfferRules.Default);
        var offer = h.Drafted.Single().Offer; var rep = h.Reports.Single();
        Assert.True(offer.Packs.Sum(p => p.Cost) > offer.Budget, "setup: six packs cost more than the purse, or this case says nothing");
        Assert.NotEmpty(rep.Refused);
        Assert.All(rep.Refused, x => Assert.Equal("gold", x.Reason));                // the army's own verdict, not the offer's
        Assert.Equal(0, r.Rounds[0].OfferRefused);                                   // an offered pack that cannot be paid is not an offer violation
        Assert.True(rep.GoldSpent <= offer.Budget);
        Assert.NotEmpty(rep.Accepted);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task The_unit_offer_is_scaled_to_the_purse_the_round_opens_with_and_is_recorded(string which)
    {
        var h = new Hands();
        var r = await One(Army(which), h, OfferRules.Default, rounds: 3);
        for (int i = 0; i < 3; i++)
        {
            var (army, offer) = h.Drafted[i];
            Assert.Equal(army.Gold, offer.Budget);
            Assert.True(SeededOffers.Instance.Units(7, i, army.Gold, OfferRules.Default).Packs.SequenceEqual(offer.Packs));
            Assert.Same(offer, r.Rounds[i].UnitOffer);
        }
    }

    // ---- the doctrine is bound by the offer ----

    static async Task<string?> DoctrineBinding(string which, OfferRules rules)
    {
        var h = new Hands();
        h.Choose = (army, offer) =>
        {
            int outside = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => army.Legal.Allows(t) && !offer.Tokens.Contains(t));   // legal, and not offered
            return DoctrineSet.Of(offer.Tokens[0], outside, offer.Tokens[0], offer.Tokens[1]);
        };
        var r = await One(Army(which), h, rules);
        var offered = h.Chosen.Single().Offer.Tokens;
        var rec = r.Rounds[0];
        if (rec.PlayerDoctrine != DoctrineSet.Of(offered[0], 0, 0, offered[1])) return $"doctrine {rec.PlayerDoctrine}: only the offered tokens, each once, may stay";
        if (rec.OfferRefused != 2) return $"the record counted {rec.OfferRefused} refusals, not 2";
        if (rec.IllegalDropped != 0) return $"the illegal-token counter moved ({rec.IllegalDropped}): an unoffered token is the offer's refusal, not an illegal one";
        return null;
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_token_outside_the_offer_or_picked_twice_is_removed_and_counted(string which)
        => Assert.Null(await DoctrineBinding(which, OfferRules.Default));

    [Theory, MemberData(nameof(Armies))]
    public async Task A_pipeline_that_lets_any_token_through_is_caught(string which)
        => Assert.NotNull(await DoctrineBinding(which, OfferRules.Default with { Enforce = false }));

    [Theory, MemberData(nameof(Armies))]
    public async Task Up_to_four_offered_tokens_are_kept_and_an_empty_pick_is_allowed(string which)
    {
        var four = new Hands { Choose = (_, o) => DoctrineSet.Of(o.Tokens.Take(4).ToArray()) };
        var r = await One(Army(which), four, OfferRules.Default);
        Assert.Equal(4, r.Rounds[0].PlayerDoctrine.Used); Assert.Equal(0, r.Rounds[0].OfferRefused);
        var none = new Hands();
        r = await One(Army(which), none, OfferRules.Default);
        Assert.Equal(0, r.Rounds[0].PlayerDoctrine.Used); Assert.Equal(0, r.Rounds[0].OfferRefused);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task The_order_offer_is_drawn_from_the_legal_list_the_draft_left_and_is_recorded(string which)
    {
        var h = new Hands { Draft = (_, offer) => [UnitOffer.Pick(offer.Packs[0])] };
        var r = await One(Army(which), h, OfferRules.Default, rounds: 3);
        for (int i = 0; i < 3; i++)
        {
            var (army, ghost, offer) = h.Chosen[i];
            Assert.True(offer.Tokens.All(army.Legal.Allows), "every offered token is legal at the moment of the choice");
            Assert.True(SeededOffers.Instance.Orders(7, i, 0, army.Legal, OfferRules.Default).Tokens.SequenceEqual(offer.Tokens));
            Assert.Same(offer, r.Rounds[i].OrderOffer);
            Assert.Equal(0, r.Rounds[i].Rerolls);
        }
    }

    // ---- rerolls ----

    sealed record RerollRun(GameResult Game, Hands Hands, int GoldBefore, int GoldAtChoice);

    static async Task<RerollRun> Rerolling(IArmyService army, OfferRules rules, bool drain)
    {
        var h = new Hands { Reroll = (_, _) => true };
        if (drain) h.Draft = (a, offer) => { var left = a.Gold; var l = new List<DraftPick>(); foreach (var p in offer.Packs.OrderBy(p => p.Cost)) if (p.Cost <= left) { l.Add(UnitOffer.Pick(p)); left -= p.Cost; } return l; };
        var r = await One(army, h, rules);
        return new RerollRun(r, h, h.Drafted[0].Army.Gold, h.Chosen[0].Army.Gold);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_reroll_costs_gold_draws_from_its_own_stream_and_stops_at_the_limit(string which)
    {
        var run = await Rerolling(Army(which), OfferRules.Default, drain: false);
        var rec = run.Game.Rounds[0];
        Assert.Equal(2, rec.Rerolls);                                               // MaxRerolls, though the player asked again
        Assert.Equal(run.GoldBefore - 2 * OfferRules.Default.RerollCost, run.GoldAtChoice);
        Assert.Equal(0, rec.OfferRefused);
        Assert.Equal(2, rec.OrderOffer!.Rerolls); Assert.Equal(0, rec.OrderOffer.RerollsLeft); Assert.Equal(OfferRules.Default.RerollCost, rec.OrderOffer.RerollCost);
        Assert.Equal(2, run.Hands.Rerolls.Count);                                   // asked with the first offer and with the first reroll, never a third time
        foreach (var (army, offer) in run.Hands.Rerolls)
            Assert.True(SeededOffers.Instance.Orders(7, 0, offer.Rerolls, army.Legal, OfferRules.Default).Tokens.SequenceEqual(offer.Tokens), $"offer {offer.Rerolls} is not the reroll's own draw");
        Assert.False(run.Hands.Rerolls[0].Offer.Tokens.SequenceEqual(rec.OrderOffer.Tokens), "a reroll changed nothing");
        Assert.True(SeededOffers.Instance.Orders(7, 0, 2, run.Hands.Chosen[0].Army.Legal, OfferRules.Default).Tokens.SequenceEqual(rec.OrderOffer.Tokens));
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_reroll_the_purse_cannot_pay_is_refused_counted_and_changes_nothing(string which)
    {
        var rules = OfferRules.Default with { RerollCost = 5 };
        var run = await Rerolling(Army(which), rules, drain: true);
        var rec = run.Game.Rounds[0];
        Assert.True(run.Hands.Chosen[0].Army.Gold < 5, "setup: the drain left less than a reroll");
        Assert.Equal(0, rec.Rerolls);
        Assert.Equal(1, rec.OfferRefused);
        Assert.Equal(run.Hands.Chosen[0].Army.Gold, run.Hands.Rerolls[0].Army.Gold);       // nothing was taken
        Assert.True(SeededOffers.Instance.Orders(7, 0, 0, run.Hands.Chosen[0].Army.Legal, rules).Tokens.SequenceEqual(rec.OrderOffer!.Tokens), "the first offer stands");
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_free_reroll_is_caught(string which)
    {
        var run = await Rerolling(Army(which, ArmyMut.PayIsFree), OfferRules.Default, drain: false);
        Assert.NotEqual(run.GoldBefore - 2 * OfferRules.Default.RerollCost, run.GoldAtChoice);     // the case above goes red: the gold did not leave
        run = await Rerolling(Army(which, ArmyMut.PayAlwaysYes), OfferRules.Default with { RerollCost = 5 }, drain: true);
        Assert.NotEqual(0, run.Game.Rounds[0].Rerolls);                                              // and a purse that cannot pay was allowed to
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_cost_of_nothing_makes_a_reroll_free_by_the_rules_and_the_army_is_not_asked(string which)
    {
        var run = await Rerolling(Army(which, ArmyMut.PayAlwaysYes), OfferRules.Default with { RerollCost = 0, MaxRerolls = 1 }, drain: false);
        Assert.Equal(1, run.Game.Rounds[0].Rerolls);
        Assert.Equal(run.GoldBefore, run.GoldAtChoice);
    }

    // ---- the general is bound by the offer ----

    /// <summary>Suggests the same four tokens every round, whatever the mask says (it ignores the mask on purpose).</summary>
    sealed class FixedGeneral(params int[] tokens) : IGeneral
    {
        public List<GeneralInput> Inputs { get; } = [];
        public int ExamplesSeen => 0;
        public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct) { Inputs.Add(input); return ValueTask.FromResult(new GeneralOutput(DoctrineSet.Of(tokens), 500)); }
        public ValueTask LearnAsync(GeneralExample e, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => ValueTask.CompletedTask;
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task The_generals_mask_is_the_offer_and_the_legal_list_and_what_it_suggests_outside_is_removed_and_counted(string which)
    {
        var gen = new FixedGeneral(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1), DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2), DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.WhenLost(2));
        var h = new Hands();
        var r = await One(Army(which), h, OfferRules.Default, general: gen, rounds: 12);
        Assert.Equal(12, gen.Inputs.Count);
        for (int i = 0; i < 12; i++)
        {
            var (army, ghost, offer) = h.Chosen[i];
            Assert.Equal(army.Legal.Bits & offer.Mask.Bits, gen.Inputs[i].Legal.Bits);              // the mask the general saw: offer AND legal
            Assert.True(offer.Mask.AllowsAll(ghost.Doctrine), $"round {i}: the ghost shown to the player holds a token the offer does not");
            Assert.True(army.Legal.AllowsAll(ghost.Doctrine));
        }
        Assert.True(r.Rounds.Sum(x => x.IllegalDropped) > 0, "a general that ignores the mask must be caught offering tokens outside the offer");
        // the net's input is unchanged: only the mask differs from the army's own read
        Assert.All(gen.Inputs, g => Assert.Equal(GeneralSummary.Length, g.Summary.Count));
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task With_the_offers_off_the_general_sees_the_armys_own_mask_and_nothing_is_offered_or_refused(string which)
    {
        var gen = new FixedGeneral(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1));
        var h = new Hands { Choose = (_, _) => DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age), 5, 6, 7), Draft = (_, _) => [new DraftPick(0, 1)] };
        var r = await One(Army(which), h, OfferRules.Off, general: gen, rounds: 2);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(h.Chosen[i].Army.Legal.Bits, gen.Inputs[i].Legal.Bits);
            Assert.False(r.Rounds[i].UnitOffer!.Active); Assert.False(r.Rounds[i].OrderOffer!.Active);
            Assert.Equal(0, r.Rounds[i].OfferRefused); Assert.Equal(0, r.Rounds[i].Rerolls);
        }
        Assert.Equal(1, h.Reports[0].Accepted.Count);
    }

    // ---- the whole game ----

    static DoctrineGame Game(OfferRules rules, int rounds = 8, bool real = false, IOfferSource? offers = null, bool oldPlacement = false, bool fifthRound = true)
    {
        // fifthRound false = the game before sets and sieges (no besieger named, no set bonus, no siege clock), to reproduce the earlier pins
        var b = DoctrineBuilder.New().WithPlayer(new ScriptedPlayer(1)).WithRules(new DoctrineDomain { MaxRounds = rounds, Offers = rules, Siege = fifthRound });
        var battle = fifthRound ? new TracerBattleOptions() : new TracerBattleOptions { Set = false, SiegeClock = false };
        var armyRules = oldPlacement ? new ArmyRules { BalancedPlacement = false } : null;
        if (offers is not null) b.WithOffers(offers);
        if (real) b.WithDefaults(r => r.WithArmy(new PleromaArmyService(armyRules)).WithTracer(TracerWorlds.Scratch()).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!, battle)).WithGeneral(new TinyHoloGeneral()));
        else b.WithArmy(new StubArmyService(armyRules));
        return b.Build();
    }

    [Fact]
    public async Task The_scripted_player_plays_only_what_is_offered_so_the_pipeline_refuses_nothing()
    {
        foreach (var seed in new[] { 1, 2, 3, 1234 })
        {
            var r = await Game(OfferRules.Default).PlayAsync(seed);
            Assert.All(r.Rounds, x => { Assert.Equal(0, x.OfferRefused); Assert.Equal(0, x.IllegalDropped); Assert.True(x.UnitOffer!.Active && x.OrderOffer!.Active); });
            Assert.Contains(r.Rounds, x => x.PlayerDoctrine.Used > 0);
        }
    }

    [Fact]
    public async Task The_same_seed_offers_the_same_things_and_other_seeds_offer_other_things()
    {
        var a = await Game(OfferRules.Default).PlayAsync(77); var b = await Game(OfferRules.Default).PlayAsync(77); var c = await Game(OfferRules.Default).PlayAsync(78);
        Assert.Equal(a.Digest(), b.Digest());
        for (int i = 0; i < a.Rounds.Count; i++)
        {
            Assert.True(a.Rounds[i].OrderOffer!.Tokens.SequenceEqual(b.Rounds[i].OrderOffer!.Tokens));
            Assert.True(a.Rounds[i].UnitOffer!.Packs.SequenceEqual(b.Rounds[i].UnitOffer!.Packs));
        }
        Assert.False(a.Rounds[0].OrderOffer!.Tokens.SequenceEqual(c.Rounds[0].OrderOffer!.Tokens));
        Assert.False(a.Rounds[1].OrderOffer!.Tokens.SequenceEqual(a.Rounds[2].OrderOffer!.Tokens));
    }

    [Fact]
    public async Task A_stand_in_source_is_the_one_the_pipeline_asks()
    {
        // the pipeline asks the context's source, not the default: a mutant source's flaw reaches the record
        var game = Game(OfferRules.Default, 2, offers: new MutantOfferSource(SeededOffers.Instance, OfferMut.IgnoresRound));
        var r = await game.PlayAsync(5);
        Assert.True(r.Rounds[0].OrderOffer!.Tokens.SequenceEqual(r.Rounds[1].OrderOffer!.Tokens) || r.Rounds[0].UnitOffer!.Packs.Count > 0);
        Assert.True(r.Rounds[0].UnitOffer!.Packs.SequenceEqual(SeededOffers.Instance.Units(5, 0, r.Rounds[0].UnitOffer!.Budget, OfferRules.Default).Packs));
    }

    [Fact]
    public async Task The_offers_off_reproduce_the_old_game_to_the_digest()
    {
        // 2026-10-19 third-round pins, set before the offers: the old free choice must come back exactly when the offers are switched off.
        // (the placement is switched back to the old kind-mod-3 seeding too: the balance rule of placement is the fourth round's other change)
        // (fifth round: the third-round values 15698248093412764465 / 771457392613517103 are no longer reproducible; a draft now buys in kind order, so these are the same game with canonical picks)
        Assert.Equal(13508779435671945360UL, (await Game(OfferRules.Off, real: false, oldPlacement: true).PlayAsync(1234)).Digest());
        Assert.Equal(17505358676471193907UL, (await Game(OfferRules.Off, real: true, oldPlacement: true, fifthRound: false).PlayAsync(1234)).Digest());
    }

    [Fact]
    public async Task The_offers_on_change_the_game()
    {
        Assert.NotEqual((await Game(OfferRules.Off).PlayAsync(1234)).Digest(), (await Game(OfferRules.Default).PlayAsync(1234)).Digest());
    }

    [Fact]
    public async Task A_host_pool_that_widens_by_round_reaches_the_offers_of_the_game()
    {
        var rules = OfferRules.Default with { OrderPool = (round, t) => t <= 20 || round >= 3, KindPool = (round, k) => k < 3 || round >= 3 };
        var r = await Game(rules, 6).PlayAsync(9);
        Assert.All(r.Rounds.Where(x => x.Round < 3), x => { Assert.All(x.OrderOffer!.Tokens, t => Assert.True(t <= 20)); Assert.All(x.UnitOffer!.Packs, p => Assert.True(p.Kind < 3)); });
        Assert.Equal(0, r.Rounds.Sum(x => x.OfferRefused));
    }
}

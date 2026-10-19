namespace Doctrine.Tests;

/// <summary>
/// The duel end to end (Doctrine\todo\duel-seam-2026-10-19.md, pre-registered items 2, 5, 6, 7): shared offers, the authority fighting the army each client built, the two
/// views, siege parity, the winner rule, the idle counter, the message bytes. Every named mutant is run against its check and must turn it red.
/// </summary>
public class DuelTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static TheoryData<string> Armies => new("stub", "pleroma");

    // ---- shared offers -------------------------------------------------------------------------------------------------------------

    [Theory, MemberData(nameof(Armies))]
    public async Task Both_seats_are_offered_the_same_cards_from_the_shared_seed(string army)
    {
        var game = DuelKit.Game(army, "stub", 3);
        for (int seed = 1; seed <= 25; seed++)
        {
            var a = game.NewDuelSeat(seed, Seat.A); var b = game.NewDuelSeat(seed, Seat.B);
            Assert.Equal(a.Read().Digest(), b.Read().Digest());                                   // a mirror: the same levy and the same gold
            Assert.True(DuelKit.SamePacks(a.UnitOffer, b.UnitOffer), $"seed {seed}: the pack offers differ");
            Assert.True(DuelKit.SamePacks(a.UnitOffer, SeededOffers.Instance.Units(seed, 0, a.Read().Gold, OfferRules.Default)), "the duel's offer is the single-player offer");
            var packs = a.UnitOffer.Packs.Take(1).Select(UnitOffer.Pick).ToList();
            await a.DraftAsync(packs); await b.DraftAsync(packs);
            Assert.True(DuelKit.SameOrders(a.OrderOffer!, b.OrderOffer!), $"seed {seed}: the order offers differ");
            Assert.True(await a.RerollAsync() && await b.RerollAsync());
            Assert.True(DuelKit.SameOrders(a.OrderOffer!, b.OrderOffer!), $"seed {seed}: the rerolled offers differ");
        }
    }

    [Fact]
    public void Unit_packs_are_scaled_to_each_seats_own_purse_from_one_stream()
    {
        var offers = SeededOffers.Instance; var rules = OfferRules.Default;
        for (int seed = 1; seed <= 40; seed++)
            for (int round = 0; round < 4; round++)
            {
                var small = offers.Units(seed, round, 14, rules); var big = offers.Units(seed, round, 60, rules);
                Assert.All(small.Packs, p => Assert.True(p.Cost <= 14)); Assert.All(big.Packs, p => Assert.True(p.Cost <= 60));
                Assert.Equal(14, small.Budget); Assert.Equal(60, big.Budget);
                // one stream: the kinds both purses are offered come in the same relative order
                var common = small.Packs.Select(p => p.Kind).Intersect(big.Packs.Select(p => p.Kind)).ToList();
                Assert.Equal(common, small.Packs.Select(p => p.Kind).Where(common.Contains));
                Assert.Equal(common, big.Packs.Select(p => p.Kind).Where(common.Contains));
            }
    }

    [Fact]
    public async Task After_a_round_each_seat_is_offered_packs_for_its_own_purse_and_cards_from_its_own_legal_list()
    {
        var game = DuelKit.Game("stub", "stub", 3);
        int differing = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var session = new DuelKit.Session(game, seed, false, DuelProbe.None, 0);
            await session.NextAsync();                                       // round 0 played: both clients are now in round 1 with their own purses
            foreach (var s in new[] { session.A, session.B })
            {
                Assert.Equal(s.Read().Gold, s.UnitOffer.Budget);
                Assert.All(s.UnitOffer.Packs, p => Assert.True(p.Cost <= s.Read().Gold));
                Assert.True(DuelKit.SamePacks(s.UnitOffer, SeededOffers.Instance.Units(seed, 1, s.Read().Gold, OfferRules.Default)));
                await s.DraftAsync([]);
                Assert.All(s.OrderOffer!.Tokens, t => Assert.True(s.Read().Legal.Allows(t), "an offered card outside the seat's own legal list"));
            }
            if (session.A.Read().Gold != session.B.Read().Gold || !DuelKit.SameOrders(session.A.OrderOffer!, session.B.OrderOffer!)) differing++;
        }
        Assert.True(differing >= 3, $"only {differing} of 12 seeds gave the two seats different state after a round; the check would be vacuous");
    }

    [Fact]
    public async Task A_reroll_by_either_seat_draws_its_own_next_stream_index_and_is_revealed_with_the_picks()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        int seen = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var ca = game.NewDuelSeat(seed, Seat.A); var cb = game.NewDuelSeat(seed, Seat.B);
            await ca.DraftAsync([]); await cb.DraftAsync([]);
            var first = ca.OrderOffer!;
            Assert.Equal(0, first.Rerolls);
            int goldBefore = ca.Read().Gold;
            Assert.True(await ca.RerollAsync());
            Assert.Equal(goldBefore - OfferRules.Default.RerollCost, ca.Read().Gold);
            Assert.Equal(1, ca.OrderOffer!.Rerolls);
            Assert.True(await cb.RerollAsync()); Assert.True(await cb.RerollAsync());                 // B rerolls twice, A once
            Assert.Equal(2, cb.OrderOffer!.Rerolls); Assert.Equal(0, cb.RerollsLeft);
            Assert.False(await cb.RerollAsync(), "a third reroll is beyond the rules");
            if (!ca.OrderOffer.Tokens.SequenceEqual(first.Tokens)) seen++;
            // the offer after k rerolls is the stream's index k from the legal list the army held then
            Assert.True(DuelKit.SameOrders(cb.OrderOffer!, SeededOffers.Instance.Orders(seed, 0, 2, cb.Read().Legal, OfferRules.Default)));
            var pa = await ca.SealAsync(DoctrineSet.Of(ca.OrderOffer.Tokens.Take(2).ToArray())); var pb = await cb.SealAsync(DoctrineSet.Empty);
            Assert.Equal(1, pa.Rerolls); Assert.Equal(2, pb.Rerolls);                                  // revealed with the picks
            // the authority replays the reroll count from the reveal and pays for it
            var duel = game.NewDuel(seed);
            var sa = await duel.SeatOf(Seat.A).ApplyPicksAsync(pa); var sb = await duel.SeatOf(Seat.B).ApplyPicksAsync(pb);
            Assert.Equal(1, sa.Rerolls); Assert.Equal(2, sb.Rerolls);
            Assert.Equal(ca.Sealed!.DigestAfterOrders, sa.DigestAfterOrders); Assert.Equal(cb.Sealed!.DigestAfterOrders, sb.DigestAfterOrders);
            Assert.True(DuelKit.SameOrders(ca.OrderOffer, sa.Orders));
        }
        Assert.True(seen >= 12, $"a reroll changed the offer in only {seen} of 20 seeds");
    }

    [Fact]
    public async Task A_reveal_that_asks_for_more_rerolls_than_the_rules_allow_is_cut_and_counted_like_the_single_player_game()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        var duel = game.NewDuel(3);
        var s = duel.SeatOf(Seat.A);
        var big = new DuelPicks([], DoctrineSet.Empty, 0, 9);                       // nine rerolls: two are allowed
        var round = await s.ApplyPicksAsync(big);
        Assert.Equal(OfferRules.Default.MaxRerolls, round.Rerolls);
        Assert.True(round.OfferRefused >= 1);
    }

    // ---- the authority fights the army each client built -----------------------------------------------------------------------------

    [Theory, MemberData(nameof(Armies))]
    public async Task Every_honest_client_confirms_the_authoritys_figures_for_its_own_army(string army)
    {
        var game = DuelKit.Game(army, "stub", 5);
        for (int seed = 1; seed <= 6; seed++)
        {
            var played = await DuelKit.Honest(game, seed);
            Assert.Equal(5, played.Count);
            foreach (var p in played)
            {
                Assert.True(p.CheckA.Ok, $"seed {seed} round {p.Result.Round}: seat A's check failed: {p.CheckA}");
                Assert.True(p.CheckB.Ok, $"seed {seed} round {p.Result.Round}: seat B's check failed: {p.CheckB}");
                Assert.Equal(p.Result.Digest(), p.Wire.Digest());                                              // both seats saw the same result
                Assert.True(DuelKit.SamePacks(p.Result.A.Units, p.ClientUnitsA) && DuelKit.SamePacks(p.Result.B.Units, p.ClientUnitsB));
                Assert.True(DuelKit.SameOrders(p.Result.A.Orders, p.ClientOrdersA) && DuelKit.SameOrders(p.Result.B.Orders, p.ClientOrdersB));
            }
        }
    }

    [Fact]
    public async Task A_seat_that_built_a_different_army_than_it_revealed_is_told_so_and_only_that_seat()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        var duel = game.NewDuel(5);
        var ca = game.NewDuelSeat(5, Seat.A); var cb = game.NewDuelSeat(5, Seat.B);
        var pa = await DuelKit.Plan(ca, new GameRng(1)); var pb = await DuelKit.Plan(cb, new GameRng(2));
        // seat A reveals different packs than it built locally (it cheats, or its client is wrong)
        var lie = new DuelPicks([], pa.Doctrine, pa.Formation, pa.Rerolls);
        Assert.NotEqual(pa, lie);
        var result = await duel.RunRoundAsync(lie, pb);
        var checkA = await ca.ApplyResultAsync(result); var checkB = await cb.ApplyResultAsync(result);
        Assert.False(checkA.AfterOrdersMatches); Assert.False(checkA.Ok);
        Assert.True(checkB.Ok);
    }

    [Fact]
    public async Task A_seat_that_is_paid_wrong_spoils_is_told_so()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        var duel = game.NewDuel(9);
        var ca = game.NewDuelSeat(9, Seat.A); var cb = game.NewDuelSeat(9, Seat.B);
        var pa = await DuelKit.Plan(ca, new GameRng(1)); var pb = await DuelKit.Plan(cb, new GameRng(2));
        var result = await duel.RunRoundAsync(pa, pb);
        var cheated = result with { A = result.A with { Spoils = result.A.Spoils with { Gold = result.A.Spoils.Gold + 10 } } };
        var check = await ca.ApplyResultAsync(cheated);
        Assert.False(check.SpoilsMatchRules); Assert.False(check.Ok);
    }

    [Fact]
    public async Task A_mirrored_duel_starts_equal_and_fights_equal_armies_in_round_one()
    {
        var game = DuelKit.Game("stub", "stub", 3);
        for (int seed = 1; seed <= 10; seed++)
        {
            var p = (await DuelKit.Honest(game, seed, mirrored: true, only: 1))[0];
            Assert.Equal(p.Result.A.DigestAfterOrders, p.Result.B.DigestAfterOrders);
            Assert.Equal(p.Result.A.Doctrine, p.Result.B.Doctrine);
            Assert.Equal(p.Result.A.Cohorts, p.Result.B.Cohorts);
            Assert.Equal(p.PicksA, p.PicksB);
        }
    }

    // ---- the views -------------------------------------------------------------------------------------------------------------------

    /// <summary>The two seats' readings of results are each other's mirror. Null when they are.</summary>
    static async Task<string?> ViewChecks(DuelProbe probe)
    {
        var game = DuelKit.Game("stub", "tracer", 3);
        for (int seed = 1; seed <= 4; seed++)
            foreach (var p in await DuelKit.Honest(game, seed))
            {
                var r = p.Wire;
                var va = r.ViewFor(Seat.A, probe); var vb = r.ViewFor(Seat.B, probe);
                string at = $"seed {seed} round {r.Round}: ";
                if (va.You != Seat.A || vb.You != Seat.B) return at + "a seat is not shown as 'you' in its own view";
                if (va.Record.PlayerDigest != r.A.DigestAtClose || vb.Record.PlayerDigest != r.B.DigestAtClose) return at + "the viewer's own digest is not in PlayerDigest";
                if (va.Record.EnemyDigest != vb.Record.PlayerDigest || vb.Record.EnemyDigest != va.Record.PlayerDigest) return at + "the opponent's digest is not the other view's own";
                if (va.Record.PlayerDoctrine != r.A.Doctrine || vb.Record.PlayerDoctrine != r.B.Doctrine) return at + "the viewer's doctrine is not its own";
                if (va.Record.Spoils != r.A.Spoils || vb.Record.Spoils != r.B.Spoils) return at + "the viewer's spoils are not its own";
                if (va.Opponent.Doctrine != r.B.Doctrine || vb.Opponent.Doctrine != r.A.Doctrine) return at + "the opponent's doctrine is not the other seat's";
                if (!va.Opponent.Army.Cohorts.SequenceEqual(r.B.Cohorts) || !vb.Opponent.Army.Cohorts.SequenceEqual(r.A.Cohorts)) return at + "the opponent's army is not the other seat's";
                if (va.Record.Result.AttackerWon != (r.Winner == Seat.A) || vb.Record.Result.AttackerWon != (r.Winner == Seat.B)) return at + "'you won' (AttackerWon in your own view) is not the seat's win";
                if (va.YouWon != (r.Winner == Seat.A) || vb.YouWon != (r.Winner == Seat.B) || (va.YouWon && vb.YouWon)) return at + "YouWon is wrong";
                if (!va.Record.Result.AttackerLosses.SequenceEqual(r.Result.AttackerLosses) || !vb.Record.Result.AttackerLosses.SequenceEqual(r.Result.DefenderLosses)) return at + "'your losses' are not the seat's";
                if (ReplayCodec.Digest(va.Record.Replay) != ReplayCodec.Digest(r.Replay)) return at + "seat A's recording is not the shared one";
                if (ReplayCodec.Digest(DuelMirror.Replay(vb.Record.Replay)) != ReplayCodec.Digest(r.Replay)) return at + "seat B's recording is not the shared one, mirrored";
                int youA = va.Record.Replay.Frames[0].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength), youB = vb.Record.Replay.Frames[0].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength);
                if (youA != r.A.Cohorts.Sum(c => c.Count) || youB != r.B.Cohorts.Sum(c => c.Count)) return at + "the recording's 'player' side is not the viewer's army";
                if (va.YouBesieged != (r.Besieger == Seat.A) || vb.YouBesieged != (r.Besieger == Seat.B)) return at + "YouBesieged is wrong";
                if (vb.Record.Replay.Besieger != (r.Replay.Besieger is { } s ? (s == Side.Player ? Side.Enemy : Side.Player) : (Side?)null)) return at + "the mirrored recording names the wrong besieger";
            }
        return null;
    }

    [Fact] public async Task Each_seats_view_of_a_result_is_the_mirror_of_the_other_and_the_viewer_is_always_you() => Assert.Null(await ViewChecks(DuelProbe.None));
    [Fact] public async Task Views_that_are_swapped_between_the_seats_are_caught() => Assert.NotNull(await ViewChecks(DuelProbe.SeatViewsSwapped));

    [Fact]
    public async Task Mirroring_a_recording_twice_is_the_recording()
    {
        var game = DuelKit.Game("stub", "tracer", 2);
        foreach (var p in await DuelKit.Honest(game, 3))
        {
            var once = DuelMirror.Replay(p.Result.Replay);
            Assert.NotEqual(ReplayCodec.Digest(p.Result.Replay), ReplayCodec.Digest(once));
            Assert.Equal(ReplayCodec.Digest(p.Result.Replay), ReplayCodec.Digest(DuelMirror.Replay(once)));
            Assert.Equal(ReplayCodec.Encode(p.Result.Replay), ReplayCodec.Encode(DuelMirror.Replay(once)));
        }
    }

    // ---- siege parity -------------------------------------------------------------------------------------------------------------------

    /// <summary>Seat A besieges even rounds, seat B odd ones, in a siege arena; nobody on an open field; and the briefing agrees. Null when so.</summary>
    static async Task<string?> ParityChecks(DuelProbe probe)
    {
        var game = DuelKit.Game("stub", "tracer", 8);
        int sieges = 0;
        for (int seed = 1; seed <= 25; seed++)
        {
            var duel = game.NewDuel(seed, probe);
            for (int r = 0; r < 8; r++)
            {
                var arena = duel.ArenaFor(r);
                Seat? want = arena.IsSiege ? (r % 2 == 0 ? Seat.A : Seat.B) : null;
                if (arena.IsSiege) sieges++;
                if (duel.BesiegerOf(r) != want) return $"seed {seed} round {r} ({arena.Id}): besieger {duel.BesiegerOf(r)?.ToString() ?? "nobody"}, expected {want?.ToString() ?? "nobody"}";
                var brief = (await game.BriefingAsync(seed, r, 0)).Arena.Besieger;
                if (brief != want?.Side()) return $"seed {seed} round {r}: the single-player briefing names {brief?.ToString() ?? "nobody"}";
            }
        }
        return sieges >= 20 ? null : $"only {sieges} siege rounds in the sample; the check would be vacuous";
    }

    [Fact] public async Task Seat_A_besieges_even_rounds_and_seat_B_odd_ones_and_the_briefing_agrees() => Assert.Null(await ParityChecks(DuelProbe.None));
    [Fact] public async Task A_siege_parity_that_is_wrong_is_caught() => Assert.NotNull(await ParityChecks(DuelProbe.SiegeParityWrong));

    [Fact]
    public async Task The_besieger_in_the_result_is_the_one_the_round_names_and_the_recording_is_version_three_only_for_a_siege()
    {
        var game = DuelKit.Game("stub", "tracer", 8);
        for (int seed = 1; seed <= 3; seed++)
            foreach (var p in await DuelKit.Honest(game, seed))
            {
                var arena = game.ArenaFor(seed, p.Result.Round);
                Assert.Equal(arena.IsSiege ? (p.Result.Round % 2 == 0 ? Seat.A : Seat.B) : (Seat?)null, p.Result.Besieger);
                Assert.Equal(arena.IsSiege ? 3 : 2, p.Result.Replay.Version);                       // a siege is recorded in version 3, an open field still in 2
            }
    }

    // ---- the offers, watched failing -----------------------------------------------------------------------------------------------------

    static async Task<string?> OfferChecks(DuelProbe probe)
    {
        var game = DuelKit.Game("stub", "stub", 3);
        for (int seed = 1; seed <= 8; seed++)
            foreach (var p in await DuelKit.Honest(game, seed, probe: probe))
            {
                string at = $"seed {seed} round {p.Result.Round}: ";
                if (!DuelKit.SamePacks(p.Result.A.Units, p.ClientUnitsA) || !DuelKit.SamePacks(p.Result.B.Units, p.ClientUnitsB)) return at + "the authority's pack offer for a seat is not the offer that seat drew locally";
                if (!DuelKit.SameOrders(p.Result.A.Orders, p.ClientOrdersA) || !DuelKit.SameOrders(p.Result.B.Orders, p.ClientOrdersB)) return at + "the authority's order offer for a seat is not the offer that seat drew locally";
                if (!p.CheckA.Ok || !p.CheckB.Ok) return at + "a seat's own army does not match the authority's figures";
            }
        return null;
    }

    [Fact] public async Task The_authoritys_offers_are_the_offers_each_client_drew() => Assert.Null(await OfferChecks(DuelProbe.None));
    [Fact] public async Task Offers_drawn_from_a_different_seed_for_one_seat_are_caught() => Assert.Contains("offer", await OfferChecks(DuelProbe.OffersDifferPerSeat));

    // ---- the winner, the spoils, the idle counter --------------------------------------------------------------------------------------------

    [Fact]
    public void More_fields_wins_a_tie_goes_to_more_power_and_equal_is_drawn()
    {
        Assert.Equal(Seat.A, new DuelScore(2, 1, 0, 999).Winner); Assert.Equal(DuelDecider.Fields, new DuelScore(2, 1, 0, 999).DecidedBy);
        Assert.Equal(Seat.B, new DuelScore(0, 3, 999, 0).Winner);
        Assert.Equal(Seat.B, new DuelScore(1, 1, 10, 11).Winner); Assert.Equal(DuelDecider.Power, new DuelScore(1, 1, 10, 11).DecidedBy);
        Assert.Equal(Seat.A, new DuelScore(0, 0, 12, 11).Winner);
        Assert.Null(new DuelScore(1, 1, 5, 5).Winner); Assert.Equal(DuelDecider.Drawn, new DuelScore(1, 1, 5, 5).DecidedBy);
        // the same rule written out again, against every small score
        for (int fa = 0; fa < 4; fa++) for (int fb = 0; fb < 4; fb++) for (int pa = 0; pa < 3; pa++) for (int pb = 0; pb < 3; pb++)
        {
            Seat? want = fa > fb ? Seat.A : fb > fa ? Seat.B : pa > pb ? Seat.A : pb > pa ? Seat.B : null;
            Assert.Equal(want, new DuelScore(fa, fb, pa, pb).Winner);
        }
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task Rounds_three_five_and_eight_each_end_the_duel_with_the_standing_the_results_add_up_to(string army)
    {
        foreach (int rounds in new[] { 3, 5, 8 })
        {
            var game = DuelKit.Game(army, "stub", rounds);
            var duel = game.NewDuel(rounds);
            var ca = game.NewDuelSeat(rounds, Seat.A); var cb = game.NewDuelSeat(rounds, Seat.B);
            int fa = 0, fb = 0; DuelRoundResult? last = null;
            for (int r = 0; r < rounds; r++)
            {
                Assert.False(duel.Finished);
                var result = await duel.RunRoundAsync(await DuelKit.Plan(ca, new GameRng((ulong)(1 + r))), await DuelKit.Plan(cb, new GameRng((ulong)(100 + r))));
                Assert.Equal(r, result.Round); Assert.Equal(r == rounds - 1, result.Final);
                if (result.Winner == Seat.A) fa++; else if (result.Winner == Seat.B) fb++;
                Assert.True((await ca.ApplyResultAsync(result)).Ok); Assert.True((await cb.ApplyResultAsync(result)).Ok);
                last = result;
            }
            Assert.True(duel.Finished); Assert.Equal(rounds, duel.Round);
            var score = duel.Score;
            Assert.Equal(fa, score.FieldsA); Assert.Equal(fb, score.FieldsB);
            Assert.Equal(score, last!.Score);
            Assert.Equal(DuelScore.PowerOf(duel.SeatOf(Seat.A).Read()), score.PowerA);
            Assert.Equal(DuelScore.PowerOf(ca.Read()), score.PowerA);                                    // the client's own army says the same about its power
            Assert.Equal(DuelScore.PowerOf(cb.Read()), score.PowerB);
            Assert.Equal(duel.SeatOf(Seat.B).Read().Digest(), cb.Read().Digest());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await duel.PlayRoundAsync());
        }
    }

    [Fact]
    public void Power_is_the_battles_own_rating_summed_over_the_cohorts()
    {
        var read = DuelKit.Game("stub", "stub", 1).NewDuelSeat(4, Seat.A).Read();
        Assert.Equal(read.Cohorts.Sum(c => UnitRules.Rating(c.Count, c.PowerPct, c.Traits)), DuelScore.PowerOf(read));
        Assert.True(DuelScore.PowerOf(read) > 0);
    }

    [Fact]
    public async Task Each_seat_is_paid_for_its_own_field_by_the_rule_the_single_player_game_uses()
    {
        var game = DuelKit.Game("stub", "stub", 4);
        var d = game.Rules;
        foreach (var p in await DuelKit.Honest(game, 7))
        {
            var res = p.Result.Result;
            Assert.Equal(RoundLaws.BaseSpoils(d, res.Outcome == BattleStatus.AttackerWon, res.DefenderLosses.Sum(l => l.Lost)), p.Result.A.Spoils);
            Assert.Equal(RoundLaws.BaseSpoils(d, res.Outcome == BattleStatus.DefenderWon, res.AttackerLosses.Sum(l => l.Lost)), p.Result.B.Spoils);
            if (res.Outcome == BattleStatus.Draw) Assert.Equal(0, p.Result.A.Spoils.Experience + p.Result.B.Spoils.Experience);
            else Assert.Equal(1, p.Result.A.Spoils.Experience + p.Result.B.Spoils.Experience);              // exactly one seat won the field
        }
    }

    [Fact]
    public void Three_auto_seals_in_a_row_mark_a_seat_idle_and_a_real_seal_ends_the_streak()
    {
        var duel = DuelKit.Game("stub", "stub", 3).NewDuel(1);
        Assert.Equal(3, DuelGame.IdleAfter);
        Assert.False(duel.IsIdle(Seat.A));
        duel.MarkAutoSealed(Seat.A); duel.MarkAutoSealed(Seat.A);
        Assert.False(duel.IsIdle(Seat.A)); Assert.Equal(2, duel.IdleStreak(Seat.A)); Assert.Equal(0, duel.IdleStreak(Seat.B));
        duel.MarkSealed(Seat.A); Assert.Equal(0, duel.IdleStreak(Seat.A));
        duel.MarkAutoSealed(Seat.A); duel.MarkAutoSealed(Seat.A); duel.MarkAutoSealed(Seat.A);
        Assert.True(duel.IsIdle(Seat.A)); Assert.False(duel.IsIdle(Seat.B));
    }

    // ---- the hand-in ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_round_waits_for_both_reveals_ignores_duplicates_and_refuses_the_wrong_round()
    {
        var game = DuelKit.Game("stub", "stub", 3);
        var duel = game.NewDuel(2);
        var play = duel.PlayRoundAsync().AsTask();
        Assert.False(duel.Ready);
        Assert.True(duel.HandIn(Seat.A, 0, DuelPicks.Empty));
        Assert.False(duel.HandIn(Seat.A, 0, new DuelPicks([], DoctrineSet.Of(4))), "a duplicate does nothing");
        await Task.Delay(30); Assert.False(play.IsCompleted, "the round must wait for seat B");
        Assert.False(duel.HandIn(Seat.B, 3, DuelPicks.Empty), "a round beyond the duel");
        Assert.False(duel.HandIn(Seat.B, -1, DuelPicks.Empty));
        Assert.True(duel.HandIn(Seat.B, 0, DuelPicks.Empty));
        var result = await play;
        Assert.Equal(0, result.Round); Assert.Equal(1, duel.Round);
        Assert.Equal(DoctrineSet.Empty, result.A.Doctrine);                      // the first hand-in stood, not the duplicate
        Assert.False(duel.HandIn(Seat.A, 0, DuelPicks.Empty), "a round already played");
        Assert.True(duel.HandIn(Seat.A, 1, DuelPicks.Empty));                   // the next round may be handed in early
    }

    [Fact]
    public async Task A_seat_steps_out_of_order_throw_and_leave_the_seat_usable()
    {
        var s = DuelKit.Game("stub", "stub", 2).NewDuelSeat(1, Seat.A);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await s.RerollAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await s.SealAsync(DoctrineSet.Empty));
        await s.DraftAsync([]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await s.DraftAsync([]));
        await s.RerollAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await s.FormAsync(1));
        await s.SealAsync(DoctrineSet.Empty);
    }

    [Fact]
    public async Task Offers_off_is_a_free_duel_with_nothing_refused()
    {
        var game = DuelKit.Game("stub", "stub", 2, offers: OfferRules.Off);
        var played = await DuelKit.Honest(game, 4);
        Assert.All(played, p => { Assert.False(p.Result.A.Units.Active); Assert.False(p.Result.A.Orders.Active); Assert.True(p.CheckA.Ok && p.CheckB.Ok); });
    }

    [Fact]
    public async Task A_formation_is_a_free_turn_of_the_ranks_that_both_the_client_and_the_authority_apply()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        foreach (int f in new[] { 0, 1, 2 })
        {
            var ca = game.NewDuelSeat(6, Seat.A); var duel = game.NewDuel(6);
            await ca.DraftAsync([]);
            var before = ca.Read().Cohorts.Select(c => c.Slot).ToList();
            await ca.FormAsync(f);
            Assert.Equal(before.Select(s => (s + f) % 3), ca.Read().Cohorts.Select(c => c.Slot));
            var picks = await ca.SealAsync(DoctrineSet.Empty);
            Assert.Equal(f, picks.Formation);
            var sa = await duel.SeatOf(Seat.A).ApplyPicksAsync(picks);
            Assert.Equal(ca.Sealed!.DigestAfterOrders, sa.DigestAfterOrders);
            Assert.Equal(0, sa.IllegalDropped);
        }
    }

    [Fact]
    public async Task A_revealed_formation_outside_zero_to_two_is_removed_and_counted_and_the_ranks_stay()
    {
        var game = DuelKit.Game("stub", "stub", 2);
        var plain = await game.NewDuel(6).SeatOf(Seat.A).ApplyPicksAsync(DuelPicks.Empty);
        foreach (int f in new[] { 3, 7, -1 })
        {
            var s = await game.NewDuel(6).SeatOf(Seat.A).ApplyPicksAsync(new DuelPicks([], DoctrineSet.Empty, f));
            Assert.Equal(1, s.IllegalDropped);
            Assert.Equal(plain.DigestAfterOrders, s.DigestAfterOrders);
        }
    }
    [Fact]
    public async Task Two_packs_of_one_kind_listed_in_any_order_build_the_same_army_for_the_client_and_the_authority()
    {
        // FOUND while building the duel: with too little gold for both, the pack bought first wins, so the client must apply its packs in the order the reveal carries them.
        var game = DuelKit.Game("stub", "stub", 2, offers: OfferRules.Off);
        var c = game.NewDuelSeat(3, Seat.A);
        await c.DraftAsync([new DraftPick(2, 5), new DraftPick(2, 3)]);                 // unsorted; the purse is 12 and a plain soldier costs 2: only one of them can be bought
        var picks = await c.SealAsync(DoctrineSet.Empty);
        var s = await game.NewDuel(3).SeatOf(Seat.A).ApplyPicksAsync(picks);
        Assert.Equal(c.Sealed!.DigestAfterOrders, s.DigestAfterOrders);
        Assert.Equal(c.Read().Cohorts, s.Cohorts);
    }
    // ---- the message bytes ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_round_result_survives_its_bytes_and_every_damaged_message_is_refused_without_throwing()
    {
        var game = DuelKit.Game("stub", "tracer", 2);
        var p = (await DuelKit.Honest(game, 11))[0];
        var bytes = p.Result.Encode();
        Assert.True(DuelRoundResult.TryDecode(bytes, out var back, out var why), why);
        Assert.Equal(bytes, back!.Encode());
        Assert.Equal(p.Result.Digest(), back.Digest());
        Assert.Equal(p.Result.ViewFor(Seat.B).Record.PlayerDigest, back.ViewFor(Seat.B).Record.PlayerDigest);
        Assert.False(DuelRoundResult.TryDecode([.. bytes, 0], out _, out _));
        var rng = new Random(2);
        for (int cut = 0; cut < bytes.Length; cut += Math.Max(1, bytes.Length / 400)) Assert.False(DuelRoundResult.TryDecode(bytes[..cut], out _, out _), $"a message cut at {cut} decoded");
        for (int i = 0; i < 300; i++)
        {
            var damaged = (byte[])bytes.Clone(); damaged[rng.Next(damaged.Length)] ^= (byte)(1 << rng.Next(8));
            DuelRoundResult.TryDecode(damaged, out _, out _);                    // must not throw; whether a flipped digest bit decodes is not the codec's to say
        }
        output.WriteLine($"round result message: {bytes.Length} bytes (replay {ReplayCodec.Encode(p.Result.Replay).Length}); a reveal is {p.PicksA.Canonical().Length} bytes + 16 nonce, a commit 32 bytes");
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task The_game_of_a_duel_is_reproducible_from_its_seed_and_its_picks()
    {
        var one = await DuelKit.Honest(DuelKit.Game("pleroma", "tracer", 3), 21);
        var two = await DuelKit.Honest(DuelKit.Game("pleroma", "tracer", 3), 21);
        Assert.Equal(one.Select(p => p.Result.Digest()), two.Select(p => p.Result.Digest()));
        Assert.All(one, p => Assert.True(p.CheckA.Ok && p.CheckB.Ok));
    }
}

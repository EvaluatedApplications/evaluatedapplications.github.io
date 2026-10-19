namespace Doctrine.Tests;

/// <summary>
/// The general under the roguelike offers (Core\Offers.cs): its input is the offer (the mask reaches it already cut to offer AND legal list), its
/// answer is the offered cards ranked the way this player would take them. Numbers pinned here come from EXP-OFFER-1 (OfferProbe.cs), with margin.
/// </summary>
public class OfferGeneralTests
{
    static GeneralOptions Opt(GeneralMode mode, bool history = false) => GeneralChecks.Fast(mode) with { History = history };

    /// <summary>Teach <paramref name="n"/> rounds of a scripted style, one example per round, exactly as the game's imitation stage does.</summary>
    static async Task Teach(IGeneral g, int style, int seed, int n)
    {
        var stream = OfferProbe.Stream(seed, n);
        var r = new GameRng(GameRng.Mix(seed, 9200 + style)); var prev = Array.Empty<int>();
        foreach (var rd in stream)
        {
            var p = OfferProbe.Choose(style, rd, prev, r); prev = p;
            await g.LearnAsync(new GeneralExample(rd.Input, DoctrineSet.Of(p), false), default);
        }
    }

    public static IEnumerable<object[]> Shapes() =>
    [
        [GeneralMode.TableOnly, false], [GeneralMode.Hybrid, false], [GeneralMode.NetOnly, false], [GeneralMode.TableOnly, true], [GeneralMode.Hybrid, true],
    ];

    /// <summary>Null when the suggestion is a legal ordered subset of the offer: only offered tokens, none twice, at most four, packed left (no gap before a token).</summary>
    static string? LegalSubset(DoctrineSet d, OfferProbe.Round rd)
    {
        var t = d.Tokens; bool ended = false; var seen = new HashSet<int>();
        foreach (var x in t)
        {
            if (x == 0) { ended = true; continue; }
            if (ended) return $"{d}: a card after an empty slot";
            if (!rd.Offer.Contains(x)) return $"{d}: {DoctrineVocabulary.Name(x)} is not on offer";
            if (!rd.Input.Legal.Allows(x)) return $"{d}: {DoctrineVocabulary.Name(x)} is not legal";
            if (!seen.Add(x)) return $"{d}: {DoctrineVocabulary.Name(x)} twice";
        }
        return null;
    }

    [Theory, MemberData(nameof(Shapes))]
    public async Task A_new_general_says_nothing_under_an_offer(GeneralMode mode, bool history)
    {
        var g = new TinyHoloGeneral(Opt(mode, history));
        foreach (var rd in OfferProbe.Stream(3, 20))
        {
            var o = await g.SuggestAsync(rd.Input, default);
            Assert.True(o.Doctrine.IsEmpty && o.ConfidencePermille == 0 && o.SlotConfidence == new SlotPermille(0, 0, 0, 0), $"a new general said {o.Doctrine}");
        }
        Assert.Equal(0, g.ExamplesSeen);
    }

    [Theory, MemberData(nameof(Shapes))]
    public async Task The_suggestion_is_a_legal_ordered_subset_of_the_offer_and_is_never_empty_once_taught(GeneralMode mode, bool history)
    {
        foreach (var style in new[] { 0, 1, 3, 5 })
        {
            var g = new TinyHoloGeneral(Opt(mode, history));
            await Teach(g, style, 5, 30);
            foreach (var rd in OfferProbe.Stream(6, 40))
            {
                var o = await g.SuggestAsync(rd.Input, default);
                Assert.Null(LegalSubset(o.Doctrine, rd));
                if (mode != GeneralMode.NetOnly) Assert.False(o.Doctrine.IsEmpty, $"style {style}: taught for 30 rounds and still no pick from {string.Join(",", rd.Offer.Select(DoctrineVocabulary.Name))}");
            }
        }
    }

    [Fact]
    public async Task A_single_card_offer_or_an_empty_one_is_answered_with_that_card_or_nothing()
    {
        var g = new TinyHoloGeneral(Opt(GeneralMode.Hybrid));
        await Teach(g, 1, 5, 30);
        int one = DoctrineVocabulary.Field(FieldOrderKind.Hold, 1);
        var s = OfferProbe.Stream(6, 1)[0].Summary;
        var a = await g.SuggestAsync(new GeneralInput(0, s, LegalMask.None.With(one)), default);
        Assert.True(a.Doctrine.Tokens.All(t => t == 0 || t == one), $"{a.Doctrine}");
        var b = await g.SuggestAsync(new GeneralInput(0, s, LegalMask.None), default);
        Assert.True(b.Doctrine.IsEmpty);
    }

    [Theory, MemberData(nameof(Shapes))]
    public async Task Two_generals_taught_alike_answer_alike_under_offers(GeneralMode mode, bool history)
    {
        var a = new TinyHoloGeneral(Opt(mode, history)); var b = new TinyHoloGeneral(Opt(mode, history));
        await Teach(a, 4, 8, 25); await Teach(b, 4, 8, 25);
        foreach (var rd in OfferProbe.Stream(9, 30)) Assert.Equal(await a.SuggestAsync(rd.Input, default), await b.SuggestAsync(rd.Input, default));
    }

    /// <summary>The held-out reading: taught 40 rounds of a style, shown 100 fresh offers (never seen, never learned): share of the player's picks the suggestion contained.</summary>
    static async Task<(double Recall, double FirstRight, double Empty)> HeldOut(Func<IGeneral> make, int style, int teachSeed = 5)
    {
        var g = make(); await Teach(g, style, teachSeed, 40);
        var r = new GameRng(GameRng.Mix(77, style)); var prev = Array.Empty<int>(); double recall = 0, first = 0; int empty = 0, n = 0;
        foreach (var rd in OfferProbe.Stream(teachSeed + 100, 100))
        {
            var want = OfferProbe.Choose(style, rd, prev, r); prev = want;
            var sug = (await g.SuggestAsync(rd.Input, default)).Doctrine.Tokens.Where(t => t != 0).ToArray();
            if (sug.Length == 0) empty++;
            recall += want.Count(sug.Contains) / (double)want.Length; first += sug.Length > 0 && sug[0] == want[0] ? 1 : 0; n++;
        }
        return (recall / n, first / n, empty / (double)n);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task A_taste_learned_from_forty_rounds_carries_to_offers_it_has_never_seen(int style)
    {
        var (recall, first, empty) = await HeldOut(() => new TinyHoloGeneral(Opt(GeneralMode.TableOnly)), style);
        // the random null contains k/8 of the player's picks (about 0.4); a learned taste is far above it, and says something every time
        Assert.True(recall >= 0.55, $"style {style}: recall {recall:F3}");   // measured 0.62 to 0.80 across the three; style 2 is bounded because its Rotate cards are often not offered
        Assert.True(first >= 0.45, $"style {style}: the first card was the player's first {first:F3} of the time");
        Assert.Equal(0.0, empty);
    }

    [Fact]
    public async Task Players_with_different_tastes_get_different_generals_under_the_same_offers()
    {
        var a = new TinyHoloGeneral(Opt(GeneralMode.TableOnly)); var b = new TinyHoloGeneral(Opt(GeneralMode.TableOnly));
        await Teach(a, 0, 5, 40); await Teach(b, 1, 5, 40);                      // biggest army orders versus field orders on the front line
        int armyA = 0, fieldB = 0, n = 0;
        foreach (var rd in OfferProbe.Stream(6, 60))
        {
            var da = (await a.SuggestAsync(rd.Input, default)).Doctrine; var db = (await b.SuggestAsync(rd.Input, default)).Doctrine;
            if (DoctrineVocabulary.Decode(da.S0).Family == OrderFamily.Army) armyA++;
            if (DoctrineVocabulary.Decode(db.S0).Family == OrderFamily.Field) fieldB++;
            n++;
        }
        Assert.True(armyA >= 0.85 * n && fieldB >= 0.85 * n, $"first cards: army {armyA}/{n}, field {fieldB}/{n}");
    }

    [Fact]
    public async Task Taking_the_favourite_off_the_offer_gives_the_next_best_offered_card_not_nothing()
    {
        var g = new TinyHoloGeneral(Opt(GeneralMode.TableOnly));
        await Teach(g, 1, 5, 40);
        foreach (var rd in OfferProbe.Stream(6, 40))
        {
            var full = (await g.SuggestAsync(rd.Input, default)).Doctrine;
            var cut = new LegalMask(rd.Input.Legal.Bits).Without(full.S0);
            var smaller = (await g.SuggestAsync(new GeneralInput(rd.Input.Round, rd.Input.Summary, cut), default)).Doctrine;
            Assert.DoesNotContain(full.S0, smaller.Tokens);
            Assert.False(smaller.IsEmpty, "the favourite was removed from the offer and the general had nothing else to say");
            Assert.True(cut.AllowsAll(smaller));
        }
    }

    [Fact]
    public async Task Shipped_default_is_the_hybrid_with_the_tiny_net_learning_alongside_and_history_off()
    {
        // Coordinator decision 2026-10-19: EXP-OFFER-1 found the net adds nothing measurable over the tables (about -0.006 exact at N=40),
        // but the product keeps its HoloFormer: it learns alongside (12 to 16 ms a round) and takes a slot only when it has been right
        // more often than the tables (Margin), so the player's suggestion is the tables' until it proves better. History stays off.
        var g = new TinyHoloGeneral();
        Assert.Equal(GeneralMode.Hybrid, g.Options.Mode);
        Assert.False(g.Options.History);
        Assert.True(g.NetParameters > 0);
        Assert.Equal(0, g.NetPasses);
        await Teach(g, 1, 5, 5);
        Assert.True(g.NetPasses > 0);
    }

    [Fact]
    public async Task Last_rounds_picks_are_read_only_when_asked_and_the_answer_stays_inside_the_offer()
    {
        var a = new TinyHoloGeneral(Opt(GeneralMode.TableOnly, history: false)); var b = new TinyHoloGeneral(Opt(GeneralMode.TableOnly, history: true));
        await Teach(a, 4, 5, 40); await Teach(b, 4, 5, 40);
        int differ = 0;
        foreach (var rd in OfferProbe.Stream(6, 60))
        {
            var x = (await a.SuggestAsync(rd.Input, default)).Doctrine; var y = (await b.SuggestAsync(rd.Input, default)).Doctrine;
            Assert.Null(LegalSubset(y, rd));
            if (x != y) differ++;
        }
        Assert.True(differ > 0, "history on and off gave the same answers on every offer: the input is not read");
    }

    // ---- the match record, measured on "did the player take the card it ranked" ---------------------------------------

    /// <summary>
    /// The record by hand: before each round is learned ask for the ranking; slot k COUNTS when the card ranked k-th or the card the player took k-th is not End,
    /// and HITS when they are the same card; halve everything when 64 rounds have passed; the figure must read 1000*hits/(counted+2) at every round.
    /// </summary>
    static async Task<string?> RecordIsWhatItSays(Func<IGeneral> make, int style, int seed, int n)
    {
        var g = make(); var stream = OfferProbe.Stream(seed, n);
        var r = new GameRng(GameRng.Mix(seed, 9200 + style)); var prev = Array.Empty<int>();
        int trials = 0; var hit = new int[4]; var cnt = new int[4];
        for (int i = 0; i < n; i++)
        {
            var rd = stream[i];
            var o = await g.SuggestAsync(rd.Input, default);
            if (o.SlotConfidence is not { } sc) return $"round {i}: no record";
            for (int k = 0; k < 4; k++)
                if (sc[k] != 1000 * hit[k] / (cnt[k] + 2)) return $"round {i} slot {k}: reported {sc[k]}, hand count {hit[k]} of {cnt[k]}";
            var p = OfferProbe.Choose(style, rd, prev, r); prev = p;
            var took = DoctrineSet.Of(p); var shown = o.Doctrine;
            trials++;
            for (int k = 0; k < 4; k++) if (shown[k] != 0 || took[k] != 0) { cnt[k]++; if (shown[k] == took[k]) hit[k]++; }
            if (trials >= 64) { trials /= 2; for (int k = 0; k < 4; k++) { hit[k] /= 2; cnt[k] /= 2; } }
            await g.LearnAsync(new GeneralExample(rd.Input, took, false), default);
        }
        return null;
    }

    [Theory]
    [InlineData(GeneralMode.TableOnly, 1, 3)] [InlineData(GeneralMode.TableOnly, 3, 4)] [InlineData(GeneralMode.Hybrid, 0, 5)]
    public async Task The_match_record_counts_the_cards_the_player_took_in_the_order_it_ranked_them(GeneralMode mode, int style, int seed)
        => Assert.Null(await RecordIsWhatItSays(() => new TinyHoloGeneral(Opt(mode)), style, seed, 90));

    [Fact]
    public async Task The_match_record_climbs_for_a_player_with_a_taste_and_stays_low_for_one_without()
    {
        async Task<SlotPermille> After(int style)
        {
            var g = new TinyHoloGeneral(Opt(GeneralMode.TableOnly)); await Teach(g, style, 5, 40);
            return (await g.SuggestAsync(OfferProbe.Stream(6, 1)[0].Input, default)).SlotConfidence!.Value;
        }
        var taste = await After(1); var none = await After(3);
        Assert.True(taste.S0 >= 300, $"a player with a taste read {taste}");
        Assert.True(none.S0 < 300 && none.S1 < 300 && none.S2 < 300 && none.S3 < 300, $"a random player read {none}");
    }

    // ---- mutants: each breaks one thing; the named check must go red -------------------------------------------------

    static Func<IGeneral> Broken(GeneralFault f) => () => new TinyHoloGeneral(Opt(GeneralMode.TableOnly), f);

    static async Task<string?> StaysInOffer(Func<IGeneral> make)
    {
        var g = make(); await Teach(g, 1, 5, 30);
        foreach (var rd in OfferProbe.Stream(6, 40)) { var e = LegalSubset((await g.SuggestAsync(rd.Input, default)).Doctrine, rd); if (e is not null) return e; }
        return null;
    }

    [Fact]
    public async Task A_general_that_may_name_a_card_outside_the_offer_is_caught()
    {
        Assert.Null(await StaysInOffer(Broken(GeneralFault.None)));
        Assert.NotNull(await StaysInOffer(Broken(GeneralFault.NetOutputUnmasked)));
    }

    [Fact]
    public async Task A_general_that_ranks_the_whole_vocabulary_and_ignores_the_offer_is_caught_by_going_quiet()
    {
        var clean = await HeldOut(() => new TinyHoloGeneral(Opt(GeneralMode.TableOnly)), 1);
        var broken = await HeldOut(Broken(GeneralFault.OfferIgnored), 1);
        Assert.Equal(0.0, clean.Empty);
        Assert.True(broken.Empty > 0.2 || broken.Recall < clean.Recall - 0.2, $"ignoring the offer left recall {broken.Recall:F2} (clean {clean.Recall:F2}), empty {broken.Empty:F2}");
    }

    [Fact]
    public async Task A_general_whose_taste_table_never_learns_is_caught()
    {
        var clean = await HeldOut(() => new TinyHoloGeneral(Opt(GeneralMode.TableOnly)), 1);
        var broken = await HeldOut(Broken(GeneralFault.TasteDeaf), 1);
        Assert.True(broken.Recall < clean.Recall - 0.1 || broken.Empty > 0.2, $"a deaf taste table still reached recall {broken.Recall:F2} (clean {clean.Recall:F2}), empty {broken.Empty:F2}");
    }

    [Fact]
    public async Task A_general_that_shares_its_taste_with_every_other_is_caught()
    {
        var a = new TinyHoloGeneral(Opt(GeneralMode.TableOnly), GeneralFault.SharedState); await Teach(a, 1, 5, 30);
        var b = new TinyHoloGeneral(Opt(GeneralMode.TableOnly), GeneralFault.SharedState);
        var o = await b.SuggestAsync(OfferProbe.Stream(6, 1)[0].Input, default);
        Assert.False(o.Doctrine.IsEmpty, "this check needs the mutant to leak; if it no longer leaks the next line is vacuous");
        var c = new TinyHoloGeneral(Opt(GeneralMode.TableOnly)); await Teach(c, 1, 5, 30);
        var clean = new TinyHoloGeneral(Opt(GeneralMode.TableOnly));
        Assert.True((await clean.SuggestAsync(OfferProbe.Stream(6, 1)[0].Input, default)).Doctrine.IsEmpty, "a new, honest general must be empty whatever another learned");
    }

    // ---- cost ----------------------------------------------------------------------------------------------------------

    /// <summary>Gated (DOCTRINE_PROBE=1): milliseconds per decision and per Learn call, for the shipped shape and the hybrid. Written to DOCTRINE_PROBE_OUT.</summary>
    [Fact]
    public async Task Cost_of_a_decision_and_a_learn_call()
    {
        if (Environment.GetEnvironmentVariable("DOCTRINE_PROBE") != "1") return;
        var sb = new System.Text.StringBuilder();
        foreach (var (name, o) in new[] { ("shipped TableOnly", new GeneralOptions()), ("Hybrid", new GeneralOptions { Mode = GeneralMode.Hybrid }), ("Hybrid history", new GeneralOptions { Mode = GeneralMode.Hybrid, History = true }) })
        {
            var g = new TinyHoloGeneral(o);
            var stream = OfferProbe.Stream(4, 60); var r = new GameRng(5); var prev = Array.Empty<int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int rep = 0; rep < 3; rep++) for (int i = 0; i < 20; i++) { var p = OfferProbe.Choose(1, stream[i], prev, r); prev = p; await g.LearnAsync(new GeneralExample(stream[i].Input, DoctrineSet.Of(p), false), default); }
            double learnMs = sw.Elapsed.TotalMilliseconds / 60;
            for (int i = 0; i < 20; i++) await g.SuggestAsync(stream[i].Input, default);       // warm
            sw.Restart();
            for (int rep = 0; rep < 10; rep++) for (int i = 0; i < 60; i++) await g.SuggestAsync(stream[i].Input, default);
            double suggestMs = sw.Elapsed.TotalMilliseconds / 600;
            sb.AppendLine($"{name}: suggest {suggestMs:F3} ms, learn {learnMs:F2} ms (60 calls, includes the first, cold ones), net parameters {g.NetParameters}, net passes {g.NetPasses}");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("DOCTRINE_PROBE_OUT") ?? Path.Combine(Path.GetTempPath(), "offer-cost.txt"), sb.ToString());
    }
}

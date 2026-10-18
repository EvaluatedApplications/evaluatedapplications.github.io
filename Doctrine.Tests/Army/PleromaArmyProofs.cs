using Phasor;

namespace Doctrine.Tests;

/// <summary>
/// The Pleroma army's own proofs: every operation against the brute-force <see cref="ArmyModel"/>, cell for cell (the store equals a
/// store rebuilt from the model's cohorts), random operation sequences checked after every step, the army code, and the Phasor gaps
/// pinned to the published package. Mutations of this section were watched red (see the report), not only asserted.
/// </summary>
public sealed class PleromaArmyProofs
{
    static readonly PleromaArmyService Service = new();

    static PleromaArmy Inner(IArmy a) => (PleromaArmy)a;

    /// <summary>The model starts where a fresh army starts (read once); from there every step is compared cell for cell.</summary>
    static ArmyModel ModelOf(IArmy a)
    {
        var r = a.Read();
        var m = new ArmyModel(a.Side) { Round = r.Round, Gold = r.Gold };
        foreach (var c in r.Cohorts) { m.Soldiers[c.Key] = c.Count; m.Slot[c.Key] = c.Slot; m.Vet[c.Key] = c.Veterancy; }
        if (r.Cohorts.Count > 0) m.FlipMask = (int)r.Cohorts[0].Traits ^ (int)Roster.TraitsOf(r.Cohorts[0].Key);
        return m;
    }

    static void SameArmy(ArmyModel m, IArmy a, string step)
    {
        Assert.True(Inner(a).Army.Equals(m.Rebuild()), $"after {step}: the Pleroma store differs from the store rebuilt from the model");
        Assert.Equal(m.Read().Digest(), a.Read().Digest());
        Assert.Equal(m.Score(), a.Score());
        // the code carries the whole army: the restored copy reads the same and holds the same cells (auras included)
        Assert.True(Service.TryParse(a.ToCode(), out var back, out var err), $"after {step}: {err}");
        Assert.True(Inner(back!).Army.Equals(Inner(a).Army), $"after {step}: the code lost a cell");
        Assert.Equal(a.Read().Digest(), back!.Read().Digest());
    }

    static async Task<DraftReport> Draft(IArmy a, int kind, int count) =>
        await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(kind, count)]), default);

    static async Task<ArmyOrdersReport> Order(IArmy a, int token) =>
        await a.ApplyArmyOrdersAsync(Doctrine.Of(token), default);

    static void ModelOrder(ArmyModel m, DoctrineOrder o)
    {
        switch ((ArmyOrderKind)o.Kind)
        {
            case ArmyOrderKind.Rotate: m.Rotate(o.A); break;
            case ArmyOrderKind.Flip: m.Flip(o.A); break;
            case ArmyOrderKind.Age: m.Age(); break;
            case ArmyOrderKind.Merge: m.Merge(o.A); break;
            case ArmyOrderKind.Aura: m.Aura(o.A); break;
        }
    }

    static IEnumerable<int> ArmyTokens() => Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army);

    // ---- one operation at a time, cell for cell ----

    [Fact]
    public async Task Draft_matches_the_model_cell_for_cell()
    {
        var a = Service.Create(Side.Player, 5); var m = ModelOf(a);
        await a.ApplySpoilsAsync(new Spoils(40, 0), default); m.Spoils(40, 0);
        SameArmy(m, a, "spoils");
        var rep = await Draft(a, 3, 4); m.Draft(3, 4);
        Assert.Single(rep.Accepted);
        SameArmy(m, a, "draft 4 archers");
        rep = await Draft(a, 3, 2); m.Draft(3, 2);
        SameArmy(m, a, "draft 2 more archers");
        rep = await Draft(a, 7, 999); // too dear: refused, changes nothing
        Assert.Equal("gold", rep.Refused[0].Reason);
        SameArmy(m, a, "refused draft");
    }

    [Fact]
    public async Task Rotate_matches_the_model_cell_for_cell_and_cycles_the_three_slots()
    {
        var a = Service.Create(Side.Player, 8); var m = ModelOf(a);
        foreach (var shift in new[] { 1, 2, 1, 1, 2 })
        {
            Assert.Equal(1, (await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Rotate, shift))).Applied);
            m.Rotate(shift);
            SameArmy(m, a, $"rotate {shift}");
        }
        // three single steps return every cohort to its slot
        var b = Service.Create(Side.Player, 8); var before = b.Read().Cohorts.ToDictionary(c => c.Key, c => c.Slot);
        for (int i = 0; i < 3; i++) await Order(b, DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1));
        Assert.All(b.Read().Cohorts, c => Assert.Equal(before[c.Key], c.Slot));
    }

    [Fact]
    public async Task Flip_matches_the_model_and_a_drafted_kind_takes_the_flipped_traits()
    {
        var a = Service.Create(Side.Player, 4); var m = ModelOf(a);
        await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Flip, 2)); m.Flip(2);
        SameArmy(m, a, "flip 2");
        await Draft(a, 0, 3); m.Draft(0, 3);     // Spearmen join under the flip
        SameArmy(m, a, "draft after flip");
        Assert.Equal((Trait)((int)Roster.TraitsOf(0) ^ (1 << 2)), a.Read().Cohorts.Single(c => c.Key == 0).Traits);
        await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Flip, 2)); m.Flip(2);   // twice is the identity
        SameArmy(m, a, "flip 2 again");
    }

    [Fact]
    public async Task Age_and_spoils_experience_match_the_ring_of_nine()
    {
        var a = Service.Create(Side.Enemy, 6); var m = ModelOf(a);
        for (int i = 0; i < 10; i++) { await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Age)); m.Age(); SameArmy(m, a, $"age {i + 1}"); }
        await a.ApplySpoilsAsync(new Spoils(0, 13), default); m.Spoils(0, 13);
        SameArmy(m, a, "spoils 13 experience (13 mod 9)");
        Assert.All(a.Read().Cohorts, c => Assert.InRange(c.Veterancy, 0, PleromaArmyLayout.Period - 1));
    }

    [Fact]
    public async Task Merge_matches_the_model_and_moves_every_soldier()
    {
        var a = Service.Create(Side.Player, 9); var m = ModelOf(a);
        await Draft(a, 0, 2); m.Draft(0, 2);
        await Draft(a, 1, 3); m.Draft(1, 3);
        int total = a.Read().Soldiers;
        Assert.Equal(1, (await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Merge, 0))).Applied);
        m.Merge(0);
        SameArmy(m, a, "merge kind 1 into kind 0");
        Assert.Equal(total, a.Read().Soldiers);      // a merge keeps every soldier
        Assert.DoesNotContain(a.Read().Cohorts, c => c.Key == 1);
    }

    [Fact]
    public async Task Aura_matches_the_model_and_is_legal_once_per_round()
    {
        var a = Service.Create(Side.Player, 3); var m = ModelOf(a);
        await Draft(a, 1, 1); m.Draft(1, 1);                       // a Shieldwall (Heavy, Shielded) so Heavy has a carrier
        int holy = DoctrineVocabulary.Army(ArmyOrderKind.Aura, 3);  // Holy: legal only with a Priest, as the model says
        Assert.Equal(m.Legal().Allows(holy), a.Read().Legal.Allows(holy));
        Assert.Equal(1, (await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Aura, 0))).Applied);   // Heavy
        m.Aura(0);
        SameArmy(m, a, "aura Heavy");
        Assert.Equal(0, (await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Aura, 0))).Applied);  // already active
        await a.EndRoundAsync(default); m.EndRound();
        SameArmy(m, a, "end of round clears the aura");
    }

    [Fact]
    public async Task Losses_match_the_model_and_clamp()
    {
        var a = Service.Create(Side.Enemy, 12); var m = ModelOf(a);
        var c0 = a.Read().Cohorts.OrderByDescending(c => c.Count).First();
        int removed = await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, 3)], default);
        m.Losses(c0.Key, 3);
        Assert.Equal(3, removed);
        SameArmy(m, a, "3 losses");
        removed = await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, 10_000), new CohortLoss(99, 0, 4), new CohortLoss(c0.Key, 0, -2)], default);
        m.Losses(c0.Key, 10_000);
        SameArmy(m, a, "a huge loss, an unknown kind and a negative loss");
        Assert.True(removed >= 0);
    }

    [Fact]
    public async Task End_of_round_advances_the_clock_in_the_store()
    {
        var a = Service.Create(Side.Player, 2); var m = ModelOf(a);
        await a.EndRoundAsync(default); m.EndRound();
        await a.EndRoundAsync(default); m.EndRound();
        SameArmy(m, a, "two rounds");
        Assert.Equal(2, a.Read().Round);
    }

    // ---- random sequences, every step compared ----

    [Fact]
    public async Task Random_operation_sequences_match_the_model_after_every_step()
    {
        int checkedSteps = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var side = seed % 2 == 0 ? Side.Player : Side.Enemy;
            var a = Service.Create(side, seed); var m = ModelOf(a);
            var r = new GameRng((ulong)seed * 7919UL + 17);
            var tokens = ArmyTokens().ToArray();
            for (int step = 0; step < 60; step++)
            {
                string what;
                switch (r.Next(9))
                {
                    case 0:
                    {
                        int kind = r.Next(Roster.Size), count = 1 + r.Next(4);
                        bool affordable = Roster.PriceOf(kind) * count <= m.Gold;
                        var rep = await Draft(a, kind, count);
                        Assert.Equal(affordable ? 1 : 0, rep.Accepted.Count);
                        if (affordable) m.Draft(kind, count);
                        what = $"draft {kind}x{count}";
                        break;
                    }
                    case 1: case 2: case 3: case 4:
                    {
                        int token = tokens[r.Next(tokens.Length)];
                        bool legal = m.Legal().Allows(token);
                        var rep = await Order(a, token);
                        Assert.Equal(legal ? 1 : 0, rep.Applied);
                        Assert.Equal(legal ? 0 : 1, rep.Skipped);
                        if (legal) ModelOrder(m, DoctrineVocabulary.Decode(token));
                        what = $"order {DoctrineVocabulary.Name(token)} legal={legal}";
                        break;
                    }
                    case 5:
                    {
                        int kind = r.Next(Roster.Size + 1), lost = r.Next(9) - 1;
                        if (kind < Roster.Size) { await a.ApplyLossesAsync([new CohortLoss(kind, 0, lost)], default); m.Losses(kind, lost); }
                        what = $"losses {kind} x{lost}";
                        break;
                    }
                    case 6:
                    {
                        int gold = r.Next(15) - 2, exp = r.Next(4) - 1;
                        await a.ApplySpoilsAsync(new Spoils(gold, exp), default); m.Spoils(gold, exp);
                        what = $"spoils {gold}/{exp}";
                        break;
                    }
                    case 7:
                        await a.EndRoundAsync(default); m.EndRound();
                        what = "end round";
                        break;
                    default:
                    {
                        // a field token is the battle's: the army counts nothing for it
                        int field = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field);
                        var rep = await Order(a, field);
                        Assert.Equal(0, rep.Applied + rep.Skipped);
                        what = "field token";
                        break;
                    }
                }
                SameArmy(m, a, $"seed {seed} step {step} ({what})");
                checkedSteps++;
            }
        }
        Assert.True(checkedSteps >= 2400);
    }

    [Fact]
    public async Task Cull_is_never_legal_on_this_shape()
    {
        var a = Service.Create(Side.Enemy, 21);
        await Draft(a, 2, 1);
        await Draft(a, 5, 1);
        for (int b = 0; b < DoctrineVocabulary.Traits; b++)
            Assert.False(a.Read().Legal.Allows(DoctrineVocabulary.Army(ArmyOrderKind.Cull, b)));
    }

    // ---- the army code ----

    [Fact]
    public async Task Code_round_trip_rebuilds_the_same_cells()
    {
        for (int seed = 1; seed <= 25; seed++)
        {
            var a = Service.Create(Side.Player, seed);
            await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1 + seed % 2));
            await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Flip, seed % 8));
            await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Age));
            await a.ApplySpoilsAsync(new Spoils(seed % 9, seed % 5), default);
            await a.EndRoundAsync(default);
            await a.ApplySpoilsAsync(new Spoils(50, 0), default);
            await Draft(a, seed % 8, 2);
            await Draft(a, 1, 1);                                              // a Shieldwall: the Heavy aura has a carrier
            Assert.Equal(1, (await Order(a, DoctrineVocabulary.Army(ArmyOrderKind.Aura, 0))).Applied);
            Assert.NotEmpty(a.Read().Cohorts.Where(c => c.PowerPct > 100));    // the aura is in the army when the code is made
            var code = a.ToCode();
            Assert.True(Service.TryParse(code, out var b, out var err), err);
            Assert.True(Inner(a).Army.Equals(Inner(b!).Army), $"seed {seed}: the restored store differs cell for cell");
            Assert.Equal(code, b!.ToCode());
            Assert.Equal(a.Score(), b.Score());
        }
    }

    [Fact]
    public void Code_refuses_damage_and_never_throws()
    {
        var good = Service.Create(Side.Player, 7).ToCode();
        var rng = new GameRng(99);
        const string alphabet = "0123456789.:;-DA2x";
        int refused = 0, accepted = 0;
        for (int i = 0; i < 1500; i++)
        {
            var chars = good.ToCharArray();
            int edits = 1 + rng.Next(3);
            for (int e = 0; e < edits; e++) chars[rng.Next(chars.Length)] = alphabet[rng.Next(alphabet.Length)];
            var damaged = new string(chars);
            if (rng.Next(4) == 0) damaged = damaged[..rng.Next(damaged.Length)];
            bool ok = Service.TryParse(damaged, out var army, out var error);
            if (ok)
            {
                accepted++;
                Assert.NotNull(army);
                Assert.True(Service.TryParse(army!.ToCode(), out _, out _), "an accepted code must re-encode to an accepted code");
            }
            else { refused++; Assert.Null(army); Assert.False(string.IsNullOrEmpty(error)); }
        }
        Assert.True(refused > 1000, $"expected most damage refused, got {refused} refused and {accepted} accepted");
        Assert.False(Service.TryParse(new string('9', 5000), out _, out _));
        Assert.False(Service.TryParse("DA2.0.0.-1.0..", out _, out _));
        Assert.False(Service.TryParse("DA2.0.0.5.256..", out _, out _));
        Assert.False(Service.TryParse("DA2.0.0.5.0..0:1:3:0", out _, out _));
        Assert.False(Service.TryParse("DA2.0.0.5.0..1:1:0:0;1:2:0:0", out _, out _));
    }

    // ---- the Phasor gaps, pinned to the published package (a fixed Phasor makes these go red for the right reason) ----

    [Fact]
    public void Restrict_is_refused_on_the_army_shape_pinning_phasor_CG1()
    {
        var p = new Pleroma(PleromaArmyLayout.ArmyShape);
        p.Add(new HologramItem(0, 1, 0, 0), 3);
        Assert.Throws<NotSupportedException>(() => p.Restrict(FlagPredicate.Compile(8, x => (x & 1) == 1)));
        Assert.Equal(3, p.Total());   // refused, untouched
    }

    [Fact]
    public void Bind_multiplies_counts_so_it_cannot_merge_cohorts_pinning_phasor_AG1()
    {
        var a = new Pleroma(PleromaArmyLayout.KeysShape); a.Add(0UL, 3);
        var b = new Pleroma(PleromaArmyLayout.KeysShape); b.Add(0UL, 2);
        a.Bind(b);
        Assert.Equal(6, a.Total());   // 3 x 2, not 3 + 2
    }

    // ---- the game, end to end, with this army swapped in ----

    [Fact]
    public async Task The_game_with_the_pleroma_army_plays_eight_rounds_and_is_deterministic()
    {
        var rules = new DoctrineDomain { MaxRounds = 8 };
        var r1 = await DoctrineBuilder.New().WithArmy(new PleromaArmyService()).WithRules(rules).Build().PlayAsync(41);
        var r2 = await DoctrineBuilder.New().WithArmy(new PleromaArmyService()).WithRules(rules).Build().PlayAsync(41);
        Assert.Equal(8, r1.Rounds.Count);
        Assert.Equal(r1.Digest(), r2.Digest());
        Assert.All(r1.Rounds, x => Assert.Equal(0, x.IllegalDropped));
        Assert.All(r1.Rounds, x => Assert.NotEqual(BattleStatus.Running, x.Result.Outcome));
        Assert.True(Service.TryParse(r1.PlayerArmyCode, out var back, out _));
        Assert.Equal(r1.PlayerArmyCode, back!.ToCode());
        Console.WriteLine($"PLEROMA-ARMY GAME seed 41 digest {r1.Digest():X16} player code {r1.PlayerArmyCode}");
    }
}

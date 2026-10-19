namespace Doctrine.Tests;

/// <summary>The replay format's versions: 2 (world flags, version 1 still read) and 3 (a besieger byte and the clock-fell flag, version 2 still read), and the Tracer section's use of them.</summary>
public class ReplayVersionTests
{
    static BattleReplay Tiny(int version, int flags1, Side? besieger = null) => new(version, "open-field", 7, 5,
        [new ReplayFrame(0, [new ReplayBlob(0, Side.Player, 0, 0, 32, 64, 9), new ReplayBlob(1, Side.Enemy, 1, 0, 900, 64, 9)], []),
         new ReplayFrame(1, [new ReplayBlob(0, Side.Player, 0, 0, 40, 64, 9), new ReplayBlob(1, Side.Enemy, 1, 0, 890, 64, 8)], [new ReplayEvent(0, 1, 1)], flags1)],
        new BattleResult(BattleStatus.AttackerWon, 1, [], [new CohortLoss(1, 0, 1)]), besieger);

    /// <summary>The version-1 layout written independently of ReplayCodec (the fixture a stored recording would be): no world flags.</summary>
    static byte[] HandWrittenVersion1()
    {
        var b = new List<byte>();
        void V(int v) { uint z = (uint)((v << 1) ^ (v >> 31)); while (z >= 0x80) { b.Add((byte)(z | 0x80)); z >>= 7; } b.Add((byte)z); }
        void S(string s) { var u = System.Text.Encoding.UTF8.GetBytes(s); V(u.Length); b.AddRange(u); }
        void Blob(int id, byte side, int key, int slot, int x, int y, int strength) { V(id); b.Add(side); V(key); V(slot); V(x); V(y); V(strength); }
        b.AddRange("DRP"u8.ToArray());
        V(1); S("open-field"); V(7); V(5);          // version 1, arena, seed, tick limit
        V(2);                                        // two frames
        V(0); V(2); Blob(0, 0, 0, 0, 32, 64, 9); Blob(1, 1, 1, 0, 900, 64, 9); V(0);                     // frame 0: tick, blobs, no events (and no flags)
        V(1); V(2); Blob(0, 0, 0, 0, 40, 64, 9); Blob(1, 1, 1, 0, 890, 64, 8); V(1); V(0); V(1); V(1);   // frame 1: one event (0 hit 1 for 1)
        b.Add((byte)BattleStatus.AttackerWon); V(1);                                                       // result: outcome, ticks
        V(0);                                        // attacker losses: none
        V(1); V(1); V(0); V(1);                      // defender losses: kind 1, slot 0, 1 lost
        return b.ToArray();
    }

    [Fact]
    public void Version_two_carries_world_flags_bit_for_bit_and_is_still_what_a_battle_without_a_besieger_is_written_in()
    {
        Assert.Equal(3, BattleReplay.CurrentVersion);
        Assert.Equal(2, BattleReplay.VersionFor(null));   // the lowest version that can carry it: an open-field battle is byte for byte what it was
        Assert.Equal(3, BattleReplay.VersionFor(Side.Player)); Assert.Equal(3, BattleReplay.VersionFor(Side.Enemy));
        var r = Tiny(2, WorldFlag.GateFallen);
        var bytes = ReplayCodec.Encode(r);
        Assert.True(ReplayCodec.TryDecode(bytes, out var back, out var err), err);
        Assert.Equal(2, back!.Version);
        Assert.Equal([0, WorldFlag.GateFallen], back.Frames.Select(f => f.WorldFlags));
        Assert.Equal(ReplayCodec.Digest(r), ReplayCodec.Digest(back));
        Assert.True(ReplayCodec.Encode(back).AsSpan().SequenceEqual(bytes));
    }

    [Fact]
    public void A_version_one_recording_is_still_read_with_no_flags_and_written_back_unchanged()
    {
        var v1 = HandWrittenVersion1();
        Assert.True(ReplayCodec.TryDecode(v1, out var r, out var err), err);
        Assert.Equal(1, r!.Version);
        Assert.All(r.Frames, f => Assert.Equal(0, f.WorldFlags));
        Assert.Equal(2, r.Frames.Count);
        Assert.Equal(BattleStatus.AttackerWon, r.Result.Outcome);
        Assert.True(ReplayCodec.Encode(r).AsSpan().SequenceEqual(v1), "a version-1 recording must round-trip bit for bit in its own layout");
        Assert.Equal(ReplayCodec.Digest(Tiny(1, 0)), ReplayCodec.Digest(r));
    }

    [Fact]
    public void The_digest_sees_world_flags_and_unknown_versions_are_refused()
    {
        Assert.NotEqual(ReplayCodec.Digest(Tiny(2, 0)), ReplayCodec.Digest(Tiny(2, WorldFlag.GateFallen)));
        foreach (var bad in new[] { 0, 4, 99, -1 })
        {
            var bytes = ReplayCodec.Encode(Tiny(2, 0) with { Version = bad });
            Assert.False(ReplayCodec.TryDecode(bytes, out var r, out var why));
            Assert.Null(r);
            Assert.Contains("version", why);
        }
    }

    [Fact]
    public void Cut_version_two_recordings_are_refused_without_throwing()
    {
        var bytes = ReplayCodec.Encode(Tiny(2, WorldFlag.GateFallen));
        for (int cut = 0; cut < bytes.Length; cut++) Assert.False(ReplayCodec.TryDecode(bytes.AsSpan(0, cut), out _, out _), $"cut at {cut}");
    }

    [Fact]
    public async Task Tracer_sets_the_gate_flag_from_the_frame_after_the_gate_falls_and_nowhere_else()
    {
        var engine = TracerWorlds.Engine();
        var layout = engine.LayoutOf("falling-gate")!;
        Assert.True(layout.GateFallsAtTick > 0);
        var army = Enumerable.Range(0, 8).Select(k => Sample.Cohort(k, 90, k % 3)).ToArray();
        var setup = new BattleSetup("falling-gate", 18, 60, new BattleSide(Side.Player, army, []), new BattleSide(Side.Enemy, army, []));
        var replay = (await Sample.RunAsync(engine, setup)).Replay!;
        Assert.True(replay.Frames.Count > layout.GateFallsAtTick + 2, "the fight must outlast the gate");
        foreach (var f in replay.Frames)
            Assert.Equal(f.Tick > layout.GateFallsAtTick, (f.WorldFlags & WorldFlag.GateFallen) != 0);
        Assert.Equal(0, replay.Frames[0].WorldFlags);

        // the gate switched off, and an arena without a gate, never show the bit
        var noGate = (await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { DynamicObstacles = false }), setup)).Replay!;
        Assert.All(noGate.Frames, f => Assert.Equal(0, f.WorldFlags));
        var open = (await Sample.RunAsync(engine, Sample.Setup("open-field", 3, 40))).Replay!;
        Assert.All(open.Frames, f => Assert.Equal(0, f.WorldFlags));
    }

    [Fact]
    public async Task The_stub_battle_never_changes_its_arena()
    {
        var replay = (await Sample.RunAsync(new StubBattleEngine(), Sample.Setup("open-field", 3, 40))).Replay!;
        Assert.All(replay.Frames, f => Assert.Equal(0, f.WorldFlags));
    }
}

/// <summary>The composition root: which sections the game is made of is answered in ONE place, and an explicit choice always wins.</summary>
public class CompositionRootTests
{
    sealed class CountingArmies(IArmyService i) : IArmyService
    {
        public int Calls;
        public IArmy Create(Side s, int seed) { Calls++; return i.Create(s, seed); }
        public bool TryParse(string c, out IArmy? a, out string e) { Calls++; return i.TryParse(c, out a, out e); }
    }

    [Fact]
    public async Task A_default_fills_every_seam_the_caller_left_unset()
    {
        var armies = new CountingArmies(new PleromaArmyService());
        var game = DoctrineBuilder.New().WithDefaults(b => b.WithArmy(armies)).WithRules(new DoctrineDomain { MaxRounds = 2 }).Build();
        Assert.Same(armies, game.Context.Armies);
        Assert.IsType<StubBattleEngine>(game.Context.Battle);       // not named by the root: the stub fills in
        Assert.Equal(2, (await game.PlayAsync(5)).Rounds.Count);
        Assert.True(armies.Calls > 0);
    }

    [Fact]
    public void An_explicit_choice_always_beats_the_default_in_either_order()
    {
        var mine = new StubArmyService(); var theirs = new PleromaArmyService();
        Assert.Same(mine, DoctrineBuilder.New().WithArmy(mine).WithDefaults(b => b.WithArmy(theirs)).Build().Context.Armies);
        Assert.Same(mine, DoctrineBuilder.New().WithDefaults(b => b.WithArmy(theirs)).WithArmy(mine).Build().Context.Armies);
        var engine = new StubBattleEngine();
        var g = DoctrineBuilder.New().WithBattle(engine).WithDefaults(b => b.WithTracer(TracerWorlds.Scratch()).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!))).Build();
        Assert.Same(engine, g.Context.Battle);
    }

    [Fact]
    public async Task An_engine_that_needs_a_world_is_built_by_the_root_from_the_finished_context()
    {
        var world = TracerWorlds.Scratch();
        var game = DoctrineBuilder.New()
            .WithDefaults(b => b.WithArmy(new PleromaArmyService()).WithTracer(world).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!)))
            .WithRules(new DoctrineDomain { MaxRounds = 3 }).Build();
        Assert.IsType<TracerBattleEngine>(game.Context.Battle);
        Assert.Same(world, game.Context.Tracer!.World);
        var a = await game.PlayAsync(41);
        var b = await DoctrineBuilder.New()
            .WithDefaults(x => x.WithArmy(new PleromaArmyService()).WithTracer(TracerWorlds.Scratch()).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!)))
            .WithRules(new DoctrineDomain { MaxRounds = 3 }).Build().PlayAsync(41);
        Assert.Equal(3, a.Rounds.Count);
        Assert.Equal(a.Digest(), b.Digest());
    }

    [Fact]
    public async Task With_no_default_the_game_is_the_reference_stubs_exactly_as_before()
    {
        var plain = await DoctrineBuilder.New().Build().PlayAsync(41);
        var viaRoot = await DoctrineBuilder.New().WithDefaults(_ => { }).Build().PlayAsync(41);
        Assert.Equal(plain.Digest(), viaRoot.Digest());
    }
}

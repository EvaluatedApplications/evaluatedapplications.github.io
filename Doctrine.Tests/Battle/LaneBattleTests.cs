using Doctrine.Lane;

namespace Doctrine.Tests;

// FAST tests of the lane battle (the battler layer). The full proof, the mutants and the cost table are Showroom\tools\DoctrineLaneProof; these pin the contract in about a second.
public class LaneBattleTests
{
    // a short map and a short phase keep every test well under a second
    static LaneSetup Small(LaneProbe probe = LaneProbe.None) => new() { Lanes = 1, LaneCols = 24, PhaseTicks = 60, BaseHp = 3, Probe = probe };
    static Task<LaneBattle> New(LaneSetup s, int seed = 1) => LaneBattle.CreateAsync(s, seed, TracerWorlds.Context());

    [Fact]
    public void The_constants_are_ten_ticks_a_second_and_a_thirty_second_phase()
    {
        Assert.Equal(10, LaneRules.TicksPerSecond);
        Assert.Equal(30, LaneRules.PhaseSeconds);
        Assert.Equal(300, LaneRules.PhaseTicks);
        Assert.Equal(LaneRules.PhaseTicks, new LaneSetup().PhaseTicks);
    }

    [Fact]
    public async Task Same_seed_and_plays_give_the_same_hash_at_every_pause()
    {
        async Task<List<(int, ulong)>> Run()
        {
            var b = await New(Small(), 5);
            b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 6), new SpawnPlay(Side.Enemy, 0, 2, 6)]);
            for (int i = 0; i < 6; i++) await b.StepAsync();
            return b.Replay.Pauses.Select(p => (p.Tick, p.Hash)).ToList();
        }
        var a = await Run();
        Assert.Equal(a, await Run());
        Assert.True(a.Count >= 5);
    }

    [Fact]
    public async Task Stepping_in_chunks_is_the_same_match_as_stepping_by_phase()
    {
        var phase = await New(Small(), 3); phase.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 5), new SpawnPlay(Side.Enemy, 0, 3, 5)]);
        var chunk = await New(Small(), 3); chunk.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 5), new SpawnPlay(Side.Enemy, 0, 3, 5)]);
        for (int i = 0; i < 4; i++) await phase.StepAsync();
        while (chunk.Tick < 240) await chunk.StepAsync(7);
        Assert.Equal(phase.Tick, chunk.Tick);
        Assert.Equal(phase.StateHash(), chunk.StateHash());
    }

    [Fact]
    public async Task A_play_in_the_middle_of_a_phase_is_refused_and_changes_nothing()
    {
        var b = await New(Small());
        await b.StepAsync(25);
        var h = b.StateHash();
        Assert.False(b.AtPause);
        Assert.Throws<InvalidOperationException>(() => b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 1)]));
        Assert.Equal(h, b.StateHash());
        await b.StepAsync();
        Assert.True(b.AtPause);
        Assert.True(b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 1)])[0].Applied);
    }

    [Fact]
    public async Task A_spawn_puts_bodies_in_the_lane_and_the_roster_and_bad_plays_are_refused()
    {
        var b = await New(new LaneSetup { Lanes = 3, LaneCols = 24, PhaseTicks = 60 });
        var r = b.ApplyPlays([new SpawnPlay(Side.Player, 2, 3, 4), new SpawnPlay(Side.Player, 3, 0, 1), new SpawnPlay(Side.Player, 0, 9, 1), new OrderPlay(Side.Player, 0, FieldOrderKind.Flank)]);
        Assert.True(r[0].Applied); Assert.Equal(4, r[0].Accepted);
        Assert.All(r.Skip(1), x => Assert.False(x.Applied));
        var u = b.Snapshot().Units;
        Assert.Equal(4, u.Count);
        Assert.All(u, x => { Assert.Equal(2, x.Lane); Assert.Equal(3, x.Kind); Assert.Equal(Side.Player, x.Side); });
        Assert.Equal(4, b.RosterCount(Side.Player, 2, 3));
        Assert.Equal(4, b.RosterTotal(Side.Player));
    }

    [Fact]
    public async Task A_lone_army_takes_the_base_and_a_finished_match_stops()
    {
        var b = await New(Small());
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 12)]);
        for (int i = 0; i < 40 && b.Status == LaneStatus.Running; i++) await b.StepAsync();
        Assert.Equal(LaneStatus.PlayerWon, b.Status);
        Assert.Equal(0, b.EnemyBaseHp);
        Assert.Equal(b.BaseMaxHp, b.PlayerBaseHp);
        int t = b.Tick;
        Assert.Equal(0, (await b.StepAsync()).TicksRun);
        Assert.Equal(t, b.Tick);
        Assert.False(b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 1)])[0].Applied);
    }

    [Fact]
    public async Task The_hard_cap_is_a_draw_on_the_cap_tick()
    {
        var b = await New(Small() with { TickCap = 100 });
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 2), new OrderPlay(Side.Player, 0, FieldOrderKind.Hold)]);
        while (b.Status == LaneStatus.Running) await b.StepAsync();
        Assert.Equal(LaneStatus.Draw, b.Status);
        Assert.Equal(100, b.Tick);
    }

    [Fact]
    public async Task Survivors_persist_and_the_hash_sees_any_one_hit_point()
    {
        var b = await New(Small(), 2);
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 6), new SpawnPlay(Side.Enemy, 0, 0, 6)]);
        await b.StepAsync(); var before = b.Snapshot().Units.Select(x => x.Id).ToHashSet();
        await b.StepAsync(); await b.StepAsync();
        Assert.All(b.Snapshot().Units, x => Assert.Contains(x.Id, before));   // nobody new appears, the rest are the same bodies
        var h = b.StateHash();
        b.PokeBodyHp(0, -1); Assert.NotEqual(h, b.StateHash()); b.PokeBodyHp(0, 1);
        b.PokeBaseHp(Side.Enemy, -1); Assert.NotEqual(h, b.StateHash()); b.PokeBaseHp(Side.Enemy, 1);
        Assert.Equal(h, b.StateHash());
    }

    [Fact]
    public async Task The_replay_is_plays_plus_seed_and_plays_back_to_the_same_hashes()
    {
        var b = await New(Small(), 9);
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 6), new SpawnPlay(Side.Enemy, 0, 2, 6)]);
        await b.StepAsync();
        b.ApplyPlays([new OrderPlay(Side.Player, 0, FieldOrderKind.Hold), new SpawnPlay(Side.Enemy, 0, 3, 2)]);
        await b.StepAsync(); await b.StepAsync();
        var bytes = LaneReplayCodec.Encode(b.Replay);
        Assert.True(bytes.Length < 200);
        Assert.True(LaneReplayCodec.TryDecode(bytes, out var back, out _));
        var check = await LaneReplayPlayer.PlayAsync(back!, TracerWorlds.Context());
        Assert.True(check.Ok, check.Why);
        Assert.Equal(b.StateHash(), check.Final.StateHash());
        Assert.False(LaneReplayCodec.TryDecode(bytes.AsSpan(0, bytes.Length - 2), out _, out _));
    }

    [Fact]
    public async Task The_map_is_its_own_mirror_with_walls_between_lanes()
    {
        var b = await New(new LaneSetup { Lanes = 3, LaneCols = 24 });
        var l = b.Layout;
        Assert.All(l.Rows, r => Assert.Equal(r, new string(r.Reverse().ToArray())));
        Assert.Equal(3, l.LaneCentreY.Count);
        Assert.All(new[] { 8, 17 }, row => Assert.Contains('#', l.Rows[row]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => New(new LaneSetup { Lanes = 4 }));
    }

    // ---- mutants of this layer: each must break the check named ----

    [Fact] public async Task A_hash_that_ignores_hit_points_is_caught()
    {
        var b = await New(Small(LaneProbe.HashIgnoresHp), 2);
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 3)]);
        var h = b.StateHash(); b.PokeBodyHp(0, -1);
        Assert.Equal(h, b.StateHash());   // the mutant: the same check as above would fail
    }

    [Fact] public async Task A_step_that_runs_past_the_pause_is_caught()
    {
        var b = await New(Small(LaneProbe.PhaseOffByOne));
        var r = await b.StepAsync();
        Assert.NotEqual(0, b.Tick % b.PhaseTicks);   // the mutant overshoots, the real step does not (see The_constants and Stepping_in_chunks)
        Assert.Equal(b.PhaseTicks + 1, r.TicksRun);
    }
}

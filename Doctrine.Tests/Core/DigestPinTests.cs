namespace Doctrine.Tests;

/// <summary>
/// Wire and replay formats are frozen across mechanical changes: an 8-round game under the stub config and under the real-section config
/// (Pleroma army + Tracer battle + tiny HoloFormer general, as the Showroom composes them) keeps these exact digests.
/// </summary>
public class DigestPinTests
{
    static async Task<ulong> Play(bool real, int seed)
    {
        var b = DoctrineBuilder.New().WithPlayer(new ScriptedPlayer(1)).WithRules(new DoctrineDomain { MaxRounds = 8 });
        if (real)
            b.WithDefaults(r => r.WithArmy(new PleromaArmyService()).WithTracer(TracerWorlds.Scratch())
                .WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!)).WithGeneral(new TinyHoloGeneral()));
        var r = await b.Build().PlayAsync(seed);
        Assert.Equal(8, r.Rounds.Count);
        return r.Digest();
    }

    [Fact]
    public async Task The_eight_round_stub_game_digest_is_pinned()
    {
        Assert.Equal(14219199923527622001UL, await Play(false, 1234));
    }

    [Fact]
    public async Task The_eight_round_real_section_game_digest_is_pinned()
    {
        Assert.Equal(1827372264668017971UL, await Play(true, 1234));
    }
}

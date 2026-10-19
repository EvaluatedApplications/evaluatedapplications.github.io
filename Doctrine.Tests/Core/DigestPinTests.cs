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
        // Re-pinned 2026-10-19 (was 14219199923527622001): a Flip now costs gold (Roster.RelabelCost; Doctrine\todo\difficulty-2026-10-19.md). The scripted
        // player and the scripted enemy both order random legal tokens, and the legal list no longer offers a Flip the army cannot pay for.
        // Re-pinned again 2026-10-19, second round (was 1796544520915972963): vocabulary version 2 adds three trigger tokens (ids 54..56, nothing moves), so the
        // scripted players draw from a larger legal list; an Age and an Aura now cost gold and are legal only when affordable; the purse pays interest at the
        // close of a round. All three change what the random doctrines order and what gold the army holds; the wire format of tokens 0..53 is unchanged.
        // Re-pinned a third time 2026-10-19, third round (was 5025665426094534569): Age and Aura are FREE again (the coordinator reverted the pricing), the draft prices are derived from\n        // worth (a Spearman costs 2) for the player, the scripted enemy and the scripted player, and the stub's tick limit counts power, not soldiers. The wire format is unchanged.\n        Assert.Equal(15698248093412764465UL, await Play(false, 1234));
    }

    [Fact]
    public async Task The_eight_round_real_section_game_digest_is_pinned()
    {
        // Re-pinned a fifth time 2026-10-19, second round (was 12988139016327917885): the same three causes as the stub pin above (trigger tokens in the
        // vocabulary, Age and Aura priced, interest on the purse), so the random doctrines and the gold of both sides change. Tracer's battle is untouched
        // for a doctrine without a trigger (A_trigger_that_never_fires_changes_nothing...).
        // Re-pinned a third time 2026-10-19 (was 2333107022278238892): a Flip now costs gold, so the random doctrines of both sides change (see the stub pin above).
        // Re-pinned again 2026-10-19 (was 9094490212791655651): the arena meshes are now baked as their own mirror image (the right half of every arena
        // is split along the reflection of the left half's diagonal). With one diagonal everywhere, mirrored armies leaned to the attacker 43 to 8 on
        // the ridge (large-N sweep, same note). Every Tracer battle changes; the stub digest is untouched.
        // Earlier re-pin 2026-10-19 (was 1827372264668017971): the Tracer battle changed on purpose. The defender's formation block is now mirrored
        // (the attacker's partial last row was its front, the defender's its rear) and agent ids interleave the two sides in conflict priority.
        // Found by the balance harness (Doctrine\todo\balance-harness-2026-10-19.md). The stub digest above is untouched.
        // Re-pinned a sixth time 2026-10-19, third round (was 8746276498987472616): the same army causes as the stub pin (Age and Aura free, derived draft prices) plus the Tracer battle\n        // now has the counter cycle (a class hits the class it counters half again as hard) and counts power at the tick limit. A battle without a counter and without a standoff is unchanged.\n        Assert.Equal(771457392613517103UL, await Play(true, 1234));
    }
}

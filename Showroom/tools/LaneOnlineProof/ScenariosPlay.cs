using System.Text;
using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using static H;

static partial class Scenarios
{
    static (LaneCommitMsg? Commit, LaneRevealMsg? Reveal) Wire(Pair p, int n, LaneRole role)
    {
        string suffix = "/" + n + "/" + LaneOnlineProtocol.RoleName(role);
        var c = p.Mon.Log.Where(l => l.Topic.EndsWith("/commit" + suffix)).Select(l => LaneWire.ReadCommit(l.Payload)).FirstOrDefault();
        var r = p.Mon.Log.Where(l => l.Topic.EndsWith("/reveal" + suffix)).Select(l => LaneWire.ReadReveal(l.Payload)).FirstOrDefault();
        return (c, r);
    }

    // 4. ---- commit, reveal, apply --------------------------------------------------------------------------------------------------

    public static async Task CommitReveal(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("commit and reveal: lock in, show after both, check, apply together");
        var p = new Pair(pa, pb);
        var err = await p.StartAsync();
        Check(err is null && p.A.Phase == OnlinePhase.Playing && p.B.Phase == OnlinePhase.Playing, "both are in the battle: " + err);
        var A = p.A; var B = p.B;
        Check(A.Match!.Stage == MatchStage.Pause && B.Match!.Stage == MatchStage.Pause && A.PauseOpen && B.PauseOpen && A.LocalSide == Side.Player && B.LocalSide == Side.Enemy && A.Match.Tick == 0, "the first pause is open on both, the host on the left base and the guest on the right");
        Check(A.Match!.Human is not null && B.Match!.Human is not null && A.Match.Human.Buy(1, 0, out _) && A.Match.Human.Order(1, FieldOrderKind.Hold, out _) && B.Match.Human.Buy(2, 3, out _), "plays are staged at the pause as in a game against the computer");

        // A is ready first
        var planA = A.Match!.Human!.CurrentPlan(); var planB = B.Match!.Human!.CurrentPlan();
        Check(await A.ReadyAsync() && A.MyCommitted && !await A.ReadyAsync(), "Ready on one side means 'I have committed' (and only once)");
        Check(!A.Match.Human.Buy(0, 0, out var why) && Has(why, "locked") && !A.Match.Human.Order(0, FieldOrderKind.Hunt, out _) && !A.Match.Human.RemovePack(0), "once ready the plays are locked in: no more packs, no orders, no taking back");
        await p.Step(100);
        Check(Has(A.StatusLine, "Waiting for Lord Bo") && Has(B.StatusLine, "Lord Ada is ready") && B.OpponentCommitted && !A.OpponentCommitted, "the one who is ready first sees 'waiting for', the other sees that they are ready");
        Check(A.Match.Stage == MatchStage.Pause && B.Match.Stage == MatchStage.Pause, "nothing is applied while one side has not committed");
        Check(p.Mon.Log.All(l => !l.Topic.Contains("/reveal/")), "no reveal is on the wire while only one commitment is in");
        await B.ReadyAsync();
        await p.Step(100);
        Check(A.Match.Stage == MatchStage.Run && B.Match.Stage == MatchStage.Run && A.Match.Tick == B.Match.Tick && A.Match.Tick < 5 && A.Match.PauseHashes.Count == 1 && B.Match.PauseHashes.Count == 1, "with both committed both reveal and the battle starts on both, from the same pause tick");
        var order = p.Mon.RevealsAfterCommits(p.Code);
        Check(order.Ok, "every reveal went out after BOTH commitments: " + order.Why);

        var ca = Wire(p, 0, LaneRole.Host); var cb = Wire(p, 0, LaneRole.Guest);
        Check(ca.Commit is not null && ca.Reveal is not null && cb.Commit is not null && cb.Reveal is not null, "both commitments and both reveals were published");
        Check(ca.Commit != null && ca.Reveal != null && LaneOnlineProtocol.Verify(ca.Commit.C, p.Code, 0, 0, LaneRole.Host, ca.Reveal.Nonce, ca.Reveal.Plan) && LaneOnlineProtocol.Verify(cb.Commit!.C, p.Code, 0, 0, LaneRole.Guest, cb.Reveal!.Nonce, cb.Reveal.Plan), "each reveal matches its commitment");
        Check(ca.Reveal!.Plan == LanePlanCodec.Encode(planA) && cb.Reveal!.Plan == LanePlanCodec.Encode(planB) && !ca.Reveal.Missed && !cb.Reveal.Missed, "what was revealed is what was staged");
        Check(A.Match.Battle.RosterCount(Side.Player, 1, 0) == LaneEconomy.PackSize && A.Match.Battle.OrderOf(Side.Player, 1) == FieldOrderKind.Hold && A.Match.Battle.Bodies(Side.Enemy) == LaneEconomy.PackSize, "the host's game has the host's pack and order and the guest's pack");
        Check(B.Match.Battle.RosterCount(Side.Player, 1, 0) == LaneEconomy.PackSize && B.Match.Battle.OrderOf(Side.Player, 1) == FieldOrderKind.Hold && B.Match.Battle.Bodies(Side.Enemy) == LaneEconomy.PackSize && A.Match.Battle.StateHash() == B.Match.Battle.StateHash(),
            "the guest's game has the very same bodies and the same state hash");
        Check(A.Match.Human.Packs.Count == 0 && B.Match.Human.Packs.Count == 0 && A.Errors == 0 && B.Errors == 0, "the seats start the run clean and nothing faulted");
        await p.Step(1000, a: true, b: false);
        Check(!A.Match.Human.Buy(0, 0, out _), "in the run plays are refused as ever");
        await p.DisposeAsync();
    }

    public static async Task Tampered(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("a reveal that does not match its commitment is refused");
        var p = new Pair(pa, pb);
        // the guest's client shows the host a plan with one more pack than it committed to
        p.TuneB = o => o.RevealTamper = (n, plan, nonce) => ("0.1," + plan, nonce);
        var err = await p.StartAsync();
        Check(err is null, "joined: " + err);
        p.AutoA = p.AutoB = true;
        await p.Step(250); await p.Step(250); await p.Step(250);
        Check(p.A.End is { Kind: OnlineEndKind.PlaysRefused } && Has(p.A.End.Text, "did not match what they locked in") && p.A.Phase == OnlinePhase.Ended, "the host refuses the reveal and stops the match, saying why: " + p.A.End?.Kind + " " + p.A.End?.Text);
        Check(p.B.End is { Kind: OnlineEndKind.PlaysRefused } && p.B.Phase == OnlinePhase.Ended, "the guest is told the match was stopped because its plays did not match: " + p.B.End?.Kind);
        Check(p.A.Match!.Tick == 0 && p.A.Match.Stage == MatchStage.Pause && p.A.Match.Battle.Bodies(Side.Player) == 0 && p.A.Match.Battle.Bodies(Side.Enemy) == 0, "nothing of that pause was applied on the host's side");
        await p.DisposeAsync();
    }
}
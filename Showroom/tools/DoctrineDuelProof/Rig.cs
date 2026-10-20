using System.Text;
using Doctrine;
using Doctrine.Battle;
using ShowroomDoctrine.Duel;
using ShowroomPlenum;
using static H;

/// <summary>Two services (seat A hosts, seat B joins) on one in-memory broker with a hand-driven clock and a monitor, plus the helpers that play them.</summary>
sealed class Rig
{
    public LoopbackBroker Broker = new();
    public TestClock Clock = new();
    public Monitor Mon = null!;
    public DoctrineDuel A = null!, B = null!;
    public WrapFactory FA = null!, FB = null!;
    public string Code = "TEST01";
    public int Rounds = 3;
    public long Open;                                   // the clock reading when the first round opened
    public readonly List<(string Who, bool ForOpponent)> AutoCalls = [];

    public static DoctrineGame Stub(int rounds)
        => DoctrineBuilder.New().WithArmy(new global::Doctrine.Army.StubArmyService()).WithBattle(new StubBattleEngine())
            .WithRules(new DoctrineDomain { MaxRounds = rounds }).Build();

    public static async Task<Rig> Make(Func<int, DoctrineGame>? game = null, int rounds = 3, int timer = 45, string code = "TEST01", int seed = 7,
        Action<DuelOptions>? tuneA = null, Action<DuelOptions>? tuneB = null, Action<WrapRelay>? wrapA = null, Action<WrapRelay>? wrapB = null, bool start = true)
    {
        var rig = new Rig { Code = code, Rounds = rounds };
        rig.Mon = new Monitor(rig.Broker, rig.Clock);
        rig.FA = new WrapFactory(rig.Broker, wrapA); rig.FB = new WrapFactory(rig.Broker, wrapB);
        DuelOptions Opts(string who, WrapFactory f, Action<DuelOptions>? tune)
        {
            var o = new DuelOptions
            {
                GameFactory = game ?? Stub, Relays = f, Clock = rig.Clock, CreateProbeMs = 0, JoinWaitMs = 300,
                CodeSource = () => code, SeedSource = () => seed,
                AutoPicker = (seat, round, forOpp, ct) => { rig.AutoCalls.Add((who, forOpp)); return DuelAutoPickers.Starter(seat, round, forOpp, ct); },
            };
            tune?.Invoke(o);
            return o;
        }
        rig.A = new DoctrineDuel(Opts("A", rig.FA, tuneA));
        rig.B = new DoctrineDuel(Opts("B", rig.FB, tuneB));
        await rig.A.CreateRoomAsync("Ann", rounds, timer);
        string? err = await rig.B.JoinAsync(code, "Bo");
        Check(err is null, "the second seat joins by code" + (err is null ? "" : ": " + err));
        if (start)
        {
            await rig.B.SetReadyAsync(); await rig.A.SetReadyAsync();
            await rig.Settle();
            rig.Open = rig.Clock.UnixMs;
        }
        return rig;
    }

    public async Task Settle()
    {
        for (int i = 0; i < 3; i++) { await A.SettleAsync(); await B.SettleAsync(); }
    }

    public void Tick() { A.Tick(); B.Tick(); }
    public async Task TickSettle() { Tick(); await Settle(); }

    public static async Task<DuelPicks> PagePick(DoctrineDuel d) => await DuelAutoPickers.Starter(d.LocalSeat!, d.Round, false, default);
    public async Task Seal(DoctrineDuel d) { var p = await PagePick(d); Check(await d.SealAsync(p), $"seat {d.Me} seals in round {d.Round}"); }

    /// <summary>Both seats seal by hand, host first.</summary>
    public async Task SealBoth() { await Seal(A); await Seal(B); await Settle(); }

    public async Task ContinueBoth() { await A.ContinueAsync(); await B.ContinueAsync(); await Settle(); }

    /// <summary>A whole duel with both seats sealing by hand every round; checks each round closes on both seats.</summary>
    public Task PlayAll() => PlayAllFrom(0);
    public async Task PlayAllFrom(int first)
    {
        for (int r = first; r < Rounds; r++)
        {
            Check(A.Phase == DuelPhase.Sealing && B.Phase == DuelPhase.Sealing && A.Round == r && B.Round == r, $"round {r + 1} opens on both seats (A {A.Phase} r{A.Round}, B {B.Phase} r{B.Round})");
            await SealBoth();
            Check(A.Phase == DuelPhase.Result && B.Phase == DuelPhase.Result, $"round {r + 1} closes on both seats (A {A.Phase}, B {B.Phase})");
            await ContinueBoth();
        }
    }

    public static string DigestLine(DoctrineDuel d) => string.Join(",", d.History.Select(h => h.Result.Digest().ToString("x")));

    /// <summary>Both seats hold the same recording, the same facts and a passing check for every round; the final standing is the same.</summary>
    public void VerifyAgree(string what)
    {
        Check(A.History.Count == Rounds && B.History.Count == Rounds, $"{what}: {Rounds} rounds closed on both seats (A {A.History.Count}, B {B.History.Count})");
        int n = Math.Min(A.History.Count, B.History.Count);
        bool sameDigest = true, sameReplay = true, mirror = true, checks = true;
        for (int i = 0; i < n; i++)
        {
            var a = A.History[i]; var b = B.History[i];
            sameDigest &= a.Result.Digest() == b.Result.Digest();
            sameReplay &= ReplayCodec.Digest(a.Result.Replay) == ReplayCodec.Digest(b.Result.Replay) && a.Result.Replay.Frames.Count == b.Result.Replay.Frames.Count;
            mirror &= a.You.You == Seat.A && b.You.You == Seat.B && a.You.YouWon == (a.Result.Winner == Seat.A) && b.You.YouWon == (b.Result.Winner == Seat.B)
                      && !(a.You.YouWon && b.You.YouWon) && a.You.YouBesieged == (a.Result.Besieger == Seat.A) && b.You.YouBesieged == (b.Result.Besieger == Seat.B);
            checks &= a.Check.Ok && b.Check.Ok;
        }
        Check(sameDigest, $"{what}: every round's result digest is equal on both seats");
        Check(sameReplay, $"{what}: both seats hold the same recording (replay digest and frame count) every round");
        Check(mirror, $"{what}: each seat reads the same result as 'you' (outcome and besieger mirror)");
        Check(checks, $"{what}: each seat's own army digest matches the host's account, every round");
        Check(A.Errors == 0 && B.Errors == 0, $"{what}: no internal fault (A {A.Errors}, B {B.Errors})");
        if (A.End is { } ea && B.End is { } eb)
        {
            Check(ea.Kind == DuelEndKind.Final && eb.Kind == DuelEndKind.Final && ea.Score == eb.Score, $"{what}: the same final score on both seats ({ea.Score.FieldsA}-{ea.Score.FieldsB})");
            Check(ea.YouWon != eb.YouWon || ea.YouWon is null, $"{what}: the final result reads as win/loss from each seat, or drawn on both");
        }
    }

    public void VerifyWire(string what, bool revealRule = true)
    {
        var outside = Mon.TopicsOutside(Code).ToList();
        Check(outside.Count == 0, $"{what}: every topic is under plenum/v1/doctrine/{Code}/" + (outside.Count == 0 ? "" : " (found " + string.Join(", ", outside.Take(3)) + ")"));
        if (revealRule)
        {
            var (ok, why) = Mon.RevealsAfterCommits(Code);
            Check(ok, $"{what}: no reveal goes out before both commitments" + (ok ? "" : " - " + why));
        }
    }

    /// <summary>A rogue client re-sends every retained message of the room, as a relay that re-delivers old state would.</summary>
    public async Task Redeliver()
    {
        var rogue = Broker.NewClient("rogue-" + Guid.NewGuid().ToString("N")[..6]);
        await rogue.ConnectAsync();
        foreach (var t in Broker.RetainedTopics.Where(t => t.StartsWith(DuelProtocol.Root + Code + "/", StringComparison.Ordinal)))
            await rogue.PublishAsync(t, Encoding.UTF8.GetBytes(Broker.RetainedText(t)!), false, 1);
        await Settle();
    }

    public string Root => DuelProtocol.Root + Code + "/";
}

using System.Diagnostics;
using Doctrine.Lane;
using ShowroomLane;

// Proof for the Doctrine lane battle (the battler layer). See the csproj header.
var sw = Stopwatch.StartNew();

if (args.Length > 0 && args[0] == "cost") { await Cost.Run(args.Skip(1).ToArray()); return 0; }
if (args.Length > 0 && args[0] == "find-draw") { await Cost.FindDraw(); return 0; }
if (args.Length > 1 && args[0] == "hashes" && int.TryParse(args[1], out var matchSeed))
{
    // the playable match, bot against bot, default setup: the page shows the same short hashes when "Let the bots play" is ticked for the same seed
    var m = await LaneMatch.CreateAsync(new LaneMatchOptions { Seed = matchSeed, Player = new LaneBot(), Enemy = new LaneBot() }, Rig.Ctx());
    await m.PlayToEndAsync();
    Console.WriteLine($"seed {matchSeed}, default setup, bot v bot: {m.PauseHashes.Count} hashes (tick:hash), result {m.Status} at tick {m.Tick}");
    foreach (var (tick, hash) in m.PauseHashes) Console.WriteLine($"  tick {tick,5}  {LaneMatch.ShortHash(hash)}  {hash:x16}");
    return 0;
}
if (args.Length > 0 && args[0] == "hashes")
{
    // the pause hashes of two standard matches, for comparing runs across machines, processor counts and builds
    foreach (var (lanes, seed) in new[] { (3, 7), (2, 9) })
    {
        var m = await Rig.Run(new LaneSetup { Lanes = lanes, BaseHp = 500 }, seed, Rig.StandardPlan(lanes), 8);
        Console.WriteLine($"lanes {lanes} seed {seed}: " + string.Join(" ", m.Replay.Pauses.Select(x => $"{x.Tick}:{x.Hash:x16}")));
    }
    return 0;
}
if (args.Length > 0 && args[0] == "lean") { await Cost.Lean(args.Length > 1 && args[1] == "swap", args.Length > 2 ? int.Parse(args[2]) : 60); return 0; }
if (args.Length > 0 && args[0] == "debug") { await Cost.Debug(); return 0; }

async Task Run(string name, Func<Task> f)
{
    try { await f(); } catch (Exception e) { H.Check(false, name + " threw " + e.GetType().Name + ": " + e.Message + (e.StackTrace is { } st ? "\n" + string.Join("\n", st.Split('\n').Take(4)) : "")); }
    Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");
}

await Run("determinism", () => Scenarios.Determinism(LaneProbe.None));
await Run("phase split", () => Scenarios.PhaseSplit(LaneProbe.None));
await Run("plays at a pause", () => Scenarios.PlaysAtPause(LaneProbe.None));
await Run("play effects", () => Scenarios.PlayEffects(LaneProbe.None));
await Run("persistence", () => Scenarios.Persistence(LaneProbe.None));
await Run("win draw cap", () => Scenarios.WinDrawCap(LaneProbe.None));
await Run("hash sensitivity", () => Scenarios.HashSensitivity(LaneProbe.None));
await Run("counter ring", () => Scenarios.CounterRing(LaneProbe.None));
await Run("ground", Scenarios.Ground);
await Run("replay", Scenarios.Replay);
await Run("match: timing", () => MatchProof.Timing(LaneMatchProbe.None));
await Run("match: plays at the pause", () => MatchProof.PlaysAtPause(LaneMatchProbe.None));
await Run("match: economy", () => MatchProof.Economy(LaneMatchProbe.None));
await Run("match: determinism and the bot", () => MatchProof.Determinism(LaneMatchProbe.None));
await Run("match: picture", () => MatchProof.Picture(LaneMatchProbe.None));

H.Section("mutants (each breaks the sim in ONE way and must turn the named group red)");
int caught = 0, total = 0;
async Task Mutant(string name, LaneProbe p, Func<LaneProbe, Task> group)
{
    total++;
    H.Quiet = true; H.BatchFails = 0;
    string how = "";
    try { await group(p); } catch (Exception e) { H.BatchFails++; how = " (threw " + e.GetType().Name + ")"; }
    int red = H.BatchFails; string first = H.FirstRed;
    H.Quiet = false;
    if (red > 0) { caught++; Console.WriteLine($"  caught: {name}: {red} check(s) red{how}; first: {first}"); }
    else { H.Failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); }
}
await Mutant("plays are accepted mid-phase", LaneProbe.PlaysMidPhase, Scenarios.PlaysAtPause);
await Mutant("a step runs one tick past the pause", LaneProbe.PhaseOffByOne, Scenarios.PhaseSplit);
await Mutant("the strike cooldown is re-seeded at every Step call", LaneProbe.CooldownResetsPerStep, Scenarios.PhaseSplit);
await Mutant("ids are handed out in play order, so arrival order of two players' plays matters", LaneProbe.IdsInPlayOrder, Scenarios.Determinism);
await Mutant("the spawn jitter is unseeded", LaneProbe.UnseededJitter, Scenarios.Determinism);
await Mutant("the hash leaves the base hit points out", LaneProbe.HashIgnoresBase, Scenarios.HashSensitivity);
await Mutant("the hash leaves the body hit points out", LaneProbe.HashIgnoresHp, Scenarios.HashSensitivity);
await Mutant("the hash leaves the last body's position out", LaneProbe.HashIgnoresLastBody, Scenarios.HashSensitivity);
await Mutant("survivors vanish at every pause", LaneProbe.SurvivorsClearedAtPause, Scenarios.Persistence);
await Mutant("the dead stay in the Pleroma roster", LaneProbe.RosterKeepsTheDead, Scenarios.Persistence);
await Mutant("a spawn ignores its lane", LaneProbe.SpawnIgnoresLane, Scenarios.PlayEffects);
await Mutant("an order play changes nothing", LaneProbe.OrderIgnored, Scenarios.PlayEffects);
await Mutant("the body cap is ignored", LaneProbe.CapOnBodiesIgnored, Scenarios.PlayEffects);
await Mutant("a fallen base does not end the match", LaneProbe.BaseFallDoesNotEnd, Scenarios.WinDrawCap);
await Mutant("the hard tick cap is ignored", LaneProbe.CapIgnored, Scenarios.WinDrawCap);
await Mutant("both bases falling at once is scored a player win", LaneProbe.DrawIsAWin, Scenarios.WinDrawCap);
await Mutant("the counter ring is not applied", LaneProbe.CountersOff, Scenarios.CounterRing);
await Mutant("the counter ring is read backwards", LaneProbe.CountersReversed, Scenarios.CounterRing);
async Task MatchMutant(string name, LaneMatchProbe p, Func<LaneMatchProbe, Task> group)
{
    total++;
    H.Quiet = true; H.BatchFails = 0;
    string how = "";
    try { await group(p); } catch (Exception e) { H.BatchFails++; how = " (threw " + e.GetType().Name + ")"; }
    int red = H.BatchFails; string first = H.FirstRed;
    H.Quiet = false;
    if (red > 0) { caught++; Console.WriteLine($"  caught: {name}: {red} check(s) red{how}; first: {first}"); }
    else { H.Failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); }
}
await MatchMutant("match: a human may stage plays in the middle of a run", LaneMatchProbe.PlaysAnyTime, MatchProof.PlaysAtPause);
await MatchMutant("match: the pause clock never ends the pause", LaneMatchProbe.PauseClockIgnored, MatchProof.Timing);
await MatchMutant("match: Ready does nothing", LaneMatchProbe.ReadyIgnored, MatchProof.PlaysAtPause);
await MatchMutant("match: unspent points are dropped at the next pause", LaneMatchProbe.CarryLost, MatchProof.Economy);
await MatchMutant("match: the bot draws from an unseeded source", LaneMatchProbe.BotUnseeded, MatchProof.Determinism);
await MatchMutant("match: the bot ignores the match seed", LaneMatchProbe.BotIgnoresSeed, MatchProof.Determinism);
await MatchMutant("match: a plan over the purse is accepted", LaneMatchProbe.OverBudgetAccepted, MatchProof.Economy);
await MatchMutant("match: speed shortens the pause", LaneMatchProbe.SpeedScalesPause, MatchProof.Timing);
await MatchMutant("match: the run counts eleven ticks a second", LaneMatchProbe.RunTooFast, MatchProof.Timing);
await MatchMutant("match: the picture between ticks is drawn from the wrong end", LaneMatchProbe.InterpolationReversed, MatchProof.Picture);
await MatchMutant("match: the two sides' plays are applied in two batches", LaneMatchProbe.PlaysInTwoBatches, MatchProof.PlaysAtPause);
H.Check(caught == total, $"{caught} of {total} mutants caught");

Console.WriteLine(H.Failures == 0 ? $"ALL PASS ({H.Checks} checks, {sw.Elapsed.TotalSeconds:F1} s)" : $"{H.Failures} FAILED of {H.Checks}");
return H.Failures == 0 ? 0 : 1;

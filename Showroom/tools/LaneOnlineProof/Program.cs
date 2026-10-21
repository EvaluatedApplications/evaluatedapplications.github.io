using System.Diagnostics;
using ShowroomLane;
using ShowroomPlenum;

// Proof for the online lane battle. See the csproj header.
var sw = Stopwatch.StartNew();

var argv = args.Where(a => !a.StartsWith("-")).ToArray();   // (dotnet run may hand its own switches on)
if (argv.Length > 0 && argv[0] == "live")
{
    int code = await Live.Run(argv.Length > 1 ? argv[1] : PlenumRelayConfig.DefaultUrl);
    Console.WriteLine(H.Failures == 0 && code == 0 ? $"LIVE PASS ({H.Checks} checks)" : $"LIVE FAILED (exit {code}, {H.Failures} failed checks)");
    return code;
}

if (argv.Length > 0 && argv[0] == "sweep") return await Live.Sweep(argv.Length > 1 ? argv[1] : PlenumRelayConfig.DefaultUrl);

string only = argv.FirstOrDefault() ?? "";
async Task Run(string name, Func<Task> f)
{
    if (only.Length > 0 && only != name) return;
    var t = f();
    if (await Task.WhenAny(t, Task.Delay(180_000)) != t) { H.Check(false, name + " timed out"); return; }
    try { await t; } catch (Exception e) { H.Check(false, name + " threw " + e.GetType().Name + ": " + e.Message + (e.StackTrace is { } st ? "\n" + string.Join("\n", st.Split('\n').Take(5)) : "")); }
    Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");
}

var N = LaneOnlineProbe.None;
await Run("wire", () => { Scenarios.Wire(); return Task.CompletedTask; });
await Run("lobby list", () => Scenarios.LobbyList(LaneLobbyProbe.None));
await Run("rooms", () => Scenarios.Rooms(N, N));
await Run("commit reveal", () => Scenarios.CommitReveal(N, N));
await Run("tampered", () => Scenarios.Tampered(N, N));
await Run("full", () => Scenarios.FullMatch(N, N));
await Run("mismatch", () => Scenarios.HashMismatch(N, N));
await Run("messages", () => Scenarios.Messages(N, N));
await Run("rematch", () => Scenarios.Rematch(N, N));
await Run("topics", () => Scenarios.Topics(N, N));
await Run("mirror", () => Scenarios.Mirror());
await Run("timeouts", () => Scenarios.Timeouts(N, N));
await Run("disconnects", () => Scenarios.Disconnects(N, N));

if (only.Length == 0)
{
    H.Section("mutants (each breaks ONE thing of the service and must turn the named group red)");
    int caught = 0, total = 0;
    async Task Mutant(string name, Func<Task> group)
    {
        total++;
        H.Quiet = true; H.BatchFails = 0;
        string how = "";
        try { var t = group(); if (await Task.WhenAny(t, Task.Delay(120_000)) != t) { H.BatchFails++; how = " (timed out)"; } else await t; }
        catch (Exception e) { H.BatchFails++; how = " (threw " + e.GetType().Name + ")"; }
        int red = H.BatchFails; string first = H.FirstRed;
        H.Quiet = false;
        if (red > 0) { caught++; Console.WriteLine($"  caught: {name}: {red} check(s) red{how}; first: {first}"); }
        else { H.Failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); }
    }
    await Mutant("a reveal goes out right after the player's own commitment (before the other's)", () => Scenarios.CommitReveal(LaneOnlineProbe.RevealBeforeCommits, N));
    await Mutant("a tampered reveal is accepted", () => Scenarios.Tampered(LaneOnlineProbe.AcceptTamperedReveal, N));
    await Mutant("the pause hashes are never compared", () => Scenarios.HashMismatch(LaneOnlineProbe.IgnoreHashMismatch, LaneOnlineProbe.IgnoreHashMismatch));
    await Mutant("a client whose battle leaves the other's plays out", () => Scenarios.FullMatch(N, LaneOnlineProbe.DropsRemotePlays));
    await Mutant("a client whose hash message tells a different number", () => Scenarios.FullMatch(N, LaneOnlineProbe.LyingHash));
    await Mutant("no pause is ever declared missed", () => Scenarios.Timeouts(LaneOnlineProbe.NeverDeclareMiss, N));
    await Mutant("a pause is declared missed with no grace", () => Scenarios.Timeouts(LaneOnlineProbe.NoGrace, N));
    await Mutant("missed pauses in a row are never counted towards the end", () => Scenarios.Timeouts(LaneOnlineProbe.MissStreakIgnored, N));
    await Mutant("a reveal that says 'you were too late' does not void the late player's plays", () => Scenarios.Timeouts(N, LaneOnlineProbe.FlagIgnored));
    await Mutant("a second commitment or reveal for the same pause replaces the first", () => Scenarios.Messages(LaneOnlineProbe.NoIdempotence, LaneOnlineProbe.NoIdempotence));
    await Mutant("a message of an older game is read as the current one", () => Scenarios.Rematch(LaneOnlineProbe.GenIgnored, LaneOnlineProbe.GenIgnored));
    await Mutant("presence is published outside plenum/v1/doctrine-lane/", () => Scenarios.Topics(LaneOnlineProbe.WrongTopic, LaneOnlineProbe.WrongTopic));
    await Mutant("a room whose host went silent long ago is still joined", () => Scenarios.Rooms(N, LaneOnlineProbe.StaleRoomAccepted));
    await Mutant("a player who stays away past the grace never loses the match for it", () => Scenarios.Disconnects(LaneOnlineProbe.NeverGone, N));
    await Mutant("lobby entries never expire", () => Scenarios.LobbyList(LaneLobbyProbe.NoExpiry));
    await Mutant("rooms of another build are listed", () => Scenarios.LobbyList(LaneLobbyProbe.ShowsOtherBuilds));
    await Mutant("a lobby message whose code differs from its topic is believed", () => Scenarios.LobbyList(LaneLobbyProbe.CodeNotChecked));
    H.Check(caught == total, $"{caught} of {total} mutants caught");
}

Console.WriteLine(H.Failures == 0 ? $"ALL PASS ({H.Checks} checks, {sw.Elapsed.TotalSeconds:F1} s)" : $"{H.Failures} FAILED of {H.Checks}");
return H.Failures == 0 ? 0 : 1;

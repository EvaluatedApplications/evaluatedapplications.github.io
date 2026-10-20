using System.Diagnostics;
using ShowroomDoctrine.Duel;
using ShowroomPlenum;

// Proof for the Doctrine duel service. See the csproj header.
var sw = Stopwatch.StartNew();

if (args.Length > 0 && args[0] == "live")
{
    int code = await Live.Run(args.Length > 1 ? args[1] : PlenumRelayConfig.DefaultUrl);
    Console.WriteLine(H.Failures == 0 && code == 0 ? $"LIVE PASS ({H.Checks} checks)" : $"LIVE FAILED (exit {code}, {H.Failures} failed checks)");
    return code;
}

async Task Run(string name, Func<Task> f)
{
    var t = f();
    if (await Task.WhenAny(t, Task.Delay(60000)) != t) { H.Check(false, name + " timed out"); return; }
    try { await t; } catch (Exception e) { H.Check(false, name + " threw " + e.GetType().Name + ": " + e.Message); }
}

await Run("wire", () => { Scenarios.Wire(); return Task.CompletedTask; });
await Run("real duel", Scenarios.RealDuel); Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");
await Run("chunked", Scenarios.Chunked);
await Run("timeout", Scenarios.Timeout);
await Run("tamper", Scenarios.Tamper);
await Run("duplicates", Scenarios.Duplicates);
await Run("dropped", Scenarios.Dropped);
await Run("host leaves", Scenarios.HostLeaves);
await Run("rematch", Scenarios.Rematch);
Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");

H.Section("mutants (each must turn at least one check red)");
int caught = 0, total = 0;
async Task Mutant(string name, Action set)
{
    total++;
    DoctrineDuelFaults.Reset(); set();
    H.Quiet = true; H.BatchFails = 0;
    string how = "";
    try { await Task.WhenAny(Scenarios.Core(), Task.Delay(40000)); } catch (Exception e) { H.BatchFails++; how = " (threw " + e.GetType().Name + ")"; }
    int red = H.BatchFails;
    H.Quiet = false; DoctrineDuelFaults.Reset();
    if (red > 0) { caught++; Console.WriteLine($"  caught: {name}: {red} check(s) red{how}; first: {H.FirstRed}"); }
    else { H.Failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); }
}
await Mutant("reveal sent before both commitments (order swapped)", () => DoctrineDuelFaults.RevealBeforeCommits = true);
await Mutant("the clock is ignored (no auto-seal)", () => DoctrineDuelFaults.IgnoreTimeout = true);
await Mutant("a duplicate is applied again", () => DoctrineDuelFaults.NoIdempotence = true);
await Mutant("a tampered reveal is accepted", () => DoctrineDuelFaults.AcceptTamperedReveal = true);
await Mutant("a topic outside plenum/v1/doctrine/<code>/", () => DoctrineDuelFaults.WrongTopic = true);
H.Check(caught == total, $"{caught} of {total} mutants caught");

Console.WriteLine(H.Failures == 0 ? $"ALL PASS ({H.Checks} checks, {sw.Elapsed.TotalSeconds:F1} s)" : $"{H.Failures} FAILED of {H.Checks}");
return H.Failures == 0 ? 0 : 1;

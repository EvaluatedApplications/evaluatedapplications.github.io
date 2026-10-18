using System.Diagnostics;
using ShowroomPlenum;

// Proof for the Plenum duel and relay. See the csproj header.
var sw = Stopwatch.StartNew();

if (args.Length > 0 && args[0] == "live")
{
    int code = await Live.Run(args.Length > 1 ? args[1] : PlenumRelayConfig.DefaultUrl);
    Console.WriteLine(H.Failures == 0 && code == 0 ? $"LIVE PASS ({H.Checks} checks)" : $"LIVE FAILED (exit {code}, {H.Failures} failed checks)");
    return code;
}

await Wire.Run();
Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");
await Scenarios.Lobby();
await Scenarios.Duel();
await Scenarios.Random20();
Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:F1} s)");

// ---------------------------------------------------------------------------------------------- mutants
H.Section("mutants (each must turn at least one duel check red)");
int caught = 0, total = 0;
async Task Mutant(string name, Action set)
{
    total++;
    PlenumFaults.Reset(); set();
    H.Quiet = true; H.BatchFails = 0;
    string how = "";
    try { await Scenarios.Duel(); } catch (Exception e) { H.BatchFails++; how = " (threw " + e.GetType().Name + ")"; }
    int red = H.BatchFails;
    H.Quiet = false; PlenumFaults.Reset();
    if (red > 0) { caught++; Console.WriteLine($"  caught: {name}: {red} check(s) red{how}; first: {H.FirstRed}"); }
    else { H.Failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); }
}
await Mutant("orders applied seat 1 before seat 0", () => PlenumFaults.SwapApplyOrder = true);
await Mutant("a repeated order is applied again (not idempotent)", () => PlenumFaults.NoIdempotence = true);
await Mutant("round hash comparison skipped", () => PlenumFaults.SkipHashCompare = true);
await Mutant("Flip target ignored (always my own board)", () => PlenumFaults.IgnoreFlipTarget = true);
await Mutant("previous-state check on orders skipped", () => PlenumFaults.SkipPrevCheck = true);
await Mutant("any client may order for any seat", () => PlenumFaults.TrustAnySeat = true);
await Mutant("a spectator may submit an order", () => PlenumFaults.SpectatorsMaySubmit = true);
H.Check(caught == total, $"{caught} of {total} mutants caught");

Console.WriteLine(H.Failures == 0 ? $"ALL PASS ({H.Checks} checks, {sw.Elapsed.TotalSeconds:F1} s)" : $"{H.Failures} FAILED of {H.Checks}");
return H.Failures == 0 ? 0 : 1;

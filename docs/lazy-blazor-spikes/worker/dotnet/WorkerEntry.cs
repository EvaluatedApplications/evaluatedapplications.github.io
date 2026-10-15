using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Spike.Worker;

/// <summary>Spike: the .NET side of a worker runtime. One generic entry (Call) dispatches by name; progress and items go out through one JSImport.</summary>
[SupportedOSPlatform("browser")]
public static partial class WorkerEntry
{
    static readonly Dictionary<int, CancellationTokenSource> Live = new();

    [JSImport("emit", "worker-host.mjs")]
    static partial void Emit(int callId, string kind, string json);

    [JSExport]
    public static async Task<string> Call(int callId, string method, string argsJson)
    {
        var cts = new CancellationTokenSource();
        Live[callId] = cts;
        try
        {
            var a = JsonDocument.Parse(argsJson).RootElement;
            switch (method)
            {
                case "Echo": return argsJson;
                case "Primes": return (await PrimesAsync(a[0].GetInt32(), p => Emit(callId, "progress", p.ToString("F3")), cts.Token)).ToString();
                case "Stream": { int n = a[0].GetInt32(); for (int i = 0; i < n; i++) { cts.Token.ThrowIfCancellationRequested(); Emit(callId, "item", i.ToString()); if (i % 50 == 0) await Task.Delay(1); } return n.ToString(); }
                case "BusyNoYield": { var sw = Stopwatch.StartNew(); long x = 0; while (sw.ElapsedMilliseconds < a[0].GetInt32()) x++; return x.ToString(); }
                case "Heap": return GC.GetTotalMemory(false).ToString();
                default: throw new InvalidOperationException("no method " + method);
            }
        }
        catch (OperationCanceledException) { return "\u0001canceled"; }   // a canceled Task returned through a JSExport Promise never settled in this runtime: report cancellation as a value
        finally { Live.Remove(callId); }
    }

    [JSExport]
    public static void Cancel(int callId) { bool f = Live.TryGetValue(callId, out var c); if (f) c.Cancel(); }

    // Cooperative: a 12 ms slice, then hand the thread back to the event loop so a cancel message (and the progress post) can be delivered.
    static async Task<int> PrimesAsync(int limit, Action<double> progress, CancellationToken ct)
    {
        int count = 0; var sw = Stopwatch.StartNew();
        for (int n = 2; n <= limit; n++)
        {
            bool prime = true;
            for (int d = 2; (long)d * d <= n; d++) if (n % d == 0) { prime = false; break; }
            if (prime) count++;
            if ((n & 1023) == 0 && sw.ElapsedMilliseconds >= 12)
            {
                ct.ThrowIfCancellationRequested();
                progress((double)n / limit);
                await Task.Delay(1, CancellationToken.None);   // NOT Delay(0): that returns an already-completed task and never reaches the event loop
                sw.Restart();
            }
        }
        return count;
    }
}
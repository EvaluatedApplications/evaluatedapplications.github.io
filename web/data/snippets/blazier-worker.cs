using Blazier.Workers;

[WorkerService]
public interface IPrimes
{
    Task<int> CountAsync(int limit, IProgress<double> progress, CancellationToken cancellationToken);
}

public sealed class Primes : IPrimes
{
    public async Task<int> CountAsync(int limit, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var slice = new CooperativeSlice();   // yields about every 12 ms so a cancel message can be read
        int count = 0;
        for (int n = 2; n <= limit; n++)
        {
            bool prime = true;
            for (int d = 2; d * d <= n; d++) if (n % d == 0) { prime = false; break; }
            if (prime) count++;
            if (slice.IsDue)
            {
                progress.Report((double)n / limit);
                await slice.YieldIfDueAsync(cancellationToken);
            }
        }
        return count;
    }
}

public static class PrimeDemo
{
    // Call from a button handler: the work runs in a Web Worker and the page stays responsive.
    public static async Task<int> RunAsync(Action<double> onProgress, CancellationToken cancellationToken)
    {
        await using var worker = await BlazierWorker.StartAsync();
        return await worker.Get<IPrimes>().CountAsync(5_000_000, new Progress<double>(onProgress), cancellationToken);
    }
}

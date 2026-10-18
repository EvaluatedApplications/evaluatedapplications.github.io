namespace Doctrine.Tests;

/// <summary>A contract invariant that did not hold. Thrown by <see cref="Expect"/>, caught by the case runner.</summary>
public sealed class ContractViolation(string message) : Exception(message);

public static class Expect
{
    public static void True(bool ok, string because) { if (!ok) throw new ContractViolation(because); }
    public static void Equal<T>(T expected, T actual, string what)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new ContractViolation($"{what}: expected {expected}, got {actual}"); }
}

/// <summary>One named invariant of a seam. <see cref="RunAsync"/> returns null when it holds, else why it does not.</summary>
public sealed record ContractCase<T>(string Name, Func<T, Task> Body)
{
    public async Task<string?> RunAsync(T sut)
    {
        try { await Body(sut); return null; }
        catch (ContractViolation v) { return v.Message; }
        catch (Exception e) { return $"threw {e.GetType().Name}: {e.Message}"; }
    }
}

public sealed record ContractFailure(string Case, string Why) { public override string ToString() => $"{Case}: {Why}"; }

public static class ContractRunner
{
    /// <summary>Run every case on a fresh system under test from <paramref name="create"/>. Empty list = the contract holds.</summary>
    public static async Task<IReadOnlyList<ContractFailure>> RunAllAsync<T>(IReadOnlyList<ContractCase<T>> cases, Func<T> create)
    {
        var failures = new List<ContractFailure>();
        foreach (var c in cases)
        {
            var why = await c.RunAsync(create());
            if (why is not null) failures.Add(new(c.Name, why));
        }
        return failures;
    }
}

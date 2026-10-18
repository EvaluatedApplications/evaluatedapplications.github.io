using Tracer;
using Tracer.Integration;

namespace Doctrine.Tests;

/// <summary>Tracer test worlds keep their tuner state in a scratch folder of their own (removed when the test process exits), never in the shared temp folder.</summary>
public static class TracerWorlds
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "doctrine-tests", Guid.NewGuid().ToString("N"));
    static TracerWorlds() => AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };

    public static PathfindingWorld Scratch() => new(tunerStatePath: Path.Combine(Root, Guid.NewGuid().ToString("N")));
    public static TracerContext Context() => new(Scratch());
    public static TracerBattleEngine Engine(TracerBattleOptions? options = null) => new(Context(), options);
}

// The Tracer battle section held to the same battle and replay contracts as the stub.
public sealed class TracerBattleContract : BattleContractTests { protected override IBattleEngine Create() => TracerWorlds.Engine(); }
public sealed class TracerReplayContract : ReplayContractTests { protected override IBattleEngine Create() => TracerWorlds.Engine(); }

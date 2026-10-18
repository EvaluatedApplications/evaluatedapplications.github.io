global using Xunit;
global using Doctrine.Army;
global using Doctrine.Battle;
global using Doctrine.General;
global using Doctrine.Player;

namespace Doctrine.Tests;

// The xunit faces of the contracts. Each section owner adds ONE derived class in their own folder, e.g.
//   public sealed class RealArmyContract : ArmyContractTests { protected override IArmyService Create() => new PleromaArmyService(); }
// and the same cases that bind the stub then bind the real section. Do not copy or edit the cases; amend CONTRACT.md and the case together.

public abstract class ArmyContractTests
{
    protected abstract IArmyService Create();
    public static TheoryData<string> CaseNames => new(ArmyContract.Cases.Select(c => c.Name));
    [Theory, MemberData(nameof(CaseNames))]
    public async Task Holds(string name) => Assert.Null(await ArmyContract.Cases.Single(c => c.Name == name).RunAsync(Create()));
}

public abstract class BattleContractTests
{
    protected abstract IBattleEngine Create();
    public static TheoryData<string> CaseNames => new(BattleContract.Cases.Select(c => c.Name));
    [Theory, MemberData(nameof(CaseNames))]
    public async Task Holds(string name) => Assert.Null(await BattleContract.Cases.Single(c => c.Name == name).RunAsync(Create()));
}

/// <summary>Replay invariants on the recordings an engine makes: a short fight, a long one, a one-sided one, a tick-limited one.</summary>
public abstract class ReplayContractTests
{
    protected abstract IBattleEngine Create();
    public static TheoryData<string> CaseNames => new(ReplayContract.Cases.Select(c => c.Name));

    public static IEnumerable<(string Name, BattleSetup Setup)> Fights(IBattleEngine e) =>
    [
        ("short", Sample.Setup(e.Arenas[0].Id, 21, 25, 12, 12)),
        ("long", Sample.Setup(e.Arenas[^1].Id, 22, 120, 40, 40)),
        ("one-sided", Sample.Setup(e.Arenas[0].Id, 23, 120, 200, 5)),
        ("limited", Sample.Setup(e.Arenas[0].Id, 24, 2, 40, 40)),
    ];

    [Theory, MemberData(nameof(CaseNames))]
    public async Task Holds(string name)
    {
        var e = Create();
        foreach (var (fight, setup) in Fights(e))
        {
            var replay = (await Sample.RunAsync(e, setup)).Replay!;
            Assert.True(null == await ReplayContract.Cases.Single(c => c.Name == name).RunAsync(replay), $"{name} failed on the '{fight}' fight");
        }
    }
}

public abstract class GeneralContractTests
{
    protected abstract IGeneral Create();
    protected virtual int Examples => GeneralContract.DefaultImitationExamples;
    public static TheoryData<string> CaseNames => new(GeneralContract.Cases.Select(c => c.Name));
    [Theory, MemberData(nameof(CaseNames))]
    public async Task Holds(string name)
    {
        var cases = GeneralContract.Build(Examples);
        Assert.Null(await cases.Single(c => c.Name == name).RunAsync(Create));
    }
}

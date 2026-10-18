namespace Doctrine.Tests;

// The stub held to the battle and replay contracts. tracer-owner: add your own derived classes beside these.
public sealed class StubBattleContract : BattleContractTests { protected override IBattleEngine Create() => new StubBattleEngine(); }
public sealed class StubReplayContract : ReplayContractTests { protected override IBattleEngine Create() => new StubBattleEngine(); }

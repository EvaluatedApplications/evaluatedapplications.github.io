namespace Doctrine.Tests;

// The stub held to the army contract. phasor-owner: add your own derived class beside this one (do not replace it until the real section is in).
public sealed class StubArmyContract : ArmyContractTests { protected override IArmyService Create() => new StubArmyService(); }
public sealed class StubArmyPricedContract : ArmyPricedContractTests { protected override IArmyService Create() => new StubArmyService(ArmyContract.PricedRules); }

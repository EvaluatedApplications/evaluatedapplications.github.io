namespace Doctrine.Tests;

// The Pleroma army held to the same 19 ArmyContract cases as the stub (phasor-owner's section).
public sealed class PleromaArmyContract : ArmyContractTests { protected override IArmyService Create() => new PleromaArmyService(); }

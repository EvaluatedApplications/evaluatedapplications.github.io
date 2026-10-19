namespace Doctrine.Tests;

// The real general held to the shared general contract, in all three modes. The default (the two tables, no net) is what the game plugs in.
public sealed class TinyHoloGeneralContract : GeneralContractTests { protected override IGeneral Create() => new TinyHoloGeneral(); }

// Hybrid (tables plus the per-card net) is still offered by the options, so it is held to the contract too.
public sealed class TinyHoloGeneralHybridContract : GeneralContractTests
{ protected override IGeneral Create() => new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.Hybrid, PassesPerLearn = 32, PassCap = 4800 }); }

// The lookup half alone and the net alone must each satisfy the contract too: the arbitration never has to rescue a learner from a broken rule.
public sealed class TinyHoloGeneralTableOnlyContract : GeneralContractTests
{ protected override IGeneral Create() => new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.TableOnly }); }

// The net alone: fewer replay passes per call keep the 11 cases fast; the cases themselves are unchanged.
public sealed class TinyHoloGeneralNetOnlyContract : GeneralContractTests
{ protected override IGeneral Create() => new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.NetOnly, PassesPerLearn = 32, PassCap = 4800 }); }

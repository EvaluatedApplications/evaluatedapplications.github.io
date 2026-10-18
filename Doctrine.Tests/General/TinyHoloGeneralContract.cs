namespace Doctrine.Tests;

// The real general held to the shared general contract, in all three modes. The default (hybrid) is what the game plugs in.
public sealed class TinyHoloGeneralContract : GeneralContractTests { protected override IGeneral Create() => new TinyHoloGeneral(); }

// The lookup half alone and the net alone must each satisfy the contract too: the arbitration never has to rescue a learner from a broken rule.
public sealed class TinyHoloGeneralTableOnlyContract : GeneralContractTests
{ protected override IGeneral Create() => new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.TableOnly }); }

// The net alone: fewer replay passes per call keep the 11 cases fast; the cases themselves are unchanged.
public sealed class TinyHoloGeneralNetOnlyContract : GeneralContractTests
{ protected override IGeneral Create() => new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.NetOnly, PassesPerLearn = 32, PassCap = 4800 }); }

namespace Doctrine.Tests;

// The stub held to the general contract. algformer-owner: add your own derived class beside this one.
public sealed class StubGeneralContract : GeneralContractTests { protected override IGeneral Create() => new TableLookupGeneral(); }

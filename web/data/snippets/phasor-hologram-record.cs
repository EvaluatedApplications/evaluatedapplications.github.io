using Phasor;

var dim = 256;  // Hologram default dimension

// A record is a superposition of role-value pairs
var nameRole = Hologram.FromSymbol("name", dim);
var lily = Hologram.FromSymbol("Lily", dim);
var ageRole = Hologram.FromSymbol("age", dim);
var seven = Hologram.FromNumber(7, dim);
var record = Hologram.Zero(dim) + nameRole * lily + ageRole * seven;   // bind, then bundle

// Unbind a role and correlate with candidate values
var nameField = record / nameRole;
var ageField = record / ageRole;
Console.WriteLine($"Name field has Lily:  {nameField.Correlate(lily):F2}");
Console.WriteLine($"Name field has John:  {nameField.Correlate(Hologram.FromSymbol("John", dim)):F2}");
Console.WriteLine($"Age field has 7:  {ageField.Correlate(seven):F2}");
Console.WriteLine($"Age field has 5:  {ageField.Correlate(Hologram.FromNumber(5, dim)):F2}");

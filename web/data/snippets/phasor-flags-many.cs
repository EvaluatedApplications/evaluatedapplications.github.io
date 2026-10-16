using Phasor;

var monday = new Pleroma(new HologramShape { FlagBits = 3 });
var tuesday = new Pleroma(new HologramShape { FlagBits = 3 });
monday.Add(new HologramItem(Key: 1, Flags: 0b101), 2);
tuesday.Add(new HologramItem(Key: 1, Flags: 0b100), 6);

// One bit set, many stores: result[store] is the marginal on bit 2 of that store.
long[][] onBit2 = Pleroma.FlagsMany(new[] { monday, tuesday }, new[] { 2 });
System.Console.WriteLine($"FlagsMany bit 2: monday {string.Join(" ", onBit2[0])} | tuesday {string.Join(" ", onBit2[1])}");

// Many bit sets, many stores: result[bitSet][store].
long[][][] union = Pleroma.FlagsUnion(new[] { monday, tuesday }, new[] { new[] { 0 }, new[] { 1, 2 } });
System.Console.WriteLine($"FlagsUnion bit 0: monday {string.Join(" ", union[0][0])} | tuesday {string.Join(" ", union[0][1])}");
System.Console.WriteLine($"FlagsUnion bits 1,2: monday {string.Join(" ", union[1][0])} | tuesday {string.Join(" ", union[1][1])}");

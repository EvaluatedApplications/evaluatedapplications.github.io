using Phasor;

var store = new Pleroma(new HologramShape { FlagBits = 3 });
store.Add(new HologramItem(Key: 1, Flags: 0b101), 2);   // bits 0 and 2 set
store.Add(new HologramItem(Key: 2, Flags: 0b100), 1);   // bit 2 only
store.Add(new HologramItem(Key: 3, Flags: 0b011), 4);   // bits 0 and 1 set

// Marginal on flag bits 0 and 2, with no store built: index u has bit 0 = flag bit 0, bit 1 = flag bit 2.
long[] onBits02 = store.Flags(new[] { 0, 2 });
System.Console.WriteLine($"bits {{0,2}}: {string.Join(" ", onBits02)}");

// The empty list is the total.
System.Console.WriteLine($"bits {{}}: {string.Join(" ", store.Flags(Array.Empty<int>()))}");

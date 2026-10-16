using Phasor;

var narrow = new Pleroma(new HologramShape { FlagBits = 2 });
narrow.Add(new HologramItem(Key: 7, Flags: 0b10), 3);
narrow.Add(new HologramItem(Key: 8, Flags: 0b01), 1);

// One zero flag bit inserted at position 1: old bit 1 moves to bit 2, the new bit 1 is zero on every item.
Pleroma wide = narrow.WidenFlags(1, 1);
long[] counts = wide.Flags();
System.Console.WriteLine($"flag bits {wide.Shape.FlagBits}: word 100 -> {counts[0b100]}, word 001 -> {counts[0b001]}, word 010 -> {counts[0b010]}");
System.Console.WriteLine($"source store still {narrow.Shape.FlagBits} flag bits, total {narrow.Total()}");

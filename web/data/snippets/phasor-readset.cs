using Phasor;

var a = new Pleroma(new HologramShape { FlagBits = 3 });
var b = new Pleroma(new HologramShape { FlagBits = 3 });
a.Add(new HologramItem(Key: 1, Flags: 0b101), 2);
b.Add(new HologramItem(Key: 1, Flags: 0b011), 5);

// Declare every read once; Read answers all of them for all stores in one call.
var reads = new PleromaReadSet();
int onBit0 = reads.AddFlagMarginal(new[] { 0 });
int onBits12 = reads.AddFlagMarginal(new[] { 1, 2 });
int total = reads.AddTotal();
int bitCounts = reads.AddBitCounts();

var result = reads.Read(new[] { a, b });   // result[read handle, store index]
System.Console.WriteLine($"a: bit 0 {string.Join(" ", result[onBit0, 0])} | total {result[total, 0][0]} | bit counts {string.Join(" ", result[bitCounts, 0])}");
System.Console.WriteLine($"b: bits 1,2 {string.Join(" ", result[onBits12, 1])} | total {result[total, 1][0]}");

// Run: a kernel flips bit 0 of every item in clones of the stores, then the same reads are answered.
var flip = new PleromaKernel(a.Shape);
flip.Add(flagMask: 0b001);
var ran = reads.Run(flip, new[] { a, b });
System.Console.WriteLine($"after flip, a: bit 0 {string.Join(" ", ran[onBit0, 0])}; input a unchanged: {string.Join(" ", result[onBit0, 0])}");

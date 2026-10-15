using Phasor;

var a = new CycleHologram(capacity: 4);
var b = new CycleHologram(capacity: 4);
for (ulong id = 0; id < 10_000; id++) { a.Add(id); b.Add(id); }
a.Add(777_001);        // only on A
b.Add(777_002, 2);     // only on B, two copies

byte[] wire = a.Serialize();                   // what A sends
Console.WriteLine($"wire size {wire.Length} bytes");
var delta = CycleHologram.Deserialize(wire);
delta.Subtract(b);                             // B subtracts its own hologram: A minus B
if (delta.TryDecode(out var diff))
    foreach (var (symbol, count) in diff.OrderBy(d => d.Item1))
        Console.WriteLine($"{symbol}: {count}");

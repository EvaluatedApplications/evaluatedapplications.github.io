using Phasor;

var store = new Pleroma(new HologramShape { FlagBits = 4, KeyCapacity = 8 });
store.Add(new HologramItem(Key: 1, Flags: 0b0011), 2);   // two copies
store.Add(new HologramItem(Key: 1, Flags: 0b0110), 1);   // one copy
store.Add(new HologramItem(Key: 2, Flags: 0b0001), 3);   // three copies

long[] bitCounts = store.BitCounts();                     // exact count of items with each bit set
uint parity = store.FlagParity();                         // XOR of every stored word
Console.WriteLine($"{bitCounts[0]} {bitCounts[1]} {bitCounts[2]} {bitCounts[3]} parity {parity}");

using Phasor;

var store = new Pleroma(new HologramShape { FlagBits = 6 });
store.Add(new HologramItem(Key: 1, Flags: 0b000101), 3);
store.Add(new HologramItem(Key: 2, Flags: 0b111000), 2);
store.Add(new HologramItem(Key: 3, Flags: 0b000101), 1);

// Exact counts of three flag words; the store is not transformed as a whole.
long[] few = store.ReadFlagWords(new uint[] { 0b000101, 0b111000, 0b000000 });
System.Console.WriteLine($"words: {string.Join(" ", few)}");

// Same values as indexing the whole-store read.
long[] all = store.Flags();
System.Console.WriteLine($"matches Flags(): {few[0] == all[0b000101] && few[1] == all[0b111000] && few[2] == all[0]}");

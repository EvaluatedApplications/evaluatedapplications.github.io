using Phasor;

var batched = new Pleroma(new HologramShape { FlagBits = 4 });
var sequential = new Pleroma(new HologramShape { FlagBits = 4 });

uint[] words = { 0b0001, 0b0010, 0b1111 };
long[] counts = { 5, 2, 7 };

// One pass over the cells for all three words, at key 9.
batched.AddFlagWords(words, counts, key: 9);

// The same adds, one item at a time.
for (int j = 0; j < words.Length; j++)
    sequential.Add(new HologramItem(Key: 9, Flags: words[j]), counts[j]);

System.Console.WriteLine($"total {batched.Total()}, word 0001 {batched.Flags()[0b0001]}, word 1111 {batched.Flags()[0b1111]}");
System.Console.WriteLine($"equal to {words.Length} sequential Adds: {batched.Equals(sequential)}");

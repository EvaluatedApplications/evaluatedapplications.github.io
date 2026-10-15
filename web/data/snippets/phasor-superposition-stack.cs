using Phasor;

var shape = new HologramShape { KeyCapacity = 4, FlagBits = 3, TimePeriod = 7, JointFaces = HologramFaces.Flags | HologramFaces.Time };
var store = new Pleroma(shape);                          // 96 cells for this shape, whatever the item count
store.Add(new HologramItem(Key: 10, Flags: 0b001, Time: 0), 3);
store.Add(new HologramItem(Key: 20, Flags: 0b010, Time: 2), 2);
store.Add(new HologramItem(Key: 30, Flags: 0b100, Time: 4), 1);

if (store.TryItems(out var items))
    foreach (var (item, count) in items)
        Console.WriteLine($"key {item.Key} flags {item.Flags} time {item.Time} count {count}");

var kernel = new PleromaKernel(shape);
kernel.Add(weight: 1L);                                  // keep every item
kernel.Add(flagMask: 0b001, timeShift: 1, weight: 2L);   // and twice: flip bit 0, age one tick
store.Apply(kernel);                                     // one pass over all 96 cells

if (store.TryKeys(out var keys))
    foreach (var k in keys)
        Console.WriteLine($"key total {k}");
Console.WriteLine(string.Join(" ", store.Flags()));
Console.WriteLine(string.Join(" ", store.Times()));
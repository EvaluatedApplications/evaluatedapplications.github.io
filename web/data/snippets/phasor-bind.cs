using Phasor;

var shape = new HologramShape { KeyCapacity = 8, FlagBits = 4, TimePeriod = 9 };
var store = new Pleroma(shape);
store.Add(new HologramItem(Key: 10, Flags: 0b0011, Time: 2), 3);
store.Add(new HologramItem(Key: 20, Flags: 0b0111, Time: 5), 1);

// Bind with a one-item store: every item moves by the same key, flags and time shift.
var shift = new Pleroma(shape);
shift.Add(new HologramItem(Key: 1, Flags: 0b0001, Time: 1));
store.Bind(shift);

if (store.TryItems(out var items))
    foreach (var (item, count) in items)
        Console.WriteLine($"{item.Key}: flags {item.Flags}, time {item.Time}, count {count}");

using Phasor;

var store = new Pleroma(new HologramShape
{
    KeyCapacity = 16,                                       // whole items come back for up to 16 distinct keys
    FlagBits = 8,                                           // an 8-bit flag word per item
    TimePeriod = 63,                                        // a 63-tick clock
    JointFaces = HologramFaces.Flags | HologramFaces.Time,  // also store flags x time joint counts
});
store.Add(new HologramItem(Key: 1001, Flags: 0b0000_0101, Time: 0), 3);
store.Add(new HologramItem(Key: 2002, Flags: 0b1000_0001, Time: 5), 2);

if (store.TryItems(out var items))                          // refuses past 16 keys instead of answering wrongly
    foreach (var (item, count) in items)
        Console.WriteLine($"{item.Key}: flags {item.Flags}, time {item.Time}, count {count}");

long[] perFlagWord = store.Flags();                         // exact at any load
long near = store.Near(0b0000_0100, radius: 1);             // items whose flags differ from 0b100 in at most 1 bit
long[] joint = store.JointCounts(HologramFaces.Flags | HologramFaces.Time);   // index = word * 63 + tick
Console.WriteLine($"{perFlagWord[5]} {near} {joint[5 * 63 + 0]} {joint[129 * 63 + 5]}");

store.Diffuse(stayNumerator: 1, stayDenominator: 3);        // each item stays with probability 1/3, else moves one tick
long[] perTick = store.Times();                             // TimeScale (3) times the exact expected counts
Console.WriteLine($"scale {store.TimeScale}: {perTick[0]} {perTick[1]} {perTick[5]} {perTick[6]}");

using Phasor;

var store = new Pleroma(new HologramShape
{
    KeyCapacity = 4,
    FlagBits = 3,
    TimePeriod = 7,
    JointFaces = HologramFaces.Flags | HologramFaces.Time,
});
store.Add(new HologramItem(Key: 10, Flags: 0b001, Time: 0), 3);
store.Add(new HologramItem(Key: 20, Flags: 0b010, Time: 2), 2);
store.Add(new HologramItem(Key: 30, Flags: 0b100, Time: 4), 1);
Console.WriteLine("before flags " + string.Join(" ", store.Flags()));
Console.WriteLine("before times " + string.Join(" ", store.Times()));

// One kernel: 1x identity plus 2x (flip flag bit 0 and age one tick), applied to every item in one pass.
var kernel = new PleromaKernel(store.Shape);
kernel.Add(weight: 1L);
kernel.Add(flagMask: 0b001, timeShift: 1, weight: 2L);
store.Apply(kernel);

Console.WriteLine("after flags  " + string.Join(" ", store.Flags()));
Console.WriteLine("after times  " + string.Join(" ", store.Times()));

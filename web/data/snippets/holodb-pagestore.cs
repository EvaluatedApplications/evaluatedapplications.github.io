using HoloDb;
using Phasor;

var shape = new HologramShape { KeyCapacity = 2, KeyCycle = 110, CompleteKeys = true };
var dir = Path.Combine(Path.GetTempPath(), "plrstore-restore");
if (Directory.Exists(dir)) Directory.Delete(dir, true);

using (var store = PleromaPageStore.Open(dir, shape))
{
    store.AddBatch(new[] { new PageAdd(42, 5, 3) });
    var point = store.Checkpoint();                       // a restore point: id and journal position
    store.AddBatch(new[] { new PageAdd(42, 5, 10), new PageAdd(9, 1, 1) });
    Console.WriteLine($"{store.Counts(42)[5]} {store.Contains(9)}");

    store.RestoreTo(point.Id);                            // back to the point, no recount
    Console.WriteLine($"{store.Counts(42)[5]} {store.Contains(9)}");

    Console.WriteLine(store.Verify().Clean);
    Console.WriteLine(store.Repair().Unrecoverable.Count);
}
Directory.Delete(dir, true);
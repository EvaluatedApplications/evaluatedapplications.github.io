using PrismFormer;

int ctx = 64, dim = 32;
int shifts = HoloShape.ShiftsFor(ctx, dim);                       // the normal way to choose Shifts
bool collides = HoloShape.ShiftsCollide(shifts, dim, golden: true);
Console.WriteLine($"ShiftsFor({ctx},{dim}) = {shifts}, collides = {collides}");
Console.WriteLine($"bind rank {HoloShape.BindRank(shifts, dim)}, reads back clean {HoloShape.CleanCapacity(shifts, dim)}");

// A starting shape from a width alone. Treat it as a place to begin, not a guarantee.
foreach (var width in new[] { 64, 128, 256 })
{
    var s = HoloShape.Derive(width, stackK: 2);
    Console.WriteLine($"Derive({width}): shifts {s.Shifts}, context {s.Context}, layers {s.Layers}");
}
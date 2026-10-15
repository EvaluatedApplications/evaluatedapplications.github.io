using Phasor;

var dim = 256;

// Linear band: binding two numbers adds them
var sumEncoded = Hologram.FromNumber(3, dim) * Hologram.FromNumber(5, dim);
Console.WriteLine(PhasorCodec.DecodeSum(sumEncoded, max: 20));

// Log band: binding two numbers multiplies them
var productEncoded = Hologram.FromNumber(2, dim) * Hologram.FromNumber(4, dim);
Console.WriteLine(PhasorCodec.DecodeProduct(productEncoded, max: 16));

// In a loop, use the in-place methods: they reuse one buffer instead of allocating per call
double[] accumulator = new double[dim];
for (int i = 0; i < 100; i++)
    Hologram.BundleInto(accumulator, Hologram.FromNumber(i, dim).Reals);
Console.WriteLine($"accumulated {accumulator.Length} components");

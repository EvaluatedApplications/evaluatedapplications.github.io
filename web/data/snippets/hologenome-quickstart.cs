using HoloDb;
using HoloGenome;

var encoder = new GenomeEncoder(new GenomeEncoderOptions { Dim = 512, Twist = OlsonTwist.Instance });
string seq = "ATGCGTACGTTAGCAAAAATTTTTGCGCGATCGATCGTAGCTAGCTAGCA";
double[] h = encoder.Encode(seq);

// How many turns does the helix make along this sequence?
Console.WriteLine($"{h.Length} reals; helix turns: {encoder.HelixPhases(seq)[^1] / (2 * Math.PI):F2}");

// The mean measured twist gives the helical repeat.
double sum = 0;
foreach (char a in "ACGT") foreach (char b in "ACGT") sum += OlsonTwist.Instance.TwistDegrees(a, b);
Console.WriteLine($"{360 / (sum / 16):F4} bases per turn");

// Store the window beside ordinary SQL columns and read it back.
var store = new GenomeWindowStore(new HoloDbService(new HoloDbOptions()));
store.Put([new GenomeWindow(1, "demo", 0, seq, h)]);
Console.WriteLine(store.Get(1)!.Sequence == seq);
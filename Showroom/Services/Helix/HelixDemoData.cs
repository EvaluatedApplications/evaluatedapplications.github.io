// The built-in demo set, and the code that makes it. THIS IS SYNTHETIC: random 50-base sequences and a "measurement" made by running
// a phasor model whose period is set to 10.4 bases per turn, plus noise. It exists to check the instrument (a planted signal should be
// found, controls should not find it). It is not a measurement of any real DNA and its scores say nothing about real DNA.
// Integer-only generator (splitmix64), so a given row count yields the same rows in a browser and in a console app, and a smaller set is
// the first rows of a larger one. This file is also embedded and shown on the page.
using System.Globalization;
using System.Text;
using HoloGenome;

namespace HelixDemo;

public static class HelixDemoData
{
    public const int Length = 50;
    public const double PlantedPeriod = 10.4;   // bases per turn, put into the measurement on purpose
    public const double NoiseWidth = 0.3;       // the measurement gets noise uniform in +/- 0.15
    public const ulong Seed = 20261015UL;

    // The ten class weights of the planted model (real and imaginary parts), in DinucleotideClasses.Keys order.
    static readonly double[] P = [0.3, 0.1, -0.2, 0.05, 0.4, 0.8, -0.1, 0.0, 0.2, -0.6];
    static readonly double[] Q = [0.0, 0.2, 0.1, -0.1, 0.3, -0.5, 0.05, 0.1, 0.4, 0.2];

    public static Dataset Make(int rows)
    {
        ulong state = Seed;
        ulong Next() { state += 0x9E3779B97F4A7C15UL; ulong z = state; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }

        var planted = new PhasorPeriodModel(P, Q, PlantedPeriod);
        var ids = new List<string>(rows); var seqs = new List<string>(rows); var ys = new List<double>(rows);
        var chars = new char[Length];
        for (int i = 0; i < rows; i++)
        {
            for (int k = 0; k < Length; k++) chars[k] = "ACGT"[(int)((Next() >> 33) & 3)];
            string s = new(chars);
            double noise = NoiseWidth * ((Next() >> 11) * (1.0 / (1UL << 53)) - 0.5);
            ids.Add($"demo{i + 1}"); seqs.Add(s); ys.Add(planted.Score(s) + noise);
        }
        return new Dataset(ids, seqs, ys, []);
    }

    /// <summary>One "SEQUENCE,measurement" line per row, measurements written so they read back exactly.</summary>
    public static string ToText(Dataset d)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < d.Sequences.Count; i++)
            sb.Append(d.Sequences[i]).Append(',').AppendLine(d.Measurements![i].ToString("R", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

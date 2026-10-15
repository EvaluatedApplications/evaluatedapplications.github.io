// Adds the "helix" block to web/data/diagrams/hologenome-scores.json by running the published HoloGenome package.
//   dotnet run -c Release -- ../../data/diagrams/hologenome-scores.json
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HoloGenome;

string path = args.Length > 0 ? args[0] : "hologenome-scores.json";
var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

var asm = typeof(GenomeEncoder).Assembly;
var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

const string seq = "ATGCGTACGTTAGCAAAAATTTTTGCGCGATCGATCGTAGCTAGCTAGCA";
var measured = new GenomeEncoder(new GenomeEncoderOptions { Dim = 512, Twist = OlsonTwist.Instance });
var constant = new GenomeEncoder(new GenomeEncoderOptions { Dim = 512, Twist = new ConstantTwist() });

double[] pm = measured.HelixPhases(seq), pc = constant.HelixPhases(seq);
double Turns(double r) => r / (2 * Math.PI);

double sum = 0; int n = 0;
foreach (char a in "ACGT") foreach (char b in "ACGT") { sum += OlsonTwist.Instance.TwistDegrees(a, b); n++; }

static double Cos(double[] x, double[] y)
{
    double d = 0, nx = 0, ny = 0;
    for (int i = 0; i < x.Length; i++) { d += x[i] * y[i]; nx += x[i] * x[i]; ny += y[i] * y[i]; }
    return d / Math.Sqrt(nx * ny);
}
string rc = new string(seq.Reverse().Select(c => c switch { 'A' => 'T', 'T' => 'A', 'C' => 'G', _ => 'C' }).ToArray());

var helix = new JsonObject
{
    ["sequence"] = seq,
    ["measuredTurns"] = new JsonArray(pm.Select(p => (JsonNode)Math.Round(Turns(p), 4)).ToArray()),
    ["constantTurns"] = new JsonArray(pc.Select(p => (JsonNode)Math.Round(Turns(p), 4)).ToArray()),
    ["constantDegrees"] = 34.3,
    ["basesPerTurn"] = Math.Round(360 / (sum / n), 4),
    ["dim"] = measured.Dim,
    ["reverseComplementCosine"] = Math.Round(Cos(measured.Encode(seq), measured.Encode(rc)), 4),
};
root["helix"] = helix;
var pkg = root["package"]!.AsObject();
pkg["version"] = version;
File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new UTF8Encoding(false));
Console.WriteLine($"wrote helix block for HoloGenome {version}: {pm.Length} phases, final turns {Turns(pm[^1]):F2} measured, {Turns(pc[^1]):F2} constant, rc cosine {helix["reverseComplementCosine"]}");
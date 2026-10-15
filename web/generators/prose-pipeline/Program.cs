// Generates web/data/diagrams/prose-pipeline.json by running the published Prose package.
//   dotnet run -c Release -- ../../data/diagrams/prose-pipeline.json <folder with alice.txt, holmes.txt, pride.txt>
using System.Reflection;
using System.Text.Json;
using Prose;

string outPath = args.Length > 0 ? args[0] : "prose-pipeline.json";
string corpusDir = args.Length > 1 ? args[1] : "corpus";
var version = typeof(ProseEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

// ---- strip the Gutenberg licence header and footer, then mine ----
string cleanDir = Path.Combine(Path.GetTempPath(), "prose-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cleanDir);
string Clean(string t)
{
    int a = t.IndexOf("*** START OF"); if (a >= 0) a = t.IndexOf('\n', a) + 1; else a = 0;
    int b = t.IndexOf("*** END OF"); if (b < 0) b = t.Length;
    return t.Substring(a, b - a);
}
var texts = new Dictionary<string, string>();
foreach (var f in Directory.GetFiles(corpusDir, "*.txt"))
{
    var text = Clean(File.ReadAllText(f));
    texts[Path.GetFileNameWithoutExtension(f)] = text;
    File.WriteAllText(Path.Combine(cleanDir, Path.GetFileName(f)), text);
}
var prose = new ProseEngine();
prose.Mine(cleanDir);
var k = prose.Store.BuildKnowledge();
Console.WriteLine($"mined {prose.Store.SentencesMined} sentences, vocabulary {k.Vocab.Count}, templates {k.Templates.Count}, strict {k.Strict}");

// ---- helpers ----
string Lemma(ParsedSentence p, int i) => i >= 0 && i < p.Toks.Count ? p.Toks[i].Lemma : "";
object Sentence(ParsedSentence p)
{
    var cl = p.Clauses.FirstOrDefault();
    return new
    {
        text = p.Text,
        toks = p.Toks.Where(t => t.Pos != Pos.Punct).Select(t => new object[] { t.Text, t.Pos.ToString(), t.Role.ToString() }).ToList(),
        template = p.Template,
        subject = cl != null && cl.Subject >= 0 ? p.Toks[cl.Subject].Text : null,
        verbs = cl?.Verbs.Select(v => p.Toks[v].Text).ToList() ?? new List<string>(),
        objects = cl?.Objects.Select(o => p.Toks[o].Text).ToList() ?? new List<string>(),
        subjectLemma = cl != null && cl.Subject >= 0 ? Lemma(p, cl.Subject) : null,
        verbLemma = cl != null && cl.Verbs.Count > 0 ? Lemma(p, cl.Verbs[0]) : null,
        objectLemma = cl != null && cl.Objects.Count > 0 ? Lemma(p, cl.Objects[0]) : null,
        indirect = cl != null && cl.IndirectObject >= 0 ? p.Toks[cl.IndirectObject].Text : null,
        indirectLemma = cl != null && cl.IndirectObject >= 0 ? Lemma(p, cl.IndirectObject) : null,
        predicate = cl != null && cl.Predicate >= 0 ? p.Toks[cl.Predicate].Text : null,
        copular = cl != null && cl.Copular,
    };
}
long PairCount(Dictionary<string, List<(string, long)>> map, string? key, string? value)
{
    if (key == null || value == null || !map.TryGetValue(key, out var list)) return 0;
    foreach (var (v, c) in list) if (v == value) return c;
    return 0;
}

// ---- the passage: plain sentences, parsed by the rules; the tables they meet below were mined from the novels ----
var passage = ProseEngine.Parse("The wise king rules the peaceful kingdom. The king gives the poor man a book. The dog chases the red ball. The sky is blue. The queen sends the man a letter.");
var panda = ProseEngine.Parse("The panda eats, shoots, and leaves. The panda eats shoots and leaves.");

// ---- generated sentences, each checked against the mined tables and the validity gate ----
var generated = new List<object>();
var gen = prose.Generate(12, seed: 1);
foreach (var text in gen)
{
    var p = ProseEngine.ParseSentence(text);
    var s = (dynamic)Sentence(p);
    string? sl = s.subjectLemma, vl = s.verbLemma, ol = s.objectLemma;
    generated.Add(new
    {
        text, toks = s.toks, template = s.template, subject = s.subject, verb = ((List<string>)s.verbs).FirstOrDefault(), obj = ((List<string>)s.objects).FirstOrDefault(),
        subjectLemma = sl, verbLemma = vl, objectLemma = ol,
        svCount = PairCount(k.VerbsForSubject, sl, vl), voCount = ol == null ? (long?)null : PairCount(k.ObjsForVerb, vl, ol),
        gate = ValidityGate.Check(text), attested = Attested.All(text, k),
    });
}

// ---- the mined table slices every lemma on the page needs ----
var lemmas = new HashSet<string>();
void Need(string? l) { if (!string.IsNullOrEmpty(l)) lemmas.Add(l); }
foreach (dynamic g in generated) { Need((string?)g.subjectLemma); Need((string?)g.verbLemma); Need((string?)g.objectLemma); }
object Top(Dictionary<string, List<(string, long)>> map, string key) =>
    map.TryGetValue(key, out var list) ? list.OrderByDescending(x => x.Item2).Take(6).Select(x => new object[] { x.Item1, x.Item2 }).ToList() : new List<object[]>();
var svTop = new SortedDictionary<string, object>(); var voTop = new SortedDictionary<string, object>();
foreach (var l in lemmas)
{
    if (k.VerbsForSubject.ContainsKey(l)) svTop[l] = Top(k.VerbsForSubject, l);
    if (k.ObjsForVerb.ContainsKey(l)) voTop[l] = Top(k.ObjsForVerb, l);
}

// ---- a larger check: generate many sentences and re-test each with the package's own gate ----
const int Bulk = 2000;
var many = prose.Generate(Bulk, seed: 7);
int gatePass = many.Count(ValidityGate.Check), attestedPass = many.Count(s => Attested.All(s, k));
Console.WriteLine($"bulk: {many.Count} sentences, {many.Distinct().Count()} distinct, gate {gatePass}, attested {attestedPass}");

var qa = prose.GenerateQa(4, seed: 4).Select(x => new { context = x.Context, question = x.Question, answer = x.Answer }).ToList();

var doc = new
{
    schema = "ea-diagram/1", kind = "prose-pipeline", generator = "web/generators/prose-pipeline",
    package = new { id = "EvaluatedApplications.Prose", version },
    corpus = new
    {
        books = new[] { "Alice's Adventures in Wonderland", "The Adventures of Sherlock Holmes", "Pride and Prejudice" },
        sentences = prose.Store.SentencesMined, vocabulary = k.Vocab.Count, templates = k.Templates.Count, strict = k.Strict,
    },
    inputs = new object[]
    {
        new { id = "plain", label = "Plain sentences", sentences = passage.Select(Sentence).ToList() },
        new { id = "panda", label = "The panda", sentences = panda.Select(Sentence).ToList() },
    },
    svTop, voTop,
    generated,
    qa,
    bulk = new { requested = Bulk, produced = many.Count, distinct = many.Distinct().Count(), gatePass, attestedPass },
};
File.WriteAllText(outPath, JsonSerializer.Serialize(doc), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Prose {version}: wrote {outPath} ({new FileInfo(outPath).Length} bytes); passage sentences {passage.Count}, generated {generated.Count}");
foreach (var p in passage) Console.WriteLine("  passage: " + ProseEngine.Explain(p).Replace("\n", " | "));
foreach (var g in generated) Console.WriteLine("  generated: " + ((dynamic)g).text + "  sv=" + ((dynamic)g).svCount + " vo=" + ((dynamic)g).voCount);
try { Directory.Delete(cleanDir, true); } catch { }
using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, page 1: site/prose.html transcribed verbatim into a PageSpec. Chosen specifically
/// because it is structurally DIFFERENT from phasor.html (the Phase 1 proof) in the ways that
/// matter for broadening the pipeline's proof surface, not just re-confirming the same path:
///   - Every card in both CardGrid sections uses a real per-card CatOverride (CardSpec.CatOverride),
///     and it's the two-tone "chord" gradient shape (a composite/multi-package page, HoloDb +
///     AlgFormer) paired with a CatRootOverride (--cat-root) — neither field was exercised by
///     Phasor, whose cards are all one plain solid category. This is the exact gap
///     platform-architecture.md §9 named as unexercised.
///   - Page category (body[data-cat], "holodb-algformer") and the section sec-head dot colour
///     (var(--c-algformer)) are DIFFERENT values on this page — Phasor's happened to use the same
///     token for both, so this is the first real proof PageSpec.Category vs .CategoryDotVar are
///     genuinely independent, not just accidentally never-diverged.
///   - No .prism-beam hero graphic (ShowPrismBeam=false) — Phasor always had one.
///   - Only 4 sections (Prose, CardGrid, CardGrid, Snippets) and NO closing ClosingStack section —
///     Phasor has all 4 SectionKinds including ClosingStack; Prose exercises the "page ends after
///     the last content section, straight to footer" shape ClosingStack-having pages don't.
/// </summary>
public static class ProsePageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "Prose — a grammar-driven synthetic corpus generator for .NET",
            Description: "Prose reads text through the rules of grammar, mines what it learns into a HoloDb database, and recombines that knowledge into new, grammatical, plausible sentences and Q&amp;A pairs — to grow a training corpus without a large model.",
            Canonical: "https://evaluatedapplications.github.io/prose.html",
            OgTitle: "Prose — a grammar-driven synthetic corpus generator for .NET",
            OgDescription: "Rules-first parsing and mining, recombined into fresh grammatical sentences and Q&amp;A pairs — no training required.",
            OgUrl: "https://evaluatedapplications.github.io/prose.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.Prose","description":"Prose reads text through the rules of grammar, mines what it learns into a HoloDb database, and recombines that knowledge into new, grammatical, plausible sentences and Q&A pairs — to grow a training corpus without a large model.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.0.2","url":"https://evaluatedapplications.github.io/prose.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.Prose","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("Prose · machine learning")
            .Headline("Grammar rules first. A model only if you want one.")
            .Lede("Prose reads plain text through the rules of grammar, keeps what it learns in a <a href=\"/holodb/\">HoloDb</a> database, and recombines that knowledge into new sentences that are grammatical, use words suited to their slots, and read plausibly. It exists to grow a training corpus: mine the grammatical knowledge already in your text — which nouns, verbs and adjectives go together, and how — then generate more of it, along with question/answer pairs for reading-comprehension training data.")
            .Fact("<b>v1.0.2</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("zero-training parser")
            .Fact("optional plausibility model")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.Prose")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.Prose", CtaStyle.Ghost, externalNewTab: true)
            .Related("HoloDb", "/holodb/")
            .Related("AlgFormer", "/algformer.html")
            .BarTitle("prose.app"))
        .Section(SectionSpec.Prose(
            "The problem it solves", "volume and variety, without a big model",
            "Language models need volume and variety of grammatically correct text. Hand-writing or scraping more data doesn't scale, and most \"data augmentation\" either pastes fragments together ungrammatically or needs its own large model to run. Prose takes a different route: parse real text into its grammatical structure with deterministic rules, store what nouns/verbs/adjectives actually co-occur with what, and recombine those attested pieces into fresh sentences that are guaranteed to parse correctly before they're ever written out."))
        .Section(SectionSpec.CardGrid(
            "Why it's different", "rules-first, nothing imaginary",
            new[]
            {
                new CardSpec("Rules-first, not model-first",
                    "Parsing, mining and generation all run on deterministic grammar rules with zero training required. An optional small language model can layer on to rank candidates by plausibility, but Prose degrades gracefully to pure rules with no model at all.",
                    ChordCat, ChordRoot),
                new CardSpec("No word locked to one role",
                    "The same surface form resolves to noun or verb by context, the way a person who has learned grammar reads it — not a fixed lookup table. The proof case is the classic <em>\"Eats, Shoots &amp; Leaves\"</em> sentence: <em>\"the panda eats shoots and leaves\"</em> (no comma) parses as one verb with two coordinate noun objects; <em>\"the panda eats, shoots, and leaves\"</em> (comma) parses as three coordinate finite verbs. Same words — the punctuation alone flips the reading, and Prose gets both right.",
                    ChordCat, ChordRoot),
                new CardSpec("Nothing imaginary",
                    "Every open-class word Prose emits is a form it actually observed in the source text — it never invents an inflection. Generated sentences are re-parsed and checked against a validity gate before they're kept, and generated Q&amp;A pairs are checked the same way on both question and answer.",
                    ChordCat, ChordRoot),
                new CardSpec("Real grammatical structure",
                    "Beyond simple subject-verb-object sentences, Prose mines and generates indirect objects (\"gave him a book\"), relative clauses (\"the panda that eats bamboo\"), and multi-sentence passages where later sentences refer back to earlier entities by name or pronoun.",
                    ChordCat, ChordRoot),
            }))
        .Section(SectionSpec.CardGrid(
            "Key features", "parse, mine, generate",
            new[]
            {
                new CardSpec("Parse",
                    "Sentence splitting, part-of-speech tagging, phrase chunking and role assignment (subject, verb, objects, indirect object, predicate, relative clauses) — zero training, no external model.",
                    ChordCat, ChordRoot),
                new CardSpec("Mine",
                    "Scans a text corpus and builds a lexicon plus selectional tables (which subjects go with which verbs, which verbs take which objects, which adjectives modify which nouns), stored in HoloDb.",
                    ChordCat, ChordRoot),
                new CardSpec("Generate",
                    "Samples a sentence shape from the mined patterns, fills each slot with an attested, agreement-correct word, and keeps only sentences that re-parse validly.",
                    ChordCat, ChordRoot),
                new CardSpec("Plausibility scoring (optional)",
                    "A small language model ranks generated candidates so the output reads more sensibly, without weakening the grammatical guarantee.",
                    ChordCat, ChordRoot),
                new CardSpec("Q&amp;A generation",
                    "Turns generated sentences into context/question/answer triples, including multi-sentence passages with coreference and self-referential two-turn dialogues.",
                    ChordCat, ChordRoot),
                new CardSpec("Corpus and TSV export",
                    "Write generated sentences straight to <code>.txt</code> files or Q&amp;A pairs to a <code>prompt\\ttarget</code> TSV, ready to feed into a language model's training data.",
                    ChordCat, ChordRoot),
            }))
        .Section(SectionSpec.SnippetList(
            "Get started", "minimal example",
            new[]
            {
                new SnippetSpec(
                    """
                    var prose = new ProseEngine();
                    prose.Mine(@"C:\corpus\text");                  // parse every *.txt and mine it
                    prose.Save(@"prose.wal");                        // persist the mined tables (optional)

                    foreach (var sentence in prose.Generate(1000))   // new, grammatical, plausible sentences
                        Console.WriteLine(sentence);

                    prose.WriteCorpus(@"C:\corpus\synthetic", 100_000);   // write sentences out as .txt for an LM
                    prose.WriteQaPairs(@"C:\corpus\qa", 50_000);          // context+question -> answer TSV
                    """,
                    DescAfterHtml: "Parse a single sentence directly, no mining or database required:"),
                new SnippetSpec(
                    """
                    var p = ProseEngine.ParseSentence("The panda eats, shoots, and leaves.");
                    Console.WriteLine(ProseEngine.Explain(p));   // subject, verbs, objects, template
                    """),
            },
            limHtml: "<b>Depends on:</b> <a href=\"/holodb/\">HoloDb</a> (the storage engine that holds mined grammar knowledge) and <a href=\"/algformer.html\">AlgFormer</a> (supplies the optional plausibility-scoring model) — installing the NuGet package pulls both in automatically. <b>License:</b> proprietary, compiled library only; every capability is free to use."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));

    private const string ChordCat =
        "linear-gradient(90deg, var(--c-holodb) 0%, var(--c-holodb) 50%, var(--c-algformer) 50%, var(--c-algformer) 100%)";
    private const string ChordRoot = "var(--c-holodb)";
}

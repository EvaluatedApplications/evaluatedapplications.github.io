using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 2: site/holoformer.html transcribed verbatim. The most structurally
/// bespoke page ported so far — exercises SIX new capabilities in one page:
///   - PageSpec.Hero with HeroSpec.RawBodyHtml? No — actually this page DOES use the standard
///     hero fields (crumb override, no facts/install/cta at all, ExtraBodyHtml for the .thesis
///     figure pair) rather than a full raw override, since its hero IS still crumb/eyebrow/h1/
///     lede/related — just with several of the optional blocks (facts/install/cta/lim) genuinely
///     absent, which the existing "only render if non-empty/non-null" composer logic already
///     handles for free.
///   - HeroSpec.CrumbHtml ("Home / AlgFormer / HoloFormer, explained" — a different depth from
///     the standard two-hop crumb).
///   - HeroSpec.ExtraBodyHtml (the `.thesis` before/after figure pair, sitting between the lede
///     and the related pills on a hero with no facts/install/cta-row at all).
///   - PageSpec.PageStyleHtml (this page's own substantial page-local `.concept`/`.thesis`/`.cmp`/
///     `.closer` CSS block).
///   - PageSpec.TailScriptHtml (no `.copy` buttons anywhere on this page, so the live tail script
///     is just the year-setter, not the standard copy-button handler).
///   - SectionKind.ConceptArticle — the whole `&lt;main class="sec"&gt;` body (see
///     ConceptArticleSpec's own doc comment): 7 ConceptCards, a `.cmp` compare block, a `.note`
///     paragraph, and a raw `.closer` block.
/// </summary>
public static class HoloFormerPageSpec
{
    private const string PageStyle = """
    <style>
      /* page-local, built on the site's tokens so it stays theme-aware */
      .hero .eyebrow b{color:var(--c-algformer)}
      .hero h1 .warm{color:var(--c-algformer)} .hero h1 .cold{color:var(--spectrum-5)}
      .concept{display:grid; grid-template-columns:84px 1fr; gap:26px; align-items:start;
        padding:30px 0; border-top:1px solid var(--border)}
      .concept .glyph{width:84px; height:84px; position:sticky; top:82px}
      .concept .glyph svg{width:100%; height:100%; overflow:visible}
      .kick{font-family:var(--mono); font-size:.74rem; letter-spacing:.12em; text-transform:uppercase; color:var(--c-algformer); font-weight:600}
      .concept h2{font-size:clamp(1.35rem,3.4vw,1.7rem); margin:.5rem 0 .1rem; text-wrap:balance}
      .concept p{color:var(--ink-soft); margin:.85rem 0 0; max-width:64ch}
      .concept em{color:var(--ink); font-style:italic}
      .anchor{margin-top:1rem; padding:.7rem 1rem; background:var(--surface); border:1px solid var(--border);
        border-left:3px solid var(--c-algformer); border-radius:0 10px 10px 0; font-size:.92rem; color:var(--ink-soft)}
      .anchor b{color:var(--ink); font-weight:600}
      .anchor code{font-family:var(--mono); font-size:.82em; color:var(--c-algformer);
        background:color-mix(in srgb,var(--c-algformer) 14%,transparent); padding:1px 6px; border-radius:5px; letter-spacing:.02em; text-transform:uppercase}
      .thesis{display:grid; grid-template-columns:1fr 1fr; gap:14px; margin-top:30px; max-width:560px}
      .thesis figure{margin:0; background:var(--surface); border:1px solid var(--border); border-radius:12px; padding:16px 16px 12px; text-align:center}
      .thesis svg{width:100%; height:74px; display:block}
      .thesis figcaption{font-family:var(--mono); font-size:.68rem; letter-spacing:.05em; text-transform:uppercase; color:var(--ink-faint); margin-top:.6rem}
      .cmp{display:grid; grid-template-columns:1fr 1fr; gap:16px; margin:20px 0 4px}
      .cmp .card h3{font-size:1.1rem; margin:0 0 .8rem}
      .cmp .card.this{--cat:var(--c-algformer)} .cmp .card.this h3{color:var(--c-algformer)}
      .cmp .card.tf{--cat:var(--spectrum-5)} .cmp .card.tf h3{color:var(--spectrum-5)}
      .cmp ul{margin:0; padding-left:1.15rem; color:var(--ink-soft)} .cmp li{margin:.55rem 0; font-size:.95rem; line-height:1.5}
      .cmp li b{color:var(--ink); font-weight:600} .cmp li em{color:var(--ink); font-style:italic}
      .closer{text-align:center; padding:44px 0 6px}
      .closer p{font-size:clamp(1.4rem,4vw,1.9rem); font-weight:650; letter-spacing:-.02em; line-height:1.3; text-wrap:balance; margin:0}
      .closer .warm{color:var(--c-algformer)} .closer .cold{color:var(--spectrum-5)}
      .proof{color:var(--ink); font-weight:600}
      @media (max-width:640px){
        .concept{grid-template-columns:1fr; gap:8px} .concept .glyph{position:static; margin-bottom:4px}
        .thesis,.cmp{grid-template-columns:1fr}
      }
    </style>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloFormer — meaning as chords",
            Description: "An entry-level explanation of the holographic transformer behind AlgFormer: it doesn't write meaning as points on a map, it plays it as chords — and how that differs from an ordinary transformer.",
            Canonical: "https://evaluatedapplications.github.io/holoformer.html",
            OgTitle: "HoloFormer — meaning as chords",
            OgDescription: "A transformer writes meaning as dots on a map. This one plays it as chords. An entry-level look at the holographic transformer behind AlgFormer.",
            OgUrl: "https://evaluatedapplications.github.io/holoformer.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"TechArticle","headline":"HoloFormer — meaning as chords","description":"An entry-level explanation of the holographic transformer behind AlgFormer: it doesn't write meaning as points on a map, it plays it as chords — and how that differs from an ordinary transformer.","url":"https://evaluatedapplications.github.io/holoformer.html","author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"publisher":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"about":{"@type":"SoftwareApplication","name":"EvaluatedApplications.AlgFormer","url":"https://evaluatedapplications.github.io/algformer.html"}}
            """,
            OgType: "article"))
        .PageStyle(PageStyle)
        .TailScript("<script>document.getElementById('yr').textContent = new Date().getFullYear();</script>")
        .NavItems(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/packages/EvaluatedApplications.AlgFormer", ExternalNewTab: true),
        })
        .Hero(h => h
            .Crumb("<a href=\"/\">Home</a><span>/</span><a href=\"/algformer.html\">AlgFormer</a><span>/</span>HoloFormer, explained")
            .Eyebrow("<b>part of AlgFormer</b> · how the holographic transformer works")
            .Headline("A transformer writes meaning as <span class=\"cold\">dots on a map</span>. This one plays it as <span class=\"warm\">chords</span>.")
            .Lede("Most AI models turn words into points in space and learn enormous tables to move them around. AlgFormer's\n          holographic transformer turns words into <em>sounds</em>, and lets the physics of sound do the work. Here's how it\n          works, no maths required — and why it isn't just a small transformer.")
            .ExtraBody("""
            <div class="thesis">
                  <figure>
                    <svg viewBox="0 0 160 74" aria-hidden="true"><g style="fill:var(--spectrum-5)">
                      <circle cx="26" cy="24" r="4"/><circle cx="60" cy="14" r="4"/><circle cx="104" cy="32" r="4"/><circle cx="134" cy="18" r="4"/>
                      <circle cx="44" cy="52" r="4"/><circle cx="86" cy="58" r="4"/><circle cx="120" cy="54" r="4"/><circle cx="72" cy="36" r="4"/></g></svg>
                    <figcaption>A transformer: scattered points</figcaption>
                  </figure>
                  <figure>
                    <svg viewBox="0 0 160 74" aria-hidden="true"><g style="stroke:var(--c-algformer)" stroke-width="3" stroke-linecap="round">
                      <line x1="18" y1="16" x2="142" y2="16"/><line x1="18" y1="32" x2="142" y2="32" opacity=".55"/>
                      <line x1="18" y1="48" x2="142" y2="48" opacity=".85"/><line x1="18" y1="62" x2="142" y2="62" opacity=".4"/></g></svg>
                    <figcaption>This model: a stack of tones</figcaption>
                  </figure>
                </div>
            """)
            .Related("AlgFormer", "/algformer.html")
            .Related("AlgFormer.Gpu", "/algformer-gpu.html")
            .Related("Phasor", "/phasor.html")
            .BarTitle("holoformer.app")
            .PrismBeam())
        .Section(SectionSpec.ConceptArticle(new ConceptArticleSpec(
            Cards: new[]
            {
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\"><g style=\"stroke:var(--c-algformer)\" stroke-width=\"4\" stroke-linecap=\"round\">\n" +
                    "  <line x1=\"12\" y1=\"18\" x2=\"72\" y2=\"18\"/><line x1=\"12\" y1=\"34\" x2=\"72\" y2=\"34\" opacity=\".55\"/>\n" +
                    "  <line x1=\"12\" y1=\"50\" x2=\"72\" y2=\"50\" opacity=\".85\"/><line x1=\"12\" y1=\"66\" x2=\"72\" y2=\"66\" opacity=\".45\"/></g></svg>",
                    "01 — Every word is a chord",
                    "Meaning is a sound, not a spot.",
                    new[]
                    {
                        "Give the model a letter, a word, or a number and it writes it down as a <b>chord</b>: a particular stack of pure\n        tones played together. \"Cat\" is one chord, \"dog\" another. Chords that <em>sound</em> alike mean alike, so related\n        ideas cluster on their own — \"car\" ends up humming right next to \"streets.\"",
                        "How many tones can it stack into a chord? That's the size of its instrument. A small instrument has few strings, so\n        every chord blurs into the same muddy sound and the model just repeats itself. A bigger instrument keeps the chords\n        crisp and distinct, and suddenly it can tell everything apart.",
                    },
                    "<b>In the model:</b> the tones are the <code>dimensions</code> — the strings of the instrument. More strings, crisper meanings."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\"><g fill=\"none\" stroke-width=\"4\" stroke-linecap=\"round\">\n" +
                    "  <path d=\"M16 24 h50\" style=\"stroke:var(--c-algformer)\"/><path d=\"M16 38 h50\" style=\"stroke:var(--spectrum-5)\"/><path d=\"M16 58 h50\" style=\"stroke:var(--ink-soft)\"/></g>\n" +
                    "  <text x=\"70\" y=\"62\" font-family=\"monospace\" font-size=\"10\" style=\"fill:var(--ink-faint)\">=</text></svg>",
                    "02 — Two chords, played together",
                    "To combine ideas, it plays them at once.",
                    new[]
                    {
                        "The model has essentially <em>one</em> core move: to join two things, it plays their chords together and they merge\n        into a single new chord. That's how it links a word to its role, a question to its subject.",
                        "Here's the trick that makes it special. Numbers are chords tuned so cleverly that playing the chord for <b>6</b>\n        together with the chord for <b>9</b> literally rings out as the chord for <b>15</b>. The answer is <em>in the sound\n        itself</em> — the model never learned a times-table, the arithmetic falls out of how the tones combine. An ordinary\n        transformer has no such luck: it has to memorise \"6 + 9 = 15\" like a flashcard.",
                    },
                    "<b>In the model:</b> this one move is <code>binding</code>. For numbers, binding <em>is</em> addition and multiplication, for free."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\">\n" +
                    "  <circle cx=\"42\" cy=\"42\" r=\"29\" fill=\"none\" style=\"stroke:var(--border-2)\" stroke-width=\"2\"/>\n" +
                    "  <g style=\"stroke:var(--c-algformer)\" stroke-width=\"3\"><line x1=\"42\" y1=\"42\" x2=\"42\" y2=\"15\"/><line x1=\"42\" y1=\"42\" x2=\"67\" y2=\"55\"/><line x1=\"42\" y1=\"42\" x2=\"19\" y2=\"57\"/></g>\n" +
                    "  <circle cx=\"42\" cy=\"15\" r=\"3.5\" style=\"fill:var(--c-algformer)\"/><circle cx=\"67\" cy=\"55\" r=\"3.5\" style=\"fill:var(--c-algformer)\"/><circle cx=\"19\" cy=\"57\" r=\"3.5\" style=\"fill:var(--c-algformer)\"/></svg>",
                    "03 — Chord moves",
                    "It learns a handful of ways to change a chord.",
                    new[]
                    {
                        "Stacking chords isn't enough — the model also needs to <em>transform</em> them, to steer a question-chord toward its\n        answer-chord. It does this with a small set of learned <b>moves</b>, each one reaching out and blending in tones from\n        elsewhere on the instrument.",
                        "The more moves it has, the richer the relationships it can express — simple harmony with a few, real reasoning with\n        more. And the moves are spread out to reach right across the instrument rather than only nudging neighbouring\n        strings, so it can connect distant ideas, not just adjacent notes.",
                    },
                    "<b>In the model:</b> the moves are the <code>shifts</code>, spread <code>golden</code> across the tones so a few of them cover the whole range."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\"><g fill=\"none\" style=\"stroke:var(--c-algformer)\" stroke-width=\"3\">\n" +
                    "  <rect x=\"12\" y=\"28\" width=\"15\" height=\"28\" rx=\"3\"/><rect x=\"34\" y=\"28\" width=\"15\" height=\"28\" rx=\"3\" opacity=\".7\"/>\n" +
                    "  <rect x=\"56\" y=\"28\" width=\"15\" height=\"28\" rx=\"3\" opacity=\".45\"/></g>\n" +
                    "  <g style=\"stroke:var(--ink-faint)\" stroke-width=\"2\"><line x1=\"27\" y1=\"42\" x2=\"34\" y2=\"42\"/><line x1=\"49\" y1=\"42\" x2=\"56\" y2=\"42\"/></g></svg>",
                    "04 — A chain of arrangers",
                    "The chord passes down a line.",
                    new[]
                    {
                        "The sound isn't shaped all at once. It flows through several stages in a row, and each stage restyles it with its own\n        set of moves, adding a little more structure before handing it on — the way a melody might pass through a chain of\n        arrangers, each one enriching it.",
                    },
                    "<b>In the model:</b> the stages are the <code>layers</code>. Depth is how many times the sound gets reworked."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\">\n" +
                    "  <path d=\"M12 42 q15 -24 30 0 q15 24 30 0\" fill=\"none\" style=\"stroke:var(--ink-faint)\" stroke-width=\"2.5\" opacity=\".5\"/>\n" +
                    "  <path d=\"M12 42 q15 -28 30 0 q15 28 30 0\" fill=\"none\" style=\"stroke:var(--c-algformer)\" stroke-width=\"3.5\"/>\n" +
                    "  <text x=\"42\" y=\"76\" text-anchor=\"middle\" font-family=\"monospace\" font-size=\"11\" style=\"fill:var(--ink-faint)\">×2</text></svg>",
                    "05 — Play it through twice",
                    "Thinking, for this model, is replaying.",
                    new[]
                    {
                        "Here's the surprising part, and it happens inside every one of those arrangers. Each one doesn't play its part just\n        once — it plays it again over the top of what it just laid down, and only <em>then</em> hands the sound on to the next.\n        The first pass sketches a rough sound; the second develops that sketch into the actual answer.",
                        "Tested on the real model: with a single pass its output is noise, and only on the second pass does the music — the\n        usable answer — appear. So the two ideas stack: a chain of arrangers, each one replaying its own part, and the model\n        can \"think harder\" about a short question simply by playing it through more times, with no extra information.",
                    },
                    "<b>In the model:</b> these repeats are the <code>K iterations</code>. More passes (trained in) means deeper thinking on the same input."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\"><g fill=\"none\" style=\"stroke:var(--c-algformer)\" stroke-width=\"2\">\n" +
                    "  <circle cx=\"42\" cy=\"42\" r=\"9\" opacity=\".9\"/><circle cx=\"42\" cy=\"42\" r=\"19\" opacity=\".55\"/><circle cx=\"42\" cy=\"42\" r=\"29\" opacity=\".3\"/></g>\n" +
                    "  <circle cx=\"42\" cy=\"42\" r=\"3.5\" style=\"fill:var(--c-algformer)\"/></svg>",
                    "06 — One shared sound you tune into",
                    "It remembers like a radio, not a room.",
                    new[]
                    {
                        "This is where it parts ways most sharply with an ordinary transformer. A transformer makes every word listen to every\n        other word — a crowded room where everyone talks to everyone at once. It works, but the effort explodes as the\n        conversation grows.",
                        "This model instead folds everything it has heard into <em>one running sound</em>. To recall something, it \"tunes in\"\n        to a frequency, and the matching part rings out clear while everything else cancels to silence — exactly like finding\n        one station on a radio dial. One sound to hold it all, one dial to find any piece. Cheaper, and you can actually\n        <em>listen</em> to what it's holding in mind.",
                    },
                    "<b>In the model:</b> this is <code>holographic memory</code> — retrieval by resonance instead of everyone-compares-to-everyone."),
                new ConceptCardSpec(
                    "<svg viewBox=\"0 0 84 84\" aria-hidden=\"true\">\n" +
                    "  <path d=\"M6 42 q9 -19 18 0 t18 0 t18 0 t18 0\" fill=\"none\" style=\"stroke:var(--c-algformer)\" stroke-width=\"2.5\"/>\n" +
                    "  <path d=\"M6 42 q13 21 26 0 t26 0 t26 0\" fill=\"none\" style=\"stroke:var(--spectrum-5)\" stroke-width=\"2.5\" opacity=\".8\"/></svg>",
                    "07 — The interference does real work",
                    "When many chords pile up, new tones appear.",
                    new[]
                    {
                        "Play a lot of chords into one sound and the tones start to interfere — some reinforce, some clash, and the overlap\n        conjures faint new tones that nobody actually played: the shimmer and beats you hear inside a rich chord.",
                        "The key idea behind this model is that the interference isn't just noise to clean up — it is quietly doing the\n        computation itself, for free, in the overlap. And this is <span class=\"proof\">proven, not a hunch</span>. In one test,\n        a model with only <b>five</b> numbers to its name untangled the \"double spiral\" — a famously hard puzzle where two\n        spiral arms coil tightly around each other and have to be told apart, the kind of thing that normally needs a network\n        hundreds of times larger. Five numbers can't do that on their own; the interference between the packed-together tones\n        did the work.",
                    },
                    "<b>In the model:</b> this is <code>crosstalk as compute</code> — the overlap between packed-together ideas doing useful work (the double spiral, solved in 5 parameters)."),
            },
            CompareHeading: "Why it isn't just a small transformer",
            CompareTagline: "side by side",
            CompareCards: new[]
            {
                new ConceptCompareCardSpec("tf", "An ordinary transformer", new[]
                {
                    "Meaning is a <b>dot</b> on a vast map.",
                    "To combine ideas it uses <b>huge learned tables</b> — it has to be taught every combination, including 6 + 9.",
                    "Its thoughts are <b>silent and unreadable</b>, just numbers.",
                    "Every word compares to <b>every other word</b> — powerful but costly.",
                }),
                new ConceptCompareCardSpec("this", "The holographic transformer", new[]
                {
                    "Meaning is a <b>chord</b> — a sound.",
                    "To combine ideas it just <b>plays them together</b>, so arithmetic and composition come <em>free</em> from the physics of sound.",
                    "Its thoughts are <b>audible</b> — you can hear, and read off, what it means.",
                    "Everything folds into <b>one sound it tunes into</b> — cheaper, and it thinks by replaying.",
                }),
            },
            NoteHtml: "Because the hard parts — combining, counting,\n    remembering — are built into the medium instead of learned from scratch, this model can reason and do exact arithmetic at\n    a <em>tiny</em> size, where an ordinary model that small could only babble.",
            ClosingHtml: """
            <div class="closer">
                <p>An ordinary transformer <span class="cold">memorises</span> the music.<br>This one <span class="warm">plays</span> it.</p>
                <p class="note" style="margin-top:24px; color:var(--ink-faint)">The holographic transformer ships as part of
                  <a href="/algformer.html">AlgFormer</a> (<a href="https://www.nuget.org/packages/EvaluatedApplications.AlgFormer" target="_blank" rel="noopener">EvaluatedApplications.AlgFormer</a>
                  on NuGet), built on the <a href="/phasor.html">Phasor</a> codec.
                  Every metaphor here maps to a real mechanism — phasors truly are the mathematics of sound.</p>
                <p class="note" style="margin-top:14px"><a href="/algformer.html">← Back to AlgFormer</a></p>
              </div>
            """)))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("AlgFormer", "/algformer.html"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

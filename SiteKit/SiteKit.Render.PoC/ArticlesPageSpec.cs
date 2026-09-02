using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 3: site/articles.html transcribed verbatim. The personal-writing
/// index — genuinely different from every package page (no `docs/site.md` source, own content
/// model, see CLAUDE.md's "Articles" section). Exercises:
///   - PageSpec.Category = "" (no `data-cat` at all — not scoped to one package).
///   - HeroSpec.CrumbHtml (a one-hop "Home / Articles" crumb, no Packages hop) on a hero that has
///     facts but no install/cta/lim/related at all (already free via the existing
///     only-render-if-present composer logic).
///   - SectionSpec.SectionId (`#articles`) + SectionSpec.Raw for the `.articles` list — the
///     `&lt;article class="article-item"&gt;` entries plus the big HTML comment holding the
///     copy-to-publish template are reproduced byte-for-byte as one raw block (kept raw
///     deliberately: this content is the user's own hand-maintained publishing workflow, not a
///     shape SiteKit should re-derive or template away).
///   - PageSpec.TailScriptHtml (year-only, no copy-button handler — no `.copy` buttons on this page).
/// </summary>
public static class ArticlesPageSpec
{
    private const string ArticlesRaw = """
    <div class="articles">

          <article class="article-item">
            <h3><a href="/articles/ctx8-and-the-reverse-grow.html">Growing the Wrong Way</a></h3>
            <time class="article-date" datetime="2026-09-02">2 September 2026</time>
            <p class="article-summary">The grow to 8 tokens didn't pay off the way hard-mining ctx=4 first was supposed to — and chasing down why led to a real structural bug in how context growth has always worked.</p>
          </article>

          <article class="article-item">
            <h3><a href="/articles/ctx4-plateau.html">The Four-Token Ceiling</a></h3>
            <time class="article-date" datetime="2026-08-31">31 August 2026</time>
            <p class="article-summary">I hard-trained a tiny model pinned at 4 tokens of context to find out if my last one grew up too fast — where it plateaued, what kept improving anyway, and where my own read of the data was wrong.</p>
          </article>

          <article class="article-item">
            <h3><a href="/articles/nobody-read-the-warning.html">Nobody Read the Warning</a></h3>
            <time class="article-date" datetime="2026-08-30">30 August 2026</time>
            <p class="article-summary">Every civilisation that collapsed left us a report, and it's still readable — a look at why the warnings from Bronze Age collapse to Babel to the Talmud keep getting preserved perfectly and understood not at all.</p>
          </article>

          <!-- ============================================================================
            ARTICLE ENTRY TEMPLATE — how to publish the NEXT piece.

            1. Write the real article page first (copy site/articles/_example.html to
               site/articles/<slug>.html, fill it in, delete its "TEMPLATE" banner and
               noindex meta tag — see that file's own header comment for the full recipe).
            2. Copy the <article class="article-item"> block below, paste it as the FIRST
               child of the <div class="articles"> above (newest first), fill in the title,
               href, ISO date + display date, and a one-line, honest summary (not the whole
               dek — that lives on the article page itself).
            3. Add the new page's URL to sitemap.xml (copy the pattern the existing article
               uses) and update CLAUDE.md's Articles section (published count + slug list).

            <article class="article-item">
              <h3><a href="/articles/your-slug.html">Your article's real title</a></h3>
              <time class="article-date" datetime="2026-08-30">30 August 2026</time>
              <p class="article-summary">One honest sentence describing what the piece is about.</p>
            </article>
          ============================================================================ -->
        </div>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "Articles — Evaluated Applications",
            Description: "Long-form writing from Evaluated Applications: notes on the libraries, the maths underneath them, and why they're built the way they are. Published as pieces are finished, no fixed schedule.",
            Canonical: "https://evaluatedapplications.github.io/articles.html",
            OgTitle: "Articles — Evaluated Applications",
            OgDescription: "Long-form writing from Evaluated Applications: notes on the libraries, the maths underneath them, and why they're built the way they are.",
            OgUrl: "https://evaluatedapplications.github.io/articles.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"CollectionPage","name":"Articles — Evaluated Applications","description":"Long-form writing from Evaluated Applications: notes on the libraries, the maths underneath them, and why they're built the way they are.","url":"https://evaluatedapplications.github.io/articles.html","isPartOf":{"@type":"WebSite","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .TailScript("<script>\n  document.getElementById('yr').textContent = new Date().getFullYear();\n</script>")
        .Hero(h => h
            .Crumb("<a href=\"/\">Home</a><span>/</span>Articles")
            .Eyebrow("Writing")
            .Headline("Notes and essays, written as they're finished.")
            .Lede("Longer pieces than a product page has room for: why something is built the way it is, what we learned\n        getting there, and ideas that don't fit neatly on a package's own page. No fixed schedule, no newsletter to sign up\n        for — just check back, or come from a link.")
            .Fact("long-form")
            .Fact("no fixed schedule")
            .Fact("free to read")
            .BarTitle("articles.app"))
        .Section(SectionSpec.Raw(
            "Articles", "newest first",
            ArticlesRaw,
            id: "articles"))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

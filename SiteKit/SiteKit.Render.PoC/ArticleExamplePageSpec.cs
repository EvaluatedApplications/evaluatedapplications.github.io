using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, third batch, page 4: site/articles/_example.html transcribed verbatim. The per-article
/// page TEMPLATE (not a real page — noindexed, unlinked, see CLAUDE.md's "Articles" section) —
/// proves SectionKind.ProseArticle (see ProseArticleSpec) alongside `holodb.html`/
/// `holodb/manual/index.html`, the two other "prose-template" pages, all sharing the exact same
/// `&lt;main class="wrap"&gt;&lt;article class="prose"&gt;` shell with no `&lt;header
/// class="hero"&gt;` at all (PageSpec.Hero = null). Also exercises three real, typed additions
/// this one page needed and no prior page did: PageSpec.LeadingHtml (the file's own leading HTML
/// comment before `&lt;!DOCTYPE html&gt;`), SeoSpec.RobotsMeta (`noindex,nofollow`), and
/// ProseArticleSpec.PreArticleHtml (the amber "TEMPLATE" `.stack` banner, which sits inside
/// `&lt;main class="wrap"&gt;` but BEFORE `&lt;article class="prose"&gt;` opens — outside the
/// article's own reading column). All three generalize beyond this one page: a "deliberately
/// unlisted template/preview page" is an established site pattern (see also
/// `recycledao-preview.html`), not a one-off.
/// </summary>
public static class ArticleExamplePageSpec
{
    private const string LeadingComment = """
    <!--
      TEMPLATE FILE — NOT A PUBLISHED ARTICLE.

      This is the per-article page shape for /articles/<slug>.html — proven once here so a real
      article can be dropped into this shape without re-deriving it. It is deliberately NOT linked
      from articles.html, any nav, footer, .related pill, or sitemap.xml, and carries a noindex meta
      tag below so it can never surface in search even if it ships by accident.

      TO PUBLISH A REAL ARTICLE:
      1. Copy this file to site/articles/<slug>.html (kebab-case, matches the site's URL style —
         see /holodb-client.html, /algformer-gpu.html for the convention).
      2. Delete this whole leading HTML comment.
      3. Delete the <meta name="robots" content="noindex,nofollow"> tag below.
      4. Delete the visible amber "TEMPLATE" stack banner right after the nav.
      5. Fill in: <title>, meta description, canonical/og:url (real slug), the JSON-LD Article block
         (headline/description/datePublished/url), the crumb, h1, byline date, and the body copy.
         The .toc block is OPTIONAL — only keep it if the piece actually has several headed sections
         worth jumping between; a short essay can just flow as h2s with no on-page nav at all (delete
         the whole <div class="toc"> block in that case).
      6. Add the article to articles.html's <div class="articles"> list (see the commented template
         block there) and to sitemap.xml (copy any existing <url> line, swap the loc + a sensible
         changefreq/priority — a one-off essay is normally "monthly"/"0.5").
      7. Update this file's own CLAUDE.md entry (Articles section: published count + the new slug).
    -->
    """;

    private const string TemplateBanner = """
    <div class="stack" style="border-color:var(--warn); margin-top:28px">
        <h2 style="color:var(--warn)">TEMPLATE — not a published article</h2>
        <p>This page proves the per-article layout; it isn't real content. Delete this banner (and the
          leading HTML comment, and the noindex meta tag) when you turn this into a real piece — see the
          instructions at the top of this file's source.</p>
      </div>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "TEMPLATE — Your article's real title here",
            Description: "TEMPLATE — replace with a one-sentence, honest description of the real article.",
            Canonical: "https://evaluatedapplications.github.io/articles/_example.html",
            OgTitle: "TEMPLATE — Your article's real title here",
            OgDescription: "TEMPLATE — replace with a one-sentence description.",
            OgUrl: "https://evaluatedapplications.github.io/articles/_example.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"Article","headline":"TEMPLATE — Your article's real title here","description":"TEMPLATE — replace with a one-sentence description.","url":"https://evaluatedapplications.github.io/articles/_example.html","datePublished":"2026-08-30","author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"},"publisher":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """,
            OgType: "article",
            RobotsMeta: "noindex,nofollow"))
        .Leading(LeadingComment)
        .TailScript("<script>document.getElementById('yr').textContent = new Date().getFullYear();</script>")
        .Section(SectionSpec.ProseArticle(new ProseArticleSpec(
            CrumbHtml: "<a href=\"/articles.html\">Articles</a> <span>/</span> Your article's title",
            H1: "Your article's real title here",
            ByelineHtml: "Published <time datetime=\"2026-08-30\">30 August 2026</time>",
            LedeHtml: "One or two sentences that work as the dek: what this piece is actually about, and\n    why it's worth the reader's time. This is the only part that also appears (verbatim or trimmed) as the one-line\n    summary on the articles index.",
            Related: null,
            RelatedAllText: "All packages →",
            RelatedAllHref: "/packages.html",
            TocLeadHtml: "<!-- OPTIONAL: keep this .toc block only if the real piece has several headed sections worth\n       jumping between (see step 5 above). A short essay can delete it and just flow as plain h2s. -->",
            TocItems: new[]
            {
                new RelatedLink("First section", "#first-section"),
                new RelatedLink("Second section", "#second-section"),
                new RelatedLink("Closing thought", "#closing"),
            },
            BodyHtml: """
            <h2 id="first-section">First section</h2>
              <p>Body copy reads as plain <code>.prose</code> — the same typography the HoloDb manual uses: a real
                measured line length (max 760px), generous line-height, no card-grid clutter. This is an essay, not a
                product page, so there is deliberately no <code>.grid</code>/<code>.card</code> gallery anywhere in this
                template — replace this paragraph with real writing.</p>
              <p>A second paragraph, to show normal paragraph rhythm. <code>.prose</code> also supports inline
                <code>code</code>, <a href="/packages.html">links</a>, <strong>bold</strong> emphasis, and:</p>
              <blockquote>a pull-quote or aside, styled with the accent-coloured left rule — delete if the piece
                doesn't need one.</blockquote>
              <pre><code>// a code sample, if the piece needs one
            var example = "same .prose pre/code block the HoloDb manual uses";</code></pre>

              <h2 id="second-section">Second section</h2>
              <p>Another section, to show how h2 spacing and the optional on-page <code>.toc</code> anchors line up. Delete
                everything between here and "Closing thought" and write the real piece.</p>
              <ul>
                <li>Bullet lists render like this.</li>
                <li>Same faint ink-soft colour as body paragraphs.</li>
              </ul>

              <h2 id="closing">Closing thought</h2>
              <p>Wrap up, then link back out — every article should send the reader somewhere real, not dead-end.</p>

              <p style="margin-top:2.5rem"><a href="/articles.html">&larr; Back to Articles</a></p>
            """,
            PreArticleHtml: TemplateBanner)))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Articles", "/articles.html"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

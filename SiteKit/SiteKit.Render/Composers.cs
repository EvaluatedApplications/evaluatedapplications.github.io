using System.Text;
using SiteKit.Spec;

namespace SiteKit.Render;

// ── Fragment composers — plain string builders, no Razor/HtmlRenderer for this Phase-1 proof.
// Each is a pure function: spec fragment (+ brand) in, an HTML string out. This is the
// "implementation inside one AddStep lambda" platform-architecture.md §4 describes — the level
// HtmlRenderer/Razor would slot in at later without changing the pipeline shape above them.

public static class HeadComposer
{
    public static string Compose(PageSpec page, BrandTokens brand)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(page.Seo.Title).Append("</title>\n");
        sb.Append("<meta name=\"description\" content=\"").Append(page.Seo.Description).Append("\">\n");
        sb.Append("<link rel=\"canonical\" href=\"").Append(page.Seo.Canonical).Append("\">\n");
        sb.Append("<meta property=\"og:type\" content=\"website\">\n");
        sb.Append("<meta property=\"og:title\" content=\"").Append(page.Seo.OgTitle).Append("\">\n");
        sb.Append("<meta property=\"og:description\" content=\"").Append(page.Seo.OgDescription).Append("\">\n");
        sb.Append("<meta property=\"og:url\" content=\"").Append(page.Seo.OgUrl).Append("\">\n");
        sb.Append("<meta name=\"twitter:card\" content=\"").Append(page.Seo.TwitterCard).Append("\">\n");
        sb.Append("<link rel=\"icon\" href=\"").Append(brand.FaviconDataUri).Append("\">\n");
        sb.Append("<link rel=\"stylesheet\" href=\"/assets/site.css\">\n");
        sb.Append("<script type=\"application/ld+json\">\n").Append(page.Seo.JsonLd).Append("\n</script>\n");
        sb.Append("</head>");
        return sb.ToString();
    }
}

public static class NavComposer
{
    public static string Compose(PageSpec page, NavSpec nav, BrandTokens brand)
    {
        var sb = new StringBuilder();
        sb.Append("<body class=\"os-chrome\" data-cat=\"").Append(page.Category).Append("\">\n");
        sb.Append("<div class=\"beam\"></div>\n\n");
        sb.Append("<nav class=\"site-nav\">\n  <div class=\"wrap\">\n");
        sb.Append("    <a class=\"brand\" href=\"/\">\n");
        sb.Append("      <svg class=\"mark\" viewBox=\"0 0 32 32\" aria-hidden=\"true\">")
          .Append(brand.MarkGradientSvgDefs)
          .Append("<path d=\"M16 5L27 26H5Z\" fill=\"none\" stroke=\"url(#m)\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>\n");
        sb.Append("      ").Append(brand.CompanyName).Append("\n    </a>\n");
        sb.Append("    <input type=\"checkbox\" id=\"navtoggle\" class=\"nav-toggle\">\n");
        sb.Append("    <label for=\"navtoggle\" class=\"nav-burger\" aria-label=\"Toggle menu\"><span></span><span></span><span></span></label>\n");
        sb.Append("    <div class=\"nav-links\">\n");
        foreach (var item in nav.Items)
        {
            sb.Append("      <a href=\"").Append(item.Href).Append('"');
            if (item.ExternalNewTab) sb.Append(" target=\"_blank\" rel=\"noopener\"");
            sb.Append('>').Append(item.Text).Append("</a>\n");
        }
        sb.Append("    </div>\n  </div>\n</nav>");
        return sb.ToString();
    }
}

public static class HeroComposer
{
    public static string Compose(PageSpec page, BrandTokens brand)
    {
        var h = page.Hero;
        var sb = new StringBuilder();
        sb.Append("\n<header class=\"hero\">\n  <div class=\"wrap\">\n");
        sb.Append("    <div class=\"hero-bar\" aria-hidden=\"true\">\n");
        sb.Append("      <span class=\"win-dots\"><i></i><i></i><i></i></span>\n");
        sb.Append("      <span class=\"hero-bar-title\">").Append(h.BarTitle).Append("</span>\n");
        sb.Append("    </div>\n    <div class=\"hero-body\">\n");
        // .hero-content is a z-index lift wrapper, only needed when .prism-beam is also nested in
        // .hero-body (see site.css's own comment on the .hero-content rule: "only needed on the
        // page(s) that also nest .prism-beam in .hero-body") -- pages without a beam (the majority)
        // never carry this wrapper on the live site (verified: prose.html has no .hero-content).
        // Emitting it unconditionally was a real Phase-1 gap that only showed up once a non-beam
        // page (Prose) was ported through the pipeline -- Phasor alone couldn't have caught this,
        // it's one of the 5 pages that DOES carry the beam.
        if (h.ShowPrismBeam)
        {
            sb.Append("      <div class=\"prism-beam\" aria-hidden=\"true\">\n").Append(brand.PrismBeamSvg).Append("\n      </div>\n");
            sb.Append("      <div class=\"hero-content\">\n");
        }
        var indent = h.ShowPrismBeam ? "        " : "      ";
        sb.Append(indent).Append("<p class=\"crumb\"><a href=\"/\">Home</a><span>/</span><a href=\"/packages.html\">Packages</a><span>/</span>")
          .Append(page.Title).Append("</p>\n");
        sb.Append("        <span class=\"eyebrow\">").Append(h.Eyebrow).Append("</span>\n");
        sb.Append("        <h1>").Append(h.Headline).Append("</h1>\n");
        sb.Append("        <p class=\"lede\">").Append(h.Lede).Append("</p>\n");
        if (h.Facts.Count > 0)
        {
            sb.Append("        <div class=\"facts\">\n");
            foreach (var f in h.Facts)
                sb.Append("          <span class=\"fact\">").Append(f.Html).Append("</span>\n");
            sb.Append("        </div>\n");
        }
        if (h.InstallCommand is not null)
        {
            sb.Append("        <div class=\"install\" style=\"max-width:520px;margin-top:20px\"><code>")
              .Append(h.InstallCommand).Append("</code><button class=\"copy\" type=\"button\">copy</button></div>\n");
        }
        if (h.Ctas.Count > 0)
        {
            sb.Append("        <div class=\"cta-row\" style=\"margin-top:16px\">\n");
            foreach (var cta in h.Ctas)
                sb.Append("          ").Append(RenderCta(cta)).Append('\n');
            sb.Append("        </div>\n");
        }
        if (h.Related.Count > 0)
        {
            sb.Append("        <div class=\"related\">\n          <span class=\"related-label\">Related</span>\n");
            foreach (var r in h.Related)
            {
                sb.Append("          <a href=\"").Append(r.Href).Append('"');
                if (r.ExternalNewTab) sb.Append(" target=\"_blank\" rel=\"noopener\"");
                sb.Append('>').Append(r.Text).Append("</a>\n");
            }
            sb.Append("          <a class=\"related-all\" href=\"").Append(h.RelatedAllHref).Append("\">")
              .Append(h.RelatedAllText).Append("</a>\n        </div>\n");
        }
        if (h.ShowPrismBeam) sb.Append("      </div>\n"); // closes .hero-content
        sb.Append("    </div>\n  </div>\n</header>");
        return sb.ToString();
    }

    internal static string RenderCta(CtaLink cta)
    {
        var cls = cta.Style == CtaStyle.Primary ? "btn btn-primary" : "btn btn-ghost";
        var target = cta.ExternalNewTab ? " target=\"_blank\" rel=\"noopener\"" : "";
        return $"<a class=\"{cls}\" href=\"{cta.Href}\"{target}>{cta.Text}</a>";
    }
}

public static class SectionComposer
{
    /// <summary>Renders every section as its own fragment (one string per section) — the caller
    /// (the pipeline's RenderSections step) accumulates these into a list and joins once in
    /// ComposeHtml, not repeated whole-body concatenation. This is the direct fix for the O(n^2)
    /// `job with { BodyHtml = job.BodyHtml + ... }` pattern flagged in evalapp-owner's review.</summary>
    public static IEnumerable<string> ComposeAll(PageSpec page) => page.Sections.Select(s => Compose(s, page.CategoryDotVar));

    public static string Compose(SectionSpec s, string defaultCatVar) => s.Kind switch
    {
        SectionKind.Prose => ComposeProse(s, defaultCatVar),
        SectionKind.CardGrid => ComposeCardGrid(s, defaultCatVar),
        SectionKind.Snippets => ComposeSnippets(s, defaultCatVar),
        SectionKind.ClosingStack => ComposeClosingStack(s),
        _ => throw new InvalidOperationException($"Unknown SectionKind: {s.Kind}"),
    };

    private static string SecHead(SectionSpec s, string catVar) =>
        $"<div class=\"sec-head\"><span class=\"dot\" style=\"background:{catVar}\"></span><h2>{s.Heading}</h2><p>{s.Tagline}</p></div>";

    private static string ComposeProse(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append("\n<section class=\"sec\">\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    <p class=\"desc\" style=\"max-width:74ch\">").Append(s.ProseHtml).Append("</p>\n");
        if (s.LimHtml is not null) sb.Append("    <p class=\"lim\">").Append(s.LimHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeCardGrid(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append("\n<section class=\"sec\">\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    <div class=\"grid\">\n");
        foreach (var card in s.Cards!)
        {
            var cat = card.CatOverride ?? catVar;
            sb.Append("      <article class=\"card\" style=\"--cat:").Append(cat);
            if (card.CatRootOverride is not null) sb.Append("; --cat-root:").Append(card.CatRootOverride);
            sb.Append("\">\n");
            sb.Append("        <div class=\"card-top\"><h3>").Append(card.Title).Append("</h3></div>\n");
            sb.Append("        <p class=\"desc\">").Append(card.BodyHtml).Append("</p>\n");
            sb.Append("      </article>\n");
        }
        sb.Append("    </div>\n");
        if (s.LimHtml is not null) sb.Append("    <p class=\"lim\">").Append(s.LimHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeSnippets(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append("\n<section class=\"sec\">\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        foreach (var snip in s.Snippets!)
        {
            sb.Append("    <div class=\"snip\"><code>").Append(snip.Code).Append("</code></div>\n");
            if (snip.DescAfterHtml is not null)
                sb.Append("    <p class=\"desc\">").Append(snip.DescAfterHtml).Append("</p>\n");
        }
        if (s.LimHtml is not null) sb.Append("    <p class=\"lim\">").Append(s.LimHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeClosingStack(SectionSpec s)
    {
        var sb = new StringBuilder();
        sb.Append("\n<section class=\"sec\">\n  <div class=\"wrap\">\n    <div class=\"stack\">\n");
        sb.Append("      <h2>").Append(s.Heading).Append("</h2>\n");
        sb.Append("      <p>").Append(s.ClosingBodyHtml).Append("</p>\n");
        if (s.ClosingCtas is { Count: > 0 })
        {
            sb.Append("      <div class=\"cta-row\" style=\"margin-top:16px\">\n");
            foreach (var cta in s.ClosingCtas)
                sb.Append("        ").Append(HeroComposer.RenderCta(cta)).Append('\n');
            sb.Append("      </div>\n");
        }
        sb.Append("    </div>\n  </div>\n</section>");
        return sb.ToString();
    }
}

public static class FooterComposer
{
    public static string Compose(FooterSpec footer, string companyName)
    {
        var sb = new StringBuilder();
        sb.Append("\n<footer class=\"site\">\n  <div class=\"wrap\">\n");
        sb.Append("    <span>").Append(companyName).Append(" — .NET libraries, England.</span>\n");
        sb.Append("    <span class=\"mono\">© <span id=\"yr\"></span>");
        foreach (var link in footer.Links)
        {
            sb.Append(" · <a href=\"").Append(link.Href).Append('"');
            if (link.ExternalNewTab) sb.Append(" target=\"_blank\" rel=\"noopener\"");
            sb.Append('>').Append(link.Text).Append("</a>");
        }
        sb.Append("</span>\n  </div>\n</footer>");
        return sb.ToString();
    }
}

public static class HtmlComposer
{
    private const string TailScript = """

<script>
  document.getElementById('yr').textContent = new Date().getFullYear();
  document.querySelectorAll('.copy').forEach(function(btn){
    btn.addEventListener('click', function(){
      var code = btn.parentElement.querySelector('code').textContent;
      navigator.clipboard.writeText(code).then(function(){
        var old = btn.textContent; btn.textContent = 'copied'; btn.classList.add('done');
        setTimeout(function(){ btn.textContent = old; btn.classList.remove('done'); }, 1400);
      });
    });
  });
</script>
</body>
</html>
""";

    public static string Compose(PageRenderJob job)
    {
        var sb = new StringBuilder();
        sb.Append(job.HeadHtml).Append('\n');
        sb.Append(job.NavHtml).Append('\n');
        foreach (var frag in job.BodyFragments ?? Array.Empty<string>())
            sb.Append(frag).Append('\n');
        sb.Append(job.FooterHtml);
        sb.Append(TailScript);
        return sb.ToString();
    }
}

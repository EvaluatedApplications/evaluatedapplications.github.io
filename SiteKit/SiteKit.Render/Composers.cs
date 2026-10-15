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
        if (page.LeadingHtml is not null) sb.Append(page.LeadingHtml).Append('\n');
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"").Append(page.MetaCharset).Append("\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(page.Seo.Title).Append("</title>\n");
        if (page.Seo.RobotsMeta is not null)
            sb.Append("<meta name=\"robots\" content=\"").Append(page.Seo.RobotsMeta).Append("\">\n");
        sb.Append("<meta name=\"description\" content=\"").Append(page.Seo.Description).Append("\">\n");
        sb.Append("<link rel=\"canonical\" href=\"").Append(page.Seo.Canonical).Append("\">\n");
        sb.Append("<meta property=\"og:type\" content=\"").Append(page.Seo.OgType).Append("\">\n");
        sb.Append("<meta property=\"og:title\" content=\"").Append(page.Seo.OgTitle).Append("\">\n");
        sb.Append("<meta property=\"og:description\" content=\"").Append(page.Seo.OgDescription).Append("\">\n");
        sb.Append("<meta property=\"og:url\" content=\"").Append(page.Seo.OgUrl).Append("\">\n");
        sb.Append("<meta name=\"twitter:card\" content=\"").Append(page.Seo.TwitterCard).Append("\">\n");
        sb.Append("<link rel=\"icon\" href=\"").Append(brand.FaviconDataUri).Append("\">\n");
        sb.Append("<link rel=\"stylesheet\" href=\"/assets/site.css\">\n");
        if (page.PageStyleHtml is not null) sb.Append(page.PageStyleHtml).Append('\n');
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
        sb.Append("<body class=\"os-chrome\"");
        // Category "" (e.g. articles.html — not scoped to one package) omits data-cat entirely
        // rather than emitting an empty attribute.
        if (!string.IsNullOrEmpty(page.Category)) sb.Append(" data-cat=\"").Append(page.Category).Append('"');
        sb.Append(">\n");
        sb.Append("<div class=\"beam\"></div>\n\n");
        sb.Append("<nav class=\"site-nav\">\n  <div class=\"wrap\">\n");
        sb.Append("    <a class=\"brand\" href=\"/\">\n");
        sb.Append("      <svg class=\"mark\" viewBox=\"0 0 32 32\" aria-hidden=\"true\">")
          .Append(brand.MarkGradientSvgDefs)
          .Append("<path d=\"M16 5L27 26H5Z\" fill=\"none\" stroke=\"url(#m)\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>\n");
        sb.Append("      ").Append(brand.CompanyName).Append("\n    </a>\n");
        sb.Append("    <input type=\"checkbox\" id=\"navtoggle\" class=\"nav-toggle\">\n");
        sb.Append("    <label for=\"navtoggle\" class=\"nav-burger\" aria-label=\"").Append(page.NavBurgerAriaLabel).Append("\"><span></span><span></span><span></span></label>\n");
        sb.Append("    <div class=\"nav-links\">\n");
        foreach (var item in page.NavItemsOverride ?? nav.Items)
        {
            sb.Append("      <a href=\"").Append(item.Href).Append('"');
            if (item.CssClass is not null) sb.Append(" class=\"").Append(item.CssClass).Append('"');
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
        var h = page.Hero ?? throw new InvalidOperationException("HeroComposer.Compose called with a null PageSpec.Hero — the pipeline should have skipped this step.");
        var sb = new StringBuilder();
        if (h.LeadingCommentHtml is not null) sb.Append('\n').Append(h.LeadingCommentHtml);
        var heroClass = h.ExtraClass is null ? "hero" : "hero " + h.ExtraClass;
        sb.Append("\n<header class=\"").Append(heroClass).Append("\">\n  <div class=\"wrap\">\n");
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
        if (h.RawBodyHtml is not null)
        {
            // Full escape hatch — bypasses crumb/eyebrow/h1/lede/facts/install/cta/lim/extra/
            // related entirely. See HeroSpec.RawBodyHtml's own doc comment.
            sb.Append(h.RawBodyHtml).Append('\n');
        }
        else
        {
            sb.Append(indent).Append("<p class=\"crumb\">");
            if (h.CrumbHtml is not null) sb.Append(h.CrumbHtml);
            else sb.Append("<a href=\"/\">Home</a><span>/</span><a href=\"/packages.html\">Packages</a><span>/</span>").Append(page.Title);
            sb.Append("</p>\n");
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
                sb.Append("        <div class=\"install\" style=\"max-width:").Append(h.InstallMaxWidthPx).Append("px;margin-top:20px\"><code>")
                  .Append(h.InstallCommand).Append("</code><button class=\"copy\" type=\"button\">copy</button></div>\n");
            }
            if (h.Ctas.Count > 0)
            {
                sb.Append("        <div class=\"cta-row\" style=\"margin-top:16px\">\n");
                foreach (var cta in h.Ctas)
                    sb.Append("          ").Append(RenderCta(cta)).Append('\n');
                sb.Append("        </div>\n");
            }
            if (h.LimHtml is not null)
            {
                sb.Append("        <p class=\"lim\" style=\"margin-top:18px\">").Append(h.LimHtml).Append("</p>\n");
            }
            if (h.ExtraBodyHtml is not null)
            {
                sb.Append("        ").Append(h.ExtraBodyHtml).Append('\n');
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
        SectionKind.StackFlow => ComposeStackFlow(s, defaultCatVar),
        SectionKind.Raw => ComposeRaw(s, defaultCatVar),
        SectionKind.ToolGrid => ComposeToolGrid(s, defaultCatVar),
        SectionKind.Compare => ComposeCompare(s, defaultCatVar),
        SectionKind.ConceptArticle => ComposeConceptArticle(s, defaultCatVar),
        SectionKind.ProseArticle => ComposeProseArticle(s),
        _ => throw new InvalidOperationException($"Unknown SectionKind: {s.Kind}"),
    };

    private static string SecHead(SectionSpec s, string catVar) =>
        $"<div class=\"sec-head\"><span class=\"dot\" style=\"background:{catVar}\"></span><h2>{s.Heading}</h2><p>{s.Tagline}</p></div>";

    /// <summary>The opening `&lt;section class="sec"[ id="..."]&gt;` tag, shared by every
    /// section composer that follows the standard `.sec &gt; .wrap` shape.</summary>
    private static string SecOpen(SectionSpec s)
    {
        var comment = s.LeadingCommentHtml is null ? "" : $"\n{s.LeadingCommentHtml}";
        var tag = s.SectionId is null ? "<section class=\"sec\">" : $"<section class=\"sec\" id=\"{s.SectionId}\">";
        return $"{comment}\n{tag}";
    }

    private static string ComposeProse(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    <p class=\"desc\" style=\"max-width:74ch\">").Append(s.ProseHtml).Append("</p>\n");
        if (s.ExtraHtml is not null) sb.Append("    ").Append(s.ExtraHtml).Append('\n');
        if (s.LimHtml is not null) sb.Append("    <p class=\"lim\">").Append(s.LimHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeStackFlow(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    <div class=\"stack\">\n      <p>").Append(s.ProseHtml).Append("</p>\n");
        sb.Append("      <div class=\"flow\" style=\"margin-top:18px\">\n        ").Append(s.FlowHtml).Append("\n      </div>\n    </div>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeRaw(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    ").Append(s.RawBodyHtml).Append('\n');
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeCardGrid(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        if (s.IntroHtml is not null) sb.Append("    <p class=\"desc\" style=\"max-width:74ch;margin-bottom:20px\">").Append(s.IntroHtml).Append("</p>\n");
        sb.Append("    <div class=\"grid\">\n");
        foreach (var card in s.Cards!) RenderCard(sb, card, catVar, indent: "      ");
        sb.Append("    </div>\n");
        if (s.LimHtml is not null)
        {
            sb.Append("    <p class=\"lim\"");
            if (s.LimStyleAttr is not null) sb.Append(" style=\"").Append(s.LimStyleAttr).Append('"');
            sb.Append('>').Append(s.LimHtml).Append("</p>\n");
        }
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    /// <summary>Shared `&lt;article class="card"&gt;` renderer — CardGrid's `.grid` and
    /// Compare's `.cmp` use the exact same card shape, just a different wrapper div class.</summary>
    private static void RenderCard(StringBuilder sb, CardSpec card, string catVar, string indent)
    {
        sb.Append(indent).Append("<article class=\"card\"");
        if (!card.OmitCatStyle)
        {
            var cat = card.CatOverride ?? catVar;
            sb.Append(" style=\"--cat:").Append(cat);
            if (card.CatRootOverride is not null) sb.Append("; --cat-root:").Append(card.CatRootOverride);
            sb.Append('"');
        }
        sb.Append(">\n");
        sb.Append(indent).Append("  <div class=\"card-top\"><h3>").Append(card.Title).Append("</h3></div>\n");
        if (card.PreBodyHtml is not null) sb.Append(indent).Append("  ").Append(card.PreBodyHtml).Append('\n');
        sb.Append(indent).Append("  <p class=\"desc\">").Append(card.BodyHtml).Append("</p>\n");
        if (card.LimHtml is not null) sb.Append(indent).Append("  <p class=\"lim\">").Append(card.LimHtml).Append("</p>\n");
        sb.Append(indent).Append("</article>\n");
    }

    private static string ComposeToolGrid(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        var gridIndent = s.OmitGridWrapper ? "    " : "      ";
        if (!s.OmitGridWrapper) sb.Append("    <div class=\"grid\">\n");
        foreach (var t in s.ToolCards!)
        {
            var cat = t.CatOverride ?? catVar;
            sb.Append(gridIndent).Append("<a class=\"card tool\" href=\"").Append(t.Href).Append("\" style=\"--cat:").Append(cat);
            if (t.CatRootOverride is not null) sb.Append("; --cat-root:").Append(t.CatRootOverride);
            sb.Append("\">\n");
            sb.Append(gridIndent).Append("  <div class=\"card-top\"><h3>").Append(t.Title).Append("</h3><span class=\"tag live\">").Append(t.Tag).Append("</span>");
            if (t.Ver is not null) sb.Append("<span class=\"ver\">").Append(t.Ver).Append("</span>");
            sb.Append("</div>\n");
            sb.Append(gridIndent).Append("  <p class=\"desc\">").Append(t.DescHtml).Append("</p>\n");
            sb.Append(gridIndent).Append("  <div class=\"go-in\">").Append(t.GoInText).Append("</div>\n");
            sb.Append(gridIndent).Append("</a>\n");
        }
        if (!s.OmitGridWrapper) sb.Append("    </div>\n");
        if (s.LimHtml is not null) sb.Append("    <p class=\"lim\">").Append(s.LimHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeCompare(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        sb.Append("    <div class=\"cmp\" style=\"display:grid;grid-template-columns:1fr 1fr;gap:16px\">\n");
        foreach (var card in s.Cards!) RenderCard(sb, card, catVar, indent: "      ");
        sb.Append("    </div>\n");
        sb.Append("    <p class=\"desc\" style=\"max-width:74ch;margin-top:16px\">").Append(s.ProseHtml).Append("</p>\n");
        sb.Append("  </div>\n</section>");
        return sb.ToString();
    }

    private static string ComposeConceptArticle(SectionSpec s, string catVar)
    {
        var c = s.ConceptArticleData!;
        var sb = new StringBuilder();
        sb.Append("\n<main class=\"sec\"><div class=\"wrap\" style=\"max-width:").Append(c.WrapMaxWidthPx).Append("px\">\n\n");
        foreach (var card in c.Cards)
        {
            sb.Append("  <div class=\"concept\">\n");
            sb.Append("    <div class=\"glyph\">").Append(card.GlyphSvgInner).Append("</div>\n");
            sb.Append("    <div>\n");
            sb.Append("      <span class=\"kick\">").Append(card.Kick).Append("</span>\n");
            sb.Append("      <h2>").Append(card.Heading).Append("</h2>\n");
            foreach (var p in card.ParagraphsHtml)
                sb.Append("      <p>").Append(p).Append("</p>\n");
            sb.Append("      <div class=\"anchor\">").Append(card.AnchorHtml).Append("</div>\n");
            sb.Append("    </div>\n  </div>\n\n");
        }
        if (c.CompareCards is { Count: > 0 })
        {
            sb.Append("  <div class=\"sec-head\" style=\"margin-top:34px\"><span class=\"dot\" style=\"background:").Append(catVar).Append("\"></span><h2>")
              .Append(c.CompareHeading).Append("</h2><p>").Append(c.CompareTagline).Append("</p></div>\n");
            sb.Append("  <div class=\"cmp\">\n");
            foreach (var cc in c.CompareCards)
            {
                sb.Append("    <article class=\"card ").Append(cc.ClassName).Append("\">\n");
                sb.Append("      <h3>").Append(cc.Title).Append("</h3>\n");
                sb.Append("      <ul>\n");
                foreach (var item in cc.ItemsHtml)
                    sb.Append("        <li>").Append(item).Append("</li>\n");
                sb.Append("      </ul>\n    </article>\n");
            }
            sb.Append("  </div>\n");
        }
        if (c.NoteHtml is not null)
            sb.Append("  <p class=\"note\" style=\"margin-top:16px; color:var(--ink-faint); max-width:72ch\">").Append(c.NoteHtml).Append("</p>\n\n");
        sb.Append("  ").Append(c.ClosingHtml).Append('\n');
        sb.Append("\n</div></main>");
        return sb.ToString();
    }

    private static string ComposeProseArticle(SectionSpec s)
    {
        var a = s.ProseArticleData!;
        var sb = new StringBuilder();
        sb.Append("\n<main class=\"wrap\">\n");
        if (a.PreArticleHtml is not null) sb.Append('\n').Append(a.PreArticleHtml).Append('\n');
        sb.Append("\n<article class=\"prose\">\n\n");
        sb.Append("  <nav class=\"crumb\">").Append(a.CrumbHtml).Append("</nav>\n");
        sb.Append("  <h1>").Append(a.H1).Append("</h1>\n");
        if (a.ByelineHtml is not null)
            sb.Append("  <p class=\"article-date\" style=\"margin-top:12px\">").Append(a.ByelineHtml).Append("</p>\n");
        if (a.LedeHtml is not null)
            sb.Append("  <p class=\"lede\" style=\"margin-top:12px\">").Append(a.LedeHtml).Append("</p>\n");
        if (a.Related is { Count: > 0 })
        {
            sb.Append("\n  <div class=\"related\">\n    <span class=\"related-label\">Related</span>\n");
            foreach (var r in a.Related)
            {
                sb.Append("    <a href=\"").Append(r.Href).Append('"');
                if (r.ExternalNewTab) sb.Append(" target=\"_blank\" rel=\"noopener\"");
                sb.Append('>').Append(r.Text).Append("</a>\n");
            }
            sb.Append("    <a class=\"related-all\" href=\"").Append(a.RelatedAllHref).Append("\">")
              .Append(a.RelatedAllText).Append("</a>\n  </div>\n");
        }
        if (a.TocItems is { Count: > 0 })
        {
            if (a.TocLeadHtml is not null) sb.Append('\n').Append("  ").Append(a.TocLeadHtml).Append('\n');
            sb.Append("\n  <div class=\"toc\">\n    <h2>On this page</h2>\n    <ul>\n");
            foreach (var t in a.TocItems)
                sb.Append("      <li><a href=\"").Append(t.Href).Append("\">").Append(t.Text).Append("</a></li>\n");
            sb.Append("    </ul>\n  </div>\n");
        }
        sb.Append('\n').Append(a.BodyHtml).Append('\n');
        sb.Append("\n</article>\n</main>");
        return sb.ToString();
    }

    private static string ComposeSnippets(SectionSpec s, string catVar)
    {
        var sb = new StringBuilder();
        sb.Append(SecOpen(s)).Append("\n  <div class=\"wrap\">\n    ");
        sb.Append(SecHead(s, catVar)).Append('\n');
        foreach (var snip in s.Snippets!)
        {
            if (snip.DescBeforeHtml is not null)
                sb.Append("    <p class=\"desc\">").Append(snip.DescBeforeHtml).Append("</p>\n");
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
        if (s.LeadingCommentHtml is not null) sb.Append('\n').Append(s.LeadingCommentHtml);
        sb.Append("\n<section class=\"sec\">\n  <div class=\"wrap\">\n    <div class=\"stack\">\n");
        sb.Append("      <h2>").Append(s.Heading).Append("</h2>\n");
        sb.Append("      <p>").Append(s.ClosingBodyHtml).Append("</p>\n");
        if (s.ClosingInstallCommand is not null)
        {
            sb.Append("      <div class=\"install\" style=\"margin-top:14px\"><code>").Append(s.ClosingInstallCommand)
              .Append("</code><button class=\"copy\" type=\"button\">copy</button></div>\n");
        }
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
        sb.Append(job.Spec.TailScriptHtml is not null ? "\n" + job.Spec.TailScriptHtml + "\n</body>\n</html>" : TailScript);
        return sb.ToString();
    }
}

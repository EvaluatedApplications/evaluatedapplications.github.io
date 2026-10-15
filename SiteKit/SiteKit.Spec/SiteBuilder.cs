namespace SiteKit.Spec;

// ── The fluent builder, styled directly on EvalApp's own chain shape ───────────────────────
// (platform-architecture.md §3: "the builder chain is the architecture... reading the spec
// top to bottom should read like the page"). Site.Define(...).Page(...).Build(out spec).

public static class Site
{
    public static ISiteBuilder Define(string siteId, BrandTokens brand, NavSpec nav, string outputRoot) =>
        new SiteBuilder(siteId, brand, nav, outputRoot);
}

public interface ISiteBuilder
{
    ISiteBuilder Page(string slug, string title, string category, string categoryDotVar, Action<IPageBuilder> configure);
    ISiteBuilder Build(out SiteSpec spec);
}

public interface IPageBuilder
{
    IPageBuilder Seo(SeoSpec seo);
    /// <summary>Optional — omit entirely for a "prose-template" page (no `&lt;header
    /// class="hero"&gt;` at all, see PageSpec.Hero).</summary>
    IPageBuilder Hero(Action<IHeroBuilder> configure);
    IPageBuilder Section(SectionSpec section);
    IPageBuilder Footer(FooterSpec footer);
    /// <summary>Verbatim page-local `&lt;style&gt;...&lt;/style&gt;` block — see PageSpec.PageStyleHtml.</summary>
    IPageBuilder PageStyle(string styleBlockHtml);
    /// <summary>Full override of the closing `&lt;script&gt;` block — see PageSpec.TailScriptHtml.</summary>
    IPageBuilder TailScript(string scriptHtml);
    /// <summary>Override `&lt;meta charset&gt;` — see PageSpec.MetaCharset. Default "utf-8".</summary>
    IPageBuilder MetaCharset(string charset);
    /// <summary>Override the top nav's item list for this page only — see PageSpec.NavItemsOverride.</summary>
    IPageBuilder NavItems(IReadOnlyList<RelatedLink> items);
    /// <summary>Raw HTML before `&lt;!DOCTYPE html&gt;` — see PageSpec.LeadingHtml.</summary>
    IPageBuilder Leading(string html);
    /// <summary>Override the nav-burger's aria-label — see PageSpec.NavBurgerAriaLabel.</summary>
    IPageBuilder NavBurgerAriaLabel(string label);
}

public interface IHeroBuilder
{
    IHeroBuilder Eyebrow(string text);
    IHeroBuilder Headline(string text);
    IHeroBuilder Lede(string text);
    IHeroBuilder Fact(string html);
    IHeroBuilder Install(string command, int maxWidthPx = 520);
    IHeroBuilder Cta(string text, string href, CtaStyle style, bool externalNewTab = false);
    IHeroBuilder Related(string text, string href, bool externalNewTab = false);
    IHeroBuilder BarTitle(string title);
    IHeroBuilder PrismBeam();
    /// <summary>Optional `.lim` caveat paragraph, rendered after the CTA row and before Related — see HeroSpec.LimHtml.</summary>
    IHeroBuilder Lim(string html);
    /// <summary>Override the crumb's inner HTML — see HeroSpec.CrumbHtml.</summary>
    IHeroBuilder Crumb(string html);
    /// <summary>Raw HTML after Lim, before Related — see HeroSpec.ExtraBodyHtml.</summary>
    IHeroBuilder ExtraBody(string html);
    /// <summary>Full raw override of the whole hero-body content — see HeroSpec.RawBodyHtml.</summary>
    IHeroBuilder RawBody(string html);
    /// <summary>Extra class on `&lt;header class="hero ..."&gt;` — see HeroSpec.ExtraClass.</summary>
    IHeroBuilder ExtraHeroClass(string cssClass);
    /// <summary>Raw HTML before `&lt;header ...&gt;` — see HeroSpec.LeadingCommentHtml.</summary>
    IHeroBuilder LeadingComment(string html);
}

internal sealed class SiteBuilder : ISiteBuilder
{
    private readonly string _siteId;
    private readonly BrandTokens _brand;
    private readonly NavSpec _nav;
    private readonly string _outputRoot;
    private readonly List<PageSpec> _pages = new();

    public SiteBuilder(string siteId, BrandTokens brand, NavSpec nav, string outputRoot)
    {
        _siteId = siteId;
        _brand = brand;
        _nav = nav;
        _outputRoot = outputRoot;
    }

    public ISiteBuilder Page(string slug, string title, string category, string categoryDotVar, Action<IPageBuilder> configure)
    {
        var pb = new PageBuilder(slug, title, category, categoryDotVar);
        configure(pb);
        _pages.Add(pb.Build());
        return this;
    }

    public ISiteBuilder Build(out SiteSpec spec)
    {
        spec = new SiteSpec(_siteId, _brand, _nav, _pages, _outputRoot);
        return this;
    }
}

internal sealed class PageBuilder : IPageBuilder
{
    private readonly string _slug, _title, _category, _categoryDotVar;
    private SeoSpec? _seo;
    private HeroSpec? _hero;
    private readonly List<SectionSpec> _sections = new();
    private FooterSpec? _footer;
    private string? _pageStyleHtml;
    private string? _tailScriptHtml;
    private string _metaCharset = "utf-8";
    private IReadOnlyList<RelatedLink>? _navItemsOverride;
    private string? _leadingHtml;
    private string _navBurgerAriaLabel = "Toggle menu";

    public PageBuilder(string slug, string title, string category, string categoryDotVar)
    {
        _slug = slug; _title = title; _category = category; _categoryDotVar = categoryDotVar;
    }

    public IPageBuilder Seo(SeoSpec seo) { _seo = seo; return this; }

    public IPageBuilder Hero(Action<IHeroBuilder> configure)
    {
        var hb = new HeroBuilder();
        configure(hb);
        _hero = hb.Build();
        return this;
    }

    public IPageBuilder Section(SectionSpec section) { _sections.Add(section); return this; }

    public IPageBuilder Footer(FooterSpec footer) { _footer = footer; return this; }

    public IPageBuilder PageStyle(string styleBlockHtml) { _pageStyleHtml = styleBlockHtml; return this; }

    public IPageBuilder TailScript(string scriptHtml) { _tailScriptHtml = scriptHtml; return this; }

    public IPageBuilder MetaCharset(string charset) { _metaCharset = charset; return this; }

    public IPageBuilder NavItems(IReadOnlyList<RelatedLink> items) { _navItemsOverride = items; return this; }

    public IPageBuilder Leading(string html) { _leadingHtml = html; return this; }

    public IPageBuilder NavBurgerAriaLabel(string label) { _navBurgerAriaLabel = label; return this; }

    public PageSpec Build()
    {
        if (_seo is null) throw new InvalidOperationException($"Page '{_slug}': .Seo(...) is required.");
        // .Hero(...) is now OPTIONAL — a null Hero means a "prose-template" page with no
        // <header class="hero"> at all (see PageSpec.Hero's own doc comment).
        if (_footer is null) throw new InvalidOperationException($"Page '{_slug}': .Footer(...) is required.");
        return new PageSpec(_slug, _title, _category, _categoryDotVar, _seo, _hero, _sections, _footer, _pageStyleHtml, _tailScriptHtml, _metaCharset, _navItemsOverride, _leadingHtml, _navBurgerAriaLabel);
    }
}

internal sealed class HeroBuilder : IHeroBuilder
{
    private string _eyebrow = "", _headline = "", _lede = "", _barTitle = "";
    private string? _install;
    private int _installMaxWidthPx = 520;
    private bool _prismBeam;
    private string? _limHtml;
    private string? _crumbHtml;
    private string? _extraBodyHtml;
    private string? _rawBodyHtml;
    private string? _extraHeroClass;
    private string? _leadingCommentHtml;
    private readonly List<FactChip> _facts = new();
    private readonly List<CtaLink> _ctas = new();
    private readonly List<RelatedLink> _related = new();

    public IHeroBuilder Eyebrow(string text) { _eyebrow = text; return this; }
    public IHeroBuilder Headline(string text) { _headline = text; return this; }
    public IHeroBuilder Lede(string text) { _lede = text; return this; }
    public IHeroBuilder Fact(string html) { _facts.Add(new FactChip(html)); return this; }
    public IHeroBuilder Install(string command, int maxWidthPx = 520) { _install = command; _installMaxWidthPx = maxWidthPx; return this; }
    public IHeroBuilder Cta(string text, string href, CtaStyle style, bool externalNewTab = false)
    { _ctas.Add(new CtaLink(text, href, style, externalNewTab)); return this; }
    public IHeroBuilder Related(string text, string href, bool externalNewTab = false)
    { _related.Add(new RelatedLink(text, href, externalNewTab)); return this; }
    public IHeroBuilder BarTitle(string title) { _barTitle = title; return this; }
    public IHeroBuilder PrismBeam() { _prismBeam = true; return this; }
    public IHeroBuilder Lim(string html) { _limHtml = html; return this; }
    public IHeroBuilder Crumb(string html) { _crumbHtml = html; return this; }
    public IHeroBuilder ExtraBody(string html) { _extraBodyHtml = html; return this; }
    public IHeroBuilder RawBody(string html) { _rawBodyHtml = html; return this; }
    public IHeroBuilder ExtraHeroClass(string cssClass) { _extraHeroClass = cssClass; return this; }
    public IHeroBuilder LeadingComment(string html) { _leadingCommentHtml = html; return this; }

    public HeroSpec Build() => new(_eyebrow, _headline, _lede, _facts, _install, _ctas, _related, _barTitle, _prismBeam,
        LimHtml: _limHtml, InstallMaxWidthPx: _installMaxWidthPx, CrumbHtml: _crumbHtml, ExtraBodyHtml: _extraBodyHtml,
        RawBodyHtml: _rawBodyHtml, ExtraClass: _extraHeroClass, LeadingCommentHtml: _leadingCommentHtml);
}

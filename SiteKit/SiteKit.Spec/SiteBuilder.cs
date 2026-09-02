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
    IPageBuilder Hero(Action<IHeroBuilder> configure);
    IPageBuilder Section(SectionSpec section);
    IPageBuilder Footer(FooterSpec footer);
}

public interface IHeroBuilder
{
    IHeroBuilder Eyebrow(string text);
    IHeroBuilder Headline(string text);
    IHeroBuilder Lede(string text);
    IHeroBuilder Fact(string html);
    IHeroBuilder Install(string command);
    IHeroBuilder Cta(string text, string href, CtaStyle style, bool externalNewTab = false);
    IHeroBuilder Related(string text, string href, bool externalNewTab = false);
    IHeroBuilder BarTitle(string title);
    IHeroBuilder PrismBeam();
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

    public PageSpec Build()
    {
        if (_seo is null) throw new InvalidOperationException($"Page '{_slug}': .Seo(...) is required.");
        if (_hero is null) throw new InvalidOperationException($"Page '{_slug}': .Hero(...) is required.");
        if (_footer is null) throw new InvalidOperationException($"Page '{_slug}': .Footer(...) is required.");
        return new PageSpec(_slug, _title, _category, _categoryDotVar, _seo, _hero, _sections, _footer);
    }
}

internal sealed class HeroBuilder : IHeroBuilder
{
    private string _eyebrow = "", _headline = "", _lede = "", _barTitle = "";
    private string? _install;
    private bool _prismBeam;
    private readonly List<FactChip> _facts = new();
    private readonly List<CtaLink> _ctas = new();
    private readonly List<RelatedLink> _related = new();

    public IHeroBuilder Eyebrow(string text) { _eyebrow = text; return this; }
    public IHeroBuilder Headline(string text) { _headline = text; return this; }
    public IHeroBuilder Lede(string text) { _lede = text; return this; }
    public IHeroBuilder Fact(string html) { _facts.Add(new FactChip(html)); return this; }
    public IHeroBuilder Install(string command) { _install = command; return this; }
    public IHeroBuilder Cta(string text, string href, CtaStyle style, bool externalNewTab = false)
    { _ctas.Add(new CtaLink(text, href, style, externalNewTab)); return this; }
    public IHeroBuilder Related(string text, string href, bool externalNewTab = false)
    { _related.Add(new RelatedLink(text, href, externalNewTab)); return this; }
    public IHeroBuilder BarTitle(string title) { _barTitle = title; return this; }
    public IHeroBuilder PrismBeam() { _prismBeam = true; return this; }

    public HeroSpec Build() => new(_eyebrow, _headline, _lede, _facts, _install, _ctas, _related, _barTitle, _prismBeam);
}

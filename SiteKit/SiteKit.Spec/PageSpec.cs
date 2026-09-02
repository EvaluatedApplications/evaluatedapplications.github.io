namespace SiteKit.Spec;

// ── The declarative page/site spec ─────────────────────────────────────────────────────────
// Plain immutable data. No HTML, no rendering logic — SiteKit.Render turns this into markup.
// Shape sketched in docs/platform-architecture.md §3.1, filled in for real (not final API,
// see COMPONENTS.md for the reusable-vs-brand-specific split once this graduates further).

public enum CtaStyle { Primary, Ghost }

public enum SectionKind { Prose, CardGrid, Snippets, ClosingStack }

/// <summary>Never Eager — see platform-architecture.md §3.3, the 250x app-shell-boot gap.</summary>
public enum IslandActivation { OnClick, OnVisible }

/// <summary>A single fact/version pill in the hero, e.g. "&lt;b&gt;v1.0.3&lt;/b&gt;". Inner HTML is
/// deliberately allowed here (bold/code inline formatting) — this is content data with light
/// inline markup, the same latitude SectionSpec.ProseHtml already needs; it is NOT a place to
/// author structural HTML.</summary>
public sealed record FactChip(string Html);

public sealed record CtaLink(string Text, string Href, CtaStyle Style, bool ExternalNewTab = false);

public sealed record RelatedLink(string Text, string Href, bool ExternalNewTab = false);

public sealed record SeoSpec(
    string Title,
    string Description,
    string Canonical,
    string OgTitle,
    string OgDescription,
    string OgUrl,
    string TwitterCard,
    string JsonLd
);

public sealed record HeroSpec(
    string Eyebrow,
    string Headline,
    string Lede,
    IReadOnlyList<FactChip> Facts,
    string? InstallCommand,
    IReadOnlyList<CtaLink> Ctas,
    IReadOnlyList<RelatedLink> Related,
    string BarTitle,
    bool ShowPrismBeam = false,
    string RelatedAllText = "All packages →",
    string RelatedAllHref = "/packages.html"
);

/// <summary>CatOverride sets the card's own --cat (a solid colour OR a two-tone
/// linear-gradient() "chord" for a composite/multi-domain package, e.g. Prose). CatRootOverride
/// is the companion --cat-root a chord ALSO needs (CSS custom props can't be read back out of a
/// gradient value, so any call site that needs one real solid colour from a chorded card —
/// today: none on Prose's own page, but the pattern exists sitewide, see COMPONENTS.md's "--cat
/// / --cat-root" note) sets this second prop alongside it. Null unless CatOverride is itself a
/// gradient.</summary>
public sealed record CardSpec(string Title, string BodyHtml, string? CatOverride = null, string? CatRootOverride = null);

public sealed record SnippetSpec(string Code, string? DescAfterHtml = null);

public sealed record IslandRef(string ComponentId, string MountSelector, IslandActivation Activation);

public sealed record SectionSpec(
    SectionKind Kind,
    string Heading,
    string Tagline,
    string? ProseHtml = null,
    IReadOnlyList<CardSpec>? Cards = null,
    IReadOnlyList<SnippetSpec>? Snippets = null,
    string? LimHtml = null,
    string? ClosingBodyHtml = null,
    IReadOnlyList<CtaLink>? ClosingCtas = null,
    IReadOnlyList<IslandRef>? Islands = null
)
{
    public static SectionSpec Prose(string heading, string tagline, string proseHtml, string? limHtml = null) =>
        new(SectionKind.Prose, heading, tagline, ProseHtml: proseHtml, LimHtml: limHtml);

    public static SectionSpec CardGrid(string heading, string tagline, IReadOnlyList<CardSpec> cards, string? limHtml = null) =>
        new(SectionKind.CardGrid, heading, tagline, Cards: cards, LimHtml: limHtml);

    public static SectionSpec SnippetList(string heading, string tagline, IReadOnlyList<SnippetSpec> snippets, string? limHtml = null) =>
        new(SectionKind.Snippets, heading, tagline, Snippets: snippets, LimHtml: limHtml);

    public static SectionSpec ClosingStack(string heading, string bodyHtml, IReadOnlyList<CtaLink> ctas) =>
        new(SectionKind.ClosingStack, heading, Tagline: "", ClosingBodyHtml: bodyHtml, ClosingCtas: ctas);
}

public sealed record FooterSpec(IReadOnlyList<RelatedLink> Links);

public sealed record PageSpec(
    string Slug,               // "phasor" -> site/phasor.html
    string Title,               // breadcrumb/eyebrow display name, e.g. "Phasor"
    string Category,            // data-cat value, e.g. "foundation"
    string CategoryDotVar,      // e.g. "var(--c-foundation)" — used for .sec-head .dot / default card --cat
    SeoSpec Seo,
    HeroSpec Hero,
    IReadOnlyList<SectionSpec> Sections,
    FooterSpec Footer
);

public sealed record NavSpec(IReadOnlyList<RelatedLink> Items);

public sealed record BrandTokens(
    string CompanyName,
    string FaviconDataUri,
    string MarkGradientSvgDefs,   // the <defs><linearGradient id="..."> ... block, id parameterised by caller
    string PrismBeamSvg           // the full .prism-beam inner <svg>...</svg>, identical wherever ShowPrismBeam=true
);

public sealed record SiteSpec(
    string SiteId,
    BrandTokens Brand,
    NavSpec Nav,
    IReadOnlyList<PageSpec> Pages,
    string OutputRoot
);

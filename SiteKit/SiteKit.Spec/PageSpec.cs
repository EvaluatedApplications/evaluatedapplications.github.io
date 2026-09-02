namespace SiteKit.Spec;

// ── The declarative page/site spec ─────────────────────────────────────────────────────────
// Plain immutable data. No HTML, no rendering logic — SiteKit.Render turns this into markup.
// Shape sketched in docs/platform-architecture.md §3.1, filled in for real (not final API,
// see COMPONENTS.md for the reusable-vs-brand-specific split once this graduates further).

public enum CtaStyle { Primary, Ghost }

public enum SectionKind { Prose, CardGrid, Snippets, ClosingStack, StackFlow, Raw }

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

/// <summary>LimHtml is an optional hero-scoped caveat/note paragraph (`.lim`, page 2 of the
/// component inventory this record already covers), rendered after the CTA row and before the
/// Related pills — the shape `evalapp.html` and `holodb-protocol.html` both use for a short
/// "who should actually depend on this" aside right in the hero. Null on every hero that doesn't
/// carry one (most of them) — first exercised Phase 2's second batch (2026-09-02).</summary>
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
    string RelatedAllHref = "/packages.html",
    string? LimHtml = null,
    /// <summary>The `.install` chip's inline `max-width` in px — the live site isn't uniform
    /// here: most pages use 520 but a longer install command (e.g.
    /// `EvaluatedApplications.HoloDb.Protocol`, `EvaluatedApplications.AlgFormer.Gpu`) gets 560
    /// so the chip doesn't wrap. Found as a real (fixed, not papered-over) diff during Phase 2's
    /// second batch (2026-09-02) — algformer-gpu.html/holodb-protocol.html both failed the
    /// structural-identity check with this hardcoded to 520 before this field existed.</summary>
    int InstallMaxWidthPx = 520
);

/// <summary>CatOverride sets the card's own --cat (a solid colour OR a two-tone
/// linear-gradient() "chord" for a composite/multi-domain package, e.g. Prose). CatRootOverride
/// is the companion --cat-root a chord ALSO needs (CSS custom props can't be read back out of a
/// gradient value, so any call site that needs one real solid colour from a chorded card —
/// today: none on Prose's own page, but the pattern exists sitewide, see COMPONENTS.md's "--cat
/// / --cat-root" note) sets this second prop alongside it. Null unless CatOverride is itself a
/// gradient.</summary>
public sealed record CardSpec(string Title, string BodyHtml, string? CatOverride = null, string? CatRootOverride = null);

/// <summary>DescBeforeHtml is a description paragraph emitted immediately BEFORE this snippet
/// (the `holodb-protocol.html` "Client-side... / Server-side..." shape, where every snippet in
/// the section is introduced by its own lead-in line); DescAfterHtml is emitted immediately
/// AFTER it (the `phasor.html`/`tracer.html`/`holodb-client.html`/`evalapp-neural.html` shape,
/// where a description between two snippets reads as introducing the NEXT one even though it's
/// attached structurally to the previous SnippetSpec). Both are independently optional and a
/// section can freely mix the two shapes across its own snippet list.</summary>
public sealed record SnippetSpec(string Code, string? DescAfterHtml = null, string? DescBeforeHtml = null);

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
    IReadOnlyList<IslandRef>? Islands = null,
    /// <summary>Raw HTML inserted after a Prose section's own `&lt;p class="desc"&gt;` and
    /// before its optional `.lim` — the `holovoxel.html` "before/after screenshot pair" `.shots`
    /// figure grid is the first real use (2026-09-02). Deliberately raw (same latitude
    /// CardSpec.BodyHtml/SnippetSpec.Code already get), not a new typed figure-gallery spec —
    /// one real bespoke block doesn't earn its own schema yet.</summary>
    string? ExtraHtml = null,
    /// <summary>Raw inner HTML of a `.flow` diagram row (spans/`&lt;em&gt;` joined by + and =,
    /// e.g. evalapp.html's "SemaphoreSlim + MediatR + ... = EvalApp"), used only by
    /// SectionKind.StackFlow.</summary>
    string? FlowHtml = null,
    /// <summary>Raw HTML dropped verbatim inside the section's `.wrap`, right after its own
    /// `.sec-head` — used only by SectionKind.Raw for genuinely bespoke one-off content (e.g.
    /// evalapp.html's "where the ideas come from" `&lt;table&gt;`) that doesn't fit Prose/
    /// CardGrid/Snippets/StackFlow and isn't common enough yet to earn its own typed spec.</summary>
    string? RawBodyHtml = null
)
{
    public static SectionSpec Prose(string heading, string tagline, string proseHtml, string? limHtml = null, string? extraHtml = null) =>
        new(SectionKind.Prose, heading, tagline, ProseHtml: proseHtml, LimHtml: limHtml, ExtraHtml: extraHtml);

    public static SectionSpec CardGrid(string heading, string tagline, IReadOnlyList<CardSpec> cards, string? limHtml = null) =>
        new(SectionKind.CardGrid, heading, tagline, Cards: cards, LimHtml: limHtml);

    public static SectionSpec SnippetList(string heading, string tagline, IReadOnlyList<SnippetSpec> snippets, string? limHtml = null) =>
        new(SectionKind.Snippets, heading, tagline, Snippets: snippets, LimHtml: limHtml);

    public static SectionSpec ClosingStack(string heading, string bodyHtml, IReadOnlyList<CtaLink> ctas) =>
        new(SectionKind.ClosingStack, heading, Tagline: "", ClosingBodyHtml: bodyHtml, ClosingCtas: ctas);

    /// <summary>A `.sec-head`-titled section whose body is a `.stack` holding one prose
    /// paragraph plus a `.flow` diagram row — distinct from ClosingStack, which has no
    /// `.sec-head`/`.wrap` framing and is always the page's final CTA block.</summary>
    public static SectionSpec StackFlow(string heading, string tagline, string proseHtml, string flowHtml) =>
        new(SectionKind.StackFlow, heading, tagline, ProseHtml: proseHtml, FlowHtml: flowHtml);

    /// <summary>An escape hatch for one-off bespoke content (tables, custom widgets) that isn't
    /// common enough yet to justify its own typed SectionKind — still gets the standard
    /// `.sec`/`.wrap`/`.sec-head` framing, just a raw body.</summary>
    public static SectionSpec Raw(string heading, string tagline, string rawBodyHtml) =>
        new(SectionKind.Raw, heading, tagline, RawBodyHtml: rawBodyHtml);
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
    FooterSpec Footer,
    /// <summary>Full verbatim `&lt;style&gt;...&lt;/style&gt;` block for the rare page that
    /// still carries page-local CSS (e.g. `holovoxel.html`'s `.shots` figure grid) — emitted in
    /// `&lt;head&gt;` right after the shared stylesheet `&lt;link&gt;` and before the JSON-LD
    /// `&lt;script&gt;`, matching every such page's live markup order. Null on every page that
    /// only uses `site.css` (the large majority).</summary>
    string? PageStyleHtml = null
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

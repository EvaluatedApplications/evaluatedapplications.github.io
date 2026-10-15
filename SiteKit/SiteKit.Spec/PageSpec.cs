namespace SiteKit.Spec;

// ── The declarative page/site spec ─────────────────────────────────────────────────────────
// Plain immutable data. No HTML, no rendering logic — SiteKit.Render turns this into markup.
// Shape sketched in docs/platform-architecture.md §3.1, filled in for real (not final API,
// see COMPONENTS.md for the reusable-vs-brand-specific split once this graduates further).

public enum CtaStyle { Primary, Ghost }

public enum SectionKind { Prose, CardGrid, Snippets, ClosingStack, StackFlow, Raw, ToolGrid, Compare, ConceptArticle, ProseArticle }

/// <summary>Never Eager — see platform-architecture.md §3.3, the 250x app-shell-boot gap.</summary>
public enum IslandActivation { OnClick, OnVisible }

/// <summary>A single fact/version pill in the hero, e.g. "&lt;b&gt;v1.0.3&lt;/b&gt;". Inner HTML is
/// deliberately allowed here (bold/code inline formatting) — this is content data with light
/// inline markup, the same latitude SectionSpec.ProseHtml already needs; it is NOT a place to
/// author structural HTML.</summary>
public sealed record FactChip(string Html);

public sealed record CtaLink(string Text, string Href, CtaStyle Style, bool ExternalNewTab = false);

/// <summary>CssClass is an optional extra class on the rendered `&lt;a&gt;` (e.g. `"active"` —
/// the HoloDb manual's own nav highlights its own "Manual" link this way). Null on every other
/// use, first exercised by the HoloDb manual page (Phase 2, third batch).</summary>
public sealed record RelatedLink(string Text, string Href, bool ExternalNewTab = false, string? CssClass = null);

public sealed record SeoSpec(
    string Title,
    string Description,
    string Canonical,
    string OgTitle,
    string OgDescription,
    string OgUrl,
    string TwitterCard,
    string JsonLd,
    /// <summary>`og:type` — "website" (the default, every plain product/index page) or "article"
    /// (the explainer/reference pages: `holoformer.html`, `holodb.html`, `holodb/manual/index.html`,
    /// `articles/_example.html`). Was hardcoded to "website" in HeadComposer until this field
    /// existed — the diff against `holoformer.html` caught the real, unhandled divergence
    /// (Phase 2, third batch, 2026-09-02).</summary>
    string OgType = "website",
    /// <summary>`&lt;meta name="robots" content="..."&gt;`, rendered right after `&lt;title&gt;`
    /// and before the description meta — null (the default) omits the tag entirely. Today only
    /// `articles/_example.html` sets this (`"noindex,nofollow"` — a deliberately unlisted
    /// template page, see PageSpec.LeadingHtml).</summary>
    string? RobotsMeta = null
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
    int InstallMaxWidthPx = 520,
    /// <summary>Overrides the crumb's inner HTML (default is the standard two-hop
    /// `Home / Packages / {page.Title}`). Used by pages with a different breadcrumb depth —
    /// `holoformer.html` ("Home / AlgFormer / HoloFormer, explained"), `articles.html`
    /// ("Home / Articles", no Packages hop). Null = the standard two-hop crumb. Phase 2, third
    /// batch (2026-09-02).</summary>
    string? CrumbHtml = null,
    /// <summary>Raw HTML inserted after the (optional) `.lim` and before the (optional)
    /// `.related` pills — e.g. `holoformer.html`'s `.thesis` before/after figure pair, sitting
    /// between the lede and the related links on a hero with no facts/install/cta at all. Null
    /// on every hero that doesn't need one. Phase 2, third batch (2026-09-02).</summary>
    string? ExtraBodyHtml = null,
    /// <summary>A full override of the hero-body's inner content — bypasses crumb/eyebrow/h1/
    /// lede/facts/install/cta-row/lim/extra/related entirely, emitting this raw HTML instead
    /// (still inside the shared hero-bar/prism-beam wrapper mechanics). An escape hatch for a
    /// hero widget-heavy enough that forcing it through the typed fields above would mean most
    /// of them going unused while fighting the ones that don't fit (e.g. the HoloDb hub's
    /// `.race`/`.fourstrip` hero, which has no crumb, no facts pills, and a install-after-cta-row
    /// order the standard composer doesn't produce). Null on every other page. Phase 2, third
    /// batch (2026-09-02) — see COMPONENTS.md for the "type what recurs, escape-hatch what's
    /// genuinely one-off" rule this follows.</summary>
    string? RawBodyHtml = null,
    /// <summary>An extra space-separated class appended to `&lt;header class="hero ..."&gt;` —
    /// the HoloDb hub's own `hd-hero` page-local class (its `&lt;style&gt;` block sets
    /// `.hd-hero{padding:...}`, a hero-only override no other page needs). Null on every hero
    /// that doesn't carry one.</summary>
    string? ExtraClass = null,
    /// <summary>Raw HTML (typically an HTML comment) emitted immediately before
    /// `&lt;header class="hero..."&gt;` — the HoloDb hub's own `&lt;!-- HERO --&gt;` section
    /// marker, one of eight identical markers on that page (the others are
    /// SectionSpec.LeadingCommentHtml). Null on every hero without one.</summary>
    string? LeadingCommentHtml = null
);

/// <summary>CatOverride sets the card's own --cat (a solid colour OR a two-tone
/// linear-gradient() "chord" for a composite/multi-domain package, e.g. Prose). CatRootOverride
/// is the companion --cat-root a chord ALSO needs (CSS custom props can't be read back out of a
/// gradient value, so any call site that needs one real solid colour from a chorded card —
/// today: none on Prose's own page, but the pattern exists sitewide, see COMPONENTS.md's "--cat
/// / --cat-root" note) sets this second prop alongside it. Null unless CatOverride is itself a
/// gradient. LimHtml is an optional second `.lim` caveat paragraph INSIDE the card, after the
/// `.desc` body (e.g. `holodb/index.html`'s "Transactional SQL" card: a `.desc` plus its own
/// `.lim` limitation note, both inside the one `&lt;article&gt;`) — distinct from
/// SectionSpec.LimHtml, which is one shared note after the whole `.grid`. PreBodyHtml is raw
/// HTML emitted right after `.card-top`, before `.desc` (e.g. a `.snip` code sample that belongs
/// to this one card, not the whole section — `holodb/index.html`'s "Deploy" cards). OmitCatStyle
/// drops the `style="--cat:...”` attribute entirely rather than falling back to the section's
/// default category var — for a card that's deliberately uncoloured (same "Deploy" cards, whose
/// first entry carries no `--cat` at all on the live page). All three null/false by default —
/// first exercised by the HoloDb hub page (Phase 2, third batch, 2026-09-02).</summary>
public sealed record CardSpec(
    string Title, string BodyHtml, string? CatOverride = null, string? CatRootOverride = null,
    string? LimHtml = null, string? PreBodyHtml = null, bool OmitCatStyle = false);

/// <summary>A `.card.tool` gallery entry (`&lt;a class="card tool"&gt;`, not `&lt;article&gt;`) —
/// distinct from CardSpec because it's a single clickable link, not a card that may nest an
/// overlay `.card-link`, and has its own shape: an optional `.tag`/`.ver` pair in `.card-top`,
/// and a closing `.go-in` "Open X →" line instead of trailing after `.desc`. Ver is nullable —
/// `holodb/index.html`'s lone "The Analyst" tool card has a `.tag` but no `.ver` pill at all.
/// First typed for `algformer.html`'s "Try it live" gallery + `holodb/index.html`'s single-card
/// echo of the same component (Phase 2, third batch, 2026-09-02).</summary>
public sealed record ToolCardSpec(
    string Title, string Href, string Tag, string? Ver, string DescHtml, string GoInText,
    string? CatOverride = null, string? CatRootOverride = null);

/// <summary>One `.concept` block in `holoformer.html`'s "meaning as chords" article: a sticky
/// glyph SVG, a mono "kick" label, an h2, one or more prose paragraphs, and a closing `.anchor`
/// callout tying the metaphor back to a real model term. Typed (not left as SectionSpec.Raw)
/// because the shape repeats seven times on the one page it's used on and is a real, reusable
/// "concept explainer" component, not a one-off block.</summary>
public sealed record ConceptCardSpec(string GlyphSvgInner, string Kick, string Heading, IReadOnlyList<string> ParagraphsHtml, string AnchorHtml);

/// <summary>One side of a `.cmp` "ordinary vs. this model" comparison card inside a
/// ConceptArticle — ClassName is the card's own modifier class (`"tf"`/`"this"` on
/// `holoformer.html`), Title its h3, ItemsHtml its `&lt;li&gt;` bullet list (already-formatted
/// inline HTML per item, same latitude CardSpec.BodyHtml gets).</summary>
public sealed record ConceptCompareCardSpec(string ClassName, string Title, IReadOnlyList<string> ItemsHtml);

/// <summary>The whole bespoke body of a "concept explainer" article page (today: only
/// `holoformer.html`) — a `&lt;main class="sec"&gt;&lt;div class="wrap"&gt;` wrapping N
/// ConceptCards, an optional `.sec-head`-titled two-card `.cmp` comparison, an optional trailing
/// `.note` paragraph, and a closing `.closer` block (kept raw — a big-type pull-quote plus
/// attribution links, genuinely one-off, not worth typing further than the cards it follows).
/// This is the single SectionSpec on a page that uses it (bypasses the normal multi-`.sec`
/// composition entirely, matching the live page's own single-`&lt;main&gt;` shape).</summary>
public sealed record ConceptArticleSpec(
    IReadOnlyList<ConceptCardSpec> Cards,
    string? CompareHeading,
    string? CompareTagline,
    IReadOnlyList<ConceptCompareCardSpec>? CompareCards,
    string? NoteHtml,
    string ClosingHtml,
    int WrapMaxWidthPx = 900
);

/// <summary>The whole body of a "prose-template" reference page (today: `holodb.html`,
/// `holodb/manual/index.html`) — `&lt;main class="wrap"&gt;&lt;article class="prose"&gt;`, no
/// `&lt;header class="hero"&gt;` at all (see PageSpec.Hero being nullable for the matching
/// pipeline change). Types the shell that recurs across both pages (crumb, h1, optional lede,
/// optional `.related` pills, optional `.toc`) and leaves the actual h2-sectioned body content —
/// genuinely one-off prose/tables/snippets per page — as a single raw BodyHtml block, the same
/// "type what recurs, escape-hatch what's one-off" split SectionSpec.Raw already established.</summary>
public sealed record ProseArticleSpec(
    string CrumbHtml,
    string H1,
    string? LedeHtml,
    /// <summary>The `.article-date` byline paragraph ("Published &lt;time&gt;...") rendered
    /// between the h1 and the lede — the per-article page shape (`articles/_example.html`) has
    /// one, the two package reference pages (`holodb.html`, `holodb/manual/index.html`) don't.
    /// Inserted as its own field, not folded into LedeHtml, since it has its own semantic wrapper
    /// (`&lt;p class="article-date"&gt;`, not `&lt;p class="lede"&gt;`).</summary>
    IReadOnlyList<RelatedLink>? Related,
    string RelatedAllText,
    string RelatedAllHref,
    IReadOnlyList<RelatedLink>? TocItems,
    string BodyHtml,
    string? ByelineHtml = null,
    /// <summary>Raw HTML (typically an HTML comment) emitted immediately before the `.toc`
    /// block — `articles/_example.html`'s own "OPTIONAL: keep this .toc block only if..." editorial
    /// note. Null on every page without one.</summary>
    string? TocLeadHtml = null,
    /// <summary>Raw HTML emitted inside `&lt;main class="wrap"&gt;` right BEFORE `&lt;article
    /// class="prose"&gt;` opens — `articles/_example.html`'s amber "TEMPLATE — not a published
    /// article" `.stack` warning banner, which deliberately sits outside `.prose` (it isn't part
    /// of the article's own reading column). Null on every other prose-template page.</summary>
    string? PreArticleHtml = null
);

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
    string? RawBodyHtml = null,
    /// <summary>`.card.tool` gallery entries — used only by SectionKind.ToolGrid.</summary>
    IReadOnlyList<ToolCardSpec>? ToolCards = null,
    /// <summary>An `.install` chip inside a ClosingStack, between its `&lt;p&gt;` and
    /// `.cta-row` (the HoloDb hub's "Get started" closing stack). Null on every ClosingStack
    /// that doesn't carry one (most of them) — used only by SectionKind.ClosingStack.</summary>
    string? ClosingInstallCommand = null,
    /// <summary>The whole bespoke concept-article body — used only by SectionKind.ConceptArticle,
    /// exactly one such section per page that uses this kind.</summary>
    ConceptArticleSpec? ConceptArticleData = null,
    /// <summary>The whole prose-template article body — used only by SectionKind.ProseArticle,
    /// exactly one such section per page that uses this kind.</summary>
    ProseArticleSpec? ProseArticleData = null,
    /// <summary>Optional `id="..."` on the `&lt;section class="sec"&gt;` tag itself, for same-page
    /// anchors (`articles.html`'s `#articles`, the HoloDb hub's `#how`/`#workload`/`#benchmarks`/
    /// `#features`/`#deploy`). Null on the majority of sections, which don't need one. Not
    /// meaningful on ClosingStack/ConceptArticle/ProseArticle (none of the live pages anchor into
    /// those).</summary>
    string? SectionId = null,
    /// <summary>Raw HTML (typically an HTML comment) emitted immediately before
    /// `&lt;section class="sec"...&gt;` — the HoloDb hub's own `&lt;!-- HOW IT WORKS --&gt;`-style
    /// section markers (one per section on that page). Null on every section without one.</summary>
    string? LeadingCommentHtml = null,
    /// <summary>An intro `&lt;p class="desc"&gt;` rendered between the `.sec-head` and the
    /// `.grid` — used only by SectionKind.CardGrid (the HoloDb hub's "One store, four databases'
    /// worth of jobs" section is the first CardGrid to need lead-in prose before its cards, distinct
    /// from LimHtml which trails AFTER the grid).</summary>
    string? IntroHtml = null,
    /// <summary>An inline `style="..."` value on the trailing `.lim` paragraph — used only by
    /// SectionKind.CardGrid. Most CardGrid `.lim`s carry no inline style; the HoloDb hub's
    /// "Deploy" section is the first with one (`margin-top:16px`).</summary>
    string? LimStyleAttr = null,
    /// <summary>Omits the `.grid` wrapper div — used only by SectionKind.ToolGrid, for the HoloDb
    /// hub's single-card "Try it live" section (its lone `.card.tool` is a direct child of
    /// `.wrap`, unlike `algformer.html`'s 3-card gallery which DOES wrap in `.grid`). False
    /// (wrap in `.grid`, the more common shape) by default.</summary>
    bool OmitGridWrapper = false
)
{
    public static SectionSpec Prose(string heading, string tagline, string proseHtml, string? limHtml = null, string? extraHtml = null, string? id = null) =>
        new(SectionKind.Prose, heading, tagline, ProseHtml: proseHtml, LimHtml: limHtml, ExtraHtml: extraHtml, SectionId: id);

    public static SectionSpec CardGrid(string heading, string tagline, IReadOnlyList<CardSpec> cards, string? limHtml = null, string? id = null, string? introHtml = null) =>
        new(SectionKind.CardGrid, heading, tagline, Cards: cards, LimHtml: limHtml, SectionId: id, IntroHtml: introHtml);

    public static SectionSpec SnippetList(string heading, string tagline, IReadOnlyList<SnippetSpec> snippets, string? limHtml = null, string? id = null) =>
        new(SectionKind.Snippets, heading, tagline, Snippets: snippets, LimHtml: limHtml, SectionId: id);

    public static SectionSpec ClosingStack(string heading, string bodyHtml, IReadOnlyList<CtaLink> ctas) =>
        new(SectionKind.ClosingStack, heading, Tagline: "", ClosingBodyHtml: bodyHtml, ClosingCtas: ctas);

    /// <summary>Same as ClosingStack, plus an `.install` chip between the `&lt;p&gt;` and the
    /// `.cta-row` — the HoloDb hub's "Get started" closing block.</summary>
    public static SectionSpec ClosingStackWithInstall(string heading, string bodyHtml, string installCommand, IReadOnlyList<CtaLink> ctas) =>
        new(SectionKind.ClosingStack, heading, Tagline: "", ClosingBodyHtml: bodyHtml, ClosingCtas: ctas, ClosingInstallCommand: installCommand);

    /// <summary>A `.sec-head`-titled section whose body is a `.stack` holding one prose
    /// paragraph plus a `.flow` diagram row — distinct from ClosingStack, which has no
    /// `.sec-head`/`.wrap` framing and is always the page's final CTA block.</summary>
    public static SectionSpec StackFlow(string heading, string tagline, string proseHtml, string flowHtml) =>
        new(SectionKind.StackFlow, heading, tagline, ProseHtml: proseHtml, FlowHtml: flowHtml);

    /// <summary>An escape hatch for one-off bespoke content (tables, custom widgets) that isn't
    /// common enough yet to justify its own typed SectionKind — still gets the standard
    /// `.sec`/`.wrap`/`.sec-head` framing, just a raw body.</summary>
    public static SectionSpec Raw(string heading, string tagline, string rawBodyHtml, string? id = null) =>
        new(SectionKind.Raw, heading, tagline, RawBodyHtml: rawBodyHtml, SectionId: id);

    /// <summary>A `.grid` of `.card.tool` gallery entries — same `.sec`/`.sec-head`/`.grid`
    /// framing as CardGrid, different card shape (see ToolCardSpec).</summary>
    public static SectionSpec ToolGrid(string heading, string tagline, IReadOnlyList<ToolCardSpec> tools, string? id = null, bool omitGridWrapper = false) =>
        new(SectionKind.ToolGrid, heading, tagline, ToolCards: tools, SectionId: id, OmitGridWrapper: omitGridWrapper);

    /// <summary>A `.sec-head`-titled two-column `.cmp` card comparison (exactly the same
    /// `&lt;article class="card"&gt;` shape CardGrid uses, just rendered inside `.cmp` instead of
    /// `.grid`) followed by one trailing prose paragraph — `algformer.html`'s "Two cores, same
    /// shape" section.</summary>
    public static SectionSpec Compare(string heading, string tagline, IReadOnlyList<CardSpec> cards, string closingProseHtml) =>
        new(SectionKind.Compare, heading, tagline, Cards: cards, ProseHtml: closingProseHtml);

    /// <summary>The whole bespoke concept-explainer article body — see ConceptArticleSpec. Always
    /// the sole section on a page that uses it.</summary>
    public static SectionSpec ConceptArticle(ConceptArticleSpec data) =>
        new(SectionKind.ConceptArticle, "", "", ConceptArticleData: data);

    /// <summary>The whole prose-template reference-page body — see ProseArticleSpec. Always the
    /// sole section on a page that uses it (paired with PageSpec.Hero = null).</summary>
    public static SectionSpec ProseArticle(ProseArticleSpec data) =>
        new(SectionKind.ProseArticle, "", "", ProseArticleData: data);
}

public sealed record FooterSpec(IReadOnlyList<RelatedLink> Links);

public sealed record PageSpec(
    string Slug,               // "phasor" -> site/phasor.html
    string Title,               // breadcrumb/eyebrow display name, e.g. "Phasor"
    string Category,            // data-cat value, e.g. "foundation" — "" omits data-cat entirely
    string CategoryDotVar,      // e.g. "var(--c-foundation)" — used for .sec-head .dot / default card --cat
    SeoSpec Seo,
    /// <summary>Null for a "prose-template" page (`holodb.html`, `holodb/manual/index.html`) —
    /// no `&lt;header class="hero"&gt;` at all, the page's ProseArticle section supplies its own
    /// crumb/h1 instead. The render pipeline skips HeroComposer entirely when this is null (see
    /// SiteKitPipeline's RenderHero step).</summary>
    HeroSpec? Hero,
    IReadOnlyList<SectionSpec> Sections,
    FooterSpec Footer,
    /// <summary>Full verbatim `&lt;style&gt;...&lt;/style&gt;` block for the rare page that
    /// still carries page-local CSS (e.g. `holovoxel.html`'s `.shots` figure grid) — emitted in
    /// `&lt;head&gt;` right after the shared stylesheet `&lt;link&gt;` and before the JSON-LD
    /// `&lt;script&gt;`, matching every such page's live markup order. Null on every page that
    /// only uses `site.css` (the large majority).</summary>
    string? PageStyleHtml = null,
    /// <summary>A full override of the closing `&lt;script&gt;...&lt;/script&gt;` block. Null
    /// (the default) uses HtmlComposer's standard year+copy-button script. Several bespoke pages
    /// carry a different tail script: a year-only script with no copy-button handler at all
    /// (pages with no `.copy` buttons — `holoformer.html`, `articles.html`,
    /// `articles/_example.html`, `holodb.html`, `holodb/manual/index.html`), or a differently
    /// minified variant of the same copy-button handler (`holodb/index.html`'s own
    /// slightly-terser inline version). Phase 2, third batch (2026-09-02).</summary>
    string? TailScriptHtml = null,
    /// <summary>The page's `&lt;meta charset&gt;` value, verbatim — "utf-8" on every page except
    /// `holodb.html`, which live-carries the uppercase literal `"UTF-8"` (a real, harmless,
    /// pre-existing inconsistency in the hand-authored page, reproduced here rather than quietly
    /// "fixed" out from under a byte-identity proof). Phase 2, third batch (2026-09-02).</summary>
    string MetaCharset = "utf-8",
    /// <summary>Per-page override of the top nav's item list — most pages share one SiteSpec-level
    /// NavSpec, but a few carry a genuinely different nav (extra items on the HoloDb family pages,
    /// or `holoformer.html`'s NuGet link pointing at the AlgFormer package directly instead of the
    /// site's usual profile URL). Null (the default) uses the site-level NavSpec unchanged.</summary>
    IReadOnlyList<RelatedLink>? NavItemsOverride = null,
    /// <summary>Raw HTML emitted before `&lt;!DOCTYPE html&gt;` — the one live use is
    /// `articles/_example.html`'s leading HTML comment (the "TEMPLATE FILE — NOT A PUBLISHED
    /// ARTICLE" publishing-recipe comment). Null on every real page.</summary>
    string? LeadingHtml = null,
    /// <summary>The nav-burger's `aria-label` — "Toggle menu" on every page except `holodb.html`,
    /// which live-carries the shorter literal `"Menu"` (a real, harmless, pre-existing
    /// inconsistency, reproduced rather than silently "fixed").</summary>
    string NavBurgerAriaLabel = "Toggle menu"
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

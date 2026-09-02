using SiteKit.Spec;

namespace SiteKit.Render;

// ── Render-in-progress jobs — the accumulating data records EvalApp steps thread through ──
// Deliberately separate from the *Spec types (SiteSpec/PageSpec are pure input data; these
// carry render PROGRESS). Per evalapp-owner's review: BodyFragments accumulates as a list
// through the steps and is joined exactly once in ComposeHtml — no O(n^2) string
// re-concatenation per section (the bug in the original §3.2 sketch).

/// <summary>One page's render-in-progress state. Threaded through the innermost per-page
/// sub-task chain (nested inside the per-site ForEach, itself inside the top-level ForEach —
/// see SiteKitPipeline.Build()).</summary>
public sealed record PageRenderJob(
    PageSpec Spec,
    BrandTokens Brand,
    NavSpec Nav,
    string OutputRoot,
    string? HeadHtml = null,
    string? NavHtml = null,
    IReadOnlyList<string>? BodyFragments = null,
    string? FooterHtml = null,
    string? FinalHtml = null,
    string? OutputPath = null
);

/// <summary>One site's render-in-progress state — the per-item type flowing through the
/// "pages" ForEach nested inside the "sites" ForEach.</summary>
public sealed record SiteRenderJob(
    SiteSpec Spec,
    IReadOnlyList<string>? WrittenFiles = null
);

/// <summary>The whole-run job — the top-level type SiteKitPipeline.Build()'s compiled
/// pipeline actually accepts. A "site build" can cover many SiteSpecs at once (many client
/// sites rendered in one resource-gated fan-out), matching platform-architecture.md §3.2's
/// "render N pages (or many client sites at once) for free" payoff.</summary>
public sealed record MultiSiteBuildJob(
    IReadOnlyList<SiteSpec> Sites,
    IReadOnlyList<string>? WrittenFiles = null
);

using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, page 2: site/tracer.html transcribed verbatim into a PageSpec. Chosen for further
/// structural variety beyond Phasor (Phase 1) and Prose (Phase 2's first page):
///   - A Snippets section with exactly ONE snippet, no DescAfterHtml, and no section-level LimHtml
///     at all — every other ported page so far had 2 snippets and/or a closing .lim. Exercises the
///     "all-optional-fields-actually-omitted" path through ComposeSnippets.
///   - A CardGrid section ("What you need to know") that ALSO carries a .lim caveat note in the
///     SAME section — SectionSpec.CardGrid already accepts limHtml, but no ported page so far had
///     actually used a CardGrid section with BOTH cards and a trailing .lim together.
///   - Only 4 hero facts (fewest so far), a single hero Cta, plain (non-composite) single-package
///     category matching CategoryDotVar 1:1 — the "ordinary" case, a useful control alongside
///     Prose's composite/chord case.
///   - No .prism-beam (re-confirms the HeroComposer fix from the Prose pass on a second, unrelated
///     non-beam page).
/// </summary>
public static class TracerPageSpec
{
    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "Tracer — navmesh pathfinding and game AI for .NET",
            Description: "Tracer is a pathfinding and game-AI SDK for .NET: navmesh baking, multi-agent pathfinding with goal-sharing, dynamic obstacles, line-of-sight, fog-of-war, influence maps, combat, and a bake-free grid-tactics lane — all self-tuning to your frame budget.",
            Canonical: "https://evaluatedapplications.github.io/tracer.html",
            OgTitle: "Tracer — navmesh pathfinding and game AI for .NET",
            OgDescription: "Multi-agent path queries, dynamic obstacles, line-of-sight, and combat, all self-tuning to your frame budget.",
            OgUrl: "https://evaluatedapplications.github.io/tracer.html",
            TwitterCard: "summary",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.Tracer","description":"Tracer is a pathfinding and game-AI SDK for .NET: navmesh baking, multi-agent pathfinding with goal-sharing, dynamic obstacles, line-of-sight, fog-of-war, influence maps, combat, and a bake-free grid-tactics lane — all self-tuning to your frame budget.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.1.2","url":"https://evaluatedapplications.github.io/tracer.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.Tracer","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .Hero(h => h
            .Eyebrow("Tracer · spatial &amp; games")
            .Headline("Pathfinding is the easy 10%. Tracer does the rest.")
            .Lede("Tracer is a pathfinding and game-AI SDK for real-time simulations with many moving agents: crowds, RTS-style unit groups, tactical squads. It bakes triangle-based navmeshes, routes single agents and whole swarms across them, reacts to obstacles that appear or vanish mid-game, and layers the surrounding game-AI toolkit — line-of-sight, fog-of-war, influence maps, combat engagement — on top. Every heavy service runs on the <a href=\"/evalapp.html\">EvalApp</a> pipeline, which tunes its own concurrency to the workload instead of needing a hand-picked thread count.")
            .Fact("<b>v1.1.2</b>")
            .Fact("net<b>8.0</b>")
            .Fact("triangle navmesh + grid-tactics lane")
            .Fact("self-tuning concurrency")
            .Install("dotnet add package EvaluatedApplications.Tracer")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.Tracer", CtaStyle.Ghost, externalNewTab: true)
            .Related("HoloVoxel", "/holovoxel.html")
            .Related("Phasor", "/phasor.html")
            .BarTitle("tracer.app"))
        .Section(SectionSpec.Prose(
            "The problem it solves", "everything around \"find me a path\"",
            "Most pathfinding libraries stop at \"find me a path.\" Real games need a lot more around that: hundreds of agents converging on the same goal without re-solving the same route hundreds of times; obstacles that block and unblock at runtime without a full re-bake; visibility and cover queries for AI perception; a frame-time budget the pathfinder has to live inside of, not blow through. Tracer builds all of that in as one coherent SDK, running on a data-parallel pipeline that tunes its own concurrency to the workload."))
        .Section(SectionSpec.CardGrid(
            "What it does", "the whole nav problem",
            new[]
            {
                new CardSpec("Navmesh baking", "Turn raw triangle geometry into a queryable navmesh, with obstacle-polygon exclusion and an optional hierarchical (coarse + fine) bake for large levels."),
                new CardSpec("Single- and multi-agent pathfinding", "One-shot smoothed paths, or a full swarm tick where agents sharing a goal reuse the same solve instead of paying for it per agent."),
                new CardSpec("Dynamic obstacles", "Block and unblock regions of a baked mesh at runtime without a full re-bake."),
                new CardSpec("Line-of-sight, fog-of-war, influence maps", "Visibility and area-control queries for AI perception and decision-making."),
                new CardSpec("Combat engagement", "Spatial-hashed attacker/defender resolution with pluggable damage/dodge/block formulas."),
                new CardSpec("Grid tactics", "A separate, bake-free lane (line-of-sight, movement range, cover, target selection) for tile/square-grid tactics games that don't need a navmesh at all."),
            }))
        .Section(SectionSpec.CardGrid(
            "Why it's useful", "built for scale",
            new[]
            {
                new CardSpec("One SDK for the whole nav problem", "Not just A* — the layer of things games actually need around it ships with it."),
                new CardSpec("Built for scale", "Goal-sharing means many agents converging on one point solve close to the cost of one; adaptive path budgeting keeps large swarms inside a frame-time budget under pressure."),
                new CardSpec("Two lanes", "A full triangle navmesh for open/continuous worlds, plus a pure grid-tactics lane for tile games — pick the one that matches your level format, no forced conversion."),
                new CardSpec("Self-tuning", "Every stateful service runs on the EvalApp pipeline, which adapts its own concurrency live rather than needing a hand-picked thread pool per project."),
            }))
        .Section(SectionSpec.SnippetList(
            "Get started", "minimal example",
            new[]
            {
                new SnippetSpec(
                    """
                    using Tracer;

                    var world = new PathfindingWorld();

                    // Bake a navmesh from triangle geometry
                    var baker = world.CreateNavMeshBakerService();
                    var mesh = await baker.BakeAsync(vertices, triangleIndices);

                    // Query a single smoothed path
                    var single = world.CreateSingleAgentService();
                    var waypoints = await single.QueryPathAsync(mesh, start, goal);

                    // Or tick a whole swarm of agents at once
                    var multi = world.CreateMultiAgentService();
                    var result = await multi.TickAsync(agents, mesh, tickNumber: 0);
                    """),
            }))
        .Section(SectionSpec.CardGrid(
            "What you need to know", "good to know",
            new[]
            {
                new CardSpec("Dependencies", "<a href=\"/evalapp.html\">EvalApp</a> (the data-parallel pipeline runtime) and <code>Microsoft.Extensions.DependencyInjection.Abstractions</code>."),
                new CardSpec("Proven in use", "Built alongside the Eden game project, which uses Tracer for navmesh movement, perception/fog, combat resolution, and tactical squad control."),
            },
            limHtml: "<b>Target:</b> .NET 8.0. <b>License:</b> proprietary. All capabilities are free to use today; a license key is reserved for possible future advanced features, none gated currently."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

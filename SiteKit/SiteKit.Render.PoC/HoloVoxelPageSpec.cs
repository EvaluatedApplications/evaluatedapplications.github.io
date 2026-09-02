using SiteKit.Spec;

namespace SiteKit.Render.PoC;

/// <summary>
/// Phase 2, second batch, page 2: site/holovoxel.html transcribed verbatim into a PageSpec.
/// Exercises two more brand-new composer additions:
///   - PageSpec.PageStyleHtml — holovoxel.html is one of only three pages site-wide that still
///     carries a page-local `&lt;style&gt;` block (the `.shots` before/after screenshot grid);
///     emitted between the shared stylesheet `&lt;link&gt;` and the JSON-LD `&lt;script&gt;`.
///   - SectionSpec.ExtraHtml — the "What the same engine output can look like" section is a
///     Prose section whose body needs the `.shots` figure grid inserted between its own
///     `&lt;p class="desc"&gt;` and its `.lim`.
/// Also exercises .PrismBeam() on a page OTHER than Phasor — holovoxel.html reuses the exact
/// same shared `.prism-beam` SVG markup (verified byte-identical against PhasorPageSpec.Brand()'s
/// PrismBeamSvg before transcribing this page), confirming the shared brand asset really is
/// shared, not a per-page copy that happens to look similar.
/// </summary>
public static class HoloVoxelPageSpec
{
    public const string PageStyle = """
    <style>
      .shots{display:grid; grid-template-columns:1fr 1fr; gap:14px; margin-top:8px}
      .shots figure{margin:0; background:var(--surface); border:1px solid var(--border); border-radius:var(--radius); padding:10px; overflow:hidden}
      .shots img{display:block; width:100%; height:auto; border-radius:8px}
      .shots figcaption{font-family:var(--mono); font-size:.76rem; color:var(--ink-faint); margin-top:8px; text-align:center}
      @media (max-width:640px){ .shots{grid-template-columns:1fr} }
    </style>
    """;

    public static void Configure(IPageBuilder p) => p
        .Seo(new SeoSpec(
            Title: "HoloVoxel — a holographic voxel engine for .NET",
            Description: "HoloVoxel stores a chunk of the world as one holographic superposition, so level-of-detail falls out of the read instead of being a separate mesh system. Crisp near, dreamy far, engine-only, verified headless.",
            Canonical: "https://evaluatedapplications.github.io/holovoxel.html",
            OgTitle: "HoloVoxel — a holographic voxel engine for .NET",
            OgDescription: "Level-of-detail isn't a mesh trick, it's how reading the world works. A holographic voxel engine, engine-only, verified headless.",
            OgUrl: "https://evaluatedapplications.github.io/holovoxel.html",
            TwitterCard: "summary_large_image",
            JsonLd: """
            {"@context":"https://schema.org","@type":"SoftwareApplication","name":"EvaluatedApplications.HoloVoxel","description":"HoloVoxel stores a chunk of the world as one holographic superposition, so level-of-detail falls out of the read instead of being a separate mesh system. Crisp near, dreamy far, engine-only, verified headless.","applicationCategory":"DeveloperApplication","operatingSystem":".NET 8.0+","softwareVersion":"1.3.0","url":"https://evaluatedapplications.github.io/holovoxel.html","downloadUrl":"https://www.nuget.org/packages/EvaluatedApplications.HoloVoxel","offers":{"@type":"Offer","price":"0","priceCurrency":"USD"},"author":{"@type":"Organization","name":"Evaluated Applications","url":"https://evaluatedapplications.github.io/"}}
            """))
        .PageStyle(PageStyle)
        .Hero(h => h
            .Eyebrow("HoloVoxel · spatial &amp; games")
            .Headline("Level-of-detail isn't a mesh trick. It's how reading the world works.")
            .Lede("Most voxel engines store a world as arrays of blocks and fake distant detail with mesh simplification, mipmaps, or LOD popping. HoloVoxel stores a chunk of the world as <strong>one holographic superposition</strong> instead: every voxel is bound (<code>position ⊗ content</code>) and summed into a single vector, using the <a href=\"/phasor.html\">Phasor</a> VSA codec. Reading a voxel back is a correlation against that vector, and correlating with fewer components gives a cheaper, fuzzier answer — near stays crisp, far goes soft, with no discrete LOD tiers and no popping between them.")
            .Fact("<b>v1.3.0</b>")
            .Fact("net<b>8.0</b>+")
            .Fact("engine only, no renderer")
            .Fact("verified headless")
            .Fact("free to use")
            .Install("dotnet add package EvaluatedApplications.HoloVoxel")
            .Cta("NuGet →", "https://www.nuget.org/packages/EvaluatedApplications.HoloVoxel", CtaStyle.Ghost, externalNewTab: true)
            .Related("Tracer", "/tracer.html")
            .Related("Phasor", "/phasor.html")
            .BarTitle("holovoxel.app")
            .PrismBeam())
        .Section(SectionSpec.CardGrid(
            "Why it's different", "the same read, at whatever cost you choose",
            new[]
            {
                new CardSpec("LOD is free", "There's no separate simplified mesh to generate, store, or stitch — the same hologram answers a near read and a far read, at whatever cost you choose to pay for that read."),
                new CardSpec("Memory scales with perceived detail", "A chunk only stores as many components as its LOD will ever need to read back, losslessly — the first <em>k</em> components of a truncated hologram are bit-identical to reading the same chunk in full."),
                new CardSpec("The blur is honest, not aesthetic", "Distant terrain looks soft because the engine genuinely has less information at that budget, not because a texture got downsampled — the same signal that saves compute also drives the look."),
                new CardSpec("Engine only, plug into anything", "No rendering or graphics dependency, verified headless. You bring the renderer, block palette, and world source (or use the built-in procedural generator)."),
            }))
        .Section(SectionSpec.Prose(
            "What the same engine output can look like", "a reference shading pass, not a shipped renderer",
            "HoloVoxel hands you decoded colour fields, triangulated meshes with analytic per-vertex normals, and a confidence signal for how unambiguous each decoded point is — drawing them is up to your own renderer. As a proof of concept, the same mesh/confidence output was shaded with ordinary Lambertian lighting, heightfield ambient occlusion, and confidence-driven haze at distance, against a naive flat-block bake of the identical chunk data.",
            limHtml: "Same chunk, same engine data — <code>DecodeMesh</code>'s analytic normals and <code>FieldConfidence</code>'s ambiguity signal did the work; only the downstream shading changed. This shading recipe is a proof of concept, not something the package ships — HoloVoxel stays renderer-agnostic by design.",
            extraHtml: """
            <div class="shots">
                  <figure><img src="/assets/holovoxel/before.png" alt="A HoloVoxel chunk rendered as flat, unlit blocks — the naive baseline." loading="lazy"><figcaption>naive flat-block bake</figcaption></figure>
                  <figure><img src="/assets/holovoxel/after.png" alt="The same HoloVoxel chunk rendered with Lambertian lighting, ambient occlusion, and confidence-driven haze from DecodeMesh's real normals." loading="lazy"><figcaption>DecodeMesh + a reference shading pass</figcaption></figure>
                </div>
            """))
        .Section(SectionSpec.CardGrid(
            "Key features", "what you get",
            new[]
            {
                new CardSpec("Holographic chunk storage", "Encode a chunk once, decode it at any component budget as a colour field or a triangulated surface mesh with analytic per-vertex normals."),
                new CardSpec("Continuous, meshless LOD", "The same stored hologram serves a crisp near view and a soft far view; a distance-to-budget curve is provided as a starting point."),
                new CardSpec("Field confidence for adaptive detail", "Exposes how unambiguous each decoded point is, turning that into a signal that spends more budget only where the world is genuinely ambiguous, instead of a flat distance curve everywhere."),
                new CardSpec("Spatial coarsening at distance", "Far reads average over a footprint of the underlying world data, for both terrain height and material, rather than sampling a single noisy point — smooth, coherent shapes instead of speckled noise, independent of the component-budget blur."),
                new CardSpec("Truncated holograms", "Storage cost tracks the LOD budget you actually plan to read at, not the size of the world."),
                new CardSpec("Entities too", "<code>HoloModel</code> stores a creature or prop the same holographic way, with the same LOD and confidence machinery, independent of the terrain grid."),
                new CardSpec("Built-in procedural generator", "<code>WorldGen</code> with biomes, height fields, and optional maze layout — or supply your own world source and skip it entirely."),
                new CardSpec("Ringworld warp", "A pure flat-to-cylinder coordinate transform for cylindrical/ringworld spaces, on top of the same flat chunk storage."),
            }))
        .Section(SectionSpec.Prose(
            "Good to know", "this is the engine, not the renderer",
            "HoloVoxel has no graphics or engine dependency and is verified headless — it hands you decoded colour fields, meshes, and confidence values; drawing them (shaders, GPU decode, a game engine binding) is up to the consuming front end. It's built on Phasor: HoloVoxel's chunk hologram is a direct application of the Phasor VSA codec, so if you're already using Phasor elsewhere, the same binding/correlation model is reused here for space instead of arbitrary data.",
            limHtml: "<b>No license gate:</b> every capability in this package is free to use today; DLL-only distribution, proprietary license. <b>Get it:</b> <code>dotnet add package EvaluatedApplications.HoloVoxel</code>."))
        .Footer(new FooterSpec(new[]
        {
            new RelatedLink("Home", "/"),
            new RelatedLink("Packages", "/packages.html"),
            new RelatedLink("HoloDb", "/holodb/"),
            new RelatedLink("NuGet", "https://www.nuget.org/profiles/evaluatedapplications", ExternalNewTab: true),
        }));
}

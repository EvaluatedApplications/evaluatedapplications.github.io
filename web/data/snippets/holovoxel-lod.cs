using HoloVoxel.Core;

var world = new HoloWorld(seed: 2024);
var chunk = world.GetChunk(0, 0);

// Distance in, component budget out: continuous, with no discrete tiers.
foreach (var distance in new[] { 0, 64, 128, 256, 512 })
    Console.WriteLine($"distance {distance,3}: {Lod.Components(distance, world.Comps)} components");

// Spend more only where a read is ambiguous. Confidence is the margin between the winning block and the runner-up.
var confidence = chunk.FieldConfidence(components: 48);
float average = Lod.AverageConfidence(confidence, c => c.Confidence);
int baseBudget = Lod.Components(distance: 256, maxComps: world.Comps);
Console.WriteLine($"average confidence {average:F2}: {baseBudget} components stay {Lod.AdaptiveComponents(baseBudget, average, maxComps: world.Comps)}");

// Below the confidence floor the budget is boosted toward the maximum, and never lowered.
Console.WriteLine($"average confidence 0.20: {baseBudget} components become {Lod.AdaptiveComponents(baseBudget, 0.2f, maxComps: world.Comps)}");
using HoloVoxel.Core;

var world = new HoloWorld(seed: 2024);

// A creature is stored the same way as a chunk: one small hologram, decoded with the same level-of-detail rules.
var (voxels, palette) = Creatures.Wraith();
var wraith = new HoloModel(world, voxels, palette);

var crisp = wraith.Decode(step: 1, components: world.Comps);
var fuzzy = wraith.Decode(step: 1, components: 24);
double blur = crisp.Zip(fuzzy, (a, b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)).Average() / 3;
Console.WriteLine($"wraith: {crisp.Count} cells, bounding box ({wraith.MinX},{wraith.MinY},{wraith.MinZ}) to ({wraith.MaxX},{wraith.MaxY},{wraith.MaxZ})");
Console.WriteLine($"mean colour change from 24 components against the full budget: {blur:F3}");
Console.WriteLine($"confidence values: {wraith.FieldConfidence(components: world.Comps).Count}");
using HoloVoxel.Core;

var world = new HoloWorld(seed: 2024);

// A near chunk at the full budget: crisp, one cell per surface column of a 16 by 16 chunk.
var near = world.GetChunk(0, 0).DecodeField(step: 1, components: world.Comps);
Console.WriteLine($"near: {near.Count} cells from {world.Comps} components, each {near[0].Size} wide");

// A distant chunk, decoded cheaply and fuzzily: coarse cells and a small budget.
var far = world.GetChunk(40, 0).DecodeField(step: 8, components: 40);
Console.WriteLine($"far: {far.Count} cells from 40 components, each {far[0].Size} wide");
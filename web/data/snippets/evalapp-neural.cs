using EvalApp;
using EvalApp.Neural;

Eval.App("NeuralTuned")
    .WithResource(ResourceKind.Network, Tunable.Between(1, 8, 2))
    .WithNeuralTuning()        // after WithResource, before DefineDomain
    .DefineDomain("Orders")
        .DefineTask<Batch>("Fetch")
            .ForEach(
                select: b => b.Items,
                merge: (b, items) => b with { Items = items.ToList() },
                collectionName: "Items",
                configure: sub => sub
                    .Gate(ResourceKind.Network, onWaiting: null, configure: g => g
                        .AddStep("Download", async (int i, CancellationToken ct) => { await Task.Delay(5, ct); return i + 1; })))
        .Run(out ICompiledPipeline<Batch> pipeline)
    .Build();

for (var run = 0; run < 3; run++)
{
    var result = await pipeline.RunAsync(new Batch(Enumerable.Range(0, 16).ToList()));
    Console.WriteLine($"run {run + 1}: {result.GetData().Items.Count} items, success {result.IsSuccess}");
}

public sealed record Batch(List<int> Items);
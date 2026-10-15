using EvalApp;

var store = Path.Combine(Path.GetTempPath(), "evalapp-tuning-sample.json");

Eval.App("Tuned")
    .WithResource(ResourceKind.Network, Tunable.Between(1, 8, 2))
    .WithTuning(storePath: store)
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

for (var run = 0; run < 5; run++)
    await pipeline.RunAsync(new Batch(Enumerable.Range(0, 16).ToList()));

// A read-only window on the compiled plan: each ForEach node counts its calls (and, in Inline and FanOut, how they ran).
foreach (var node in pipeline.Report().Of("ForEach"))
    Console.WriteLine($"{node.Label}: {node.Calls} calls");

File.Delete(store);

public sealed record Batch(List<int> Items);
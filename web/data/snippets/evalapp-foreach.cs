using EvalApp;

var progress = 0;

Eval.App("ForEach")
    .DefineDomain("Orders")
        .DefineTask<LineItemsData>("CalcLineTotals")
            .ForEach(
                select: d => d.Items,
                merge: (d, items) => d with { Items = items.ToList() },
                collectionName: "Items",
                parallelism: Tunable.ForItems(),
                configure: sub => sub
                    .OnItemCompleted((item, result) => Interlocked.Increment(ref progress))
                    .AddStep("Calc", (LineItem i) => i with { LineTotal = i.Price * i.Quantity }))
        .Run(out ICompiledPipeline<LineItemsData> pipeline)
    .Build();

var items = Enumerable.Range(1, 12).Select(n => new LineItem($"item-{n}", n, 2.5m)).ToList();
var result = await pipeline.RunAsync(new LineItemsData(items));
var total = result.GetData().Items.Sum(i => i.LineTotal);
Console.WriteLine($"items: {result.GetData().Items.Count}, progress callbacks: {progress}, total: {total}");

public sealed record LineItem(string Name, int Quantity, decimal Price, decimal LineTotal = 0m);
public sealed record LineItemsData(List<LineItem> Items);
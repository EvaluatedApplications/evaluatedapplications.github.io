using EvalApp;

Eval.App("Gate")
    .WithResource(ResourceKind.Database, Tunable.Between(1, 8, 2))
    .WithResource(ResourceKind.Of("report-engine"), maxConcurrency: 1)
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .Gate(ResourceKind.Database, onWaiting: null, configure: g => g
                .AddStep("CheckInventory", d => d with { Status = "Checked" }))
            .Gate("CustomPool", Tunable.FixedAt(1),
                onWaiting: null,
                configure: g => g.AddStep("RunReport", d => d with { Status = "Reported" }))
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

var result = await pipeline.RunAsync(new OrderData("Alice", 10m));
Console.WriteLine(result.GetData().Status);

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");
using EvalApp;

Eval.App("Branches")
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .If(d => d.Total > 100,
                then: b => b.AddStep("VipDiscount", d => d with { Total = d.Total * 0.8m, Status = "VIP" }),
                @else: b => b.AddStep("StandardProcess", d => d with { Status = "Standard" }))
            .AddParallelGroup(
                pg => pg
                    .AddBranch("CalcTax", d => d with { Tax = d.Total * 0.08m })
                    .AddBranch<CalcShippingBranch>("CalcShipping")
                    .AddBranch("CalcHandling", new CalcHandlingFeeStep()),
                mergeStrategy: new SumNumericFieldsMerge())
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

foreach (var total in new[] { 50m, 200m })
{
    var d = (await pipeline.RunAsync(new OrderData("Alice", total))).GetData();
    Console.WriteLine($"{d.Status}: total {d.Total}, tax {d.Tax}, shipping {d.Shipping}, handling {d.Handling}");
}

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New",
    decimal Tax = 0m, decimal Shipping = 0m, decimal Handling = 0m);

public sealed class CalcShippingBranch : PureStep<OrderData>
{
    public override OrderData Execute(OrderData data) => data with { Shipping = 9.99m };
}

public sealed class CalcHandlingFeeStep : IStep<OrderData>
{
    public ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct = default)
        => ValueTask.FromResult(data with { Handling = 1.5m });
}

public sealed class SumNumericFieldsMerge : IMergeStrategy<OrderData>
{
    public OrderData Merge(OrderData original, IReadOnlyList<OrderData> results)
        => original with
        {
            Tax      = results.Max(r => r.Tax),
            Shipping = results.Max(r => r.Shipping),
            Handling = results.Max(r => r.Handling),
        };
}
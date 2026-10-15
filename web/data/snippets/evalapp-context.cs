using EvalApp;

Eval.App("Context")
    .WithContext(new AppGlobal { Prefix = "ORD" })
    .DefineDomain("Orders", new OrderDomain { Region = "US" })
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep<TagWithRegionStep>("TagRegion")   // context steps are added with AddStep<TStep>() only
            .AddStep<NotifyStep>("Notify")
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

var result = await pipeline.RunAsync(new OrderData("Alice", 10m));
Console.WriteLine(result.GetData().Status);

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");
public sealed class AppGlobal : GlobalContext { public string Prefix { get; init; } = ""; }
public sealed class OrderDomain : DomainContext { public string Region { get; init; } = ""; }

public sealed class TagWithRegionStep : ContextPureStep<AppGlobal, OrderDomain, OrderData>
{
    protected override ValueTask<OrderData> TransformAsync(
        OrderData data, AppGlobal global, OrderDomain domain, CancellationToken ct)
        => ValueTask.FromResult(data with { Status = $"{global.Prefix}:{domain.Region}" });
}

public sealed class NotifyStep : ContextSideEffectStep<AppGlobal, OrderDomain, OrderData>
{
    protected override async ValueTask<OrderData> ExecuteAsync(
        OrderData data, AppGlobal global, OrderDomain domain, CancellationToken ct)
    {
        await Task.Yield();   // real code: the actual notification I/O
        return data with { Status = data.Status + ":Notified" };
    }
}
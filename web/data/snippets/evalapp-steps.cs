using EvalApp;

Eval.App("StepKinds")
    .WithResource(ResourceKind.Network)   // a side-effect step is gated on its declared resource
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Tax", new ApplyTaxStep())                  // PureStep<T> instance
            .AddStep("Fetch", new FetchTotalStep())              // AsyncStep<T> instance
            .AddStep<SendConfirmationStep>("SendConfirmation")   // SideEffectStep<T>: AddStep<TStep>() only
            .AddStep<PublishEventStep>("Publish", () => new PublishEventStep(new InMemoryBus()))
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

var result = await pipeline.RunAsync(new OrderData("Alice", 100m));
var data = result.GetData();
Console.WriteLine($"{data.Status}: total {data.Total}, tax {data.Tax}");

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New", decimal Tax = 0m);

public sealed class ApplyTaxStep : PureStep<OrderData>
{
    public override OrderData Execute(OrderData data) => data with { Tax = data.Total * 0.08m };
}

public sealed class FetchTotalStep : AsyncStep<OrderData>
{
    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await Task.Yield();                       // real code: await the actual I/O here
        return data with { Total = data.Total + 1m };
    }
}

public sealed class SendConfirmationStep : SideEffectStep<OrderData>
{
    public override ResourceKind? ResourceKind => EvalApp.ResourceKind.Network;
    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await Task.Yield();
        return data with { Status = "Confirmed" };
    }
}

public interface IOrderEventBus { ValueTask PublishAsync(string evt, CancellationToken ct); }
public sealed class InMemoryBus : IOrderEventBus
{
    public ValueTask PublishAsync(string evt, CancellationToken ct) => ValueTask.CompletedTask;
}

// A constructor-injected dependency: AddStep<TStep>(name, factory) builds it without a parameterless constructor.
public sealed class PublishEventStep(IOrderEventBus bus) : CrossDomainStep<OrderData, IOrderEventBus>(bus)
{
    public override ResourceKind? ResourceKind => EvalApp.ResourceKind.Network;
    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await Shared.PublishAsync("OrderPlaced", ct);
        return data with { Status = "Published" };
    }
}
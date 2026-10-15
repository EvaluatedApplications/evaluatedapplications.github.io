using EvalApp;

// Any real IServiceProvider works; this minimal one keeps the sample dependency-free.
var services = new SimpleServiceProvider();
services.Register(typeof(ValidateOrderStep), new ValidateOrderStep("DI-resolved"));

Eval.App("Di")
    .WithStepFactory(new ServiceProviderStepFactory(services))
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep<ValidateOrderStep>("Validate")     // resolved from the container, not Activator
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

var result = await pipeline.RunAsync(new OrderData("Alice", 10m));
Console.WriteLine(result.GetData().Status);

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");

public sealed class ValidateOrderStep : IStep<OrderData>
{
    private readonly string _tag;
    // AddStep<TStep>() needs a literal parameterless constructor; the tagged one is what the container supplies.
    public ValidateOrderStep() : this("default") { }
    public ValidateOrderStep(string tag) => _tag = tag;
    public ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct = default)
        => ValueTask.FromResult(data with { Status = _tag });
}

public sealed class SimpleServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    public void Register(Type type, object instance) => _services[type] = instance;
    public object? GetService(Type serviceType) => _services.TryGetValue(serviceType, out var s) ? s : null;
}
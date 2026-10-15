using EvalApp;

Eval.App("Saga")
    .WithResource(ResourceKind.Database, Tunable.FixedAt(2))
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .BeginSaga(CompensationPolicy.BestEffort)
                .AddStepWithCompensation(
                    "ReserveInventory",
                    forward:    d => d with { Status = "Reserved" },
                    compensate: d => { Console.WriteLine("compensate: release the inventory"); return d with { Status = "Unreserved" }; })
                .AddGate(
                    ResourceKind.Database,
                    onWaiting: null,
                    configure: g => g.AddStep("WriteLedger", d => d with { Total = d.Total }),
                    compensate: new RevertLedgerStep())
                .AddStep(
                    "ChargePayment",
                    d => d.Total > 1_000_000m
                        ? throw new InvalidOperationException("Payment declined")
                        : d with { Status = "Charged" })
            .EndSaga()
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

var ok = await pipeline.RunAsync(new OrderData("Ivy", 10m));
var failed = await pipeline.RunAsync(new OrderData("Jin", 2_000_000m));
Console.WriteLine($"ok: {ok.IsSuccess}, status {ok.GetData().Status}");
Console.WriteLine($"failed: {failed.IsFailure}, status after compensation {failed.GetData().Status}");

public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");

public sealed class RevertLedgerStep : IStep<OrderData>
{
    public ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct = default)
    {
        Console.WriteLine("compensate: revert the ledger entry");
        return ValueTask.FromResult(data with { Status = "LedgerReverted" });
    }
}
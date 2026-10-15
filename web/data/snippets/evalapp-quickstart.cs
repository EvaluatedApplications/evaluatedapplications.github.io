using EvalApp;

Eval.App("Orders")
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Validate", d =>
            {
                if (d.Total <= 0) throw new InvalidOperationException("Total must be positive");
                return d with { Status = "Validated" };
            })
            .AddStep("ApplyDiscount", d => d with { Total = d.Total * 0.9m })
            .AddStep("Complete", d => d with { Status = "Complete" })
        .Run(out ICompiledPipeline<OrderData> pipeline)
    .Build();

// Build once, at startup. Run as many times as you like.
var result = await pipeline.RunAsync(new OrderData("Alice", 99.99m));

switch (result)
{
    case PipelineResult<OrderData>.Success success:
        Console.WriteLine($"{success.Data.Status}: {success.Data.Total}");
        break;
    case PipelineResult<OrderData>.Failure failure:
        Console.WriteLine($"Failed: {failure.Exception.Message}");
        break;
    case PipelineResult<OrderData>.Skipped skipped:
        Console.WriteLine($"Skipped: {skipped.Reason}");
        break;
}

// Top-level statements come first, so the record goes last.
public sealed record OrderData(string CustomerName, decimal Total, string Status = "New");
using Phasor;

var stock = new CycleHologram(capacity: 8);
stock.Add(CycleHologram.Symbol("apples"), 10);
stock.Add(42, 5);                                   // integer symbols are exact
stock.Add(CycleHologram.Symbol("oranges"), 3);

if (stock.TryDecode(out var items))
{
    foreach (var (symbol, count) in items.OrderBy(i => i.Item2))
        Console.WriteLine($"count {count}");        // every count is exact
}

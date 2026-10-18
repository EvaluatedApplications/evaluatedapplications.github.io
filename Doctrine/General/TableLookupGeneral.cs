namespace Doctrine.General;

// STUB for seam (c), owned by algformer-owner. A table lookup keyed by the exact token summary: it remembers which doctrine the player
// chose in a situation and suggests the favourite again. It starts EMPTY (suggests nothing, confidence 0) as the real one must.
// The real section (the tiny HoloFormer) replaces THIS FILE'S class, keeps the folder Doctrine\General\, and must pass GeneralContractTests.
// Nothing outside General\ may reference TableLookupGeneral by type: the game reaches the general only through IGeneral.

public sealed class TableLookupGeneral : IGeneral
{
    readonly Dictionary<ulong, Dictionary<DoctrineSet, int>> _table = new();

    public int ExamplesSeen { get; private set; }

    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        if (!_table.TryGetValue(GeneralSummary.Key(input.Summary), out var seen) || seen.Count == 0)
            return ValueTask.FromResult(new GeneralOutput(DoctrineSet.Empty, 0));
        var best = seen.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.S0).ThenBy(kv => kv.Key.S1)
                       .ThenBy(kv => kv.Key.S2).ThenBy(kv => kv.Key.S3).First();
        int total = seen.Values.Sum();
        var d = Mask(best.Key, input.Legal);
        return ValueTask.FromResult(new GeneralOutput(d, best.Value * 1000 / (total + 2)));
    }

    /// <summary>Zero any token the army does not allow. The mask is applied to the OUTPUT: it is a rule, not a hint.</summary>
    static DoctrineSet Mask(DoctrineSet d, LegalMask legal)
    {
        int Keep(int t) => legal.Allows(t) ? t : 0;
        return new(Keep(d.S0), Keep(d.S1), Keep(d.S2), Keep(d.S3));
    }

    public ValueTask LearnAsync(GeneralExample example, CancellationToken ct)
    {
        Bump(example.Input, example.Chosen, 2);
        return ValueTask.CompletedTask;
    }

    public ValueTask LearnOutcomeAsync(GeneralOutcome outcome, CancellationToken ct)
    {
        Bump(outcome.Input, outcome.Chosen, outcome.PlayerWon ? 2 : -1);
        return ValueTask.CompletedTask;
    }

    void Bump(GeneralInput input, DoctrineSet d, int delta)
    {
        ExamplesSeen++;
        var key = GeneralSummary.Key(input.Summary);
        if (!_table.TryGetValue(key, out var row)) _table[key] = row = new();
        row.TryGetValue(d, out var w);
        w = Math.Max(0, w + delta);
        if (w == 0) row.Remove(d); else row[d] = w;
    }
}

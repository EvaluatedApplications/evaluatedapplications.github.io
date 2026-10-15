using PrismFormer;

// The holographic core has the same constructor shape. Swap the type, keep the workflow.
var holo = new HoloFormer(vocab: 16, shifts: 4, layers: 2, maxContext: 6, dModel: 32, frozenPrefix: 0);

var data = new List<(int[] Ctx, int Target)>
{
    (new[] { 1, 2, 3 }, 4),
    (new[] { 2, 3, 4 }, 5),
};
double loss = holo.TrainEpoch(data, batchSize: 2, lr: 5e-2, shuffleSeed: 1);

// Serve with the incremental cache: one running holographic memory per layer, so each token costs the same
// however long the context has become.
var cache = holo.NewCache();
double[] afterPrompt = holo.Prime(cache, new[] { 1, 2, 3 });
double[] afterNext = holo.Step(cache, 4);

// Read the model's mind: the resonance of the final query against every earlier position, and what it retrieved.
var alpha = new double[holo.Layers]; Array.Fill(alpha, 1.0);
int[] toks = { 1, 2, 3 };
double[][] resonance = holo.InspectAttention(toks, K: 2, alpha);
double[] retrieved = holo.InspectRetrieval(toks, K: 2, alpha);
double[] decoded = holo.DecodeFace(retrieved);          // per-token logits for what the query pulled out

Console.WriteLine($"loss={loss:F3} cache length={cache.Length} logits lengths {afterPrompt.Length},{afterNext.Length}");
Console.WriteLine($"resonance rows={resonance.Length}, positions per row={resonance[0].Length}, decoded logits={decoded.Length}");
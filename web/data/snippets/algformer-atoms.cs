using PrismFormer;

// Two HoloFormer atoms (width 32, 2 layers, vocabulary 16, context 8), coupled through the shared memory.
var field = AtomField.Create(atoms: 2, dim: 32, layers: 2, vocab: 16, context: 8, seed: 1);
field.Coupling = AtomCoupling.Memory;

// One example is one token window per atom (at most context + 1 tokens, the same length for every atom).
var examples = new List<int[][]> { new[] { new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { 1, 2, 3, 4, 5, 6, 7, 8 } } };
double loss = 0;
for (int i = 0; i < 20; i++) loss = field.TrainStepAllPositions(examples, lr: 0.01);

var result = field.InferAllPositions(examples[0]);   // result.Logits is the mean of the atoms' logits
Console.WriteLine($"loss={loss:F3} logits={result.Logits.Length} atoms={result.AtomLogits.Length}");

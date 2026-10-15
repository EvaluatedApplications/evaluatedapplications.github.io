using PrismFormer;

// Define a model. Tokens are ints; bring your own tokenizer or encoding.
var model = new AlgFormer(vocab: 16, shifts: 4, layers: 2, maxContext: 6, dModel: 32, frozenPrefix: 0);

// Train on (context tokens, target token) pairs.
var data = new List<(int[] Ctx, int Target)>
{
    (new[] { 1, 2, 3 }, 4),
    (new[] { 2, 3, 4 }, 5),
};
var trainer = new PrismTrainer(model);                 // data-parallel, run through EvalApp
double loss = trainer.TrainEpoch(data, batchSize: 2, lr: 5e-2, shuffleSeed: 1);

// Run inference.
var context = new[] { 1, 2, 3 };
int next        = model.Predict(context);                                    // argmax next token
double[] logits = model.LogitsFor(context);                                  // full logits
int[] generated = model.Generate(context, maxNewTokens: 5, temperature: 0.8, seed: 1);

Console.WriteLine($"loss={loss:F3} next={next} logits.Length={logits.Length} generated=[{string.Join(",", generated)}]");
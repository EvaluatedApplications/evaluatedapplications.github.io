using PrismFormer;
using PrismFormer.Gpu;

// Device auto-detect: a real CUDA GPU if present, otherwise ILGPU's own CPU backend running the SAME kernels.
Console.WriteLine(GpuDevice.Describe);   // "GPU: <name> (<MB> MB)" or "CPU (no CUDA GPU)"

// A small CPU HoloFormer. The GPU trainer wraps it, and the CPU copy stays the source of truth.
var model = new HoloFormer(vocab: 16, shifts: 4, layers: 2, maxContext: 6, dModel: 8);

using var trainer = new GpuHoloTrainer(model);

var batch = new (int[] Ctx, int Target)[]
{
    (new[] { 1, 2, 3 }, 4),
    (new[] { 5, 4, 3 }, 2),
};
double loss = trainer.TrainBatch(batch, lr: 5e-2);   // forward and backward on the device, one Adam step, updates `model` in place

trainer.SyncToGpu();                                  // the device is one step behind the updated CPU model: push it across
float[] logits = trainer.Logits(new[] { 1, 2, 3 });   // inference on the device
Console.WriteLine($"loss={loss:F3} logits.Length={logits.Length}");
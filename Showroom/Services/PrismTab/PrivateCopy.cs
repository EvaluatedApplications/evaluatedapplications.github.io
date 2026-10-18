using HoloKernel;
using PrismFormer;

namespace Showroom.Services.PrismTab;

/// <summary>Starting values for the knobs of the Creature and Forecaster tabs. Every one is only a default: the visitor can change all of them.</summary>
public static class PrismTabDefaults
{
    /// <summary>Items the score is measured on (the same held-out items every time).</summary>
    public const int EvalN = 120;

    /// <summary>Adam step size starting from fresh weights.</summary>
    public const double FreshLearningRate = 0.001;

    /// <summary>Adam step size starting from Prism's trained weights.</summary>
    public const double LearningRate = 0.0003;

    /// <summary>Gradient-norm clip.</summary>
    public const double Clip = 1.0;

    /// <summary>Training steps one press of Train runs.</summary>
    public const int StepsPerRun = 200;

    /// <summary>Examples per step.</summary>
    public const int ExamplesPerStep = 1;

    /// <summary>Seed of the fresh weights.</summary>
    public const int FreshSeed = 42;

    /// <summary>Score the held-out items after every this many steps, for the chart.</summary>
    public const int EvalEvery = 25;

    /// <summary>A fresh model eases its extra passes in over this many steps; it is always scored fully composed.</summary>
    public const int FreshRampSteps = 200;
}

/// <summary>
/// The visitor's own copy of Prism's model, trained in their browser on one text task. It is a NEW <see cref="HoloSession"/>: either random
/// weights of the same shape as the shared checkpoint (<see cref="Fresh"/>) or the shared checkpoint's trained weights copied from its bytes
/// (<see cref="Clone"/>). The shared session is only ever READ (its shape or its serialized bytes) and is never passed to anything that writes.
/// <c>Iters</c> and <c>IterAlphaServe</c> are not part of the serialized bytes, so both constructors set them from the shared model's K.
/// Training is <c>StackIterAccumulateAllPos</c> scoring only the last token (the answer), per-layer alpha array, Adam with a clip.
/// </summary>
public sealed class PrivateCopy
{
    public HoloSession Session { get; }
    /// <summary>True for random weights; false for a copy of the shared model's trained weights.</summary>
    public bool IsFresh { get; private init; }
    public int Seed { get; private init; }
    public int Steps { get; private set; }
    public double LastLoss { get; private set; }
    public double LearningRate { get; set; }
    public double Clip { get; set; }
    readonly AlphaRamp _ramp;

    PrivateCopy(HoloSession session, AlphaRamp ramp, double lr, double clip) { Session = session; _ramp = ramp; LearningRate = lr; Clip = clip; }

    /// <summary>A copy of the shared model's trained weights.</summary>
    public static PrivateCopy Clone(HoloSession shared, double learningRate = PrismTabDefaults.LearningRate, double clip = PrismTabDefaults.Clip)
    {
        ArgumentNullException.ThrowIfNull(shared);
        if (shared.ServeAlpha != 1.0) throw new InvalidOperationException("The shared model is served at alpha " + shared.ServeAlpha + "; a copy needs a fully composed model (alpha 1.0).");
        var copy = HoloSession.FromCheckpoint(shared.Export(), shared.KPass, shared.ServeAlpha);
        if (ReferenceEquals(copy.Model, shared.Model)) throw new InvalidOperationException("The private copy must not be the shared model.");
        if (copy.Model.Iters != shared.KPass) throw new InvalidOperationException("The private copy lost the shared model's K.");
        return new PrivateCopy(copy, new AlphaRamp(0), learningRate, clip);
    }

    /// <summary>Random weights of the SAME shape as the shared checkpoint (vocabulary, width, layers, shifts, window, K, golden spacing, trainable embeddings), from <paramref name="seed"/>.</summary>
    public static PrivateCopy Fresh(HoloSession shared, int seed = PrismTabDefaults.FreshSeed, double learningRate = PrismTabDefaults.FreshLearningRate, double clip = PrismTabDefaults.Clip)
    {
        ArgumentNullException.ThrowIfNull(shared);
        var m = shared.Model;
        // (vocab, shifts, layers, context, width, frozenPrefix 0 = every embedding trainable, no embed seed, seed, bindFfn off, golden as the shared model, normalize on, unitary off)
        var fresh = new HoloFormer(m.Vocab, m.Shifts, m.Layers, m.Context, m.Dim, 0, null, seed, false, m.Golden, true, false);
        var session = HoloSession.FromCheckpoint(fresh.Serialize(), shared.KPass, 1.0);
        if (ReferenceEquals(session.Model, shared.Model)) throw new InvalidOperationException("The fresh model must not be the shared model.");
        return new PrivateCopy(session, new AlphaRamp(PrismTabDefaults.FreshRampSteps), learningRate, clip) { IsFresh = true, Seed = seed };
    }

    /// <summary>One Adam step on a batch of examples (each: its prompt plus a right label). Returns the mean loss on the answer token.</summary>
    public double TrainBatch(TextTaskScorer scorer, IReadOnlyList<TaskItem> items, Random r)
    {
        if (items.Count == 0) throw new ArgumentException("A step needs at least one example.", nameof(items));
        var model = Session.Model;
        var alpha = new double[Math.Max(1, model.Layers)];
        Array.Fill(alpha, _ramp.Alpha);
        var g = model.NewGrads();
        double total = 0; int scored = 0;
        foreach (var item in items)
        {
            var (loss, n) = model.StackIterAccumulateAllPos(scorer.TrainingSequence(item, item.PickGood(r)), g, 1, Session.KPass, alpha);
            total += loss; scored += n;
        }
        model.Step(g, LearningRate, items.Count, 0.9, 0.999, 1e-8, Clip);
        Steps++; _ramp.Advance();
        Session.ApplyServe(_ramp.Alpha);
        LastLoss = scored > 0 ? total / scored : 0;
        return LastLoss;
    }

    public double Train(TextTaskScorer scorer, TaskItem item, Random r) => TrainBatch(scorer, new[] { item }, r);

    /// <summary>Serve fully composed (every pass at alpha 1), which is how the copy is scored and used.</summary>
    public void ServeComposed() => Session.ApplyServe(1.0);
}

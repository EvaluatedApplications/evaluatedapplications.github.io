namespace Doctrine.Tests;

/// <summary>The GENERAL contract (CONTRACT.md section c). A tiny model replaces the table; these rules stay.</summary>
public static class GeneralContract
{
    /// <summary>How many identical examples a general may need before it must repeat a choice. A real model can ask for more by
    /// raising this in its own derived test class (never above ~60: the design says "about 30 examples").</summary>
    public const int DefaultImitationExamples = 40;

    public static GeneralInput Input(int salt, LegalMask? legal = null)
    {
        var rng = new GameRng((ulong)salt * 7919UL + 13);
        var s = new int[GeneralSummary.Length];
        for (int i = 0; i < s.Length; i++) s[i] = rng.Next(GeneralSummary.Vocab);
        return new(salt % 10, s, legal ?? LegalMask.All);
    }

    static Doctrine Pick(params int[] t) => Doctrine.Of(t);
    static readonly Doctrine A = Pick(DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.Field(FieldOrderKind.Advance, 0));
    static readonly Doctrine B = Pick(DoctrineVocabulary.Army(ArmyOrderKind.Flip, 2), DoctrineVocabulary.Field(FieldOrderKind.Hold, 1), DoctrineVocabulary.Army(ArmyOrderKind.Aura, 2));

    static async Task Teach(IGeneral g, GeneralInput i, Doctrine d, int n)
    { for (int k = 0; k < n; k++) await g.LearnAsync(new GeneralExample(i, d, true), default); }

    public static IReadOnlyList<ContractCase<Func<IGeneral>>> Cases { get; } = Build(DefaultImitationExamples);

    public static IReadOnlyList<ContractCase<Func<IGeneral>>> Build(int examples) =>
    [
        new("starts-empty-nothing-is-pretrained", async make =>
        {
            var g = make();
            Expect.Equal(0, g.ExamplesSeen, "a new general has seen nothing");
            for (int s = 0; s < 10; s++)
            {
                var o = await g.SuggestAsync(Input(s), default);
                Expect.True(o.Doctrine.IsEmpty, "an untrained general suggests no orders (nothing pre-trained ships)");
                Expect.Equal(0, o.ConfidencePermille, "an untrained general has no confidence");
            }
        }),
        new("suggestions-are-always-legal", async make =>
        {
            var g = make();
            var rng = new GameRng(99);
            for (int s = 0; s < 6; s++) await Teach(g, Input(s), s % 2 == 0 ? A : B, examples);
            for (int s = 0; s < 6; s++)
                for (int m = 0; m < 25; m++)
                {
                    var mask = new LegalMask(rng.NextU64() & LegalMask.All.Bits | 1UL);
                    var o = await g.SuggestAsync(Input(s, mask), default);
                    Expect.True(mask.AllowsAll(o.Doctrine), $"suggested {o.Doctrine} which the mask {mask.Bits:x} does not allow");
                    Expect.True(o.Doctrine.Tokens.All(DoctrineVocabulary.IsValid), "suggested a token outside the vocabulary");
                }
        }),
        new("a-mask-that-allows-nothing-gives-no-orders", async make =>
        {
            var g = make();
            await Teach(g, Input(1), A, examples);
            var o = await g.SuggestAsync(Input(1, LegalMask.None), default);
            Expect.True(o.Doctrine.IsEmpty, "with only End legal the only possible suggestion is no orders");
        }),
        new("confidence-is-permille", async make =>
        {
            var g = make();
            for (int s = 0; s < 4; s++) await Teach(g, Input(s), A, examples);
            for (int s = 0; s < 6; s++)
            {
                var o = await g.SuggestAsync(Input(s), default);
                Expect.True(o.ConfidencePermille is >= 0 and <= 1000, $"confidence {o.ConfidencePermille} outside 0..1000");
            }
        }),
        new("imitation-repeats-a-consistent-choice", async make =>
        {
            var g = make();
            var i = Input(3);
            var before = await g.SuggestAsync(i, default);
            await Teach(g, i, A, examples);
            var o = await g.SuggestAsync(i, default);
            Expect.Equal(A, o.Doctrine, $"after {examples} identical examples the general should suggest that doctrine");
            Expect.True(o.ConfidencePermille > before.ConfidencePermille, "confidence should rise with consistent evidence");
        }),
        new("outcome-favours-what-won", async make =>
        {
            var g = make();
            var i = Input(4);
            await Teach(g, i, A, examples / 2); await Teach(g, i, B, examples / 2);
            for (int k = 0; k < examples; k++) await g.LearnOutcomeAsync(new GeneralOutcome(i, B, BattleStatus.AttackerWon, true), default);
            for (int k = 0; k < examples; k++) await g.LearnOutcomeAsync(new GeneralOutcome(i, A, BattleStatus.DefenderWon, false), default);
            var o = await g.SuggestAsync(i, default);
            Expect.Equal(B, o.Doctrine, "the choice that kept winning should beat the one that kept losing");
        }),
        new("examples-are-counted", async make =>
        {
            var g = make();
            await g.LearnAsync(new GeneralExample(Input(1), A, false), default);
            await g.LearnOutcomeAsync(new GeneralOutcome(Input(1), A, BattleStatus.Draw, false), default);
            Expect.Equal(2, g.ExamplesSeen, "each Learn* call is one example");
        }),
        new("suggest-is-a-pure-read", async make =>
        {
            var g = make();
            await Teach(g, Input(2), B, examples);
            var o1 = await g.SuggestAsync(Input(2), default);
            for (int k = 0; k < 5; k++) _ = await g.SuggestAsync(Input(2), default);
            var o2 = await g.SuggestAsync(Input(2), default);
            Expect.Equal(o1, o2, "asking must not change the answer");
        }),
        new("same-teaching-same-general", async make =>
        {
            var g1 = make(); var g2 = make();
            foreach (var g in new[] { g1, g2 })
            {
                await Teach(g, Input(5), A, examples);
                await Teach(g, Input(6), B, examples);
                await g.LearnOutcomeAsync(new GeneralOutcome(Input(5), A, BattleStatus.AttackerWon, true), default);
            }
            for (int s = 5; s <= 7; s++)
                Expect.Equal(await g1.SuggestAsync(Input(s), default), await g2.SuggestAsync(Input(s), default), "two generals taught identically must answer identically (no hidden randomness)");
        }),
        new("generals-do-not-share-state", async make =>
        {
            var g1 = make(); var g2 = make();
            await Teach(g1, Input(1), A, examples);
            Expect.Equal(0, g2.ExamplesSeen, "teaching one general taught another");
            Expect.True((await g2.SuggestAsync(Input(1), default)).Doctrine.IsEmpty, "a second general must still be empty");
        }),
        new("odd-input-does-not-throw", async make =>
        {
            var g = make();
            var legal = LegalMask.All;
            foreach (var i in new[] { new GeneralInput(0, [], legal), new GeneralInput(0, [1, 2], legal), new GeneralInput(99, Enumerable.Repeat(15, 40).ToArray(), legal), new GeneralInput(-1, [0], LegalMask.None) })
            {
                var o = await g.SuggestAsync(i, default);
                Expect.True(i.Legal.AllowsAll(o.Doctrine), "odd input still gets a legal answer");
            }
        }),
    ];
}

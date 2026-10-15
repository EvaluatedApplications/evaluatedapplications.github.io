using Prose;

// The same words, two different punctuation marks, two different parses.
foreach (var s in ProseEngine.Parse("The panda eats, shoots, and leaves. The panda eats shoots and leaves."))
    Console.WriteLine(ProseEngine.Explain(s));
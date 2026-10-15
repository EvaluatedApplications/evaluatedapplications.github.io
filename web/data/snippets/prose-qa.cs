using Prose;

var prose = new ProseEngine();
prose.MineText("The wise king rules the peaceful kingdom. The kingdom is calm. " +
               "The king gives the poor man a book. The man reads the book. " +
               "The dog chases the red ball. The dog barks. The sky is blue. " +
               "The queen sends the man a letter. The queen is kind.");

// Reading-comprehension pairs: a context, a question and the answer the context supports.
foreach (var pair in prose.GenerateQa(3, seed: 4))
    Console.WriteLine($"{pair.Context} | {pair.Question} -> {pair.Answer}");

// Multi-sentence passages, where later sentences refer back to earlier ones.
foreach (var pair in prose.GenerateQaPassages(2, sentencesPerPassage: 3, seed: 5))
    Console.WriteLine($"{pair.Question} -> {pair.Answer}");
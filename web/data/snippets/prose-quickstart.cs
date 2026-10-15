using Prose;

var prose = new ProseEngine();
prose.MineText("The wise king rules the peaceful kingdom. The kingdom is calm. " +
               "The king gives the poor man a book. The man reads the book. " +
               "The dog chases the red ball. The dog barks. The sky is blue. " +
               "The queen sends the man a letter. The queen is kind.");

foreach (var sentence in prose.Generate(5, seed: 1))
    Console.WriteLine(sentence);
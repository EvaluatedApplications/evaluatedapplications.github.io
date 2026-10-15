using Prose;

var prose = new ProseEngine();
prose.Mine(@"C:\corpus\text");                        // parse every *.txt in the folder and mine it
prose.Save(@"prose.wal");                              // persist the mined tables to HoloDb (optional)

foreach (var sentence in prose.Generate(1000))         // new, grammatical sentences
    Console.WriteLine(sentence);

prose.WriteCorpus(@"C:\corpus\synthetic", 100_000);    // write sentences out as .txt files for a language model
prose.WriteQaPairs(@"C:\corpus\qa", 50_000);           // write context + question -> answer pairs as a TSV
using System.Runtime.CompilerServices;

// The mutation proofs in Doctrine.Tests build a general with a deliberate fault (GeneralFault is internal on purpose: the game cannot set one).
[assembly: InternalsVisibleTo("Doctrine.Tests")]

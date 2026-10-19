using System.Runtime.CompilerServices;

// The mutation proofs in Doctrine.Tests build a general with a deliberate fault (GeneralFault is internal on purpose: the game cannot set one).
[assembly: InternalsVisibleTo("Doctrine.Tests")]
// The measurement runners (scratch projects kept as .txt under Doctrine\todo\difficulty-candidates\) read ScriptedOpponent to model the briefing; they are not shipped.
[assembly: InternalsVisibleTo("dgo5")]

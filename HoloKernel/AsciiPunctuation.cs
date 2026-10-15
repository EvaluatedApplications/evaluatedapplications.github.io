namespace HoloKernel;

/// <summary>
/// Folds "smart" UTF punctuation to its ASCII twin BEFORE text reaches a tokenizer (2026-09-16).
///
/// <para>WHY THIS EXISTS: <c>SubwordVocab.Fold</c> (AlgFormer, the serving side) maps every char outside
/// printable ASCII to a SPACE. So a curly apostrophe does not merely fail to encode, it silently becomes
/// whitespace: a visitor typing "what's this" on a phone — where iOS and Android autocorrect ' to U+2019 —
/// actually asks the model "what s this". The model then answers the question it was really asked, and the
/// glitch looks like the model being bad at contractions rather than input being damaged before it arrives.</para>
///
/// <para>The same damage in the TRAINING data is what taught the live checkpoint to write "I m not sure":
/// measured 30,639 stripped contractions across 25.8% of the chat pairs file, where for the you-are and it-is
/// forms the broken spelling had become the majority. The trainer got the matching fix in
/// <c>MintTokenizer.AsciiTwin</c> (PrismFormer studio). KEEP THE TWO TABLES IDENTICAL: train-time and
/// serve-time tokenisation disagreeing is exactly the class of bug that produced the mismatch above, and the
/// same two types have silently diverged before over <c>MaxLen</c>.</para>
///
/// <para>Deliberately a NO-OP on ASCII input: every char &lt;= 126 maps to itself, so ordinary typing, every
/// existing checkpoint, and every stored corpus tokenise byte-identically. The common case (no smart
/// punctuation at all) returns the SAME string instance without allocating.</para>
/// </summary>
public static class AsciiPunctuation
{
    /// <summary>The one table. Anything not listed is returned unchanged — including chars that are still
    /// non-ASCII, which the tokenizer's own fold then handles exactly as it did before.</summary>
    public static char Twin(char c) => c switch
    {
        '‘' or '’' or '‚' or '′' or '´' => '\'',   // curly / low-9 / prime / acute
        '“' or '”' or '„' or '″' => '"',                // curly double quotes, double prime
        '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-',
        ' ' or ' ' or ' ' or ' ' or ' ' => ' ',    // non-breaking / figure / thin spaces
        '…' => '.',                                                    // ellipsis, one char in, one char out
        _ => c,
    };

    /// <summary>Folds every smart-punctuation char in <paramref name="text"/> to its ASCII twin. Returns the
    /// input instance unchanged when there is nothing to fold, which is the overwhelmingly common case.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var hit = false;
        for (var i = 0; i < text.Length; i++) if (Twin(text[i]) != text[i]) { hit = true; break; }
        if (!hit) return text;
        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++) span[i] = Twin(src[i]);
        });
    }
}

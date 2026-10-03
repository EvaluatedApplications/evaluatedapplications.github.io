// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\CsvReader.cs, re-synced 2026-10-03 (Session 25/26 sync): verbatim upstream (CP850 fallback included).
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

using System.Text;

namespace CouncilAudit;

/// <summary>
/// Minimal RFC4180-ish CSV reader over raw bytes (no file access, so it works from a
/// browser file-picker's byte[] directly). Handles quoted fields with embedded commas,
/// quotes and newlines.
/// </summary>
public static class CsvReader
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void RegisterCodePages() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Decides the text encoding. Several UK councils (Wokingham among them) publish
    /// Windows-1252: reading that as UTF-8 does NOT throw, it silently corrupts the
    /// £ sign and any other byte outside the ASCII range, and money columns that use
    /// "£1,234.56" literal signs end up with mangled characters that still parse as
    /// *something*, so this bug does not announce itself - it has to be checked for.
    /// Rule: UTF-8 BOM present -> UTF-8. Otherwise, if the byte sequence is NOT valid
    /// UTF-8 (strict), fall back to Windows-1252. Otherwise accept UTF-8.
    /// </summary>
    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false);

        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            // Session 17 finding, hand-confirmed against raw bytes while pulling Reading's
            // full FY2020-21..FY2025-26 back-catalogue: 7 of the ~64 months are NOT
            // Windows-1252 at all - they are IBM/OEM codepage 850. Byte 0x9C is "£" in CP850
            // but "œ" (U+0153) in Windows-1252, so a 1252 decode of a CP850 file silently
            // turns every "Amount (£)" header into "Amount (œ)" - the Gross column lookup
            // then fails with nothing louder than a console warning, and that month's
            // Gross/Net go to zero for every row. Confirmed real: reading_2022-01,
            // 2022-02, 2022-12, 2023-10, 2023-11, 2025-01, 2025-04 all exhibit this.
            // Detect it the same way it was found: decode as 1252 first; if that decode
            // contains the tell-tale "œ" glyph, a real UK council export should never
            // contain that character on its own, so re-decode as CP850 instead.
            var win1252 = Encoding.GetEncoding(1252).GetString(bytes);
            if (win1252.Contains('œ'))
                return Encoding.GetEncoding(850);
            return Encoding.GetEncoding(1252);
        }
    }

    public static List<string[]> Parse(byte[] bytes, Encoding? encodingOverride = null)
    {
        var enc = encodingOverride ?? DetectEncoding(bytes);
        string content = enc.GetString(bytes).TrimStart('﻿');

        var rows = new List<string[]>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        void EndField() { fields.Add(sb.ToString()); sb.Clear(); }
        void EndRow() { EndField(); rows.Add(fields.ToArray()); fields.Clear(); }

        while (i < content.Length)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                sb.Append(c); i++; continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; i++; break;
                case ',': EndField(); i++; break;
                case '\r': i++; break;
                case '\n': EndRow(); i++; break;
                default: sb.Append(c); i++; break;
            }
        }
        // last row/field if file doesn't end with newline
        if (sb.Length > 0 || fields.Count > 0) EndRow();

        rows.RemoveAll(r => r.Length == 1 && string.IsNullOrWhiteSpace(r[0]));
        return rows;
    }
}

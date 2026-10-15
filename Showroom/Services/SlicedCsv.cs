using System.Text;

namespace Showroom.Services;

/// <summary>
/// Shares one instance of each repeated short string (a supplier name, a service area, a classification label)
/// across every row and every loaded file. A council's 300,000 rows hold only a few thousand distinct suppliers, so
/// this cuts the strings the page keeps alive (matters on a phone's memory) and makes later equality checks cheap.
/// Looks up straight from the decoded characters, so a repeat allocates nothing.
/// </summary>
public sealed class Interner
{
    readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
    readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    public Interner() { _lookup = _map.GetAlternateLookup<ReadOnlySpan<char>>(); }
    public int Count => _map.Count;

    public string Get(ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0) return string.Empty;
        if (_lookup.TryGetValue(chars, out var s)) return s;
        s = new string(chars);
        _map[s] = s;
        return s;
    }
}

/// <summary>
/// A UTF-8 CSV reader that works in time slices. Same rules as the vendored engine's CsvReader (quoted fields with
/// embedded commas, quotes and newlines; CR ignored outside quotes; blank lines dropped), but it only handles UTF-8,
/// which is what CouncilDbBuilder writes, so it can skip the encoding sniff, never builds the whole file as one giant
/// string, and hands the thread back to the browser every few hundred rows (see <see cref="Cooperative"/>).
///
/// It is for files this app itself produced. The visitor's own uploaded files still go through the engine's
/// CsvReader (they can be Windows-1252 or CP850).
/// </summary>
public static class SlicedCsv
{
    /// <param name="internMask">Bit i set = intern column i (columns with few distinct values: council, year, supplier,
    /// service area, classification). Leave a column out when nearly every value is unique (transaction ids).</param>
    public static async Task<List<string[]>> ParseAsync(byte[] bytes, Cooperative co, Interner? interner = null,
        int internMask = 0, IWorkProgress? progress = null, string? label = null)
    {
        var rows = new List<string[]>();
        int pos = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var fields = new List<string>(24);
        var scratch = new byte[512];
        var chars = new char[512];
        int sinceCheck = 0;
        while (pos < bytes.Length)
        {
            ReadRow(bytes, ref pos, fields, ref scratch, ref chars, interner, internMask);
            if (!(fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0])))
                rows.Add(fields.ToArray());
            fields.Clear();
            if (++sinceCheck >= 96)
            {
                sinceCheck = 0;
                if (co.Due)
                {
                    progress?.Report(label ?? "Reading the file", (double)pos / bytes.Length);
                    await co.YieldAsync();
                }
            }
        }
        return rows;
    }

    // Reads one record starting at pos; leaves pos just after its terminating newline (or at the end).
    static void ReadRow(byte[] b, ref int pos, List<string> fields, ref byte[] scratch, ref char[] chars,
        Interner? interner, int internMask)
    {
        int n = b.Length;
        int col = 0;
        while (true)
        {
            // ---- one field ----
            int sLen = 0;
            bool inQuotes = false;
            int start = pos;
            // fast path: an unquoted field with no quote in it is a plain slice of the buffer
            int i = pos;
            while (i < n)
            {
                byte c = b[i];
                if (c == (byte)',' || c == (byte)'\n' || c == (byte)'\r' || c == (byte)'"') break;
                i++;
            }
            bool plain = i >= n || b[i] != (byte)'"';
            if (plain)
            {
                fields.Add(Decode(b, start, i - start, ref chars, interner, col < 31 && (internMask & (1 << col)) != 0));
                // step over trailing CR(s) then the terminator
                pos = i;
                while (pos < n && b[pos] == (byte)'\r') pos++;
                if (pos >= n) return;
                byte t = b[pos++];
                if (t == (byte)',') { col++; continue; }
                return; // '\n'
            }
            // slow path: quotes somewhere in this field, same state machine as CsvReader
            if (scratch.Length < 256) scratch = new byte[256];
            i = pos;
            while (i < n)
            {
                byte c = b[i];
                if (inQuotes)
                {
                    if (c == (byte)'"')
                    {
                        if (i + 1 < n && b[i + 1] == (byte)'"') { Put(ref scratch, ref sLen, (byte)'"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    Put(ref scratch, ref sLen, c); i++; continue;
                }
                if (c == (byte)'"') { inQuotes = true; i++; continue; }
                if (c == (byte)',' || c == (byte)'\n') break;
                if (c == (byte)'\r') { i++; continue; }
                Put(ref scratch, ref sLen, c); i++;
            }
            fields.Add(Decode(scratch, 0, sLen, ref chars, interner, col < 31 && (internMask & (1 << col)) != 0));
            pos = i;
            if (pos >= n) return;
            byte term = b[pos++];
            if (term == (byte)',') { col++; continue; }
            return;
        }
    }

    static void Put(ref byte[] buf, ref int len, byte v)
    {
        if (len == buf.Length) Array.Resize(ref buf, buf.Length * 2);
        buf[len++] = v;
    }

    static string Decode(byte[] src, int start, int len, ref char[] chars, Interner? interner, bool intern)
    {
        if (len == 0) return string.Empty;
        if (!intern || interner is null) return Encoding.UTF8.GetString(src, start, len);
        if (chars.Length < len) chars = new char[Math.Max(len, chars.Length * 2)];
        int cn = Encoding.UTF8.GetChars(src, start, len, chars, 0);
        return interner.Get(chars.AsSpan(0, cn));
    }
}
